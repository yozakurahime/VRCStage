#if UNITY_EDITOR
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    public sealed class VRCAnimatorTrackingControl : StateMachineBehaviour
    {
        public int trackingHead,trackingHip,trackingLeftHand=2,trackingRightHand,trackingLeftFoot,trackingRightFoot,trackingLeftFingers=2,trackingRightFingers,trackingEyes,trackingMouth;
    }
}
#endif
