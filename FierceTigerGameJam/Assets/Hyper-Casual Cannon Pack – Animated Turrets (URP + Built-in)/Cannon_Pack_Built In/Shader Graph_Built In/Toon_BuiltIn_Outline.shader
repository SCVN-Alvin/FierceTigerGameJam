Shader "Custom/Toon_BuiltIn_Outline"
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

        // IMPORTANT: Light probes toggle (ON breaks instancing)
        [Toggle(USE_LIGHTPROBES)] _UseLightProbes ("Use Light Probes", Float) = 0

        [Toggle(MOBILE_FAKE_AO)] _MobileFakeAO ("Mobile Fake AO", Float) = 1
        _MobileAOIntensity ("AO Intensity", Range(0,2)) = 0.8
        _MobileAOPower ("AO Power", Range(0.5,6)) = 2.0
        _MobileAOUpBias ("AO Up Bias", Range(-1,1)) = 0.0
        _MobileAOMin ("AO Min", Range(0,1)) = 0.35

        [Toggle(OUTLINE_ON)] _UseOutline ("Use Outline", Float) = 0
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
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Cull [_Cull]
        ZWrite [_ZWrite]

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        #include "AutoLight.cginc"

        sampler2D _BaseMap; float4 _BaseMap_ST;
        sampler2D _ShadowTex;
        sampler2D _BumpMap;
        sampler2D _MetallicGlossMap;
        sampler2D _EmissionMap;
        sampler2D _MatCapTex;

        fixed4 _BaseColor, _HColor, _SColor;
        float _ShadowTexStrength;
        float _RampThreshold, _RampSmoothing;
        float _BumpScale;
        float _Metallic, _Smoothness;
        fixed4 _SmoothnessColor;
        fixed4 _EmissionColor;

        fixed4 _RimColor;
        float _RimMin, _RimMax, _RimLightMin, _RimLightMax;

        fixed4 _MatCapColor;
        float _MatCapIntensity;

        float _IndirectIntensity;

        float _MobileAOIntensity, _MobileAOPower, _MobileAOUpBias, _MobileAOMin;

        float4 _OutlineColor;
        float _OutlineWidth;

        float _Cutoff;

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
            ndotu = saturate(ndotu + (half)_MobileAOUpBias);
            half ao = pow(ndotu, (half)_MobileAOPower);
            ao = lerp(1.0h, ao, saturate((half)_MobileAOIntensity));
            ao = max(ao, (half)_MobileAOMin);
            return ao;
        }

        inline half3 GetTintedShadowColor(float2 uv)
        {
            half3 shadowCol = _SColor.rgb;
            #if defined(SHADOW_TEX_ON)
            half3 st = tex2D(_ShadowTex, uv).rgb;
            half3 tinted = st * shadowCol;
            shadowCol = lerp(shadowCol, tinted, saturate(_ShadowTexStrength));
            #endif
            return shadowCol;
        }

        inline half3 GetLightDirWS(float3 posWS)
        {
            if (_WorldSpaceLightPos0.w == 0.0) return normalize(_WorldSpaceLightPos0.xyz);
            return normalize(_WorldSpaceLightPos0.xyz - posWS);
        }

        struct appdata
        {
            float4 vertex  : POSITION;
            float3 normal  : NORMAL;
            float4 tangent : TANGENT;
            float2 uv      : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos      : SV_POSITION;
            float2 uv       : TEXCOORD0;
            float3 posWS    : TEXCOORD1;
            float3 nWS      : TEXCOORD2;
            float3 tWS      : TEXCOORD3;
            float3 bWS      : TEXCOORD4;
            float3 viewWS   : TEXCOORD5;

            UNITY_SHADOW_COORDS(6)
            UNITY_FOG_COORDS(7)
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);

            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv  = TRANSFORM_TEX(v.uv, _BaseMap);

            float3 nWS = UnityObjectToWorldNormal(v.normal);
            float3 tWS = UnityObjectToWorldDir(v.tangent.xyz);
            float3 bWS = cross(nWS, tWS) * (v.tangent.w * unity_WorldTransformParams.w);

            o.posWS  = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.nWS    = normalize(nWS);
            o.tWS    = normalize(tWS);
            o.bWS    = normalize(bWS);
            o.viewWS = normalize(_WorldSpaceCameraPos.xyz - o.posWS);

            UNITY_TRANSFER_SHADOW(o, o.posWS);
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        inline float3 ApplyNormalMap(v2f i, float3 nWS)
        {
            #if defined(_NORMALMAP)
            half4 nTex = tex2D(_BumpMap, i.uv);
            half3 nTS  = UnpackNormal(nTex);
            nTS.xy *= _BumpScale;
            float3x3 tbn = float3x3(normalize(i.tWS), normalize(i.bWS), normalize(i.nWS));
            return normalize(mul(nTS, tbn));
            #else
            return normalize(nWS);
            #endif
        }

        inline float3 MatCapColorFunc(float3 nWS)
        {
            float3 nVS = mul((float3x3)UNITY_MATRIX_V, nWS);
            float2 muv = nVS.xy * 0.5 + 0.5;
            float3 mc  = tex2D(_MatCapTex, muv).rgb;
            return mc * _MatCapColor.rgb * _MatCapIntensity;
        }

        fixed4 fragBase(v2f i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);

            fixed4 baseSample = tex2D(_BaseMap, i.uv);
            fixed4 albedo = baseSample * _BaseColor;

            #if defined(_ALPHATEST_ON)
            clip(albedo.a - _Cutoff);
            #endif

            float3 nWS = ApplyNormalMap(i, i.nWS);
            float3 vWS = normalize(i.viewWS);

            half atten = 1.0h;
            #if !defined(_RECEIVE_SHADOWS_OFF)
            UNITY_LIGHT_ATTENUATION(attenTmp, i, i.posWS);
            atten = attenTmp;
            #endif

            half3 L = GetLightDirWS(i.posWS);
            half3 lightCol = _LightColor0.rgb;

            half ndl  = saturate(dot(nWS, L));
            half ramp = ToonRamp(ndl);

            half3 shadowCol = GetTintedShadowColor(i.uv);
            half3 rampCol   = lerp(shadowCol, _HColor.rgb, ramp);
            rampCol         = lerp(shadowCol, rampCol, atten);

            half lit = ndl * atten;
            half directGate = smoothstep(0.05h, 0.25h, ndl) * atten;

            half metallic = _Metallic;
            half smoothness = _Smoothness;

            #if defined(_METALLICGLOSSMAP) && !defined(MOBILE_MODE)
            half4 mg = tex2D(_MetallicGlossMap, i.uv);
            metallic   = saturate(mg.r) * _Metallic;
            smoothness = saturate(mg.a) * _Smoothness;
            #endif

            half3 F0 = lerp(0.12h.xxx, albedo.rgb, metallic);
            half diffuseEnergy = 1.0h - 0.6h * metallic;

            half smoothBoost = lerp(0.6h, 1.6h, smoothness);
            half specGain    = lerp(1.0h, 15.0h, smoothness);

            half3 indirect = 0;
            #if defined(USE_LIGHTPROBES)
            indirect = ShadeSH9(half4(nWS, 1.0h)) * _IndirectIntensity;
            #else
            indirect = UNITY_LIGHTMODEL_AMBIENT.rgb * _IndirectIntensity;
            #endif

            #if defined(MOBILE_FAKE_AO)
            indirect *= MobileFakeAO_FromNormalWS((half3)nWS);
            #endif

            half3 directDiffuse = (rampCol * albedo.rgb) * diffuseEnergy;
            half3 diffuse = directDiffuse * lightCol + (indirect * albedo.rgb) * diffuseEnergy;

            half3 spec = 0;
            #if !defined(MOBILE_MODE)
            float3 h = normalize(L + vWS);
            half ndh = saturate(dot(nWS, h));
            half rough = 1.0h - smoothness;
            half expVal = lerp(128.0h, 8.0h, rough);
            half s = pow(ndh, expVal) * atten;
            spec += s * F0 * smoothBoost * specGain * directGate * _SmoothnessColor.rgb * lightCol;
            #endif

            half3 rim = 0;
            #if defined(RIM_ON)
            half rimN = 1.0h - saturate(dot(nWS, vWS));
            half rimMask = smoothstep(_RimMin, _RimMax, rimN);
            #if defined(RIM_LIGHTMASK)
            half gate = smoothstep(_RimLightMin, _RimLightMax, lit);
            rimMask *= gate;
            #endif
            rim += rimMask * _RimColor.rgb;
            #endif

            half3 matcap = 0;
            #if defined(MATCAP_ON) && !defined(MOBILE_MODE)
            matcap = MatCapColorFunc(nWS);
            #endif

            half3 emis = 0;
            #if defined(_EMISSION)
            half3 emTex = tex2D(_EmissionMap, i.uv).rgb;
            emis = emTex * _EmissionColor.rgb;
            #endif

            half3 col = diffuse + spec + rim + matcap + emis;
            UNITY_APPLY_FOG(i.fogCoord, col);
            return fixed4(col, albedo.a);
        }

        fixed4 fragAdd(v2f i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);

            fixed4 baseSample = tex2D(_BaseMap, i.uv);
            fixed4 albedo = baseSample * _BaseColor;

            #if defined(_ALPHATEST_ON)
            clip(albedo.a - _Cutoff);
            #endif

            float3 nWS = ApplyNormalMap(i, i.nWS);
            float3 vWS = normalize(i.viewWS);

            UNITY_LIGHT_ATTENUATION(attenTmp, i, i.posWS);
            half atten = attenTmp;

            half3 L = GetLightDirWS(i.posWS);
            half3 lightCol = _LightColor0.rgb;

            half ndl  = saturate(dot(nWS, L));
            half ramp = ToonRamp(ndl);

            half3 shadowCol = GetTintedShadowColor(i.uv);
            half3 rampCol   = lerp(shadowCol, _HColor.rgb, ramp);
            rampCol         = lerp(shadowCol, rampCol, atten);

            half metallic = _Metallic;
            half smoothness = _Smoothness;

            #if defined(_METALLICGLOSSMAP) && !defined(MOBILE_MODE)
            half4 mg = tex2D(_MetallicGlossMap, i.uv);
            metallic   = saturate(mg.r) * _Metallic;
            smoothness = saturate(mg.a) * _Smoothness;
            #endif

            half3 F0 = lerp(0.12h.xxx, albedo.rgb, metallic);
            half diffuseEnergy = 1.0h - 0.6h * metallic;

            half smoothBoost = lerp(0.6h, 1.6h, smoothness);
            half specGain    = lerp(1.0h, 15.0h, smoothness);

            half directGate = smoothstep(0.05h, 0.25h, ndl) * atten;

            half3 diff = (rampCol * albedo.rgb) * (ndl * atten) * lightCol * diffuseEnergy;

            half3 spec = 0;
            #if !defined(MOBILE_MODE)
            float3 h = normalize(L + vWS);
            half ndh = saturate(dot(nWS, h));
            half rough = 1.0h - smoothness;
            half expVal = lerp(128.0h, 8.0h, rough);
            half s = pow(ndh, expVal) * atten;
            spec += s * F0 * smoothBoost * specGain * directGate * _SmoothnessColor.rgb * lightCol;
            #endif

            return fixed4(diff + spec, 0);
        }

        // Outline
        struct appdataO { float4 vertex:POSITION; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct v2fO { float4 pos:SV_POSITION; UNITY_FOG_COORDS(0) UNITY_VERTEX_INPUT_INSTANCE_ID };

        v2fO vertO(appdataO v)
        {
            v2fO o; UNITY_INITIALIZE_OUTPUT(v2fO, o);
            UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);

            float3 nWS = UnityObjectToWorldNormal(v.normal);
            float3 pWS = mul(unity_ObjectToWorld, v.vertex).xyz;

            #if defined(OUTLINE_ON)
            pWS += normalize(nWS) * _OutlineWidth;
            #endif

            o.pos = UnityWorldToClipPos(pWS);
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }

        fixed4 fragO(v2fO i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            #if defined(OUTLINE_ON)
            fixed3 col = _OutlineColor.rgb;
            UNITY_APPLY_FOG(i.fogCoord, col);
            return fixed4(col, 1);
            #else
            clip(-1); return 0;
            #endif
        }

        ENDCG

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragBase
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #pragma shader_feature SHADOW_TEX_ON
            #pragma shader_feature _NORMALMAP
            #pragma shader_feature _METALLICGLOSSMAP
            #pragma shader_feature RIM_ON
            #pragma shader_feature RIM_LIGHTMASK
            #pragma shader_feature MATCAP_ON
            #pragma shader_feature MOBILE_MODE
            #pragma shader_feature _EMISSION
            #pragma shader_feature _ALPHATEST_ON
            #pragma shader_feature _RECEIVE_SHADOWS_OFF
            #pragma shader_feature MOBILE_FAKE_AO
            #pragma shader_feature USE_LIGHTPROBES
            ENDCG
        }

        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off
            Cull [_Cull]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragAdd
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #pragma shader_feature SHADOW_TEX_ON
            #pragma shader_feature _NORMALMAP
            #pragma shader_feature _METALLICGLOSSMAP
            #pragma shader_feature MOBILE_MODE
            #pragma shader_feature _ALPHATEST_ON
            ENDCG
        }

        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode"="Always" }
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertO
            #pragma fragment fragO
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature OUTLINE_ON
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            Cull [_Cull]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertSC
            #pragma fragment fragSC
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma shader_feature _ALPHATEST_ON

            struct v2fSC
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2fSC vertSC(appdata v)
            {
                v2fSC o;
                UNITY_INITIALIZE_OUTPUT(v2fSC, o);

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragSC(v2fSC i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                fixed4 a = tex2D(_BaseMap, i.uv) * _BaseColor;
                clip(a.a - _Cutoff);
                #endif
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}