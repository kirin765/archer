Shader "Last Ranger/Evening Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.035,0.08,0.18,1)
        _Horizon ("Horizon", Color) = (0.48,0.40,0.40,1)
        _Night ("Night", Range(0,1)) = 0
        _MoonDirection ("Moon direction", Vector) = (0.183,0.343,0.917,0)
        _CloudTint ("Cloud tint", Color) = (0.86,0.62,0.52,1)
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
            float4 _Zenith, _Horizon, _MoonDirection, _CloudTint;
            float _Night;
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o; o.position=UnityObjectToClipPos(vertex); o.direction=vertex.xyz; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float vnoise(float2 p)
            {
                float2 cell=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),
                            lerp(hash(cell+float2(0,1)),hash(cell+float2(1,1)),f.x),f.y);
            }
            float fbm(float2 p)
            {
                float sum=0, amp=0.55;
                for(int oct=0;oct<4;oct++) { sum+=vnoise(p)*amp; p=p*2.13+17.7; amp*=0.5; }
                return sum;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 dir=normalize(i.direction);
                float height=saturate(dir.y);
                float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(height,0.45));
                float band=pow(saturate(1-abs(dir.y-0.055)*9),3);
                sky+=float3(0.10,0.045,0.018)*band*(1-_Night);

                // Broad cloud layers drifting slowly across the dome. Two
                // scales of noise keep them soft without a texture fetch.
                if(dir.y>0.015)
                {
                    float2 dome=dir.xz/(dir.y+0.3);
                    float t=_Time.y;
                    float layer1=fbm(dome*1.35+float2(t*0.006,t*0.002));
                    float layer2=fbm(dome*3.1+float2(-t*0.011,t*0.004)+7.3);
                    float cloud=saturate(layer1*0.62+layer2*0.38-0.52)*2.4;
                    cloud*=smoothstep(0.015,0.14,dir.y); // keep the horizon line clear
                    float3 cloudCol=lerp(_CloudTint.rgb,_CloudTint.rgb*0.22,_Night);
                    sky=lerp(sky,cloudCol,cloud*0.55);
                }

                // Moon: crisp disc with a soft halo, clearer as night falls.
                float mdot=dot(dir,normalize(_MoonDirection.xyz));
                float disc=smoothstep(0.99905,0.99945,mdot);
                float halo=pow(saturate(mdot),180.0)*0.45+pow(saturate(mdot),18.0)*0.08;
                float moonVis=lerp(0.45,1.0,_Night);
                // Faint maria so the disc is not a flat circle.
                float maria=vnoise(dir.xy*380+dir.z*130)*0.16+0.84;
                sky+=(disc*maria*float3(0.92,0.95,1.02)*1.15+halo*float3(0.5,0.62,0.9))*moonVis;

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
