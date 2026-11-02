Shader "Hidden/AvatarLab/Post"
{
    Properties {_MainTex("Image",2D)="white" {}}
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex,_BloomTex,_CameraMotionVectorsTexture,_PreviousTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize,_Blur,_Post,_Lens,_Motion,_BloomTint;
        float _Threshold,_HasPrevious;
        float4 bright(v2f_img i):SV_Target
        {
            float3 c=tex2D(_MainTex,i.uv).rgb;
            float value=max(c.r,max(c.g,c.b));return float4(c*max(0,value-_Threshold)/max(value,.0001),0);
        }
        float4 blur(v2f_img i):SV_Target
        {
            return tex2D(_MainTex,i.uv)*.4+(tex2D(_MainTex,i.uv+_Blur.xy)+tex2D(_MainTex,i.uv-_Blur.xy))*.24+(tex2D(_MainTex,i.uv+_Blur.xy*2)+tex2D(_MainTex,i.uv-_Blur.xy*2))*.06;
        }
        float4 composite(v2f_img i):SV_Target
        {
            float4 original=tex2D(_MainTex,i.uv);float2 velocity=0;float radius=0;
            float2 dataUV=i.uv;
            #if UNITY_UV_STARTS_AT_TOP
                if(_MainTex_TexelSize.y<0) dataUV.y=1-dataUV.y;
            #endif
            if(_Motion.x>0)
            {
                velocity=tex2D(_CameraMotionVectorsTexture,dataUV).rg*_Motion.x;
                #if UNITY_UV_STARTS_AT_TOP
                    if(_MainTex_TexelSize.y<0) velocity.y=-velocity.y;
                #endif
                float pixels=length(velocity/abs(_MainTex_TexelSize.xy));velocity*=min(1,_Motion.y/max(pixels,.0001));
                if(_HasPrevious>.5) {float3 difference=abs(original.rgb-tex2D(_PreviousTex,i.uv).rgb);velocity*=smoothstep(.015,.1,max(difference.r,max(difference.g,difference.b)));}
            }
            if(_Lens.z>0) {float depth=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,dataUV));radius=saturate(abs(depth-_Lens.x)/max(_Lens.y,.1))*_Lens.z;}
            float3 c=original.rgb;
            if(dot(velocity,velocity)>1e-10 || radius>.01)
            {
                c=0;
                [loop] for(int k=0;k<12;k++)
                {
                    if(k>=_Motion.z) break;float t=k/max(1,_Motion.z-1);float angle=k*2.399963;
                    float2 offset=velocity*(t-.5)+float2(cos(angle),sin(angle))*sqrt(t)*radius*abs(_MainTex_TexelSize.xy);
                    c+=tex2D(_MainTex,saturate(i.uv+offset)).rgb;
                }
                c/=_Motion.z;
            }
            if(_Post.y>0) {float2 offset=(i.uv-.5)*_Post.y*.008;c.r=lerp(c.r,tex2D(_MainTex,saturate(i.uv+offset)).r,_Post.y);c.b=lerp(c.b,tex2D(_MainTex,saturate(i.uv-offset)).b,_Post.y);}
            float3 glow=tex2D(_BloomTex,i.uv).rgb*_Post.x*_BloomTint.rgb;c+=glow;
            if(_Post.z>0) {float noise=frac(sin(dot(i.uv/abs(_MainTex_TexelSize.xy)+floor(_Time.y*60),float2(12.9898,78.233)))*43758.5453)-.5;c+=noise*_Post.z*(_Lens.w>.5?original.a:1);}
            float alpha=_Post.w>.5?max(original.a,saturate(max(glow.r,max(glow.g,glow.b)))):original.a;
            return float4(max(0,c),alpha);
        }
        ENDCG
        Pass {CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment bright
            #pragma target 3.0
        ENDCG}
        Pass {CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment blur
            #pragma target 3.0
        ENDCG}
        Pass {CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment composite
            #pragma target 3.0
        ENDCG}
    }
}
