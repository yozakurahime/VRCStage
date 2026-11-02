using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AvatarLab
{
    public sealed class RgbMotion
    {
        [Serializable] public sealed class Header
        {
            public int format,frameCount;public string name,source;public float fps;
            public string[] bones;public int[] parents;public Vector3[] restPositions;public Quaternion[] restRotations;
            public string[] faceNames;
        }
        public Header Info {get;private set;}
        public Vector3[] Roots {get;private set;}
        public Quaternion[] Rotations {get;private set;}
        public HumanBodyBones[] Mapping {get;private set;}
        public float[] Faces {get;private set;}
        public float Duration=>(Info.frameCount-1)/Info.fps;
        static bool Finite(float n)=>!float.IsNaN(n) && !float.IsInfinity(n);
        static bool Valid(Vector3 v)=>Finite(v.x) && Finite(v.y) && Finite(v.z) && v.sqrMagnitude<1e8f;
        static bool Valid(Quaternion q)=>Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) && Mathf.Abs(Quaternion.Dot(q,q)-1)<.005f;
        public static RgbMotion Read(string path)
        {
            var result=new RgbMotion();
            using(var input=File.OpenRead(path)) using(var reader=new BinaryReader(input,Encoding.UTF8))
            {
                if(input.Length>64*1024*1024 || Encoding.ASCII.GetString(reader.ReadBytes(8))!="ALMOTION") throw new InvalidDataException("Arquivo .almotion inválido ou incompatível.");
                int version=reader.ReadInt32();if(version!=1 && version!=2) throw new InvalidDataException("Versão de movimento não suportada.");
                int size=reader.ReadInt32();if(size<2 || size>128*1024 || size>input.Length-input.Position) throw new InvalidDataException("Cabeçalho de movimento inválido.");
                var h=JsonUtility.FromJson<Header>(Encoding.UTF8.GetString(reader.ReadBytes(size)));result.Info=h;
                int count=h?.bones?.Length??0;
                if(h==null || h.format!=version || count<15 || count>64 || h.frameCount<2 || h.frameCount>54000 || !Finite(h.fps) || h.fps<1 || h.fps>240 || h.parents?.Length!=count || h.restPositions?.Length!=count || h.restRotations?.Length!=count || h.parents[0]!=-1 || h.bones[0]!="Hips") throw new InvalidDataException("Esqueleto ou tempo de movimento inválido.");
                int faces=version==2?h.faceNames?.Length??0:0;
                if(faces>128 || version==1 && h.faceNames?.Length>0 || faces>0 && (h.faceNames.Any(n=>string.IsNullOrEmpty(n) || n.Length>128) || h.faceNames.Distinct(StringComparer.Ordinal).Count()!=faces)) throw new InvalidDataException("Canais faciais inválidos.");
                if(input.Length-input.Position!=(long)h.frameCount*(3+count*4+faces)*4) throw new InvalidDataException("Movimento incompleto ou tamanho incompatível.");
                result.Mapping=new HumanBodyBones[count];var seen=new HashSet<HumanBodyBones>();
                for(int i=0;i<count;i++)
                {
                    if(!Enum.TryParse(h.bones[i],out HumanBodyBones bone) || !Enum.IsDefined(typeof(HumanBodyBones),bone) || bone==HumanBodyBones.LastBone || bone==HumanBodyBones.Jaw || bone==HumanBodyBones.LeftEye || bone==HumanBodyBones.RightEye || !seen.Add(bone) || i>0 && (h.parents[i]<0 || h.parents[i]>=i) || !Valid(h.restPositions[i]) || !Valid(h.restRotations[i])) throw new InvalidDataException("Osso de movimento inválido: "+h.bones[i]);
                    result.Mapping[i]=bone;
                }
                foreach(var required in new[]{HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Head,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot}) if(!seen.Contains(required)) throw new InvalidDataException("Movimento sem osso obrigatório: "+required);
                result.Roots=new Vector3[h.frameCount];result.Rotations=new Quaternion[h.frameCount*count];
                result.Faces=new float[h.frameCount*faces];
                for(int f=0;f<h.frameCount;f++)
                {
                    var root=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(!Valid(root)) throw new InvalidDataException("Posição de movimento inválida.");result.Roots[f]=root;
                    for(int b=0;b<count;b++) {var q=new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(!Valid(q)) throw new InvalidDataException("Rotação de movimento inválida.");result.Rotations[f*count+b]=q;}
                    for(int b=0;b<faces;b++) {float value=reader.ReadSingle();if(!Finite(value) || Mathf.Abs(value)>10) throw new InvalidDataException("Valor facial inválido.");result.Faces[f*faces+b]=value;}
                }
            }
            return result;
        }
        public static void Write(string path,Header header,IList<Vector3> roots,IList<Quaternion> rotations,IList<float> faces)
        {
            if(roots.Count<2 || roots.Count>54000 || rotations.Count!=roots.Count*header.bones.Length || (faces?.Count??0)!=roots.Count*(header.faceNames?.Length??0)) throw new InvalidDataException("Gravação incompleta.");
            header.frameCount=roots.Count;header.format=header.faceNames?.Length>0?2:1;string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var writer=new BinaryWriter(File.Create(temp),Encoding.UTF8))
                {
                    writer.Write(Encoding.ASCII.GetBytes("ALMOTION"));writer.Write(header.format);byte[] json=Encoding.UTF8.GetBytes(JsonUtility.ToJson(header));writer.Write(json.Length);writer.Write(json);
                    int count=header.bones.Length,fc=header.faceNames?.Length??0;
                    for(int f=0;f<roots.Count;f++) {var p=roots[f];writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);for(int b=0;b<count;b++) {var q=rotations[f*count+b];writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);}for(int b=0;b<fc;b++) writer.Write(faces[f*fc+b]);}
                }
                Read(temp);AvatarArchive.CommitFile(temp,path);
            }
            finally {if(File.Exists(temp)) File.Delete(temp);}
        }
    }

    // Runtime-only Humanoid source: no FBX importer, renderer or animation graph.
    // Unity's Humanoid mapping transports body/fingers across different bind axes.
    public sealed class RgbMotionSampler : IDisposable
    {
        readonly RgbMotion motion;readonly GameObject root;readonly Transform[] bones;readonly Vector3 neutralHip;
        readonly Avatar avatar;readonly HumanPoseHandler handler;HumanPose pose;
        readonly float sourceScale;
        public readonly Vector3 NeutralBody;public readonly float NeutralFoot;
        public RgbMotionSampler(RgbMotion data):this(data.Info) {motion=data;}
        public RgbMotionSampler(RgbMotion.Header h)
        {
            root=new GameObject("AvatarLab RGB motion source"){hideFlags=HideFlags.HideAndDontSave};bones=new Transform[h.bones.Length];neutralHip=h.restPositions[0];
            try
            {
                var mapping=h.bones.Select(n=>(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),n)).ToArray();var humans=new List<HumanBone>();
                for(int i=0;i<bones.Length;i++)
                {
                    var t=new GameObject(h.bones[i]){hideFlags=HideFlags.HideAndDontSave}.transform;
                    t.SetParent(i==0?root.transform:bones[h.parents[i]],false);t.SetPositionAndRotation(h.restPositions[i],h.restRotations[i]);bones[i]=t;
                    humans.Add(new HumanBone{boneName=t.name,humanName=HumanTrait.BoneName[(int)mapping[i]],limit=new HumanLimit{useDefaultValues=true}});
                }
                var skeleton=root.GetComponentsInChildren<Transform>().Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
                avatar=AvatarBuilder.BuildHumanAvatar(root,new HumanDescription{human=humans.ToArray(),skeleton=skeleton,upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f});
                if(avatar==null || !avatar.isValid || !avatar.isHuman) throw new InvalidDataException("A captura não forma um Humanoid válido. Confira a postura de referência.");
                var animator=root.AddComponent<Animator>();animator.avatar=avatar;animator.applyRootMotion=false;animator.fireEvents=false;
                sourceScale=animator.humanScale;handler=new HumanPoseHandler(avatar,root.transform);pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
                handler.GetHumanPose(ref pose);NeutralBody=pose.bodyPosition;
                NeutralFoot=Mathf.Min(bones[Array.IndexOf(mapping,HumanBodyBones.LeftFoot)].position.y,bones[Array.IndexOf(mapping,HumanBodyBones.RightFoot)].position.y)/sourceScale;
                foreach(var toe in new[]{HumanBodyBones.LeftToes,HumanBodyBones.RightToes}) {int i=Array.IndexOf(mapping,toe);if(i>=0) NeutralFoot=Mathf.Min(NeutralFoot,bones[i].position.y/sourceScale);}
            }
            catch {Dispose();throw;}
        }
        public HumanPose SampleLive(Vector3 hip,Quaternion[] rotations)
        {
            for(int i=0;i<bones.Length;i++) bones[i].rotation=rotations[i];bones[0].position=hip;handler.GetHumanPose(ref pose);
            pose.bodyPosition.x-=neutralHip.x/sourceScale;pose.bodyPosition.z-=neutralHip.z/sourceScale;return pose;
        }
        public void SampleFace(float time,FaceStudio face)
        {
            int count=motion?.Info.faceNames?.Length??0;if(count==0) return;
            float frame=Mathf.Clamp(time*motion.Info.fps,0,motion.Info.frameCount-1);int a=(int)frame,b=Mathf.Min(a+1,motion.Info.frameCount-1);
            for(int i=0;i<count;i++) face.SetPlaybackChannel(motion.Info.faceNames[i],Mathf.Lerp(motion.Faces[a*count+i],motion.Faces[b*count+i],frame-a));
        }
        public HumanPose Sample(float time,bool travel)
        {
            var h=motion.Info;float f=Mathf.Clamp(time*h.fps,0,h.frameCount-1);int first=(int)f,last=Mathf.Min(first+1,h.frameCount-1);float t=f-first;
            for(int i=0;i<bones.Length;i++) bones[i].rotation=Quaternion.Slerp(motion.Rotations[first*bones.Length+i],motion.Rotations[last*bones.Length+i],t);
            var position=Vector3.Lerp(motion.Roots[first],motion.Roots[last],t);
            if(!travel) {position.x=motion.Roots[0].x;position.z=motion.Roots[0].z;}
            bones[0].position=position;handler.GetHumanPose(ref pose);
            // Anchor the selected take at the actor, preserving its vertical trajectory.
            pose.bodyPosition.x-=motion.Roots[0].x/sourceScale;
            pose.bodyPosition.z-=motion.Roots[0].z/sourceScale;
            return pose;
        }
        public void Dispose() {handler?.Dispose();if(root!=null) UnityEngine.Object.Destroy(root);if(avatar!=null) UnityEngine.Object.Destroy(avatar);}
    }
}
