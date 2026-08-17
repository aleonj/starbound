// Tradelane-terrain variant of the glass-hex look (see GlassHex.shader /
// Include/GlassHexCommon.hlsl) — a man-made docking hub with glowing
// energy conduits ("rails") extending toward whichever of the hex's 6
// edges lead to another Tradelane hex, so a chain of Tradelane hexes
// reads as one connected lane rather than isolated tiles.
//
// Unlike every other per-terrain shader here, connectivity is real
// per-hex map data (is THIS SPECIFIC neighbor also Tradelane), not
// something derivable from a pure function of world position — so it
// can't use the "just hash worldPos/hexCenter" trick the others rely
// on. See MapView.ComputeTradelaneConnectionMask (computed once in C#
// when a Tradelane tile is first created) and HexTileView.SetConnectionMask,
// which delivers it here via a MaterialPropertyBlock (_ConnectionMask,
// a 6-bit int packed into a float — bit `dir` set means the neighbor in
// that direction is also Tradelane).
//
// Direction-to-angle mapping (verified against HexLayout.AxialToWorld):
// 0->30deg, 1->330deg, 2->270deg, 3->210deg, 4->150deg, 5->90deg.
Shader "StarBound/TradelaneConnector"
{
    Properties
    {
        _Color("Rail Color", Color) = (1, 1, 1, 1)
        _StructureColor("Structure Color", Color) = (0.5, 0.55, 0.62, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 30)) = 20
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
        _HubRadius("Hub Radius", Range(0.1, 0.5)) = 0.28
        _RailWidth("Rail Angular Width (radians)", Range(0.1, 0.8)) = 0.32
        _PulseSpeed("Pulse Speed", Float) = 2.0
        _ConnectionMask("Connection Mask (internal)", Float) = 0
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
                float4 _StructureColor;
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
                float _HubRadius;
                float _RailWidth;
                float _PulseSpeed;
                float _ConnectionMask;
            CBUFFER_END

            static const float StarBoundHexDirectionAngles[6] =
            {
                0.523599, // 30 deg
                5.759587, // 330 deg
                4.712389, // 270 deg
                3.665191, // 210 deg
                2.617994, // 150 deg
                1.570796  // 90 deg
            };

            GlassHexVaryings Vert(Attributes IN)
            {
                GlassHexVaryings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                OUT.hexCenter = TransformObjectToWorld(float3(0.0, 0.0, 0.0)).xy;
                return OUT;
            }

            float3 StarBoundTradelane(float2 localPos, float2 worldPos, float2 hexCenter, int connectionMask, float hubRadius, float railWidth, float pulseSpeed, float3 structureColor, float3 railColor)
            {
                float dist = length(localPos);
                float angle = atan2(localPos.y, localPos.x);

                // Starfield backdrop everywhere, same as every other
                // terrain's empty space (AsteroidFlow's gaps, PlanetSpin's
                // margin, WormholeSwirl away from the vortex) — a hex's
                // shared "what's behind it" look shouldn't depend on which
                // terrain it is. Only the hub disc and rail conduits below
                // are Tradelane-specific content drawn on top of it.
                float3 color = StarBoundStarfield(worldPos);

                // Docking hub — a small paneled disc (etched grid lines,
                // not organic noise), not a wash across the whole hex.
                float hubMask = 1.0 - smoothstep(hubRadius - 0.03, hubRadius, dist);
                float2 gridUV = localPos * 10.0;
                float2 gridFrac = abs(frac(gridUV) - 0.5);
                float gridLine = 1.0 - smoothstep(0.0, 0.05, min(gridFrac.x, gridFrac.y));
                float3 hubPanel = structureColor * lerp(1.0, 0.7, gridLine);
                color = lerp(color, hubPanel, hubMask);

                // Hub ring — soft core+halo instead of a flat-colored
                // band, so it reads as a glow rather than a painted line.
                float hubRingDist = abs(dist - hubRadius);
                float hubRing = 1.0 - smoothstep(0.0, 0.06, hubRingDist);
                float hubRingCore = 1.0 - smoothstep(0.0, 0.015, hubRingDist);
                color += railColor * hubRing * 0.35;
                color += hubRingCore * 0.4;

                // Static docking arms — a few asymmetric rectangular
                // booms (not evenly spaced, not a full ring) ending in a
                // small module block, fixed regardless of actual neighbor
                // connectivity so even an isolated Tradelane hex reads as
                // a built structure. Deliberately NOT a symmetric
                // spoke-and-rim pattern — that read as a wagon wheel.
                // Per-hex jitter (hashed from hexCenter) on both angle
                // and length so no two Tradelane hexes look identical.
                const int armCount = 3;
                const float armBaseAngles[3] = { 0.2618, 1.9199, 4.1888 }; // 15deg, 110deg, 240deg — irregular spacing

                [unroll]
                for (int arm = 0; arm < armCount; arm++)
                {
                    float armHash = StarBoundHash2(hexCenter + float(arm) * 7.0 + 9.0).x;
                    float armAngle = armBaseAngles[arm] + (armHash - 0.5) * 0.4;
                    float armLength = hubRadius + 0.16 + armHash * 0.14;

                    float c = cos(-armAngle);
                    float s = sin(-armAngle);
                    float2 rotated = float2(localPos.x * c - localPos.y * s, localPos.x * s + localPos.y * c);

                    float beamHalfWidth = 0.045;
                    float inBeam = smoothstep(hubRadius * 0.9, hubRadius, rotated.x) *
                        (1.0 - smoothstep(armLength - 0.03, armLength, rotated.x)) *
                        (1.0 - smoothstep(beamHalfWidth, beamHalfWidth + 0.02, abs(rotated.y)));
                    color = lerp(color, structureColor, inBeam * 0.9);

                    // Small module block at the boom's tip, with a soft
                    // subdued light (not a flat saturated blob) rather
                    // than the earlier hard-edged, uniformly bright dot.
                    float2 tip = float2(armLength, 0.0);
                    float2 tipLocal = rotated - tip;
                    float moduleBlock = smoothstep(0.05, 0.03, abs(tipLocal.x)) * smoothstep(0.045, 0.025, abs(tipLocal.y));
                    color = lerp(color, structureColor * 1.1, moduleBlock);

                    float tipDist = length(tipLocal);
                    float lightBlink = 0.4 + 0.25 * sin(_Time.y * 1.6 + armHash * STARBOUND_TWO_PI);
                    float softLight = exp(-tipDist * tipDist * 400.0);
                    color += railColor * softLight * lightBlink * 0.6;
                }

                // One glowing conduit per connected neighbor direction,
                // running from just outside the hub out to the hex edge —
                // a bright thin core with a softer, wider colored halo
                // around it (like a real light source) rather than one
                // flat-width flat-colored band. Per-hex phase offset
                // (hashed from hexCenter) so different lane segments
                // don't all pulse in lockstep.
                float phase = StarBoundHash2(hexCenter + 3.3).x * STARBOUND_TWO_PI;
                float pulse = 0.55 + 0.45 * sin(dist * 10.0 - _Time.y * pulseSpeed + phase);

                float railHalo = 0.0;
                float railCore = 0.0;
                for (int dir = 0; dir < 6; dir++)
                {
                    if ((connectionMask & (1 << dir)) == 0)
                        continue;

                    float dirAngle = StarBoundHexDirectionAngles[dir];
                    float angleDiff = abs(atan2(sin(angle - dirAngle), cos(angle - dirAngle)));
                    float radialMask = smoothstep(hubRadius * 0.85, hubRadius * 1.15, dist);
                    railHalo = max(railHalo, (1.0 - smoothstep(railWidth * 0.3, railWidth, angleDiff)) * radialMask);
                    railCore = max(railCore, (1.0 - smoothstep(railWidth * 0.06, railWidth * 0.18, angleDiff)) * radialMask);
                }
                color += railColor * railHalo * lerp(0.25, 0.5, pulse);
                color += railCore * lerp(0.3, 0.6, pulse);

                return color;
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                int connectionMask = (int)round(_ConnectionMask);
                float3 fieldColor = StarBoundTradelane(IN.localPos, IN.worldPos, IN.hexCenter, connectionMask, _HubRadius, _RailWidth, _PulseSpeed, _StructureColor.rgb, _Color.rgb);

                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
