Shader "Last Ranger/Fireflies"
{
    Properties
    {
        _Night ("Night", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Night;
            struct appdata
            {
                float4 vertex : POSITION;   // anchor, same for all four corners
                float2 uv : TEXCOORD0;      // billboard corner, -0.5..0.5
                float3 extra : TEXCOORD1;   // phase, size, hue mix
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float glow : TEXCOORD1;
                float hue : TEXCOORD2;
            };
            v2f vert(appdata v)
            {
                v2f o;
                float phase=v.extra.x, size=v.extra.y;
                float t=_Time.y;
                float3 anchor=v.vertex.xyz;
                anchor.x+=sin(t*0.45+phase*5.1)*0.45;
                anchor.y+=sin(t*0.80+phase*7.7)*0.28;
                anchor.z+=cos(t*0.34+phase*3.3)*0.45;
                float rate=1.2+frac(phase*0.61)*1.6;
                float pulse=0.5+0.5*sin(t*rate+phase*6.28318);
                o.glow=0.25+0.75*pulse*pulse;
                float3 camRight=UNITY_MATRIX_I_V[0].xyz;
                float3 camUp=UNITY_MATRIX_I_V[1].xyz;
                float3 world=anchor+(camRight*v.uv.x+camUp*v.uv.y)*size;
                o.position=mul(UNITY_MATRIX_VP,float4(world,1));
                o.uv=v.uv;
                o.hue=v.extra.z;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float d=length(i.uv);
                float alpha=smoothstep(0.5,0.08,d);
                float3 warm=float3(0.95,1.0,0.45);
                float3 cool=float3(0.55,1.0,0.65);
                float3 color=lerp(warm,cool,i.hue)*1.7;
                // Additive: rgb carries the falloff, twinkle and night gate.
                return float4(color*alpha*i.glow*_Night,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
