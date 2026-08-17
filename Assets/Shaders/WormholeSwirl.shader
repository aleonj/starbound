// Wormhole-terrain variant of the glass-hex look (see GlassHex.shader /
// Include/GlassHexCommon.hlsl) — a swirling vortex with a dark event
// horizon and spiral arms, plus a gravitational-lensing trick: the
// world-space position fed into the shared starfield is itself warped
// (rotated more, the closer to the wormhole's center) before sampling,
// so real background stars visibly bend and swirl in toward the
// wormhole instead of the vortex being a flat icon drawn over them.
// The lensing fades to zero by the hex edge, so it matches the plain
// (unwarped) starfield every other hex reveals — no seam.
Shader "StarBound/WormholeSwirl"
{
    Properties
    {
        _Color("Ring Color", Color) = (1, 1, 1, 1)
        _CoreColor("Core Color", Color) = (0.03, 0.01, 0.06, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 30)) = 20
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
        _RotationSpeed("Rotation Speed", Float) = 0.4
        _ArmCount("Arm Count", Float) = 3
        _LensStrength("Lens Strength", Range(0, 1)) = 0.4
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
                float4 _CoreColor;
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
                float _RotationSpeed;
                float _ArmCount;
                float _LensStrength;
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

            float3 StarBoundWormhole(float2 localPos, float2 worldPos, float2 hexCenter, float rotationSpeed, float armCount, float lensStrength, float3 ringColor, float3 coreColor)
            {
                // Per-wormhole spin direction/speed — with typically only
                // a couple of wormholes on a map, still worth them not
                // being identical clones of each other.
                float spinHash = StarBoundHash2(hexCenter + 7.3).x;
                float spinDirection = spinHash > 0.5 ? 1.0 : -1.0;
                float thisSpeed = rotationSpeed * lerp(0.8, 1.3, StarBoundHash2(hexCenter + 61.1).x) * spinDirection;

                float dist = length(localPos);
                float angle = atan2(localPos.y, localPos.x);

                // Lensing: warp the sample position fed into the shared
                // starfield, rotating it more sharply the closer to
                // center — fades to exactly zero by the hex edge
                // (lensAmount -> 0), so it matches every other hex's
                // plain starfield with no visible seam.
                float lensAmount = saturate(1.0 - dist / 0.9);
                float swirl = lensAmount * (lensAmount * 3.0 + _Time.y * thisSpeed * 0.5);
                float2 rotatedLocal = float2(
                    localPos.x * cos(swirl) - localPos.y * sin(swirl),
                    localPos.x * sin(swirl) + localPos.y * cos(swirl));
                float2 warpOffset = (rotatedLocal - localPos) * lensStrength;
                float3 lensedStarfield = StarBoundStarfield(worldPos + warpOffset);

                // Dark event horizon at the very center.
                float horizonRadius = 0.22;
                float horizonMask = 1.0 - smoothstep(horizonRadius - 0.03, horizonRadius, dist);

                // Spiral arms winding into the horizon — twist increases
                // sharply as distance shrinks, same "1/dist" trick real
                // spiral-galaxy shaders use.
                float spiralTwist = 1.4 / max(dist, 0.12) + _Time.y * thisSpeed;
                float armPattern = sin((angle + spiralTwist) * armCount);
                float armT = saturate(armPattern * 0.5 + 0.5);
                float ringZone = smoothstep(0.6, 0.15, dist) * (1.0 - horizonMask);

                float3 color = lerp(lensedStarfield, ringColor, armT * ringZone);
                color = lerp(color, coreColor, horizonMask);
                return color;
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                float3 fieldColor = StarBoundWormhole(IN.localPos, IN.worldPos, IN.hexCenter, _RotationSpeed, _ArmCount, _LensStrength, _Color.rgb, _CoreColor.rgb);

                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
