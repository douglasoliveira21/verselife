// Foam where the water is shallow, worked out for every pixel from the depth of the water over the ground (the sea level minus the terrain height, read from a
// height texture the terrain view bakes): thick lace at the waterline that pulses in and out like the surf, thinning out into the sea. HDRP unlit and transparent.
Shader "LifeVerse/ShoreFoam"
{
    Properties
    {
        _Height ("Height (R, metres)", 2D) = "black" {}
        _Noise ("Noise", 2D) = "white" {}
        _Area ("Area (x0, z0, sizeX, sizeZ) in game space", Vector) = (-128, -128, 256, 256)
        _Sea ("Sea level", Float) = -0.8
        _Nits ("Brightness in nits", Float) = 12000
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" }
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
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_Height); SAMPLER(sampler_Height);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Area;
                float _Sea;
                float _Nits;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 absolute : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.absolute = GetAbsolutePositionWS(positionRWS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Unity's z is the game's z flipped
                float2 uv = float2((input.absolute.x - _Area.x) / _Area.z, (-input.absolute.z - _Area.y) / _Area.w);
                float ground = SAMPLE_TEXTURE2D_LOD(_Height, sampler_Height, uv, 0).r;
                float depth = _Sea - ground;                         // metres of water over the ground; below zero is dry land
                float t = _TimeParameters.x;
                float n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, input.absolute.xz * 0.07 + float2(t * 0.015, -t * 0.011)).r;
                float n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, input.absolute.xz * 0.29 - float2(t * 0.03, t * 0.021)).r;
                float surge = 0.55 + 0.4 * sin(t * 1.15 + n1 * 6.2832);      // how far out from the waterline the foam reaches, breathing like the surf
                float band = saturate(1.0 - depth / (surge * 2.2)) * saturate((depth + 0.12) / 0.28);
                float lace = smoothstep(0.34, 0.68, n2 * 0.65 + n1 * 0.45 + band * 0.55);
                float alpha = band * lace * 0.9;
                float3 colour = float3(1.0, 1.0, 1.0) * _Nits * GetCurrentExposureMultiplier();
                return float4(colour, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
