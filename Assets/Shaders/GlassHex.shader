// Translucent "glass overlay" look shared by every hex tile — the base
// every per-terrain living-galaxy effect (planet spin, asteroid flow,
// mines, wormhole, tradelane) builds on. See TerrainMaterials.cs, which
// tints this shader per TerrainType via the standard _Color property.
//
// The map is meant to read as glass hexes overlaid on one continuous
// galaxy, not a self-contained scene per tile — so the tint isn't an
// opaque fill, it's a translucent wash over the shared starfield
// backdrop (see GlassHexCommon.hlsl's StarBoundStarfield). _GlassAlpha
// controls how much tint shows vs how much starfield shows through;
// it's still boosted toward the hex's edge (more solid/reflective at
// the rim, more see-through toward the center — a flat top-down tile
// has no surface normal to drive a true fresnel, so this is faked from
// distance-to-center instead), same as the facet/rim look below.
//
// Uses the hex mesh's UVs (see HexMeshFactory.CreateFlatTopHex) as a
// radius-independent local coordinate: center at (0.5, 0.5), outer
// vertices on the unit circle.
Shader "StarBound/GlassHex"
{
    Properties
    {
        _Color("Base Tint", Color) = (1, 1, 1, 1)
        _GlassAlpha("Glass Alpha", Range(0, 1)) = 0.18
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 12)) = 6
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _GlassAlpha;
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
            CBUFFER_END

            GlassHexVaryings Vert(Attributes IN)
            {
                GlassHexVaryings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);
                float3 tintedColor = StarBoundGlassColor(_Color.rgb, _RimColor.rgb, rim, facetShade);
                float tintOpacity = StarBoundGlassAlpha(_GlassAlpha, rim, _Color.a);

                float3 starfield = StarBoundStarfield(IN.worldPos);
                float3 finalColor = lerp(starfield, tintedColor, tintOpacity);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
