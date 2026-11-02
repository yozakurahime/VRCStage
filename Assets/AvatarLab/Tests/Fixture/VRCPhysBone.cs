#if UNITY_EDITOR
// Synthetic serialized fixture, not a VRChat SDK implementation.
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    public class VRCPhysBone : MonoBehaviour
    {
        public Transform rootTransform;
        public Transform[] ignoreTransforms=new Transform[0];
        public bool ignoreOtherPhysBones=true,isAnimated,resetWhenDisabled=true,snapToHand;
        public Vector3 endpointPosition=new Vector3(0,.1f,0),limitRotation;
        public int version=1,integrationType=1,multiChildType=2,immobileType,limitType=1;
        public float pull=.23f,spring=.7f,stiffness=.1f,gravity=.35f,gravityFalloff=.5f,immobile=.3f,radius=.03f,maxAngleX=35,maxAngleZ=40,maxStretch=.4f,maxSquish=.1f,stretchMotion=.2f,grabMovement=1;
        public AnimationCurve pullCurve=AnimationCurve.Linear(0,.5f,1,1.5f),radiusCurve=AnimationCurve.Linear(0,1,1,0);
        public VRCPhysBoneCollider[] colliders=new VRCPhysBoneCollider[0];
        public int allowCollision=1,allowGrabbing=1,allowPosing=1;
        public string parameter="FixturePhysics";
    }
}
#endif
