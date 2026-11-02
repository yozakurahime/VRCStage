using UnityEngine;
namespace AvatarLab
{
    public sealed class LibraryPoseContacts
    {
        readonly Transform root,hips,leftThigh,rightThigh,lu,ll,lh,ru,rl,rh;readonly float heightLocal;
        public LibraryPoseContacts(Animator animator)
        {
            root=animator.transform;hips=animator.GetBoneTransform(HumanBodyBones.Hips);leftThigh=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);rightThigh=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            lu=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);ll=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);lh=animator.GetBoneTransform(HumanBodyBones.LeftHand);
            ru=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);rl=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);rh=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var head=animator.GetBoneTransform(HumanBodyBones.Head);var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);heightLocal=Mathf.Abs(root.InverseTransformPoint(head.position).y-root.InverseTransformPoint(foot.position).y);
        }
        static void Solve(Transform a,Transform b,Transform c,Vector3 target,Vector3 hint)
        {
            var rotation=c.rotation;var origin=a.position;float l1=Vector3.Distance(origin,b.position),l2=Vector3.Distance(b.position,c.position);
            var delta=target-origin;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(l1-l2)+.00001f,l1+l2-.00001f);var forward=delta.normalized;
            var bend=Vector3.ProjectOnPlane(hint-origin,forward).normalized;if(bend.sqrMagnitude<.01f) bend=Vector3.ProjectOnPlane(Vector3.forward,forward).normalized;
            float along=(l1*l1+distance*distance-l2*l2)/(2*distance);var elbow=origin+forward*along+bend*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            a.rotation=Quaternion.FromToRotation(b.position-a.position,elbow-origin)*a.rotation;
            b.rotation=Quaternion.FromToRotation(c.position-b.position,origin+forward*distance-b.position)*b.rotation;c.rotation=rotation;
        }
        public void Apply(string id,float normalizedTime)
        {
            if(id!="hips" && id!="photo" && id!="clap") return;
            float height=root.TransformVector(Vector3.up*heightLocal).magnitude;var up=root.up;
            var side=(ru.position-lu.position).normalized;var forward=Vector3.Cross(side,up).normalized;
            if(id=="hips" || id=="photo")
            {
                side=(rightThigh.position-leftThigh.position).normalized;forward=Vector3.Cross(side,up).normalized;
                var center=hips.position+up*height*.02f+forward*height*.015f;
                float width=Vector3.Distance(leftThigh.position,rightThigh.position)*.5f+height*.06f;
                Solve(lu,ll,lh,center-side*width,lu.position-side*height*.18f-up*height*.16f-forward*height*.03f);
                Solve(ru,rl,rh,center+side*width,ru.position+side*height*.18f-up*height*.16f-forward*height*.03f);
            }
            else
            {
                float gap=Mathf.Lerp(.04f,.14f,(1+Mathf.Cos(normalizedTime*Mathf.PI*4))*.5f)*height;
                var center=(lu.position+ru.position)*.5f-up*height*.06f+forward*height*.25f;
                Solve(lu,ll,lh,center-side*gap,lu.position-side*height*.15f-up*height*.15f+forward*height*.04f);
                Solve(ru,rl,rh,center+side*gap,ru.position+side*height*.15f-up*height*.15f+forward*height*.04f);
            }
        }
    }
}
