// The sky of an island: the sky shader of the Godot client (IslandEnvironmentController.cs, in the git history) as an HDRP sky. Pass 0 draws the cube map the world is lit by,
// pass 1 the sky behind the picture. Colours are linear, in Godot's units; _IslandIntensity turns them into HDRP radiance. Loaded from Resources, so builds carry it.
Shader "Hidden/LifeVerse/IslandSky"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma vertex Vert
    #pragma editor_sync_compilation
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyUtils.hlsl"

    float4 _IslandSun;      // xyz: towards the sun, w: day (0 night, 1 full day)
    float4 _IslandWeather;  // x: cloud cover
    float4 _IslandFog;      // colour, and how much of the sky it takes
    float4 _IslandTint;     // colour, and how much
    float _IslandIntensity;
    TEXTURE2D(_IslandClouds);
    SAMPLER(sampler_IslandClouds);

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
        return output;
    }

    // the colours of the Godot shader, as linear light
    float3 SkyColour(float3 dir, float fogged)
    {
        float3 zenithDay = SRGBToLinear(float3(0.17, 0.42, 0.84));
        float3 horizonDay = SRGBToLinear(float3(0.74, 0.86, 0.97));
        float3 zenithNight = SRGBToLinear(float3(0.004, 0.01, 0.045));
        float3 horizonNight = SRGBToLinear(float3(0.03, 0.045, 0.1));
        float3 sunset = SRGBToLinear(float3(1.0, 0.52, 0.22));
        float3 sunDir = normalize(_IslandSun.xyz);
        float day = _IslandSun.w;

        float h = saturate(dir.y);
        float3 horizon = lerp(horizonNight, horizonDay, day);
        float3 colour = lerp(horizon, lerp(zenithNight, zenithDay, day), pow(h, 0.5));
        float towardSun = max(dot(dir, sunDir), 0.0);
        float lowSun = 1.0 - saturate(abs(sunDir.y) * 3.0);
        colour += sunset * pow(towardSun, 5.0) * lowSun * 0.65 * (1.0 - h);
        float above = smoothstep(-0.04, 0.02, sunDir.y);
        colour += float3(1.0, 0.96, 0.82) * (smoothstep(0.9994, 0.9998, towardSun) * 4.0 + pow(towardSun, 80.0) * 0.5) * above;
        if (dir.y < 0.0) colour = lerp(horizon, horizon * 0.8, saturate(-dir.y * 5.0));
        if (dir.y > 0.005)
        {
            // the ground coordinates of the game (Unity's z is flipped), drifting with the time
            float2 uv = float2(dir.x, -dir.z) / (dir.y + 0.14) * 0.32 + float2(_Time.y * 0.004, _Time.y * 0.004 * 0.35);
            float n = SAMPLE_TEXTURE2D_LOD(_IslandClouds, sampler_IslandClouds, uv, 0).r * 0.7 + SAMPLE_TEXTURE2D_LOD(_IslandClouds, sampler_IslandClouds, uv * 2.7, 0).r * 0.3;
            float cloud = smoothstep(1.0 - _IslandWeather.x, 1.0 - _IslandWeather.x + 0.24, n);
            float fade = smoothstep(0.005, 0.2, dir.y);
            float3 lit = lerp(float3(0.1, 0.11, 0.16), float3(1.0, 1.0, 1.0), day) * lerp(0.82, 1.0, 1.0 - n);
            colour = lerp(colour, lit, cloud * fade * 0.93);
        }
        colour = lerp(colour, colour * _IslandTint.rgb * 1.6, _IslandTint.a);
        // fog covers the sky that is seen, as in Godot; the light the sky gives the world stays that of the sky itself
        return lerp(colour, _IslandFog.rgb, _IslandFog.a * fogged);
    }

    float4 Draw(Varyings input, float exposure, float fogged)
    {
        float3 dir = -GetSkyViewDirWS(input.positionCS.xy);
        return float4(ClampToFloat16Max(SkyColour(dir, fogged) * _IslandIntensity * exposure), 1.0);
    }

    float4 FragBaking(Varyings input) : SV_Target
    {
        return Draw(input, 1.0, 0.0);
    }

    float4 FragRender(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return Draw(input, GetCurrentExposureMultiplier(), 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "FragBaking"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off
            HLSLPROGRAM
            #pragma fragment FragBaking
            ENDHLSL
        }
        Pass
        {
            Name "FragRender"
            ZWrite Off
            ZTest LEqual
            Blend Off
            Cull Off
            HLSLPROGRAM
            #pragma fragment FragRender
            ENDHLSL
        }
    }
    Fallback Off
}
