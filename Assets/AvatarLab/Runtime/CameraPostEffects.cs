using System;
using UnityEngine;
namespace AvatarLab
{
    [Serializable] public sealed class CameraPostSettings
    {
        public bool enabled,bloom,motionBlur,depthOfField;
        public float bloomIntensity=.5f,bloomThreshold=.8f,bloomRadius=2;
        public Color bloomTint=Color.white;
        public bool bloomAlpha;
        public float shutter=.5f,maxMotionPixels=24,focusDistance=2,focusRange=1,focusBlur=6;
        public float chromatic,grain;
        public int quality;
    }
    public sealed class CameraPostEffects : IDisposable
    {
        Material material;
        public bool Supported {get {Ensure();return material!=null;}}
        void Ensure() {if(material==null) {var shader=Resources.Load<Shader>("AvatarLabPost");if(shader!=null && shader.isSupported) material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}}
        public void Render(RenderTexture source,RenderTexture target,CameraPostSettings s,bool transparent,bool motionAllowed,Texture previousFrame=null)
        {
            Ensure();if(material==null || s==null || !s.enabled) {Graphics.Blit(source,target);return;}
            RenderTexture bloom=null,work=null;
            var previous=RenderTexture.active;
            try
            {
                if(s.bloom && s.bloomIntensity>0)
                {
                    int divisor=s.quality==0?4:2,w=Mathf.Max(8,source.width/divisor),h=Mathf.Max(8,source.height/divisor);
                    var format=SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32;
                    bloom=RenderTexture.GetTemporary(w,h,0,format);work=RenderTexture.GetTemporary(w,h,0,format);bloom.filterMode=work.filterMode=FilterMode.Bilinear;
                    material.SetFloat("_Threshold",Mathf.Clamp(s.bloomThreshold,0,4));Graphics.Blit(source,bloom,material,0);
                    for(int i=0;i<(s.quality==0?2:3);i++) {material.SetVector("_Blur",new Vector4(Mathf.Clamp(s.bloomRadius,.5f,6)/w,0,0,0));Graphics.Blit(bloom,work,material,1);material.SetVector("_Blur",new Vector4(0,Mathf.Clamp(s.bloomRadius,.5f,6)/h,0,0));Graphics.Blit(work,bloom,material,1);}
                }
                material.SetTexture("_BloomTex",bloom!=null?(Texture)bloom:Texture2D.blackTexture);
                material.SetColor("_BloomTint",s.bloomTint);
                material.SetVector("_Post",new Vector4(bloom!=null?Mathf.Clamp(s.bloomIntensity,0,3):0,Mathf.Clamp01(s.chromatic),Mathf.Clamp(s.grain,0,.3f),transparent && s.bloomAlpha?1:0));
                material.SetVector("_Lens",new Vector4(Mathf.Clamp(s.focusDistance,.1f,50),Mathf.Clamp(s.focusRange,.1f,20),s.depthOfField?Mathf.Clamp(s.focusBlur,0,16):0,transparent?1:0));
                material.SetVector("_Motion",new Vector4(s.motionBlur && motionAllowed?Mathf.Clamp01(s.shutter):0,Mathf.Clamp(s.maxMotionPixels,1,48),s.quality==0?8:12,0));
                material.SetTexture("_PreviousTex",previousFrame!=null?previousFrame:Texture2D.blackTexture);material.SetFloat("_HasPrevious",previousFrame!=null?1:0);
                Graphics.Blit(source,target,material,2);
            }
            finally {RenderTexture.active=previous;if(work!=null) RenderTexture.ReleaseTemporary(work);if(bloom!=null) RenderTexture.ReleaseTemporary(bloom);}
        }
        public void Dispose() {if(material!=null) UnityEngine.Object.Destroy(material);material=null;}
    }
}
