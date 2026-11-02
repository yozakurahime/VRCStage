"""Deterministic upstream-rig fixture, without licensed weights or videos."""
import argparse
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import numpy as np
from rgb_motion import convert,quaternions


def fixture(rgb_root, target):
    sys.path.insert(0,str(rgb_root))
    from mocap.mixamo_rig import skeleton
    from mocap.bvh_bind import bind_pose
    # Anatomical SMPL ordering, measured metres. No registered body data.
    rest=np.array([[0,1,0],[.1,.95,0],[-.1,.95,0],[0,1.15,0],
        [.1,.52,0],[-.1,.52,0],[0,1.3,0],[.1,.09,0],[-.1,.09,0],
        [0,1.45,0],[.1,.05,.15],[-.1,.05,.15],[0,1.55,0],
        [.1,1.5,0],[-.1,1.5,0],[0,1.7,0],[.2,1.5,0],[-.2,1.5,0],
        [.5,1.5,0],[-.5,1.5,0],[.75,1.5,0],[-.75,1.5,0]])
    rig=skeleton(rest);binding=bind_pose(rig)
    rest=binding['rest'];parents=binding['parents'];basis=binding['basis']
    frames=np.tile(np.eye(4),(121,52,1,1))
    finger=rig['names'].index('RightHandIndex1')
    for f in range(121):
        t=f/60;angle=-np.pi/2*np.sin(np.pi*t/2)**2
        c,s=np.cos(angle),np.sin(angle);turn=np.array([[c,-s,0],[s,c,0],[0,0,1]])
        bend=np.pi/3*np.sin(np.pi*t/2)**2;c,s=np.cos(bend),np.sin(bend)
        curl=np.array([[c,0,s],[0,1,0],[-s,0,c]])
        world=np.tile(np.eye(3),(52,1,1));positions=np.empty((52,3))
        for j,p in enumerate(parents):
            world[j]=(np.eye(3) if p<0 else world[p]) @ (turn if j==17 else curl if j==finger else np.eye(3))
            positions[j]=rest[j]+[t*.4,max(0,np.sin(np.pi*t))*.25,t*.2] if p<0 else positions[p]+world[p]@(rest[j]-rest[p])
        frames[f,:,:3,:3]=world@basis;frames[f,:,:3,3]=positions
    target.mkdir(parents=True,exist_ok=True)
    np.save(target/'frames.npy',frames.astype('f4'))
    np.savez(target/'asset.npz',parents=parents,fbx_rest=rest,fbx_rest_frames=basis,fbx_display_frames=np.tile(np.eye(3),(52,1,1)))
    meta=target/'initial_mixamo.json'
    def stat(path):return [path.stat().st_size,path.stat().st_mtime_ns]
    # A synthetic, legacy joint-only preview receipt, accepted by the same
    # upstream freshness checks as a committed user preview. No model inference.
    (target/'fixture.bvh').write_text('Synthetic validation receipt; not used for conversion.')
    (target/'project.json').write_text(json.dumps(dict(fps=60,pipeline=dict(model='gvhmr'))))
    meta.write_text(json.dumps(dict(version=10,kind='initial',names=rig['names'],frames=121,fps=60,
        frames_file='frames.npy',asset_file='asset.npz',bvh_file='fixture.bvh',sources={},
        code_stats={'mixamo_rig.py':stat(rgb_root/'mocap/mixamo_rig.py')},
        outputs={name:stat(target/name) for name in ('frames.npy','asset.npz','fixture.bvh')})))
    return meta


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--rgb-root',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--bridge',type=Path);args=parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='rgb-motion-fixture-') as temp:
        meta=fixture(args.rgb_root,Path(temp));before={p.name:p.read_bytes() for p in Path(temp).iterdir()}
        h=convert(meta,args.output,name='RGB bridge validation')
        assert h['frameCount']==121 and h['bones'][17]=='RightUpperArm' and h['bones'][-1]=='RightLittleDistal'
        assert before=={p.name:p.read_bytes() for p in Path(temp).iterdir()}
        cropped=args.output.with_suffix('.trim.almotion');h=convert(meta,cropped,start=30,end=90)
        assert h['frameCount']==61
        previous=args.output.read_bytes()
        try: convert(meta,args.output,validate=lambda:(_ for _ in ()).throw(ValueError('changed')))
        except ValueError: pass
        else: raise AssertionError('Generation change accepted')
        assert args.output.read_bytes()==previous
        try: convert(meta,args.output,start=100,end=20)
        except ValueError: pass
        else: raise AssertionError('Invalid trim accepted')
        for matrix in (np.eye(3),np.diag([1,-1,-1]),np.diag([-1,1,-1]),np.diag([-1,-1,1])):
            assert abs(np.linalg.norm(quaternions(matrix))-1)<1e-8
        if args.bridge:
            cli=[str(args.bridge),'--rgb-root',str(args.rgb_root.resolve()),'--metadata',str(meta),'--output',str(args.output.resolve()),'--name','RGB bridge validation']
            process=subprocess.run(cli,capture_output=True,text=True,timeout=60)
            assert process.returncode==0,(process.stdout,process.stderr)
            before=args.output.read_bytes()
            # Pending generation must be refused by the production executable.
            (meta.parent/'initial_mixamo_pending.json').write_text('{}')
            process=subprocess.run(cli,capture_output=True,text=True,timeout=60)
            assert process.returncode!=0 and args.output.read_bytes()==before
    print('PASS: upstream 52-bone rig -> ALMOTION; side mapping, proper reflection, trim, unchanged inputs, atomic generation failure, 180-degree rotations.')


if __name__=='__main__':main()
