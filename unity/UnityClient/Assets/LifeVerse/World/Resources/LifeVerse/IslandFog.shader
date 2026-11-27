// The weather's fog, as the Godot client's: it thickens with the distance from the camera (1 - e^(-density * distance)) and covers what is solid; the sky covers itself
// (IslandSky.shader) and the sea fogs itself (Ocean.shader). A full-screen pass drawn after the solid things. HDRP's own fog is not used for the weather: it also dims the
// sun on its way down through the fog, which the Godot client does not do.
Shader "Hidden/LifeVerse/IslandFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "IslandFog"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            float4 _IslandFogColour;    // colour (linear, Godot units)
            float4 _IslandFogShape;     // x: density per metre, y: radiance of white

            float4 Frag(Varyings varyings) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
                float depth = LoadCameraDepth(varyings.positionCS.xy);
                if (depth == UNITY_RAW_FAR_CLIP_VALUE) discard;
                PositionInputs position = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
                float distance = length(GetPrimaryCameraPosition() - position.positionWS);
                float amount = 1.0 - exp(-distance * _IslandFogShape.x);
                return float4(_IslandFogColour.rgb * _IslandFogShape.y * GetCurrentExposureMultiplier(), amount);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
