"""Read committed RGB MoCap preview bones; emit AvatarLab motion, never FBX.

Original AvatarLab adapter. No AI weights, body data or upstream solver included.
Public CLI requires the upstream freshness checks; conversion never writes inputs.
"""
import argparse
import json
import os
from pathlib import Path
import struct
import sys
import uuid
import numpy as np

BODY = ['Hips','LeftUpperLeg','RightUpperLeg','Spine','LeftLowerLeg','RightLowerLeg',
        'Chest','LeftFoot','RightFoot','UpperChest','LeftToes','RightToes','Neck',
        'LeftShoulder','RightShoulder','Head','LeftUpperArm','RightUpperArm',
        'LeftLowerArm','RightLowerArm','LeftHand','RightHand']


def human_names(names):
    result = []
    mapping = dict(zip(['Hips','LeftUpLeg','RightUpLeg','Spine','LeftLeg','RightLeg',
        'Spine1','LeftFoot','RightFoot','Spine2','LeftToeBase','RightToeBase','Neck',
        'LeftShoulder','RightShoulder','Head','LeftArm','RightArm','LeftForeArm',
        'RightForeArm','LeftHand','RightHand'], BODY))
    for name in names:
        if name in mapping:
            result.append(mapping[name]); continue
        found = None
        for side in ('Left','Right'):
            for finger in ('Thumb','Index','Middle','Ring','Pinky'):
                for i, segment in enumerate(('Proximal','Intermediate','Distal'), 1):
                    if name == f'{side}Hand{finger}{i}':
                        found = side + ('Little' if finger == 'Pinky' else finger) + segment
        if found is None:
            raise ValueError('Unsupported RGB MoCap bone: ' + name)
        result.append(found)
    if len(set(result)) != len(result):
        raise ValueError('Duplicate bone mapping')
    return result


def quaternions(matrix):
    """Proper matrices -> xyzw, branching on the largest quaternion component."""
    m = np.asarray(matrix, dtype=np.float64)
    if (not np.isfinite(m).all() or
        np.max(abs(m.swapaxes(-1,-2) @ m - np.eye(3))) > 2e-3 or
        np.linalg.det(m).min() < .998):
        raise ValueError('Invalid rotation matrix')
    shape = m.shape[:-2]; m = m.reshape(-1,3,3)
    terms = np.stack((1+m[:,0,0]-m[:,1,1]-m[:,2,2],
                      1-m[:,0,0]+m[:,1,1]-m[:,2,2],
                      1-m[:,0,0]-m[:,1,1]+m[:,2,2],
                      1+m[:,0,0]+m[:,1,1]+m[:,2,2]), axis=1)
    q = np.empty((len(m),4)); choice = terms.argmax(axis=1)
    for k in range(4):
        mask = choice == k; a = m[mask]
        s = 2*np.sqrt(np.maximum(terms[mask,k],1e-12))
        if k == 0:
            values = (s/4,(a[:,0,1]+a[:,1,0])/s,(a[:,0,2]+a[:,2,0])/s,(a[:,2,1]-a[:,1,2])/s)
        elif k == 1:
            values = ((a[:,0,1]+a[:,1,0])/s,s/4,(a[:,1,2]+a[:,2,1])/s,(a[:,0,2]-a[:,2,0])/s)
        elif k == 2:
            values = ((a[:,0,2]+a[:,2,0])/s,(a[:,1,2]+a[:,2,1])/s,s/4,(a[:,1,0]-a[:,0,1])/s)
        else:
            values = ((a[:,2,1]-a[:,1,2])/s,(a[:,0,2]-a[:,2,0])/s,(a[:,1,0]-a[:,0,1])/s,s/4)
        q[mask] = np.stack(values,axis=1)
    q /= np.linalg.norm(q,axis=1,keepdims=True)
    return q.reshape(*shape,4)


