Shader "Custom/TransparentWithDepth"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)
        _Alpha("Opacity", Range(0, 1)) = 1.0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.2
        [Toggle] _Wireframe("Wireframe", Float) = 0
        _WireframeColor("Wireframe Color", Color) = (0,1,0,1)
        _WireframeThickness("Wireframe Thickness", Range(0, 1)) = 0.1
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        // Pass 1: Write to depth buffer only (invisible)
        Pass
        {
            Name "DepthWrite"
            ColorMask 0
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
        
        // Pass 2: Render with transparency + lighting
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off   
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom
            #pragma target 4.0

            // URP lighting keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalOS     : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 barycentric  : TEXCOORD1;
                float3 normalWS     : TEXCOORD2;
                float3 positionWS   : TEXCOORD3;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Alpha;
                float _Smoothness;
                float _AmbientStrength;
                float _Wireframe;
                float4 _WireframeColor;
                float _WireframeThickness;
            CBUFFER_END
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.barycentric = 0;
                return OUT;
            }
            
            [maxvertexcount(3)]
            void geom(triangle Varyings IN[3], inout TriangleStream<Varyings> triStream)
            {
                for (int i = 0; i < 3; i++)
                {
                    Varyings OUT = IN[i];
                    OUT.barycentric = float3(0, 0, 0);
                    OUT.barycentric[i] = 1;
                    triStream.Append(OUT);
                }
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 color = texColor * _Color;

                // --- Lighting ---
                float3 normalWS = normalize(IN.normalWS);

                // Main directional light
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = mainLight.color * mainLight.shadowAttenuation * NdotL;

                // Simple Blinn-Phong specular
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 halfDir   = normalize(mainLight.direction + viewDirWS);
                float NdotH      = saturate(dot(normalWS, halfDir));
                float specPower  = exp2(_Smoothness * 10.0 + 1.0);
                float3 specular  = mainLight.color * mainLight.shadowAttenuation * pow(NdotH, specPower);

                // Additional lights
                int additionalLightCount = GetAdditionalLightsCount();
                for (int i = 0; i < additionalLightCount; i++)
                {
                    Light light = GetAdditionalLight(i, IN.positionWS, half4(1,1,1,1));
                    float addNdotL = saturate(dot(normalWS, light.direction));
                    diffuse  += light.color * light.shadowAttenuation * light.distanceAttenuation * addNdotL;

                    float3 addHalf = normalize(light.direction + viewDirWS);
                    float addNdotH = saturate(dot(normalWS, addHalf));
                    specular += light.color * light.shadowAttenuation * light.distanceAttenuation * pow(addNdotH, specPower);
                }

                // Ambient
                float3 ambient = SampleSH(normalWS) * _AmbientStrength;

                // Combine
                color.rgb *= (ambient + diffuse);
                color.rgb += specular * 0.5;
                color.a   *= _Alpha;
                
                // --- Wireframe ---
                if (_Wireframe > 0.5)
                {
                    float3 barys = IN.barycentric;
                    float minDist = min(barys.x, min(barys.y, barys.z));
                    float edge = 1.0 - smoothstep(_WireframeThickness - 0.005, _WireframeThickness + 0.005, minDist);
                    
                    color.rgb = lerp(color.rgb, _WireframeColor.rgb, edge);
                    color.a   = lerp(color.a,   _WireframeColor.a,   edge);
                }
                
                return color;
            }
            ENDHLSL
        }
    }
}
