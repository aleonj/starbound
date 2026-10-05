// Full-viewport backdrop behind the entire hex grid — the actual galaxy
// the hexes are meant to look like glass floating over. Without this,
// StarBoundStarfield (see Include/GlassHexCommon.hlsl) was only ever
// sampled inside each hex tile's own mesh, so the gaps between tiles and
// the area outside the grid stayed a flat camera-clear color instead of
// being part of the same continuous scene.
Shader "StarBound/GalaxyBackground"
{
    SubShader
    {
        // Plain opaque Geometry queue, NOT "Background" (1000) — URP's
        // default renderer filters its Opaque pass starting at the
        // Geometry queue (2000), so anything tagged "Background" falls
        // outside that range and silently never renders at all. The
        // Opaque pass always renders before the Transparent pass
        // (a pipeline-level ordering guarantee, independent of Z
        // position), so this reliably sits behind every hex tile
        // (Transparent queue) without needing depth-buffer tricks —
        // ZWrite stays off, same as everything else in this project.
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Include/GlassHexCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 worldPos : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                // No extra ambient tint here — empty space inside a hex's
                // own glass is pure black wherever StarBoundStarfield has
                // no star (it returns (0,0,0) there), so the background
                // has to match exactly or the seam between "inside a hex"
                // and "the gaps/outside the grid" is visible as a tonal
                // mismatch instead of one continuous galaxy. The parallax
                // star layers and nebula both live inside
                // StarBoundStarfield itself (see GlassHexCommon.hlsl) for
                // exactly this reason — every hex's own glass tile calls
                // the same function, so there's no separate "background-
                // only" layer to go out of sync with.
                return float4(saturate(StarBoundStarfield(IN.worldPos)), 1.0);
            }
            ENDHLSL
        }
    }
}
