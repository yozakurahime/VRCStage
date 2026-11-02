#if UNITY_EDITOR
using System;
using UnityEngine;
namespace AvatarLab.Editor.Fixture
{
    // Serialized test data only; this is not the SDK implementation.
    public sealed class VRCAvatarDescriptor : MonoBehaviour
    {
        public enum LayerType { Base,Additive,Gesture,Action,FX,Sitting,TPose,IKPose }
        [Serializable] public class Layer { public LayerType type; public bool isDefault; public RuntimeAnimatorController animatorController; public AvatarMask mask; }
        public Layer[] baseAnimationLayers=Array.Empty<Layer>(),specialAnimationLayers=Array.Empty<Layer>();
    }
}
#endif
