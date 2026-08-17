// Engagement marker — replaces the old flat mini-hex tint (a solid
// tier-colored hexagon covering 35% of the tile) with a small floating
// signal beacon: a rotating dashed ring, four reticle ticks, and a
// pulsing core, all tier-colored. Drawn on the same small hex-canvas
// quad HexTileView already builds (see HexMeshFactory), used purely as
// a transparent UV-mapped canvas — same "procedural shape, transparent
// outside it" approach as ShipCraft.shader — so the terrain shader
// underneath stays fully visible around the beacon instead of being
// covered by a filled color.
Shader "StarBound/EngagementBeacon"
{
    Properties
    {
        _Color("Beacon Color", Color) = (1, 1, 1, 1)
        _PulseSpeed("Pulse Speed", Float) = 1.4
        _RotationSpeed("Ring Rotation Speed", Float) = 0.5
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 localPos : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _PulseSpeed;
                float _RotationSpeed;
            CBUFFER_END

            static const float STARBOUND_TWO_PI = 6.28318530718;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 p = IN.localPos;
                float dist = length(p);
                float angle = atan2(p.y, p.x);
                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);

                // Dark "signal pad" backdrop, drawn behind the ring/ticks/
                // core — without this, the beacon's thin bright elements
                // have no contrast against busy/similarly-toned terrain
                // underneath (a green beacon over a greenish Tradelane
                // hub, for instance, all but disappeared). This guarantees
                // legibility regardless of what terrain sits underneath.
                const float padRadius = 0.8;
                float pad = 1.0 - smoothstep(padRadius - 0.08, padRadius, dist);
                float padAlpha = pad * 0.45;

                // Rotating dashed ring — reads as an active scanning
                // signal rather than a static painted circle. Widened
                // and brightened from the original pass, which read as
                // too subtle.
                const float ringRadius = 0.62;
                const float ringWidth = 0.14;
                float ring = 1.0 - smoothstep(ringWidth * 0.5, ringWidth * 0.5 + 0.03, abs(dist - ringRadius));
                float dashPhase = frac(angle / STARBOUND_TWO_PI * 8.0 - _Time.y * _RotationSpeed);
                ring *= step(0.5, dashPhase);

                // Four reticle ticks poking past the ring at N/E/S/W —
                // reads as a signal/target marker, not a plain ring.
                const float tickHalfWidth = 0.05;
                const float tickInner = ringRadius - 0.1;
                const float tickOuter = ringRadius + 0.2;
                float northTick = step(abs(p.x), tickHalfWidth) * step(tickInner, p.y) * step(p.y, tickOuter);
                float southTick = step(abs(p.x), tickHalfWidth) * step(tickInner, -p.y) * step(-p.y, tickOuter);
                float eastTick = step(abs(p.y), tickHalfWidth) * step(tickInner, p.x) * step(p.x, tickOuter);
                float westTick = step(abs(p.y), tickHalfWidth) * step(tickInner, -p.x) * step(-p.x, tickOuter);
                float ticks = max(max(northTick, southTick), max(eastTick, westTick));

                // Pulsing core+halo, same light technique used everywhere
                // else in the game (MinesHazard/ShipCraft engine glow).
                const float coreRadius = 0.24;
                float core = 1.0 - smoothstep(coreRadius * 0.4, coreRadius * 0.8, dist);
                float halo = 1.0 - smoothstep(coreRadius * 0.6, coreRadius * 1.8, dist);

                float elementsMask = saturate(ring + ticks + core * 1.3 + halo * pulse * 0.7);

                float alpha = saturate(max(padAlpha, elementsMask));
                if (alpha <= 0.0)
                    return float4(0, 0, 0, 0);

                float3 brightColor = _Color.rgb * (1.0 + pulse * 0.4) + _Color.rgb * core * 0.7;
                float3 color = lerp(float3(0, 0, 0), brightColor, elementsMask);

                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
