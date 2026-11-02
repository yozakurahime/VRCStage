using System;
using UnityEngine;

namespace AvatarLab
{
    public sealed class StageDefinition : MonoBehaviour
    {
        public string kind = "world";
        public Color ambient = new Color(.3f, .3f, .3f);
        public Material skybox;
        public LightProbes lightProbes;
        public bool hasEnvironmentSettings;
        public UnityEngine.Rendering.AmbientMode ambientMode;
        public Color ambientSky,ambientEquator,ambientGround;
        public float ambientIntensity=1,reflectionIntensity=1;
        public UnityEngine.Rendering.DefaultReflectionMode reflectionMode;
        public Texture customReflectionTexture;
        public float[] ambientSH=Array.Empty<float>();
        public Texture2D[] lightmapColors = Array.Empty<Texture2D>();
        public Texture2D[] lightmapDirections = Array.Empty<Texture2D>();
        public Texture2D[] shadowMasks = Array.Empty<Texture2D>();
        public LightmapsMode lightmapsMode;
        public Renderer[] lightmappedRenderers = Array.Empty<Renderer>();
        public int[] lightmapIndices = Array.Empty<int>();
        public Vector4[] lightmapOffsets = Array.Empty<Vector4>();
        public string[] notes = Array.Empty<string>();
    }
    [Serializable] public sealed class StageManifest
    {
        public int format = 1;
        public string kind, id, name, unityVersion, bundleSha256;
        public string prefab = "assets/avatarlabworldexport/stage.prefab";
        public string[] notes = Array.Empty<string>();
    }
}
