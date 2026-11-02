using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace AvatarLab.Editor
{
    public sealed class ReferenceMotionRig : IDisposable
    {
        public readonly GameObject root=new GameObject("AvatarLab authored motion rig");
        readonly Dictionary<HumanBodyBones,Transform> bones=new Dictionary<HumanBodyBones,Transform>();
        readonly List<HumanBone> humans=new List<HumanBone>();
        readonly Dictionary<Transform,(Vector3 position,Quaternion rotation)> rest=new Dictionary<Transform,(Vector3,Quaternion)>();
        readonly Avatar avatar; readonly HumanPoseHandler handler; HumanPose pose;
        public Transform Bone(HumanBodyBones b)=>bones[b];
        public ReferenceMotionRig()
        {
            Add(HumanBodyBones.Hips,root.transform,Vector3.up);
            Add(HumanBodyBones.Spine,Bone(HumanBodyBones.Hips),Vector3.up*.15f);
            Add(HumanBodyBones.Chest,Bone(HumanBodyBones.Spine),Vector3.up*.15f);
            Add(HumanBodyBones.Neck,Bone(HumanBodyBones.Chest),Vector3.up*.15f);
            Add(HumanBodyBones.Head,Bone(HumanBodyBones.Neck),Vector3.up*.12f);
            foreach(bool left in new[]{true,false})
            {
                float s=left?-1:1;string side=left?"Left":"Right";
                var shoulder=Part(side,"Shoulder");Add(shoulder,Bone(HumanBodyBones.Chest),new Vector3(s*.1f,.1f,0));
                var upper=Part(side,"UpperArm");Add(upper,Bone(shoulder),Vector3.right*s*.1f);
                var lower=Part(side,"LowerArm");Add(lower,Bone(upper),Vector3.right*s*.3f);
                var hand=Part(side,"Hand");Add(hand,Bone(lower),Vector3.right*s*.25f);
                int finger=0;foreach(string name in new[]{"Thumb","Index","Middle","Ring","Little"})
                {
                    float z=name=="Thumb"?.045f:(2-finger)*.025f;
                    var proximal=Part(side,name+"Proximal");Add(proximal,Bone(hand),new Vector3(s*(name=="Thumb"?.025f:.065f),0,z));
                    var middle=Part(side,name+"Intermediate");Add(middle,Bone(proximal),Vector3.right*s*.035f);
                    var distal=Part(side,name+"Distal");Add(distal,Bone(middle),Vector3.right*s*.025f);
                    var tip=new GameObject(side+name+"Tip").transform;tip.SetParent(Bone(distal),false);tip.localPosition=Vector3.right*s*.02f;finger++;
                }
                var thigh=Part(side,"UpperLeg");Add(thigh,Bone(HumanBodyBones.Hips),new Vector3(s*.1f,-.05f,0));
                var shin=Part(side,"LowerLeg");Add(shin,Bone(thigh),Vector3.down*.43f);
                var foot=Part(side,"Foot");Add(foot,Bone(shin),Vector3.down*.43f);
                Add(Part(side,"Toes"),Bone(foot),new Vector3(0,-.04f,.15f));
            }
            var skeleton=root.GetComponentsInChildren<Transform>().Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
            avatar=AvatarBuilder.BuildHumanAvatar(root,new HumanDescription{human=humans.ToArray(),skeleton=skeleton,upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f});
            if(!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Authored reference rig is not a valid Humanoid.");
            foreach(var t in root.GetComponentsInChildren<Transform>()) rest[t]=(t.localPosition,t.localRotation);
            handler=new HumanPoseHandler(avatar,root.transform);pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};
        }
        static HumanBodyBones Part(string side,string part)=>(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),side+part);
        void Add(HumanBodyBones name,Transform parent,Vector3 position)
        {
            var t=new GameObject(name.ToString()).transform;t.SetParent(parent,false);t.localPosition=position;bones[name]=t;
            humans.Add(new HumanBone{boneName=t.name,humanName=HumanTrait.BoneName[(int)name],limit=new HumanLimit{useDefaultValues=true}});
        }
        public void Reset() {foreach(var pair in rest) {pair.Key.localPosition=pair.Value.position;pair.Key.localRotation=pair.Value.rotation;}}
        public HumanPose Capture() {handler.GetHumanPose(ref pose);return pose;}
        static void Aim(Transform a,Transform b,Vector3 direction) {a.rotation=Quaternion.FromToRotation(b.position-a.position,direction)*a.rotation;}
        static void Solve(Transform a,Transform b,Transform c,Vector3 target,Vector3 hint)
        {
            var origin=a.position;float l1=Vector3.Distance(origin,b.position),l2=Vector3.Distance(b.position,c.position);
            var offset=target-origin;float distance=Mathf.Clamp(offset.magnitude,Mathf.Abs(l1-l2)+.0001f,l1+l2-.0001f);var forward=offset.normalized;
            var bend=Vector3.ProjectOnPlane(hint-origin,forward).normalized;if(bend.sqrMagnitude<.01f) bend=Vector3.ProjectOnPlane(Vector3.forward,forward).normalized;
            float along=(l1*l1+distance*distance-l2*l2)/(2*distance);var elbow=origin+forward*along+bend*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            Aim(a,b,elbow-origin);Aim(b,c,origin+forward*distance-b.position);
        }
        public void Arm(bool left,Vector3 target,Vector3 hint,Vector3 fingers,Vector3 palm)
        {
            string side=left?"Left":"Right";Solve(Bone(Part(side,"UpperArm")),Bone(Part(side,"LowerArm")),Bone(Part(side,"Hand")),target,hint);
            float sign=left?-1:1;var x=fingers.normalized*sign;var y=-palm.normalized;var z=Vector3.Cross(x,y).normalized;y=Vector3.Cross(z,x).normalized;
            Bone(Part(side,"Hand")).rotation=Quaternion.LookRotation(z,y);
        }
        public void StraightArm(bool left,float angle)
        {
            string side=left?"Left":"Right";float sign=left?-1:1;var direction=new Vector3(sign*Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad),0);
            Aim(Bone(Part(side,"UpperArm")),Bone(Part(side,"LowerArm")),direction);Aim(Bone(Part(side,"LowerArm")),Bone(Part(side,"Hand")),direction);
        }
        public void Leg(bool left,Vector3 ankle,Quaternion foot)
        {
            string side=left?"Left":"Right";Solve(Bone(Part(side,"UpperLeg")),Bone(Part(side,"LowerLeg")),Bone(Part(side,"Foot")),ankle,ankle+Vector3.forward*.8f+Vector3.up*.4f);
            Bone(Part(side,"Foot")).rotation=foot;
        }
        public void CurlForPoint()
        {
            foreach(string finger in new[]{"Thumb","Middle","Ring","Little"})
            {
                Bone(Part("Right",finger+"Proximal")).localRotation=Quaternion.Euler(0,0,finger=="Thumb"?-25:-65);
                Bone(Part("Right",finger+"Intermediate")).localRotation=Quaternion.Euler(0,0,-75);
                Bone(Part("Right",finger+"Distal")).localRotation=Quaternion.Euler(0,0,-55);
            }
        }
        public void Apply(string id,float t)
        {
            Reset();float phase=t*Mathf.PI*2,gesture=Mathf.Sin(t*Mathf.PI);gesture*=gesture;
            var hips=Bone(HumanBodyBones.Hips);
            if(id=="sit") hips.position=new Vector3(0,.57f,0);
            if(id=="kneel") hips.position=new Vector3(0,.55f,0);
            if(id=="crouch" || id=="crouchwalk") hips.position=new Vector3(0,.65f,0);
            if(id=="walk" || id=="run") hips.position=new Vector3(0,id=="run"?.94f:.96f,0);
            if(id=="land") hips.position=new Vector3(0,1-gesture*.17f,0);
            if(id=="dance") {hips.position+=Vector3.right*Mathf.Sin(phase)*.035f;hips.rotation=Quaternion.Euler(0,Mathf.Sin(phase)*8,0);}
            if(id=="photo") {hips.position+=Vector3.right*.025f;hips.rotation=Quaternion.Euler(0,10,0);}
            if(id=="crouch" || id=="crouchwalk") Bone(HumanBodyBones.Spine).localRotation=Quaternion.Euler(15,0,0);
            if(id=="bow") {Bone(HumanBodyBones.Spine).localRotation=Quaternion.Euler(gesture*32,0,0);Bone(HumanBodyBones.Chest).localRotation=Quaternion.Euler(gesture*12,0,0);}
            if(id=="nod") Bone(HumanBodyBones.Head).localRotation=Quaternion.Euler(Mathf.Sin(phase*2)*18,0,0);
            if(id=="shake") Bone(HumanBodyBones.Head).localRotation=Quaternion.Euler(0,Mathf.Sin(phase*2)*25,0);
            if(id=="breathe") Bone(HumanBodyBones.Chest).localRotation=Quaternion.Euler(Mathf.Sin(phase)*1.5f,0,0);
            if(id=="dance") Bone(HumanBodyBones.Spine).localRotation=Quaternion.Euler(0,Mathf.Cos(phase)*10,Mathf.Sin(phase)*5);
            foreach(bool left in new[]{true,false})
            {
                float s=left?-1:1,p=phase+(left?0:Mathf.PI);var ankle=new Vector3(s*.1f,.09f,0);var foot=Quaternion.identity;
                if(id=="sit") ankle.z=.43f;
                if(id=="kneel") {ankle.y=.20f;ankle.z=-.35f;foot=Quaternion.Euler(65,0,0);}
                if(id=="crouch" || id=="crouchwalk") {ankle.x=s*.14f;ankle.z=.10f;}
                if(id=="walk" || id=="run" || id=="crouchwalk") {float stride=id=="run"?.30f:.20f;ankle.z+=-Mathf.Cos(p)*stride;ankle.y+=Mathf.Pow(Mathf.Max(0,Mathf.Sin(p)),2)*(id=="run"?.15f:id=="crouchwalk"?.045f:.075f);}
                Leg(left,ankle,foot);
                var target=new Vector3(s*.28f,.89f,.04f);var hint=new Vector3(s*.38f,1.12f,.25f);var fingers=Vector3.down;var palm=left?Vector3.right:Vector3.left;
                if(id=="tpose" || id=="apose") {StraightArm(left,id=="tpose"?0:-45);continue;}
                if(id=="hips" || id=="photo") {target=new Vector3(s*.19f,1.01f,.015f);hint=new Vector3(s*.60f,1.18f,-.03f);}
                if(id=="crossed") {target=new Vector3(-s*.105f,left?1.25f:1.31f,.18f);hint=new Vector3(s*.38f,1.12f,.42f);fingers=left?Vector3.right:Vector3.left;palm=Vector3.back;}
                if(id=="point" && !left) {target=new Vector3(.20f,1.37f,.535f);hint=new Vector3(.35f,1.22f,.25f);fingers=Vector3.forward;palm=Vector3.down;}
                if(id=="wavepose" && !left || id=="wave" && !left) {target=new Vector3(.34f,1.73f,.10f);hint=new Vector3(.65f,1.43f,.10f);fingers=Quaternion.AngleAxis(id=="wave"?Mathf.Sin(phase*2)*20:0,Vector3.forward)*Vector3.up;palm=Vector3.forward;}
                if(id=="clap") {float gap=Mathf.Lerp(.045f,.18f,(1+Mathf.Cos(phase*2))*.5f);target=new Vector3(s*gap,1.32f,.35f);hint=new Vector3(s*.45f,1.15f,.16f);fingers=Vector3.up;palm=left?Vector3.right:Vector3.left;}
                if(id=="victory" || id=="stretch") {StraightArm(left,id=="victory"?75:Mathf.Lerp(-80,80,gesture));continue;}
                if(id=="sit" || id=="kneel" || id=="crouch" || id=="crouchwalk") {target.y-=1-hips.position.y;hint.y-=1-hips.position.y;}
                if(id=="walk" || id=="run" || id=="crouchwalk") {target.z+=Mathf.Cos(p)*(id=="run"?.17f:id=="crouchwalk"?.06f:.10f);hint.z+=Mathf.Cos(p)*.1f;}
                if(id=="dance") {target.y+=.10f+Mathf.Sin(p)*.08f;target.z+=.07f;}
                Arm(left,target,hint,fingers,palm);
            }
            if(id=="point") CurlForPoint();
        }
        public void Dispose() {handler.Dispose();UnityEngine.Object.DestroyImmediate(avatar);UnityEngine.Object.DestroyImmediate(root);}
    }
}
