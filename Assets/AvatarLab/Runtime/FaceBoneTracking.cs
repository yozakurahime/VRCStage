using UnityEngine;
namespace AvatarLab
{
    // Deltas use avatar/head space, independent of the rig's bone-axis convention.
    public sealed class FaceBoneTracking
    {
        sealed class Bone
        {
            public Transform transform;Quaternion basis,applied;bool owned;
            public Bone(Transform transform) {this.transform=transform;}
            public void Release() {if(transform!=null && owned && Quaternion.Angle(transform.localRotation,applied)<.01f) transform.localRotation=basis;owned=false;}
            public void Apply(Quaternion delta,Quaternion space)
            {
                if(transform==null) return;basis=transform.localRotation;
                transform.rotation=space*delta*Quaternion.Inverse(space)*transform.rotation;applied=transform.localRotation;owned=true;
            }
        }
        readonly Transform root,head;readonly Quaternion headBind;readonly Bone neckBone,headBone,left,right;
        public bool HasLeftEye=>left.transform!=null;public bool HasRightEye=>right.transform!=null;public bool HasNeck=>neckBone.transform!=null;
        public FaceBoneTracking(Animator animator)
        {
            root=animator.transform;head=animator.GetBoneTransform(HumanBodyBones.Head);
            headBind=head!=null?Quaternion.Inverse(root.rotation)*head.rotation:Quaternion.identity;
            neckBone=new Bone(animator.GetBoneTransform(HumanBodyBones.Neck));headBone=new Bone(head);
            left=new Bone(animator.GetBoneTransform(HumanBodyBones.LeftEye));right=new Bone(animator.GetBoneTransform(HumanBodyBones.RightEye));
        }
        public void Release() {right.Release();left.Release();headBone.Release();neckBone.Release();}
        public void ApplyHead(Vector3 angles,float neckShare)
        {
            float share=HasNeck?Mathf.Clamp01(neckShare):0;var delta=Quaternion.Euler(angles);var space=root.rotation;
            neckBone.Apply(Quaternion.Slerp(Quaternion.identity,delta,share),space);headBone.Apply(Quaternion.Slerp(Quaternion.identity,delta,1-share),space);
        }
        public void ApplyEye(bool isLeft,Vector3 angles)
        {
            var space=head!=null?head.rotation*Quaternion.Inverse(headBind):root.rotation;
            (isLeft?left:right).Apply(Quaternion.Euler(angles),space);
        }
    }
}
