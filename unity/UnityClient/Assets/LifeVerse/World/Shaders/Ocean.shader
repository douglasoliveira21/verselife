// The sea, ported from the water shader of the Godot client (client/Scripts/OceanSurface.cs): turquoise in the shallows to deep blue by the depth of what is behind
// each pixel, three sliding layers of ripples, foam where the water is shallow, the sky mirrored at grazing angles, the sun's glint, a fade into the horizon and,
// seen from below, a translucent ceiling. HDRP unlit and transparent: it lights itself with the sun and the sky the environment publishes (_LV globals), in the same
// units as the rest of the picture. _Detail 0 is the cheaper sea of the low presets (two ripple layers, plain foam, no clouds in the mirror).
Shader "LifeVerse/Ocean"
{
    Properties
    {
        _NormalA ("Swell (normal map)", 2D) = "bump" {}
        _NormalB ("Ripples (normal map)", 2D) = "bump" {}
        _FoamNoise ("Foam noise", 2D) = "gray" {}
        _WaveTime ("Time", Float) = 0
        _Detail ("Detail (0 cheap, 1 full)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            TEXTURE2D(_FoamNoise); SAMPLER(sampler_FoamNoise);
            TEXTURE2D(_LVCloudMap); SAMPLER(sampler_LVCloudMap);
            CBUFFER_START(UnityPerMaterial)
                float _WaveTime;
                float _Detail;
            CBUFFER_END
            // published by IslandEnvironment (linear colours, in the units of the Godot lights: 1 is white at full exposure)
            float4 _LVSunDir;      // towards the sun
            float4 _LVSunLight;    // colour times energy
            float4 _LVSkyTop;
            float4 _LVSkyHorizon;
            float4 _LVAmbient;
            float4 _LVFog;         // colour, and the distance at which it takes about two thirds (0: none)
            float _LVDay;
            float _LVClouds;       // how much the clouds cover, 0 to 1

            #define NITS 13904.0   // what 1 is worth at the fixed exposure of the world (EV 13.5)

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 relative : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.relative = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.relative);
                return output;
            }

            float3 SkyAt(float3 direction)
            {
                float h = saturate(direction.y);
                float3 colour = lerp(_LVSkyHorizon.rgb, _LVSkyTop.rgb, sqrt(h));
                if (_Detail > 0.5)
                {
                    float2 uv = float2(atan2(direction.x, direction.z) / 6.2831853, asin(h) / 1.5707963);
                    float cloud = SAMPLE_TEXTURE2D_LOD(_LVCloudMap, sampler_LVCloudMap, uv, 0).r * _LVClouds;
                    colour = lerp(colour, lerp(0.1, 1.0, _LVDay).xxx, cloud * 0.9);
                }
                return colour;
            }

            float4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 absolute = GetAbsolutePositionWS(input.relative);
                float2 at = float2(absolute.x, -absolute.z);            // the game's ground coordinates (Unity's z is flipped)
                float t = _WaveTime;

                float3 a = SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, at * 0.035 + float2(t * 0.018, t * 0.011)).rgb;
                float3 b = SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, at * 0.12 + float2(-t * 0.014, t * 0.02)).rgb;
                float3 sum = a + b * 0.8;
                float neutral = 1.8;
                if (_Detail > 0.5) { sum += SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, at * 0.5 + float2(t * 0.05, -t * 0.03)).rgb * 0.35; neutral = 2.15; }
                float3 tangent = normalize(sum - float3(neutral * 0.5, neutral * 0.5, 0.0));
                tangent = normalize(float3(tangent.xy * 1.2, tangent.z));
                float3 N = normalize(float3(tangent.x, tangent.z, -tangent.y));

                float3 V = GetWorldSpaceNormalizeViewDir(input.relative);
                float surface = LinearEyeDepth(input.relative, GetWorldToViewMatrix());
                float behind = LinearEyeDepth(LoadCameraDepth(input.positionCS.xy), _ZBufferParams);
                float depth = max(behind - surface, 0.0);
                float distance01 = length(input.relative.xz);
                float horizonFade = 1.0 - smoothstep(1100.0, 1290.0, distance01);
                float3 horizon = _LVSkyHorizon.rgb;
                float3 L = normalize(_LVSunDir.xyz);

                float3 colour; float alpha;
                if (front)
                {
                    float depth01 = 1.0 - exp(-depth / 4.0);
                    float3 body = lerp(SRGBToLinear(float3(0.05, 0.50, 0.58)), SRGBToLinear(float3(0.005, 0.07, 0.22)), sqrt(depth01));
                    float NdotV = saturate(dot(N, V));
                    body = lerp(body, SRGBToLinear(float3(0.74, 0.86, 0.97)) * 0.85, pow(1.0 - NdotV, 4.0) * 0.28);

                    float foam;
                    if (_Detail > 0.5)
                    {
                        float noise = SAMPLE_TEXTURE2D(_FoamNoise, sampler_FoamNoise, at * 0.09 + float2(t * 0.02, 0.0)).r;
                        foam = (1.0 - smoothstep(0.0, 1.4 + noise * 1.2, depth)) * smoothstep(0.25, 0.75, noise + 0.35);
                    }
                    else foam = (1.0 - smoothstep(0.0, 2.0, depth)) * 0.85;
                    foam += (1.0 - smoothstep(0.0, 0.25, depth)) * 0.6;
                    foam = saturate(foam);
                    body = lerp(body, SRGBToLinear(float3(0.95, 0.98, 1.0)), foam);
                    body = lerp(body, SRGBToLinear(float3(0.74, 0.86, 0.97)), smoothstep(500.0, 1600.0, distance01) * 0.85);
                    body *= lerp(0.07, 1.0, _LVDay);

                    float rough = lerp(0.09, 0.4, foam);
                    float NdotL = saturate(dot(N, L));
                    colour = body * (_LVSunLight.rgb * NdotL + _LVAmbient.rgb);
                    // the sky mirrored (Schlick, from a base of 0.048: Godot's SPECULAR 0.55)
                    float3 R = reflect(-V, N); R.y = abs(R.y);
                    float fresnel = 0.048 + (max(1.0 - rough, 0.048) - 0.048) * pow(1.0 - NdotV, 5.0);
                    colour += SkyAt(R) * fresnel * lerp(0.24, 1.0, _LVDay);   // as Godot, the mirrored sky dims with the ambient light at night
                    // the glint of the sun (GGX)
                    float3 H = normalize(L + V);
                    float NdotH = saturate(dot(N, H)); float LdotH = saturate(dot(L, H));
                    float a2 = max(rough * rough * rough * rough, 1e-5);
                    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
                    float D = a2 / (PI * d * d);
                    float F = 0.048 + 0.952 * pow(1.0 - LdotH, 5.0);
                    float Vis = 0.25 / max(LdotH * LdotH * (rough + 0.5), 0.05);
                    colour += _LVSunLight.rgb * min(D * Vis * F * PI, 20.0) * NdotL;
                    alpha = saturate(lerp(0.55, 0.98, depth01) + foam) * horizonFade;
                }
                else
                {
                    float facing = saturate(dot(-N, V));
                    float window = smoothstep(0.25, 0.8, facing);
                    float3 ceiling = lerp(float3(0.02, 0.17, 0.30), float3(0.32, 0.74, 0.90), window) * lerp(0.12, 1.0, _LVDay);
                    colour = ceiling * (_LVAmbient.rgb + _LVSunLight.rgb * 0.45);
                    alpha = lerp(0.92, 0.35, window) * horizonFade;
                }

                if (_LVFog.w > 0.0) colour = lerp(colour, _LVFog.rgb, 1.0 - exp(-distance01 / _LVFog.w));
                return float4(colour * NITS * GetCurrentExposureMultiplier(), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
