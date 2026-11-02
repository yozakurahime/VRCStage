#if UNITY_EDITOR
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    public sealed class VRCPlayableLayerControl : StateMachineBehaviour
    {
        public VRCAvatarDescriptor.LayerType layer=VRCAvatarDescriptor.LayerType.Action;
        public float goalWeight=1,blendDuration=.2f;
    }
}
#endif
