Shader "SFP/WaterSurface"
{
    Properties
    {
        _Color ("Water Color", Color) = (0.1, 0.3, 0.6, 0.6)
        _DepthColor ("Deep Color", Color) = (0.02, 0.08, 0.2, 0.9)
        _NormalScale ("Normal Scale", Float) = 2.0
        _NormalSpeed ("Normal Speed", Float) = 0.3
        _FresnelPower ("Fresnel Power", Float) = 3.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.9
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _DepthColor;
                float _NormalScale;
                float _NormalSpeed;
                float _FresnelPower;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(dot(hash2(i), f),
                                 dot(hash2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                            lerp(dot(hash2(i + float2(0, 1)), f - float2(0, 1)),
                                 dot(hash2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(OUT.positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y * _NormalSpeed;
                float2 uv1 = IN.positionWS.xz * _NormalScale + float2(time, time * 0.7);
                float2 uv2 = IN.positionWS.xz * _NormalScale * 1.4 - float2(time * 0.8, time * 0.6);

                float n1 = noise(uv1);
                float n2 = noise(uv2);
                float3 normal = normalize(IN.normalWS + float3(n1 * 0.1, 0, n2 * 0.1));

                float fresnel = pow(1.0 - saturate(dot(normal, IN.viewDirWS)), _FresnelPower);
                float4 col = lerp(_Color, _DepthColor, fresnel * 0.5);
                col.a = lerp(_Color.a, 0.95, fresnel);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(normal, mainLight.direction));
                col.rgb *= mainLight.color * (ndl * 0.5 + 0.5);

                float3 halfDir = normalize(mainLight.direction + IN.viewDirWS);
                float spec = pow(saturate(dot(normal, halfDir)), _Smoothness * 128.0);
                col.rgb += mainLight.color * spec * 0.3;

                return col;
            }
            ENDHLSL
        }
    }
}
