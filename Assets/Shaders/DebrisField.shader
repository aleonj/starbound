// Debris-terrain variant of the glass-hex look (see GlassHex.shader /
// Include/GlassHexCommon.hlsl) — sparse tumbling wreckage over the
// shared starfield backdrop, reusing the same Worley-scatter technique
// as AsteroidFlow/MinesHazard, but each scattered point is one of three
// distinct man-made piece types instead of a rock or a mine casing:
//
// - A flat rectangular fragment with an etched grid (a broken solar
//   panel), bluish-tinted.
// - A hollow girder frame with an X cross-brace (broken scaffolding) —
//   genuinely open structure, not a filled shape.
// - A straight-edged irregular polygon (broken hull plating), neutral
//   metal gray.
//
// Both non-panel colors are fixed/hardcoded, not driven by
// TerrainMaterials' per-terrain tint (see the _HullColor property note
// below) — wreckage is neutral debris material, not a single flat
// identity color.
//
// Each piece slowly rotates in place (tumbling wreckage) with its own
// per-piece phase/speed, and the whole field drifts like AsteroidFlow's
// rocks — debris floating in space should drift too, not sit still.
Shader "StarBound/DebrisField"
{
    Properties
    {
        // Deliberately not named "_Color" — that's the one property name
        // TerrainMaterials.cs always overwrites (via Material.color) with
        // the terrain-identity tint, which is what made every piece look
        // tinted the same flat orange/brown regardless of shading. Both
        // piece colors here are neutral, hardcoded, and untouched by it.
        _HullColor("Hull Chunk Color", Color) = (0.42, 0.44, 0.48, 1)
        _PanelColor("Solar Panel Color", Color) = (0.3, 0.5, 0.75, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 30)) = 20
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
        _FlowDirection("Flow Direction", Vector) = (1, -0.3, 0, 0)
        _FlowSpeed("Flow Speed", Float) = 0.05
        _DebrisScale("Debris Scale", Float) = 2.4
        _DebrisRadius("Debris Radius", Range(0.05, 0.4)) = 0.2
        _TumbleSpeed("Tumble Speed", Float) = 0.6
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
                float4 _HullColor;
                float4 _PanelColor;
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
                float4 _FlowDirection;
                float _FlowSpeed;
                float _DebrisScale;
                float _DebrisRadius;
                float _TumbleSpeed;
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

            float3 StarBoundDebrisField(float2 worldPos, float2 direction, float speed, float scale, float radius, float tumbleSpeed, float3 hullColor, float3 panelColor, float3 backdrop)
            {
                float2 samplePos = worldPos * scale + normalize(direction) * _Time.y * speed;
                float2 warped = StarBoundDomainWarp(samplePos, 0.15);

                float f1, f2, cellRandom;
                float2 nearestOffset;
                StarBoundWorley(warped, f1, f2, cellRandom, nearestOffset);

                float pieceSize = radius * lerp(0.7, 1.3, cellRandom);

                // Position relative to this piece's own center, rotated
                // by a per-piece tumble angle — rotation preserves
                // length, so length(rotated) still equals f1 exactly;
                // only the ANGLE changes, which is what makes the piece
                // appear to physically tumble over time.
                float2 pieceLocal = -nearestOffset;
                float tumbleAngle = _Time.y * tumbleSpeed * lerp(0.6, 1.4, cellRandom) + cellRandom * 37.0;
                float ct = cos(tumbleAngle);
                float st = sin(tumbleAngle);
                float2 rotated = float2(pieceLocal.x * ct - pieceLocal.y * st, pieceLocal.x * st + pieceLocal.y * ct);

                // Three distinct piece types, chosen per-piece from a
                // single hash so the type is fixed for that piece:
                // solar panel (45%, filled rectangle with a cell grid),
                // scaffolding (30%, a HOLLOW girder frame — most of its
                // bounding area shows backdrop through the gaps, which
                // is what makes it read as open truss structure rather
                // than another solid chunk), hull plate (25%, filled
                // irregular polygon).
                float typeHash = StarBoundHash2(float2(cellRandom * 13.0, cellRandom * 29.0)).x;

                float mask;
                float3 pieceColor;

                if (typeHash > 0.55)
                {
                    // Solar panel — filled rectangle with an etched cell
                    // grid.
                    float halfWidth = pieceSize * 0.85;
                    float halfHeight = pieceSize * 0.38;
                    float edge = 0.02;
                    float inX = 1.0 - smoothstep(halfWidth - edge, halfWidth + edge, abs(rotated.x));
                    float inY = 1.0 - smoothstep(halfHeight - edge, halfHeight + edge, abs(rotated.y));
                    mask = inX * inY;

                    float2 gridUV = rotated * 9.0;
                    float2 gridFrac = abs(frac(gridUV) - 0.5);
                    float gridLine = 1.0 - smoothstep(0.0, 0.06, min(gridFrac.x, gridFrac.y));
                    pieceColor = lerp(panelColor, panelColor * 0.55, gridLine);
                }
                else if (typeHash > 0.25)
                {
                    // Scaffolding — a hollow rectangular girder frame
                    // (border beams only) with an X cross-brace, not a
                    // filled shape. This is the piece type that actually
                    // reads as open structural framework.
                    float halfWidth = pieceSize * 0.95;
                    float halfHeight = pieceSize * 0.55;
                    float beamWidth = pieceSize * 0.09;
                    float beamEdge = 0.015;

                    float inBounds = step(abs(rotated.x), halfWidth) * step(abs(rotated.y), halfHeight);
                    float borderH = 1.0 - smoothstep(beamWidth, beamWidth + beamEdge, abs(abs(rotated.y) - halfHeight));
                    float borderV = 1.0 - smoothstep(beamWidth, beamWidth + beamEdge, abs(abs(rotated.x) - halfWidth));
                    float frame = max(borderH, borderV) * inBounds;

                    float diagScale = length(float2(halfWidth, halfHeight));
                    float diag1 = 1.0 - smoothstep(beamWidth, beamWidth + beamEdge, abs(rotated.x * halfHeight - rotated.y * halfWidth) / diagScale);
                    float diag2 = 1.0 - smoothstep(beamWidth, beamWidth + beamEdge, abs(rotated.x * halfHeight + rotated.y * halfWidth) / diagScale);
                    float brace = max(diag1, diag2) * inBounds;

                    mask = max(frame, brace);
                    pieceColor = hullColor * 1.1;
                }
                else
                {
                    // Hull plate — straight-edged irregular polygon
                    // (broken metal cut at flat angles), NOT the same
                    // smooth sine-wobbled jaggedness AsteroidFlow's rock
                    // silhouette uses — a rounded-but-jagged blob is what
                    // made this read too close to an asteroid. Each of 5
                    // facets gets its own random distance-from-center, so
                    // the edges are flat straight cuts, not organic curves.
                    const float facetCount = 5.0;
                    float angle = atan2(rotated.y, rotated.x);
                    float facetWidth = STARBOUND_TWO_PI / facetCount;
                    float facetIndex = floor(angle / facetWidth);
                    float angleInFacet = angle - facetIndex * facetWidth - facetWidth * 0.5;
                    float facetRadiusHash = StarBoundHash2(float2(cellRandom * 7.0 + facetIndex, cellRandom * 13.0 - facetIndex)).x;
                    float facetBaseRadius = pieceSize * lerp(0.75, 1.2, facetRadiusHash);
                    float polyRadius = facetBaseRadius * cos(facetWidth * 0.5) / max(cos(angleInFacet), 0.2);
                    float edge = 0.015;
                    mask = 1.0 - smoothstep(polyRadius - edge, polyRadius + edge, f1);
                    pieceColor = hullColor;
                }

                if (mask <= 0.0)
                    return backdrop;

                // Dome-lit shading, same analytic-normal technique as
                // AsteroidFlow/MinesHazard, using the UNROTATED radial
                // distance (f1) so the lighting direction stays fixed in
                // view space regardless of how the piece has tumbled.
                float radialDist = saturate(f1 / max(pieceSize, 0.0001));
                float2 radialDir = f1 > 0.0001 ? (-nearestOffset / f1) : float2(0.0, 0.0);
                float3 normal = normalize(float3(radialDir * radialDist, sqrt(saturate(1.0 - radialDist * radialDist))));
                float3 lightDir = normalize(float3(0.45, 0.6, 0.55));
                float lightFactor = saturate(dot(normal, lightDir));
                float3 shadedPiece = pieceColor * lerp(0.3, 1.3, lightFactor);

                return lerp(backdrop, shadedPiece, mask);
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                float3 starfield = StarBoundStarfield(IN.worldPos);
                float3 fieldColor = StarBoundDebrisField(IN.worldPos, _FlowDirection.xy, _FlowSpeed, _DebrisScale, _DebrisRadius, _TumbleSpeed, _HullColor.rgb, _PanelColor.rgb, starfield);

                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
