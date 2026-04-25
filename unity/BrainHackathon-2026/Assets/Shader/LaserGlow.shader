Shader "Custom/LaserGlow"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0, 0.8, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _GlowIntensity("Glow Intensity", Range(0, 5)) = 2.0
        _GlowFalloff("Glow Falloff", Range(0.1, 10)) = 2.0
        _EdgeGlow("Edge Glow", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _GlowIntensity;
                float _GlowFalloff;
                float _EdgeGlow;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                // Create radial distance from center for glow effect
                float2 center = float2(0.5, 0.5);
                float dist = distance(IN.uv, center);

                // Core glow (bright center)
                float coreGlow = 1.0 - smoothstep(0.0, 0.3, dist);
                coreGlow = pow(coreGlow, _GlowFalloff);

                // Edge glow (soft halo)
                float edgeGlow = 1.0 - smoothstep(0.3, 1.0, dist);
                edgeGlow = pow(edgeGlow, _GlowFalloff * 0.5) * _EdgeGlow;

                // Combine glows
                float totalGlow = saturate(coreGlow + edgeGlow);

                // Apply intensity and color
                half4 finalColor = _BaseColor * texColor;
                finalColor.rgb *= totalGlow * _GlowIntensity;
                finalColor.a *= totalGlow;

                return finalColor;
            }
            ENDHLSL
        }
    }
}
