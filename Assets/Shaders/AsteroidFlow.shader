// Asteroids-terrain variant of the glass-hex look (see GlassHex.shader /
// Include/GlassHexCommon.hlsl) — a handful of small drifting rock
// silhouettes over the shared starfield backdrop (StarBoundStarfield),
// NOT a wall-to-wall textured surface or a flat background color. Space
// is mostly empty and shows the same continuous galaxy every other hex
// shows through its glass; the rocks are solid objects sitting on top
// of it, the point of interest, not a floor covering every pixel.
//
// Worley/cellular noise (distance to the nearest of a scattered set of
// points) places each rock's center; a rock is only drawn within a
// small radius of its own point, everything else shows the starfield
// straight through. A domain warp bends what would otherwise be a
// perfectly circular silhouette into an irregular, organic blob.
//
// The whole point field is sampled from and drifts through each tile's
// own WORLD-space position (not its local per-tile UV), so it's a pure
// function of one continuous field spanning the whole map. A rock
// drifting across a hex boundary continues smoothly into the next
// Asteroids hex with no seam — no per-hex neighbor lookup needed. An
// isolated Asteroids hex just shows its own patch of the field (maybe
// zero, maybe one rock), which still looks reasonable on its own.
Shader "StarBound/AsteroidFlow"
{
    Properties
    {
        _Color("Rock Color", Color) = (1, 1, 1, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 12)) = 6
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
        _FlowDirection("Flow Direction", Vector) = (1, 0.35, 0, 0)
        _FlowSpeed("Flow Speed", Float) = 0.06
        _RockScale("Rock Scale", Float) = 2.8
        _RockRadius("Rock Radius", Range(0.05, 0.5)) = 0.22
        _RockContrast("Rock Surface Contrast", Range(0, 1)) = 0.5
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
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
                float4 _FlowDirection;
                float _FlowSpeed;
                float _RockScale;
                float _RockRadius;
                float _RockContrast;
            CBUFFER_END

            GlassHexVaryings Vert(Attributes IN)
            {
                GlassHexVaryings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            // Bends straight lines into organic curves before they hit the
            // cell grid below — plain Worley noise on its own has dead-
            // straight cell boundaries meeting at sharp points, which reads
            // as cut paving stones, not tumbling rock. Warping the sample
            // position first is what breaks that regularity.
            float2 StarBoundDomainWarp(float2 p, float amount)
            {
                float warpX = sin(p.y * 1.7 + p.x * 0.6) + sin(p.y * 3.1 - _Time.y * 0.05);
                float warpY = sin(p.x * 1.9 - p.y * 0.5) + sin(p.x * 2.7 + _Time.y * 0.05);
                return p + float2(warpX, warpY) * amount;
            }

            // Worley/cellular noise: f1 = distance to the nearest scattered
            // point, f2 = distance to the second-nearest. f2 - f1 is small
            // right at a cell boundary (a "crack" between two rocks) and
            // large near a cell's own point (the middle of a rock).
            //
            // Each point's jitter is constrained to the central 60% of its
            // cell (not the full 0..1 range) — with full-range jitter, two
            // points in adjacent cells can occasionally land right next to
            // each other across the shared boundary, and their rock blobs
            // fuse into one elongated shape. Keeping points away from cell
            // edges guarantees a minimum gap between neighbors.
            void StarBoundWorley(float2 samplePos, out float f1, out float f2, out float cellRandom, out float2 nearestOffset)
            {
                float2 cell = floor(samplePos);
                float2 localPos = frac(samplePos);
                f1 = 8.0;
                f2 = 8.0;
                cellRandom = 0.0;
                nearestOffset = float2(0.0, 0.0);

                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 jitter = StarBoundHash2(cell + neighbor) * 0.6 + 0.2;
                        float2 pointPos = neighbor + jitter - localPos;
                        float dist = length(pointPos);

                        if (dist < f1)
                        {
                            f2 = f1;
                            f1 = dist;
                            cellRandom = jitter.x;
                            nearestOffset = pointPos;
                        }
                        else if (dist < f2)
                        {
                            f2 = dist;
                        }
                    }
                }
            }

            // The passed-in backdrop (the shared starfield, sampled once
            // in Frag) shows through everywhere except a small blob around
            // each Worley point — sparse floating rocks, not a filled
            // surface, and rocks correctly occlude the stars behind them.
            float3 StarBoundAsteroidField(float2 worldPos, float2 direction, float speed, float scale, float radius, float contrast, float3 rockColor, float3 backdrop)
            {
                float2 samplePos = worldPos * scale + normalize(direction) * _Time.y * speed;
                // A light warp only — just enough to knock the silhouette
                // off a perfect circle into something organic. Too much
                // (this was 0.45) relocates the sample far enough to smear
                // the blob into an elongated, melted shape.
                float2 warped = StarBoundDomainWarp(samplePos, 0.15);

                float f1, f2, cellRandom;
                float2 nearestOffset;
                StarBoundWorley(warped, f1, f2, cellRandom, nearestOffset);

                // Craggy silhouette: the threshold a pixel has to cross to
                // count as "rock" varies with the angle around the rock's
                // own center (multiple sine harmonics, phase-offset per
                // rock via cellRandom so every rock has a different bump
                // pattern) instead of being a constant radius. A constant
                // radius is what made earlier passes read as smooth round
                // blobs — this is what actually makes the edge look broken
                // and jagged like a real chunk of rock.
                float angle = atan2(nearestOffset.y, nearestOffset.x);
                float jaggedness =
                    sin(angle * 5.0 + cellRandom * 41.0) * 0.5 +
                    sin(angle * 9.0 - cellRandom * 23.0) * 0.3 +
                    sin(angle * 13.0 + cellRandom * 11.0) * 0.2;
                float rockSize = radius * lerp(0.7, 1.3, cellRandom) * (1.0 + jaggedness * 0.22);
                float edge = 0.02;
                float rockMask = 1.0 - smoothstep(rockSize - edge, rockSize + edge, f1);

                if (rockMask <= 0.0)
                    return backdrop;

                // Broad secondary cell layer for larger pits/mottling...
                float2 detailWarped = StarBoundDomainWarp(samplePos * 3.2 + 17.0, 0.15);
                float df1, df2, detailRandom;
                float2 detailOffset;
                StarBoundWorley(detailWarped, df1, df2, detailRandom, detailOffset);
                float detailShade = lerp(1.0 - contrast * 0.3, 1.0 + contrast * 0.3, saturate(df1));

                // ...plus a much finer, higher-frequency grain layer on
                // top for a rough stone surface rather than a smoothly
                // shaded gradient.
                float2 grainWarped = samplePos * 11.0 + 53.0;
                float gf1, gf2, grainRandom;
                float2 grainOffset;
                StarBoundWorley(grainWarped, gf1, gf2, grainRandom, grainOffset);
                float grainShade = lerp(1.0 - contrast * 0.18, 1.0 + contrast * 0.18, saturate(gf1));

                // Thin bright glint right at the rock's own silhouette edge.
                float edgeGlow = smoothstep(rockSize - edge, rockSize, f1) * (1.0 - smoothstep(rockSize, rockSize + edge, f1));

                // Fake volumetric shading: treat each rock as a dome/sphere
                // and build its normal analytically from the direction and
                // distance to its own center — screen-space derivatives
                // (ddx/ddy on a height field) turned out too subtle,
                // because their magnitude depends on how zoomed-in the
                // camera happens to be. This version doesn't: at the
                // rock's own center the normal points straight at the
                // camera (bright), and it tilts outward toward the rim
                // (dark) regardless of zoom, which is what actually reads
                // as a lit, rounded surface instead of flat noise.
                float radialDist = saturate(f1 / max(rockSize, 0.0001));
                float2 radialDir = f1 > 0.0001 ? (-nearestOffset / f1) : float2(0.0, 0.0);
                float3 normal = normalize(float3(radialDir * radialDist, sqrt(saturate(1.0 - radialDist * radialDist))));
                float3 lightDir = normalize(float3(0.45, 0.6, 0.55));
                float lightFactor = saturate(dot(normal, lightDir));
                float shading = lerp(0.25, 1.5, lightFactor);

                float3 shadedRock = rockColor * detailShade * grainShade * shading + edgeGlow * 0.4;
                return lerp(backdrop, shadedRock, rockMask);
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                float3 starfield = StarBoundStarfield(IN.worldPos);
                float3 fieldColor = StarBoundAsteroidField(IN.worldPos, _FlowDirection.xy, _FlowSpeed, _RockScale, _RockRadius, _RockContrast, _Color.rgb, starfield);

                // No separate glass-opacity reveal step here (unlike
                // GlassHex.shader) — the starfield is already correctly
                // embedded in fieldColor (shown directly in empty space,
                // occluded where a rock sits), so reveal-more-at-center
                // doesn't apply; it would incorrectly fade rocks near the
                // hex center back toward transparent.
                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
