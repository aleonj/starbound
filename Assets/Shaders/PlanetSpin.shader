// PlanetOrStarport-terrain variant of the glass-hex look (see
// GlassHex.shader / Include/GlassHexCommon.hlsl) — a lit, spinning
// banded sphere occupying most of the hex, with the shared starfield
// showing through the margin around it (same "glass overlay on a
// living galaxy" treatment every other terrain gets at its hex edge).
// Starport-specific visuals are deliberately NOT part of this shader —
// see [Map] Starport visual distinction, split out for its own design
// discussion.
//
// The sphere is faked entirely from the hex's local UV-space position
// (see HexMeshFactory's UVs): treat distance-from-center as a sphere
// radius and reconstruct a 3D point on its surface via
// z = sqrt(planetRadius^2 - x^2 - y^2), the same trick used for
// AsteroidFlow's dome shading. Rotating that 3D point around the
// vertical axis over time — and sampling the surface pattern from the
// ROTATED point rather than the original one — is what makes bands
// appear to scroll and features appear/disappear at the limb, i.e.
// actually look like a spinning sphere rather than a static texture
// with animated UVs.
Shader "StarBound/PlanetSpin"
{
    Properties
    {
        _Color("Planet Color", Color) = (1, 1, 1, 1)
        _BandColor("Band Color", Color) = (0.85, 0.95, 0.6, 1)
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 30)) = 20
        _FacetStrength("Facet Strength", Range(0, 1)) = 0.25
        _MaxPlanetRadius("Max Planet Radius", Range(0.2, 0.6)) = 0.45
        _RotationSpeed("Rotation Speed", Float) = 0.15
        _BandFrequency("Band Frequency", Float) = 7
        _HueVariation("Hue Variation", Range(0, 1)) = 0.5
        _AtmosphereColor("Atmosphere Color", Color) = (0.7, 0.9, 1.0, 1)
        _AtmosphereStrength("Atmosphere Strength", Range(0, 1)) = 0.35
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
                float4 _BandColor;
                float4 _RimColor;
                float _RimPower;
                float _FacetStrength;
                float _MaxPlanetRadius;
                float _RotationSpeed;
                float _BandFrequency;
                float _HueVariation;
                float4 _AtmosphereColor;
                float _AtmosphereStrength;
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

            // Every value below is derived from a hash of hexCenter (this
            // tile's own world-space center — constant across the whole
            // tile, see GlassHexCommon.hlsl), not from a shader property.
            // All planet hexes share one material (TerrainMaterials caches
            // per TerrainType), so per-instance variety has to come from
            // "which world position is this" rather than a tunable that'd
            // be identical for every planet on the map.
            float3 StarBoundPlanetSurface(float2 localPos, float2 hexCenter, float maxPlanetRadius, float rotationSpeed, float bandFrequency, float hueVariation, float3 bandColorA, float3 bandColorB, float3 atmosphereColor, float atmosphereStrength)
            {
                float sizeHash = StarBoundHash2(hexCenter + 5.1).x;
                float hueHash = StarBoundHash2(hexCenter + 13.7).x;
                float freqHash = StarBoundHash2(hexCenter + 19.3).x;
                float phaseHash = StarBoundHash2(hexCenter + 27.9).x;
                float speedHash = StarBoundHash2(hexCenter + 33.1).x;
                float grainSeed = StarBoundHash2(hexCenter + 41.7).x * 100.0;

                // Every planet noticeably smaller than the old fixed size,
                // and no two planets the same size.
                float planetRadius = maxPlanetRadius * lerp(0.5, 1.0, sizeHash);

                float dist = length(localPos);
                float edge = 0.02;
                float planetMask = 1.0 - smoothstep(planetRadius - edge, planetRadius + edge, dist);
                if (planetMask <= 0.0)
                    return float3(-1.0, -1.0, -1.0); // sentinel: not on the planet

                float radialDist = saturate(dist / planetRadius);
                float2 radialDir = dist > 0.0001 ? localPos / dist : float2(0.0, 0.0);
                // Sphere surface reconstructed from the flat top-down hex
                // — z is the "toward camera" depth at this point on a
                // sphere of the given radius.
                float z = planetRadius * sqrt(saturate(1.0 - radialDist * radialDist));
                float3 spherePoint = float3(localPos, z);

                // Spin around the vertical axis: only the surface pattern
                // sampling below uses this rotated point, not the mask or
                // the lighting normal — the sphere's silhouette and where
                // the light falls on it shouldn't rotate, only its bands.
                // Phase offset (not just speed) so planets aren't all
                // showing the same face at the same moment.
                float angle = _Time.y * rotationSpeed * lerp(0.7, 1.4, speedHash) + phaseHash * STARBOUND_TWO_PI;
                float3 rotated;
                rotated.x = spherePoint.x * cos(angle) - spherePoint.z * sin(angle);
                rotated.z = spherePoint.x * sin(angle) + spherePoint.z * cos(angle);
                rotated.y = spherePoint.y;

                // Extra phase terms on the band pattern (keyed by grainSeed
                // so it varies per planet) give the band edges a swirly,
                // storm-like wobble instead of dead-straight stripes.
                float thisBandFrequency = bandFrequency * lerp(0.6, 1.7, freqHash);
                float bandPattern = sin(rotated.y * thisBandFrequency + sin(rotated.x * 2.3 + grainSeed) * 0.8 + sin(rotated.z * 1.7 - grainSeed) * 0.6);
                float bandT = bandPattern * 0.5 + 0.5;

                // Broad turbulence patches — the sample position is warped
                // before flooring into cells, so patches come out as
                // organic blobs rather than a visible blocky grid.
                float2 warpedTurb = rotated.xy + float2(sin(rotated.y * 3.1 + grainSeed) * 0.3, sin(rotated.x * 2.7 - grainSeed) * 0.3);
                float turbulence = StarBoundHash2(floor(warpedTurb * 5.0) + grainSeed).x;

                // Finer grain layered on top for close-in surface detail —
                // without this the broad patches alone still read fairly
                // smooth/flat.
                float grain = StarBoundHash2(floor(rotated.xy * 18.0) + grainSeed * 1.7).x;

                // Hue-shift both band colors by the same per-planet amount
                // so every planet is a genuinely different color, not just
                // a different band pattern on the same green/yellow pair.
                float hueShift = (hueHash - 0.5) * hueVariation;
                float3 hsvA = StarBoundRgbToHsv(bandColorA);
                float3 hsvB = StarBoundRgbToHsv(bandColorB);
                hsvA.x = frac(hsvA.x + hueShift);
                hsvB.x = frac(hsvB.x + hueShift);
                float3 shiftedA = StarBoundHsvToRgb(hsvA);
                float3 shiftedB = StarBoundHsvToRgb(hsvB);

                float3 surfaceColor = lerp(shiftedA, shiftedB, bandT);
                surfaceColor *= lerp(0.7, 1.3, turbulence);
                surfaceColor *= lerp(0.9, 1.1, grain);

                // Lit sphere: normal built from the UN-rotated local
                // position, same technique as AsteroidFlow's dome shading
                // — bright facing the fixed light direction, dark toward
                // the limb, independent of the spin above.
                float3 normal = normalize(float3(radialDir * radialDist, sqrt(saturate(1.0 - radialDist * radialDist))));
                float3 lightDir = normalize(float3(0.45, 0.6, 0.55));
                float lightFactor = saturate(dot(normal, lightDir));
                surfaceColor *= lerp(0.2, 1.3, lightFactor);

                // Thin atmospheric glow right at the sphere's own limb.
                float atmosphere = smoothstep(planetRadius * 0.82, planetRadius, dist);
                surfaceColor = lerp(surfaceColor, atmosphereColor, atmosphere * atmosphereStrength);

                return surfaceColor;
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                float3 starfield = StarBoundStarfield(IN.worldPos);
                float3 planetColor = StarBoundPlanetSurface(IN.localPos, IN.hexCenter, _MaxPlanetRadius, _RotationSpeed, _BandFrequency, _HueVariation, _Color.rgb, _BandColor.rgb, _AtmosphereColor.rgb, _AtmosphereStrength);

                // Sentinel from StarBoundPlanetSurface — this pixel is in
                // the margin between the planet's own edge and the hex's
                // edge, so it shows the starfield straight through, same
                // as empty space on an Asteroids hex.
                float3 fieldColor = planetColor.r < 0.0 ? starfield : planetColor;

                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
