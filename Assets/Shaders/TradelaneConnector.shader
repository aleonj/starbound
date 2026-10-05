// Tradelane-terrain variant of the glass-hex look (see GlassHex.shader /
// Include/GlassHexCommon.hlsl) — glowing energy conduits ("rails")
// extending toward whichever of the hex's 6 edges lead to another
// Tradelane hex, so a chain of Tradelane hexes reads as one connected
// lane rather than isolated tiles. A hex where the lane runs straight
// through (exactly two connections, directly opposite each other) is
// just the tube itself; every other hex — a dead end, a direction
// change, an isolated tile, or a junction — gets a small "construction"
// at its center instead: two tilted rings spinning like a gyroscope
// around a pulsing energy ball. See StarBoundSpinningRing (shared, in
// GlassHexCommon.hlsl) and StarBoundTradelane below; which case a hex
// falls into is derived entirely from its connection mask, not a
// separate per-hex flag.
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
                // terrain it is. Only the construction/tube content below
                // is Tradelane-specific, drawn on top of it.
                float3 color = StarBoundStarfield(worldPos);

                // A hex's role is derived entirely from its connection
                // mask — no extra per-hex C# data needed. Exactly two
                // connections that are directly opposite each other
                // (dir and dir+3) is a straight-through segment; anything
                // else (a dead end, a direction change, an isolated tile,
                // or a 3+-way junction) is a "construction" hex and gets
                // the gyroscope instead of a plain tube.
                int connectionCount = 0;
                int firstDir = -1;
                [unroll]
                for (int d = 0; d < 6; d++)
                {
                    if ((connectionMask & (1 << d)) != 0)
                    {
                        connectionCount++;
                        if (firstDir < 0)
                            firstDir = d;
                    }
                }
                bool isStraight = connectionCount == 2 && (connectionMask & (1 << ((firstDir + 3) % 6))) != 0;
                // Declared at this scope (not inside the block below) so
                // the rail conduits further down can also start just
                // outside it on a construction hex, instead of the
                // smaller gyroscope radius they used to start from.
                float housingRadius = hubRadius * 1.45;

                if (!isStraight)
                {
                    // Two rings at different fixed tilts (not perpendicular
                    // — a touch off keeps the crossing pattern from looking
                    // perfectly symmetric/mechanical) with independent
                    // per-hex hashed spin speed and phase, so constructions
                    // across the map don't all tumble in lockstep.
                    float speedHashA = StarBoundHash2(hexCenter + 51.3).x;
                    float speedHashB = StarBoundHash2(hexCenter + 67.9).x;
                    float phaseA = StarBoundHash2(hexCenter + 71.1).x * STARBOUND_TWO_PI;
                    float phaseB = StarBoundHash2(hexCenter + 83.7).x * STARBOUND_TWO_PI;
                    float spinA = _Time.y * lerp(0.9, 1.5, speedHashA) + phaseA;
                    float spinB = _Time.y * lerp(-1.5, -0.9, speedHashB) + phaseB;

                    color += StarBoundSpinningRing(localPos, hubRadius, 0.05, 0.35, spinA, structureColor);
                    color += StarBoundSpinningRing(localPos, hubRadius, 0.05, 1.25, spinB, railColor);

                    // Stable housing — a static outer ring plus a few
                    // short mounting struts anchoring it from outside, so
                    // the spinning rings read as a mechanism actually held
                    // in place rather than floating bare in space. Fixed
                    // regardless of spin; drawn after the rings (an opaque
                    // lerp, not additive) so a strut visibly occludes a
                    // ring passing behind it where they cross, like a real
                    // frame in front of the moving parts. Per-hex hashed
                    // strut angle/length (from hexCenter), same irregular-
                    // spacing spirit as the old docking arms this
                    // replaced, so no two constructions look identical.
                    float housingRingDist = abs(dist - housingRadius);
                    float housingRing = 1.0 - smoothstep(0.012, 0.03, housingRingDist);
                    color = lerp(color, structureColor * 1.1, housingRing);

                    const int strutCount = 3;
                    const float strutBaseAngles[3] = { 0.4014, 2.4435, 4.4855 }; // ~120deg apart

                    [unroll]
                    for (int i = 0; i < strutCount; i++)
                    {
                        float strutHash = StarBoundHash2(hexCenter + float(i) * 5.0 + 101.0).x;
                        float strutAngle = strutBaseAngles[i] + (strutHash - 0.5) * 0.2;

                        float cs = cos(-strutAngle);
                        float sn = sin(-strutAngle);
                        float2 rotated = float2(localPos.x * cs - localPos.y * sn, localPos.x * sn + localPos.y * cs);

                        float strutInner = hubRadius * 0.55;
                        float strutHalfWidth = 0.026;
                        float inStrut = smoothstep(strutInner - 0.02, strutInner, rotated.x) *
                            (1.0 - smoothstep(housingRadius - 0.02, housingRadius, rotated.x)) *
                            (1.0 - smoothstep(strutHalfWidth, strutHalfWidth + 0.015, abs(rotated.y)));
                        color = lerp(color, structureColor, inStrut * 0.85);

                        // Bolt-like joint blocks where the strut meets the
                        // outer ring and its own inner mounting point.
                        float2 outerJoint = rotated - float2(housingRadius, 0.0);
                        float outerBlock = smoothstep(0.045, 0.025, abs(outerJoint.x)) * smoothstep(0.04, 0.02, abs(outerJoint.y));
                        float2 innerJoint = rotated - float2(strutInner, 0.0);
                        float innerBlock = smoothstep(0.04, 0.02, abs(innerJoint.x)) * smoothstep(0.035, 0.018, abs(innerJoint.y));
                        color = lerp(color, structureColor * 1.2, max(outerBlock, innerBlock));
                    }

                    // Energy ball at the center — a bright white-hot core
                    // fading into a softer halo tinted between the two
                    // ring colors, pulsing rather than static so it reads
                    // as a contained, active power source.
                    float ballPulse = 0.6 + 0.4 * sin(_Time.y * 3.0 + StarBoundHash2(hexCenter + 91.3).x * STARBOUND_TWO_PI);
                    float ballCore = exp(-dist * dist * 60.0) * ballPulse;
                    float ballHalo = exp(-dist * dist * 14.0) * ballPulse;
                    color += float3(1.0, 1.0, 1.0) * ballCore * 0.9;
                    color += lerp(structureColor, railColor, 0.5) * ballHalo * 0.6;
                }

                // One glowing conduit per connected neighbor direction —
                // a bright thin core, a pair of thinner highlight lines
                // straddling it (the near/far rim of a lit cylinder's
                // cross-section, which is what pushes this from "a
                // glowing flat band" to "a glowing tube"), and a softer
                // wider halo around all of it. On a straight segment the
                // radial mask starts right at the hex center (no
                // construction there to leave a gap for), so the tube
                // reads as one continuous run edge to edge; on a
                // construction hex it still starts just outside the
                // housing structure, not the bare gyroscope radius, so a
                // rail doesn't appear to cut through the cage. Per-hex
                // phase offset (hashed from hexCenter) so different lane
                // segments don't all pulse in lockstep.
                float phase = StarBoundHash2(hexCenter + 3.3).x * STARBOUND_TWO_PI;
                float pulse = 0.55 + 0.45 * sin(dist * 10.0 - _Time.y * pulseSpeed + phase);
                float radialMask = isStraight ? 1.0 : smoothstep(housingRadius * 0.95, housingRadius * 1.2, dist);

                float railHalo = 0.0;
                float railCore = 0.0;
                float railWall = 0.0;
                for (int dir = 0; dir < 6; dir++)
                {
                    if ((connectionMask & (1 << dir)) == 0)
                        continue;

                    float dirAngle = StarBoundHexDirectionAngles[dir];
                    float angleDiff = abs(atan2(sin(angle - dirAngle), cos(angle - dirAngle)));
                    railHalo = max(railHalo, (1.0 - smoothstep(railWidth * 0.3, railWidth, angleDiff)) * radialMask);
                    railCore = max(railCore, (1.0 - smoothstep(railWidth * 0.06, railWidth * 0.18, angleDiff)) * radialMask);
                    float wallDiff = abs(angleDiff - railWidth * 0.55);
                    railWall = max(railWall, (1.0 - smoothstep(0.0, railWidth * 0.08, wallDiff)) * radialMask);
                }
                color += railColor * railHalo * lerp(0.25, 0.5, pulse);
                color += railColor * railWall * lerp(0.35, 0.6, pulse) * 0.7;
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