def convert(metadata, output, *, start=0, end=None, name=None, validate=None):
    metadata = Path(metadata).resolve(); output = Path(output).resolve()
    raw = metadata.read_bytes()
    if len(raw) > 4*1024**2: raise ValueError('Metadata too large')
    info = json.loads(raw)
    paths = [metadata]
    for key in ('asset_file','frames_file'):
        filename = info[key]
        if not isinstance(filename,str) or Path(filename).name != filename or '\\' in filename:
            raise ValueError('Invalid committed file path')
        paths.append(metadata.parent/filename)
    if output.suffix.lower() != '.almotion' or output in paths:
        raise ValueError('Choose a separate .almotion destination')
    stamps = [(p.stat().st_size,p.stat().st_mtime_ns) for p in paths]
    names = human_names(info['names']); count = len(names)
    if count != 52: raise ValueError('Expected the 52-bone RGB MoCap preview')
    fps = float(info['fps'])
    if not np.isfinite(fps) or not 1 <= fps <= 240: raise ValueError('Invalid FPS')
    # RGB uses +X for anatomical left; Unity Humanoid uses -X. Reflect both
    # the physical vectors and their coordinate bases (S R S, not S R).
    reflect = np.diag([-1.,1.,1.])
    with np.load(paths[1],allow_pickle=False) as asset:
        parents = np.asarray(asset['parents'],dtype=int)
        rest = np.asarray(asset['fbx_rest'],dtype=float) @ reflect
        rest_frames = reflect @ np.asarray(asset['fbx_rest_frames'],dtype=float) @ reflect
        display = np.asarray(asset['fbx_display_frames'],dtype=float)
    if parents.shape != (count,) or parents[0] != -1 or any(not 0 <= parents[j] < j for j in range(1,count)):
        raise ValueError('Invalid skeleton hierarchy')
    if rest.shape != (count,3) or not np.isfinite(rest).all() or display.shape != (count,3,3):
        raise ValueError('Invalid rest skeleton')
    rest_q = quaternions(rest_frames)
    frames = np.load(paths[2],mmap_mode='r',allow_pickle=False)
    temporary = output.with_name(output.name+'.'+uuid.uuid4().hex+'.partial')
    try:
        if frames.shape != (info['frames'],count,4,4): raise ValueError('Invalid motion dimensions')
        last = len(frames)-1 if end is None else min(int(end),len(frames)-1)
        first = int(start)
        if not 0 <= first < last or last-first+1 > 54000: raise ValueError('Choose 2 to 54000 frames')
        header = dict(format=1,name=(name or metadata.parent.name)[:100],source='toyxyz_RGBMocap',
            fps=fps,frameCount=last-first+1,bones=names,parents=parents.tolist(),
            restPositions=[dict(zip('xyz',map(float,v))) for v in rest],
            restRotations=[dict(zip('xyzw',map(float,v))) for v in rest_q])
        encoded = json.dumps(header,ensure_ascii=False,allow_nan=False).encode('utf-8')
        output.parent.mkdir(parents=True,exist_ok=True)
        previous = None
        with temporary.open('xb') as stream:
            stream.write(b'ALMOTION'+struct.pack('<II',1,len(encoded))+encoded)
            for begin in range(first,last+1,128):
                stop = min(last+1,begin+128)
                block = np.asarray(frames[begin:stop],dtype=float)
                roots = block[:,0,:3,3] @ reflect
                if not np.isfinite(roots).all() or np.max(abs(roots)) > 10000:
                    raise ValueError('Invalid root positions')
                rotation = reflect @ (block[:,:,:3,:3] @ display) @ reflect
                q = quaternions(rotation)
                for i in range(len(q)):
                    if previous is not None: q[i] *= np.where((q[i]*previous).sum(-1)<0,-1,1)[:,None]
                    previous = q[i].copy()
                values = np.concatenate((roots,q.reshape(len(q),-1)),axis=1).astype('<f4')
                stream.write(values.tobytes())
        if stamps != [(p.stat().st_size,p.stat().st_mtime_ns) for p in paths]:
            raise ValueError('Motion changed during conversion. Retry after processing finishes.')
        if validate is not None: validate()
        os.replace(temporary,output)
    finally:
        frames._mmap.close()
        temporary.unlink(missing_ok=True)
    return header


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rgb-root',required=True,type=Path)
    parser.add_argument('--metadata',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--start',default=0,type=int)
    parser.add_argument('--end',type=int)
    parser.add_argument('--name')
    args = parser.parse_args()
    try:
        root = args.rgb_root.resolve()
        if not (root/'mocap/mixamo_data.py').is_file(): raise ValueError('Select the RGB MoCap installation folder')
        sys.path.insert(0,str(root))
        # This executable is frozen; the external source distribution is not.
        # Let its paths module resolve __file__ in the selected checkout.
        frozen = getattr(sys,'frozen',False)
        try:
            sys.frozen = False
            import mocap.paths
        finally:
            sys.frozen = frozen
        from mocap.motion_tools import selected_export
        metadata = args.metadata.resolve()
        info = json.loads(metadata.read_text(encoding='utf-8-sig'))
        kind = info['kind']
        if metadata.name != kind+'_mixamo.json': raise ValueError('Select a *_mixamo.json preview receipt')
        if selected_export(metadata.parent,kind) is None:
            raise ValueError('Preview is missing, outdated or correction is pending. Use Prepare Preview / Apply Correction in RGB MoCap.')
        def validate():
            if selected_export(metadata.parent,kind) is None:
                raise ValueError('Selected motion changed. Retry after processing finishes.')
        header = convert(metadata,args.output,start=args.start,end=args.end,name=args.name,validate=validate)
        print(json.dumps(dict(ok=True,frames=header['frameCount'],fps=header['fps']),ensure_ascii=True))
        return 0
    except Exception as exc:
        print(str(exc),file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
