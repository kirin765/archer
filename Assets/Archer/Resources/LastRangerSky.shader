Shader "Last Ranger/Evening Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.035,0.08,0.18,1)
        _Horizon ("Horizon", Color) = (0.48,0.40,0.40,1)
        _Night ("Night", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Zenith, _Horizon;
            float _Night;
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o; o.position=UnityObjectToClipPos(vertex); o.direction=vertex.xyz; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float4 frag(v2f i) : SV_Target
            {
                float3 dir=normalize(i.direction);
                float height=saturate(dir.y);
                float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(height,0.45));
                float band=pow(saturate(1-abs(dir.y-0.055)*9),3);
                sky+=float3(0.10,0.045,0.018)*band*(1-_Night);
                float2 uv=float2(atan2(dir.x,dir.z),asin(dir.y))*90;
                float2 cell=floor(uv);
                float star=step(0.992,hash(cell))*pow(saturate(1-length(frac(uv)-0.5)*3),5);
                sky+=star*smoothstep(0.12,0.4,height)*lerp(0.18,0.8,_Night);
                return float4(sky,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
