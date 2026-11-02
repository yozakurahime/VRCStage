using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public class ParameterDefault { public string name; public float value; }
    [Serializable] public class ExpressionControl
    {
        public string label, parameter, parent = "";
        public int type; // 0 button, 1 toggle, 2 submenu, 3/4/5 puppets
        public float value = 1;
        public string[] axes = Array.Empty<string>();
    }
    [Serializable] public class FaceBinding
    {
        public string parameter, rendererPath, blendShape;
        public float scale = 100;
    }
    [Serializable] public sealed class AvatarAnimationLayer
    {
        public string type;
        public RuntimeAnimatorController controller;
        public AvatarMask mask;
    }
    // Independent description. No VRChat assemblies are required by the player or exporter.
    public sealed class AvatarDefinition : MonoBehaviour
    {
        public string displayName = "Avatar";
        public Animator animator;
        public RuntimeAnimatorController baseController, fxController;
        public List<AvatarAnimationLayer> animationLayers = new List<AvatarAnimationLayer>();
        public List<ParameterDefault> defaults = new List<ParameterDefault>();
        public List<ExpressionControl> controls = new List<ExpressionControl>();
        public List<FaceBinding> faceBindings = new List<FaceBinding>();
        public List<string> compatibilityNotes = new List<string>();
        public float eyeHeight = 1.65f;
    }
    [Serializable] public sealed class AvatarManifest
    {
        public int format = 1;
        public string id, name, unityVersion, bundleSha256;
        public string prefab = "assets/avatarlabexport/avatar.prefab";
        public string[] notes = Array.Empty<string>();
    }
}
