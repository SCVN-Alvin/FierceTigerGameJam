Shader "Custom/Toon_URP_Outline"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _HColor ("Highlight Color", Color) = (1,1,1,1)
        _SColor ("Shadow Color", Color) = (0.2,0.2,0.2,1)

        [Toggle(SHADOW_TEX_ON)] _UseShadowTex ("Use Shadow Texture", Float) = 0
        _ShadowTex ("Shadow Tex", 2D) = "white" {}
        _ShadowTexStrength ("Shadow Tex Strength", Range(0,1)) = 1

        _RampThreshold ("Ramp Threshold", Range(0.01,1)) = 0.75
        _RampSmoothing ("Ramp Smoothing", Range(0,1)) = 0.12

        [Toggle(MOBILE_MODE)] _MobileMode ("Mobile Mode", Float) = 0

        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1

        [Toggle(_METALLICGLOSSMAP)] _UseMetallicMap ("Use Metallic Map", Float) = 0
        _MetallicGlossMap ("Metallic R Smoothness A", 2D) = "black" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _SmoothnessColor ("Smoothness Color", Color) = (1,1,1,1)

        [Toggle(_EMISSION)] _UseEmission ("Use Emission", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Toggle(RIM_ON)] _UseRim ("Use Rim", Float) = 0
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimMin ("Rim Min", Range(0,2)) = 0.4
        _RimMax ("Rim Max", Range(0,2)) = 1.0

        [Toggle(RIM_LIGHTMASK)] _UseRimLightMask ("Use Rim Light Mask", Float) = 1
        _RimLightMin ("Rim Light Min", Range(0,1)) = 0.15
        _RimLightMax ("Rim Light Max", Range(0,1)) = 0.55

        [Toggle(MATCAP_ON)] _UseMatCap ("Use MatCap", Float) = 0
        [NoScaleOffset] _MatCapTex ("MatCap Tex", 2D) = "black" {}
        _MatCapColor ("MatCap Color", Color) = (1,1,1,1)
        _MatCapIntensity ("MatCap Intensity", Range(0,2)) = 1

        _IndirectIntensity ("Indirect Intensity", Range(0,1)) = 1

        [Toggle(USE_LIGHTPROBES)] _UseLightProbes ("Use Light Probes", Float) = 0

        // ✅ SSAO
        [Toggle(_SSAO_ON)] _SSAO ("Screen Space AO", Float) = 1

        [Toggle(MOBILE_FAKE_AO)] _MobileFakeAO ("Mobile Fake AO", Float) = 1
        _MobileAOIntensity ("AO Intensity", Range(0,2)) = 0.8
        _MobileAOPower ("AO Power", Range(0.5,6)) = 2.0
        _MobileAOUpBias ("AO Up Bias", Range(-1,1)) = 0.0
        _MobileAOMin ("AO Min", Range(0,1)) = 0.35

        // ✅ Outline
        [Toggle(OUTLINE_ON)] _UseOutline ("Outline", Float) = 0
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0,0.25)) = 0.006

        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows ("Receive Shadows", Float) = 1

        [Toggle(_ALPHATEST_ON)] _UseAlphaTest ("Alpha Clip", Float) = 0
        _Cutoff ("Cutoff", Range(0,1)) = 0.5

        [Enum(Back,2,Front,1,Off,0)] _Cull ("Cull", Float) = 2
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Cull [_Cull]
        ZWrite [_ZWrite]

        // =========================================================
        // ForwardLit (Forward + Forward+ + Multiple Lights)
        // =========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertFwd
            #pragma fragment FragFwd

            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fog

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            // ✅ IMPORTANT: support both per-vertex and per-pixel additional lights
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            // ✅ Forward+ variant
            #pragma multi_compile _ _FORWARD_PLUS

            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #pragma shader_feature_local SHADOW_TEX_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _METALLICGLOSSMAP
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local RIM_ON
            #pragma shader_feature_local RIM_LIGHTMASK
            #pragma shader_feature_local MATCAP_ON
            #pragma shader_feature_local MOBILE_MODE
            #pragma shader_feature_local MOBILE_FAKE_AO
            #pragma shader_feature_local USE_LIGHTPROBES
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _SSAO_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

            TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_ShadowTex);          SAMPLER(sampler_ShadowTex);
            TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_MatCapTex);          SAMPLER(sampler_MatCapTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;

                half4 _BaseColor,_HColor,_SColor;
                half  _ShadowTexStrength,_RampThreshold,_RampSmoothing;

                half  _BumpScale;

                half  _Metallic,_Smoothness;
                half4 _SmoothnessColor;

                half4 _EmissionColor;

                half4 _RimColor;
                half  _RimMin,_RimMax,_RimLightMin,_RimLightMax;

                half4 _MatCapColor;
                half  _MatCapIntensity;

                half  _IndirectIntensity;

                half  _MobileAOIntensity,_MobileAOPower,_MobileAOUpBias,_MobileAOMin;

                half  _OutlineWidth;
                half4 _OutlineColor;

                half  _Cutoff;
            CBUFFER_END

            inline half ToonRamp(half ndl)
            {
                half e0 = _RampThreshold - _RampSmoothing * 0.5h;
                half e1 = _RampThreshold + _RampSmoothing * 0.5h;
                return smoothstep(e0, e1, ndl);
            }

            inline half MobileFakeAO_FromNormalWS(half3 normalWS)
            {
                half ndotu = dot(normalize(normalWS), half3(0,1,0));
                ndotu = ndotu * 0.5h + 0.5h;
                ndotu = saturate(ndotu + _MobileAOUpBias);
                half ao = pow(ndotu, _MobileAOPower);
                ao = lerp(1.0h, ao, saturate(_MobileAOIntensity));
                ao = max(ao, _MobileAOMin);
                return ao;
            }

            inline half3 GetTintedShadowColor(float2 uv)
            {
                half3 shadowCol = _SColor.rgb;
                #if defined(SHADOW_TEX_ON)
                    half3 st = SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, uv).rgb;
                    shadowCol = lerp(shadowCol, st * shadowCol, saturate(_ShadowTexStrength));
                #endif
                return shadowCol;
            }

            inline half3 SampleNormalWS(float2 uv, half3 normalWS, half4 tangentWS)
            {
                #if defined(_NORMALMAP)
                    half4 nTex = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv);
                    half3 nTS  = UnpackNormalScale(nTex, _BumpScale);
                    half3x3 tbn = CreateTangentToWorld(normalWS, tangentWS.xyz, tangentWS.w);
                    return normalize(TransformTangentToWorld(nTS, tbn));
                #else
                    return normalize(normalWS);
                #endif
            }

            inline half3 MatCapColorFunc(half3 nWS)
            {
                float3 nVS = mul((float3x3)GetWorldToViewMatrix(), nWS);
                float2 muv = nVS.xy * 0.5 + 0.5;
                half3 mc  = SAMPLE_TEXTURE2D(_MatCapTex, sampler_MatCapTex, muv).rgb;
                return mc * _MatCapColor.rgb * _MatCapIntensity;
            }

            inline half SampleSSAO(float4 positionCS)
            {
                #if defined(_SSAO_ON) && defined(_SCREEN_SPACE_OCCLUSION)
                    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
                    return SAMPLE_TEXTURE2D_X(_ScreenSpaceOcclusionTexture, sampler_ScreenSpaceOcclusionTexture, uv).r;
                #else
                    return 1.0h;
                #endif
            }

            inline half3 ComputeIndirect(half3 nWS, half occSSAO)
            {
                half3 indirect = SampleSH(nWS) * _IndirectIntensity;
                indirect *= occSSAO;

                #if defined(MOBILE_FAKE_AO)
                    indirect *= MobileFakeAO_FromNormalWS(nWS);
                #endif
                return indirect;
            }

            struct AttributesFwd
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsFwd
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                half3  normalWS    : TEXCOORD2;
                half4  tangentWS   : TEXCOORD3;
                half3  viewDirWS   : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                half   fogFactor   : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsFwd VertFwd(AttributesFwd v)
            {
                VaryingsFwd o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS   = nrm.normalWS;

                half tangentSign = (half)(v.tangentOS.w * GetOddNegativeScale());
                o.tangentWS = half4(nrm.tangentWS.xyz, tangentSign);

                o.viewDirWS = normalize(GetWorldSpaceViewDir(o.positionWS));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.shadowCoord = TransformWorldToShadowCoord(o.positionWS);
                o.fogFactor = (half)ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 FragFwd(VaryingsFwd i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 albedo4 = baseSample * _BaseColor;

                #if defined(_ALPHATEST_ON)
                    clip(albedo4.a - _Cutoff);
                #endif

                half3 nWS = SampleNormalWS(i.uv, i.normalWS, i.tangentWS);
                half3 vWS = normalize(i.viewDirWS);

                half occSSAO = SampleSSAO(i.positionCS);

                Light mainLight;
                #if defined(_RECEIVE_SHADOWS_OFF)
                    mainLight = GetMainLight();
                    mainLight.shadowAttenuation = 1.0h;
                #else
                    mainLight = GetMainLight(i.shadowCoord);
                #endif

                half3 L = normalize(mainLight.direction);
                half ndl = saturate(dot(nWS, L));

                half ramp = ToonRamp(ndl);
                half3 shadowCol = GetTintedShadowColor(i.uv);
                half3 rampColMain = lerp(shadowCol, _HColor.rgb, ramp);

                half attenMain = (half)mainLight.distanceAttenuation * (half)mainLight.shadowAttenuation;
                rampColMain = lerp(shadowCol, rampColMain, attenMain);

                half metallic = _Metallic;
                half smoothness = _Smoothness;

                #if defined(_METALLICGLOSSMAP) && !defined(MOBILE_MODE)
                    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                    metallic   = saturate(mg.r) * _Metallic;
                    smoothness = saturate(mg.a) * _Smoothness;
                #endif

                half3 F0 = lerp(0.12h.xxx, albedo4.rgb, metallic);
                half diffuseEnergy = 1.0h - 0.6h * metallic;

                half3 indirect = ComputeIndirect(nWS, occSSAO);

                half3 col =
                    (rampColMain * albedo4.rgb) * diffuseEnergy * mainLight.color.rgb +
                    (indirect * albedo4.rgb) * diffuseEnergy;

                #if !defined(MOBILE_MODE)
                {
                    half3 H = normalize(L + vWS);
                    half ndh = saturate(dot(nWS, H));
                    half rough = 1.0h - smoothness;
                    half expVal = lerp(128.0h, 8.0h, rough);
                    half s = pow(ndh, expVal) * attenMain;

                    half directGate = smoothstep(0.05h, 0.25h, ndl) * attenMain;
                    half smoothBoost = lerp(0.6h, 1.6h, smoothness);
                    half specGain    = lerp(1.0h, 15.0h, smoothness);

                    col += s * F0 * smoothBoost * specGain * directGate * _SmoothnessColor.rgb * mainLight.color.rgb;
                }
                #endif

                #if defined(RIM_ON)
                {
                    half rimN = 1.0h - saturate(dot(nWS, vWS));
                    half rimMask = smoothstep(_RimMin, _RimMax, rimN);

                    #if defined(RIM_LIGHTMASK)
                        half litMain = ndl * attenMain;
                        rimMask *= smoothstep(_RimLightMin, _RimLightMax, litMain);
                    #endif

                    col += rimMask * _RimColor.rgb;
                }
                #endif

                #if defined(MATCAP_ON) && !defined(MOBILE_MODE)
                    col += MatCapColorFunc(nWS);
                #endif

                #if defined(_EMISSION)
                {
                    half3 emTex = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb;
                    col += emTex * _EmissionColor.rgb;
                }
                #endif

                // ✅ Additional lights (works in Forward and Forward+)
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    uint count = GetAdditionalLightsCount();
                    for (uint li = 0u; li < count; li++)
                    {
                        Light addL = GetAdditionalLight(li, i.positionWS);

                        half attenA = (half)addL.distanceAttenuation;
                        attenA = smoothstep(0.0h, 1.0h, attenA);

                        #if !defined(_RECEIVE_SHADOWS_OFF)
                            attenA *= (half)addL.shadowAttenuation;
                        #endif

                        half3 La = normalize(addL.direction);
                        half ndlA = saturate(dot(nWS, La));

                        half rampA = ToonRamp(ndlA);
                        rampA *= attenA;

                        half3 addToon = _HColor.rgb * rampA;
                        col += (addToon * albedo4.rgb) * addL.color.rgb * diffuseEnergy;

                        #if !defined(MOBILE_MODE)
                        {
                            half3 Ha = normalize(La + vWS);
                            half ndhA = saturate(dot(nWS, Ha));
                            half roughA = 1.0h - smoothness;
                            half expValA = lerp(128.0h, 8.0h, roughA);
                            half sA = pow(ndhA, expValA) * attenA;

                            half directGateA = smoothstep(0.05h, 0.25h, ndlA) * attenA;
                            half smoothBoostA = lerp(0.6h, 1.6h, smoothness);
                            half specGainA    = lerp(1.0h, 15.0h, smoothness);

                            col += sA * F0 * smoothBoostA * specGainA * directGateA * _SmoothnessColor.rgb * addL.color.rgb;
                        }
                        #endif
                    }
                }
                #endif

                col = MixFog(col, i.fogFactor);
                return half4(col, albedo4.a);
            }
            ENDHLSL
        }

        // =========================================================
        // Outline (SRP Batcher SAFE)
        // =========================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #pragma shader_feature_local OUTLINE_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ✅ IMPORTANT: نفس CBUFFER ديال ForwardLit (نفس الترتيب)
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;

                half4 _BaseColor,_HColor,_SColor;
                half  _ShadowTexStrength,_RampThreshold,_RampSmoothing;

                half  _BumpScale;

                half  _Metallic,_Smoothness;
                half4 _SmoothnessColor;

                half4 _EmissionColor;

                half4 _RimColor;
                half  _RimMin,_RimMax,_RimLightMin,_RimLightMax;

                half4 _MatCapColor;
                half  _MatCapIntensity;

                half  _IndirectIntensity;

                half  _MobileAOIntensity,_MobileAOPower,_MobileAOUpBias,_MobileAOMin;

                half  _OutlineWidth;
                half4 _OutlineColor;

                half  _Cutoff;
            CBUFFER_END

            struct AttributesO
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsO
            {
                float4 positionCS : SV_POSITION;
                half   fogFactor  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsO VertOutline(AttributesO v)
            {
                VaryingsO o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);

                #if defined(OUTLINE_ON)
                    float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                    posWS += normalize(nWS) * (float)_OutlineWidth;
                #endif

                o.positionCS = TransformWorldToHClip(posWS);
                o.fogFactor  = (half)ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 FragOutline(VaryingsO i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                #if defined(OUTLINE_ON)
                    half3 col = (half3)_OutlineColor.rgb;
                    col = MixFog(col, i.fogFactor);
                    return half4(col, 1);
                #else
                    clip(-1);
                    return 0;
                #endif
            }
            ENDHLSL
        }

        // =========================================================
        // DepthOnly
        // =========================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
            CBUFFER_END

            struct AttributesDO { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct VaryingsDO   { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            VaryingsDO DepthOnlyVertex(AttributesDO v)
            {
                VaryingsDO o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 DepthOnlyFragment(VaryingsDO i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                    half4 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                    clip(a.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // =========================================================
        // DepthNormals (for SSAO)
        // =========================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DNVertex
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _BumpScale;
            CBUFFER_END

            struct ADN
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 tangentWS  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VDN DNVertex(ADN v)
            {
                VDN o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);

                o.positionCS = pos.positionCS;
                o.normalWS   = nrm.normalWS;

                half tangentSign = (half)(v.tangentOS.w * GetOddNegativeScale());
                o.tangentWS = half4(nrm.tangentWS.xyz, tangentSign);

                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            inline float3 DN_ApplyNormalMap(VDN i, float3 nWS)
            {
                #if defined(_NORMALMAP)
                    half4 nTex = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv);
                    half3 nTS  = UnpackNormalScale(nTex, _BumpScale);
                    half3x3 tbn = CreateTangentToWorld(nWS, i.tangentWS.xyz, i.tangentWS.w);
                    return normalize(TransformTangentToWorld(nTS, tbn));
                #else
                    return normalize(nWS);
                #endif
            }

            half4 DNFrag(VDN i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                #if defined(_ALPHATEST_ON)
                    half4 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                    clip(a.a - _Cutoff);
                #endif

                float3 nWS = NormalizeNormalPerPixel(i.normalWS);
                nWS = DN_ApplyNormalMap(i, nWS);

                float2 oct = PackNormalOctRectEncode(nWS);
                return half4(oct.x, oct.y, 0, 0);
            }
            ENDHLSL
        }

        // =========================================================
        // ShadowCaster (Unity 2023 + Unity 6000 compatible)
        // =========================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex VertSC
            #pragma fragment FragSC
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ✅ Define ONLY for older Unity/URP (Unity 2023.x). In Unity 6000+ it's already defined.
            #if defined(UNITY_VERSION) && (UNITY_VERSION < 600000)
                inline float  LerpWhiteTo(float  x, float  t) { return lerp(1.0, x, t); }
                inline float3 LerpWhiteTo(float3 x, float  t) { return lerp(float3(1,1,1), x, t); }
                inline half   LerpWhiteTo(half   x, half   t) { return lerp(half(1.0), x, t); }
                inline half3  LerpWhiteTo(half3  x, half   t) { return lerp(half3(1,1,1), x, t); }
            #endif

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
            CBUFFER_END

            struct AttributesSC
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsSC
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsSC VertSC(AttributesSC v)
            {
                VaryingsSC o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);

                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _MainLightPosition.xyz));

                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                o.positionCS = posCS;
                return o;
            }

            half4 FragSC(VaryingsSC i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                    half4 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                    clip(a.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}