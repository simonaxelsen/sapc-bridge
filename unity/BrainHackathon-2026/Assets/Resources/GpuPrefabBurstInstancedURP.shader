Shader "Custom/URP/GpuPrefabBurstInstancedURP"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _SrcBlend("Src Blend", Float) = 1
        _DstBlend("Dst Blend", Float) = 0
        _ZWrite("Z Write", Float) = 1
        _Cull("Cull", Float) = 2
        _OpaqueTexture("Alpha Clip Enabled", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct InstanceState
            {
                float3 position;
                float scale;
                float3 velocity;
                float age;
                float3 burstDirection;
                float seed;
            };

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            StructuredBuffer<InstanceState> _States;
            StructuredBuffer<float4x4> _AnimationMatrices;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4x4 _RootMatrix;
                float4x4 _PartMatrix;
                float _Cutoff;
                float _OpaqueTexture;
                float _UseBakedAnimation;
                int _AnimationPartIndex;
                int _AnimationPartCount;
                int _AnimationFrameCount;
                float _AnimationFrameFloat;
                float _AnimationPhaseJitter;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                InstanceState state = _States[input.instanceID];

                float4x4 partMatrix = _PartMatrix;
                if (_UseBakedAnimation > 0.5 && _AnimationFrameCount > 0 && _AnimationPartCount > 0)
                {
                    float phaseOffset = frac(state.seed * _AnimationPhaseJitter);
                    float frameFloat = frac(_AnimationFrameFloat + phaseOffset) * _AnimationFrameCount;
                    int frameIndex = min((int)floor(frameFloat), _AnimationFrameCount - 1);
                    int matrixIndex = frameIndex * _AnimationPartCount + _AnimationPartIndex;
                    partMatrix = _AnimationMatrices[matrixIndex];
                }

                float4 scaledLocal = float4(input.positionOS * state.scale, 1.0);
                float4 partLocal = mul(partMatrix, scaledLocal);
                partLocal.xyz += state.position;
                float3 worldPosition = mul(_RootMatrix, partLocal).xyz;
                float3 worldNormal = normalize(mul((float3x3)_RootMatrix, mul((float3x3)partMatrix, input.normalOS)));

                Varyings output;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS = worldPosition;
                output.normalWS = worldNormal;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_OpaqueTexture > 0.5)
                {
                    clip(color.a - _Cutoff);
                }

                half3 normalWS = SafeNormalize(input.normalWS);
                half3 lighting = SampleSH(normalWS);

                Light mainLight = GetMainLight();
                lighting += mainLight.color * saturate(dot(normalWS, mainLight.direction));

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalLightCount; lightIndex++)
                {
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS);
                    lighting += additionalLight.color * saturate(dot(normalWS, additionalLight.direction)) * additionalLight.distanceAttenuation;
                }
                #endif

                color.rgb *= lighting;
                return color;
            }
            ENDHLSL
        }
    }
}
