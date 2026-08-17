// Mines-terrain variant of the glass-hex look (see GlassHex.shader /
// Include/GlassHexCommon.hlsl) — a sparse scatter of small spiky mine
// casings over the shared starfield backdrop, each with its own
// pulsing warning light. Reuses AsteroidFlow's sparse Worley-scatter
// technique (shared helpers now live in GlassHexCommon.hlsl) but static
// (mines are deployed hazards, not drifting debris) and with a sharper,
// spikier silhouette instead of a rounded rock.
//
// Casing color and warning-light color are deliberately separate
// properties (same reasoning as TradelaneConnector's structure/rail
// split): the physical mine body is a fixed dark metal, independent of
// terrain-identity color, while only the pulsing light itself carries
// TerrainMaterials' red tint — otherwise the whole mine reads as one
// flat-colored blob instead of "dark metal object with a warning light."
Shader "StarBound/MinesHazard"
{
    Properties
    {
        _Color("Warning Light Color", Color) = (1, 1, 1, 1)
        _CasingColor("Mine Casing Color", Color) = (0.12, 0.12, 0.15, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 30)) = 20
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
        _MineScale("Mine Scale", Float) = 2.0
        _MineRadius("Mine Radius", Range(0.05, 0.35)) = 0.14
        _PulseSpeed("Pulse Speed", Float) = 1.2
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
                float4 _CasingColor;
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
                float _MineScale;
                float _MineRadius;
                float _PulseSpeed;
            CBUFFER_END

            GlassHexVaryings Vert(Attributes IN)
            {
                GlassHexVaryings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                OUT.hexCenter = TransformObjectToWorld(float3(0.0, 0.0, 0.0)).xy;
                return OUT;
            }

            // The passed-in backdrop shows through everywhere except a
            // small spiky blob around each Worley point — sparse mines,
            // not a filled minefield floor.
            float3 StarBoundMineField(float2 worldPos, float scale, float radius, float pulseSpeed, float3 casingColor, float3 warningColor, float3 backdrop)
            {
                float2 samplePos = worldPos * scale; // static — mines don't drift like asteroids
                float2 warped = StarBoundDomainWarp(samplePos, 0.15);

                float f1, f2, cellRandom;
                float2 nearestOffset;
                StarBoundWorley(warped, f1, f2, cellRandom, nearestOffset);

                // Sharper, spikier silhouette than AsteroidFlow's rounded
                // rocks — a mine casing should read as angular/built, not
                // organic.
                float angle = atan2(nearestOffset.y, nearestOffset.x);
                float spikes = sin(angle * 7.0 + cellRandom * 29.0) * 0.5 + sin(angle * 13.0 - cellRandom * 17.0) * 0.3;
                float mineSize = radius * lerp(0.8, 1.2, cellRandom) * (1.0 + spikes * 0.2);
                float edge = 0.018;
                float mineMask = 1.0 - smoothstep(mineSize - edge, mineSize + edge, f1);

                if (mineMask <= 0.0)
                    return backdrop;

                // Dome-lit casing — same analytic-normal technique as
                // AsteroidFlow's rocks, for a consistent "solid object"
                // read across the living-galaxy effects.
                float radialDist = saturate(f1 / max(mineSize, 0.0001));
                float2 radialDir = f1 > 0.0001 ? (-nearestOffset / f1) : float2(0.0, 0.0);
                float3 normal = normalize(float3(radialDir * radialDist, sqrt(saturate(1.0 - radialDist * radialDist))));
                float3 lightDir = normalize(float3(0.45, 0.6, 0.55));
                float lightFactor = saturate(dot(normal, lightDir));
                float3 shadedCasing = casingColor * lerp(0.25, 1.2, lightFactor);

                // Angular panel facets etched into the casing (same wedge
                // technique as the shared hex facet shading, just applied
                // at the mine's own scale) — this is what makes it read
                // as a small built device instead of a smooth blob.
                float wedge = frac((angle / STARBOUND_TWO_PI) * 6.0);
                float facet = abs(wedge - 0.5) * 2.0;
                shadedCasing *= lerp(1.0, 0.72, facet * 0.7);

                // Small warning light with a genuine on/off flash duty
                // cycle (brief bright pulse, mostly dark) instead of a
                // smooth glow that spans most of the mine's own radius —
                // that read as "a red blob" rather than a light on a
                // structure. First pass over-corrected and was too dim/
                // rare to notice — bigger dot, brighter ember, and a
                // longer, more frequent flash.
                float lightRadius = mineSize * 0.32;
                float lightDot = 1.0 - smoothstep(lightRadius * 0.55, lightRadius, f1);
                float blinkCycle = frac(_Time.y * pulseSpeed * 0.6 + cellRandom * 13.0);
                float flash = smoothstep(0.0, 0.06, blinkCycle) * (1.0 - smoothstep(0.22, 0.32, blinkCycle));
                float3 warningGlow = warningColor * lightDot * (0.4 + flash * 1.6);

                return lerp(backdrop, shadedCasing, mineMask) + warningGlow * mineMask;
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                float3 starfield = StarBoundStarfield(IN.worldPos);
                float3 fieldColor = StarBoundMineField(IN.worldPos, _MineScale, _MineRadius, _PulseSpeed, _CasingColor.rgb, _Color.rgb, starfield);

                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
