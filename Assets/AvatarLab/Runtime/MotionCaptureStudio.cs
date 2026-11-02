using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarLab
{
    // Captures the final avatar, so VR, Leap, VMC and native animations share a format.
    [DefaultExecutionOrder(330)]
    public sealed class MotionCaptureStudio : MonoBehaviour
    {
        public LabApp app;public bool includeFace=true,playRecordedFace=true;
        public int frameRate=30;public bool Recording {get;private set;}
        public int Frames=>roots.Count;public bool Saving=>saving!=null;
        public string Status="Grave corpo, mãos e face em .almotion.";public string LastPath {get;private set;}
        public string takeName="Minha gravação";
        RgbMotion.Header header;Transform[] bones;Animator animator;AvatarDriver bound;
        List<Vector3> roots=new List<Vector3>();List<Quaternion> rotations=new List<Quaternion>();List<float> faces=new List<float>();
        Matrix4x4 anchor;Quaternion inverseRotation;Vector3 originalScale;float start,next;int limit;
        Task<string> saving;
        public static RgbMotion.Header Describe(Animator source,out Transform[] live)
        {
            using(var rest=new PoseSampler(source))
            {
                var list=new List<(HumanBodyBones id,Transform bone,Transform reference)>();
                for(int i=0;i<(int)HumanBodyBones.LastBone;i++)
                {
                    var id=(HumanBodyBones)i;if(id==HumanBodyBones.Jaw || id==HumanBodyBones.LeftEye || id==HumanBodyBones.RightEye) continue;
                    var bone=source.GetBoneTransform(id);var reference=rest.Animator.GetBoneTransform(id);
                    if(bone!=null && reference!=null) list.Add((id,bone,reference));
                }
                int Depth(Transform t) {int n=0;for(;t!=null;t=t.parent) n++;return n;}
                list=list.OrderBy(p=>Depth(p.bone)).ToList();
                if(list.Count<15 || list[0].id!=HumanBodyBones.Hips) throw new InvalidOperationException("Humanoid sem hierarquia corporal válida.");
                var h=new RgbMotion.Header{format=1,source="AvatarLab",fps=30,bones=list.Select(p=>p.id.ToString()).ToArray(),parents=new int[list.Count],restPositions=new Vector3[list.Count],restRotations=new Quaternion[list.Count]};
                live=list.Select(p=>p.bone).ToArray();
                for(int i=0;i<list.Count;i++)
                {
                    int parent=-1;for(var t=list[i].bone.parent;t!=null && parent<0;t=t.parent) parent=list.FindIndex(p=>p.bone==t);
                    h.parents[i]=parent;h.restPositions[i]=rest.Root.InverseTransformPoint(list[i].reference.position);h.restRotations[i]=Quaternion.Inverse(rest.Root.rotation)*list[i].reference.rotation;
                }
                return h;
            }
        }
        public bool StartRecording()
        {
            if(Recording || Saving) return false;
            try
            {
                bound=app.driver;animator=bound?.Definition.animator;if(animator==null || !animator.isHuman) throw new InvalidOperationException("Carregue um Humanoid antes de gravar.");
                header=Describe(animator,out bones);header.name=string.IsNullOrWhiteSpace(takeName)?"Gravação":takeName.Trim();header.fps=frameRate=Mathf.Clamp(frameRate,15,60);
                header.faceNames=includeFace?app.faces.profile.routes.Select(r=>FaceChannels.Canonical(r.source)).Concat(app.faces.LiveValues.Keys.Select(FaceChannels.Canonical)).Where(n=>!string.IsNullOrWhiteSpace(n) && n.Length<=128).Distinct(StringComparer.Ordinal).OrderBy(n=>n).Take(128).ToArray():Array.Empty<string>();
                roots=new List<Vector3>();rotations=new List<Quaternion>();faces=new List<float>();
                anchor=animator.transform.worldToLocalMatrix;inverseRotation=Quaternion.Inverse(animator.transform.rotation);originalScale=animator.transform.lossyScale;
                limit=Math.Min(54000,(64*1024*1024-131072)/((3+4*bones.Length+header.faceNames.Length)*4));
                start=Time.unscaledTime;next=0;Recording=true;Status="GRAVANDO • "+frameRate+" FPS";return true;
            }
            catch(Exception ex) {Status=ex.Message;return false;}
        }
        public void CaptureFrame()
        {
            if(!Recording || roots.Count>=limit) return;
            roots.Add(anchor.MultiplyPoint3x4(bones[0].position));
            foreach(var bone in bones) rotations.Add(inverseRotation*bone.rotation);
            foreach(string name in header.faceNames) faces.Add(Mathf.Clamp(app.faces.ReadChannel(name),-10,10));
        }
        public void StopRecording()
        {
            if(!Recording) return;Recording=false;
            if(roots.Count<2) {Status="Gravação curta demais; nenhum arquivo criado.";return;}
            string directory=app.mocap.MotionDirectory;Directory.CreateDirectory(directory);
            string safe=new string(header.name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());safe=safe.Substring(0,Math.Min(70,safe.Length));
            string path=Path.Combine(directory,safe+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".almotion");
            var h=header;var r=roots;var q=rotations;var f=faces;Status="Salvando gravação...";
            saving=Task.Run(()=>{RgbMotion.Write(path,h,r,q,f);return path;});
        }
        void Update()
        {
            if(saving==null || !saving.IsCompleted) return;var done=saving;saving=null;
            if(done.IsFaulted) Status=done.Exception.GetBaseException().Message;
            else {LastPath=done.Result;Status="Gravação salva: "+Path.GetFileName(LastPath);app.mocap.RefreshItems();}
        }
        void LateUpdate()
        {
            if(!Recording) return;
            if(bound!=app.driver || animator==null || Vector3.Distance(animator.transform.lossyScale,originalScale)>.001f) {StopRecording();return;}
            float elapsed=Time.unscaledTime-start;int catchUp=0;
            while(elapsed>=next && roots.Count<limit && catchUp++<60) {CaptureFrame();next+=1f/frameRate;}
            if(roots.Count>=limit) StopRecording();
        }
        public void DrawUI()
        {
            GUILayout.Label("Gravação de movimento • "+Status);takeName=GUILayout.TextField(takeName,70);
            GUI.enabled=!Recording && !Saving;includeFace=GUILayout.Toggle(includeFace,"Incluir canais faciais");frameRate=(int)StudioGui.Slider("FPS de gravação",frameRate,15,60);GUI.enabled=true;
            playRecordedFace=GUILayout.Toggle(playRecordedFace,"Aplicar face gravada ao reproduzir");
            GUI.enabled=!Saving;if(GUILayout.Button(Recording?"PARAR E SALVAR • "+Frames+" quadros":"GRAVAR AVATAR ATUAL")) {if(Recording) StopRecording();else StartRecording();}GUI.enabled=true;
        }
        void OnDestroy() {if(Recording) StopRecording();}
    }
}
