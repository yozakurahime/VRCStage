Shader "Hidden/AvatarLab/Grade"
{
    Properties { _MainTex ("Image", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;float4 _MainTex_TexelSize;float4 _Look,_Style;
            float4 frag(v2f_img i):SV_Target
            {
                float4 original=tex2D(_MainTex,i.uv);float3 c=original.rgb;
                if(_Style.y>0) {float2 d=_MainTex_TexelSize.xy;c=c*(1+4*_Style.y)-_Style.y*(tex2D(_MainTex,i.uv+float2(d.x,0)).rgb+tex2D(_MainTex,i.uv-float2(d.x,0)).rgb+tex2D(_MainTex,i.uv+float2(0,d.y)).rgb+tex2D(_MainTex,i.uv-float2(0,d.y)).rgb);}
                c=max(0,c)*exp2(_Look.x);c*=float3(1+_Look.w*.12,1,1-_Look.w*.12);
                c=max(0,(c-.18)*_Look.y+.18);float lum=dot(c,float3(.2126,.7152,.0722));c=max(0,lerp(lum.xxx,c,_Look.z));
                if(_Style.z>.5) c=saturate((c*(2.51*c+.03))/(c*(2.43*c+.59)+.14));
                float2 p=(i.uv-.5)*2;c*=1-_Style.x*smoothstep(.3,1.35,length(p));
                return float4(c,original.a);
            }
            ENDCG
        }
    }
}
