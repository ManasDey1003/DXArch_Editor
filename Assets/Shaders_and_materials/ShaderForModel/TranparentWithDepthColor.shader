Shader "Custom/TransparentWithDepthColor"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1, 1, 1, 1)
        _Alpha("Opacity", Range(0, 1)) = 1.0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.2

        [Toggle]_DepthColor("Depth Coloring", Float) = 0
        _DepthNear("Depth Near Distance", Float) = 0.0
        _DepthFar("Depth Far Distance", Float) = 100.0
        _DepthColorNear("Near Color", Color) = (1, 0, 0, 1)
        _DepthColorFar("Far Color", Color) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // Pass 1: Forward lighting
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _Alpha;
                float  _Smoothness;
                float  _AmbientStrength;
                float  _DepthColor;
                float  _DepthNear;
                float  _DepthFar;
                float4 _DepthColorNear;
                float4 _DepthColorFar;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs  = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 color    = texColor * _Color;

                float3 normalWS = normalize(IN.normalWS);
                if (!isFrontFace) normalWS = -normalWS;

                // Shadow coord for receiving shadows
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight    = GetMainLight(shadowCoord);

                float  NdotL   = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = mainLight.color * mainLight.shadowAttenuation * NdotL;

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 halfDir   = normalize(mainLight.direction + viewDirWS);
                float  NdotH     = saturate(dot(normalWS, halfDir));
                float  specPower = exp2(_Smoothness * 10.0 + 1.0);
                float3 specular  = mainLight.color * mainLight.shadowAttenuation * pow(NdotH, specPower);

                int additionalLightCount = GetAdditionalLightsCount();
                for (int i = 0; i < additionalLightCount; i++)
                {
                    Light light    = GetAdditionalLight(i, IN.positionWS, half4(1, 1, 1, 1));
                    float addNdotL = saturate(dot(normalWS, light.direction));
                    diffuse       += light.color * light.distanceAttenuation * light.shadowAttenuation * addNdotL;

                    float3 addHalf  = normalize(light.direction + viewDirWS);
                    float  addNdotH = saturate(dot(normalWS, addHalf));
                    specular       += light.color * light.distanceAttenuation * light.shadowAttenuation * pow(addNdotH, specPower);
                }

                float3 ambient = SampleSH(normalWS) * _AmbientStrength;

                color.rgb *= (ambient + diffuse);
                color.rgb += specular * 0.5;

                if (_DepthColor > 0.5)
                {
                    float  dist      = length(IN.positionWS - _WorldSpaceCameraPos);
                    float  t         = saturate((dist - _DepthNear) / max(_DepthFar - _DepthNear, 0.0001));
                    float4 depthTint = lerp(_DepthColorNear, _DepthColorFar, t);
                    color.rgb        = lerp(color.rgb, depthTint.rgb, depthTint.a);
                }

                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // Pass 2: Depth prepass
            Pass
            {
                Name "DepthOnly"
                Tags { "LightMode" = "DepthOnly" }
                ColorMask 0
                ZWrite On
                ZTest LEqual
                Cull Off

                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

                struct Attributes { float4 positionOS : POSITION; };
                struct Varyings   { float4 positionHCS : SV_POSITION; };

                Varyings vert(Attributes IN)
                {
                    Varyings OUT;
                    OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                    return OUT;
                }

                half4 frag(Varyings IN) : SV_Target { return 0; }
                ENDHLSL
            }

        // Pass 3: Shadow casting
        // This makes your objects cast shadows onto other objects
        /* Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Required by URP shadow system
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                // ApplyShadowBias offsets the shadow to prevent shadow acne
                // URP provides this in Shadows.hlsl
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                OUT.positionHCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDir)
                );

                // Clamp to near plane to avoid shadow holes
                #if UNITY_REVERSED_Z
                    OUT.positionHCS.z = min(OUT.positionHCS.z,
                        OUT.positionHCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionHCS.z = max(OUT.positionHCS.z,
                        OUT.positionHCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        } */
    }
}
