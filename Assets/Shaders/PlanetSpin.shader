// PlanetOrStarport-terrain variant of the glass-hex look (see
// GlassHex.shader / Include/GlassHexCommon.hlsl) — a lit, spinning
// banded sphere occupying most of the hex, with the shared starfield
// showing through the margin around it (same "glass overlay on a
// living galaxy" treatment every other terrain gets at its hex edge).
// Starport hexes share this same material (see TerrainMaterials) but get
// an entirely different look — StarBoundStarportStructure below, a small
// docked station rather than a living sphere — selected per-instance via
// _IsStarport (see HexTileView.SetIsStarport), the same "one shared
// material, per-instance branch" pattern TradelaneConnector.shader uses
// for its straight-tube-vs-construction split.
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
        _StarportStructureColor("Starport Structure Color", Color) = (0.6, 0.62, 0.68, 1)
        _StarportLightColor("Starport Light Color", Color) = (0.95, 0.85, 0.5, 1)
        _IsStarport("Is Starport (internal)", Float) = 0
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
                float4 _StarportStructureColor;
                float4 _StarportLightColor;
                float _IsStarport;
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

            // Starport variant — a small docked station instead of a
            // living planet sphere: a paneled boxy hub, two flat
            // grid-textured solar panel arrays, a slowly sweeping radar
            // antenna, a few surface greebles, radial docking arms with
            // blinking tip lights, and alternating red/green nav lights
            // at the panel tips. A first pass reused
            // StarBoundSpinningRing (the same ellipse-spin trick
            // TradelaneConnector's gyroscope uses) for a docking ring
            // here too, which read as too similar to that construction —
            // dropped entirely in favor of the panels/antenna/greebles
            // below, none of which share that technique. Same
            // "everything derived from a hexCenter hash" reasoning as
            // StarBoundPlanetSurface above — one shared material, so
            // per-instance variety has to come from world position, not a
            // tunable. Same sentinel-return convention too, for the same
            // reason (margin outside the structure shows the starfield
            // through).
            // Cheap fake-3D bevel shading for a flat rectangular element,
            // lit from the same fixed direction as StarBoundPlanetSurface's
            // sphere lighting above (kept consistent so Planets and
            // Starports read as lit by the same "sun" on one map).
            // `coord` is the pixel's position relative to the shape's own
            // center, `halfExtents` its half-width/half-height — returns
            // roughly 1 toward the lit corner and -1 toward the shadowed
            // one, the same "brighter facing the light, darker away" cue
            // every other pseudo-3D surface here uses, just applied to a
            // flat plate instead of a reconstructed sphere/dome normal.
            // Without this every panel/hub/greeble was a single flat
            // color — reads as a decal painted on the hex, not a raised
            // object.
            float StarBoundBevelLight(float2 coord, float2 halfExtents)
            {
                const float2 lightDir2D = float2(0.6, 0.8);
                float2 t = clamp(coord / max(halfExtents, 0.0001), -1.0, 1.0);
                return dot(t, lightDir2D);
            }

            float3 StarBoundStarportStructure(float2 localPos, float2 hexCenter, float maxPlanetRadius, float3 structureColor, float3 lightColor)
            {
                float sizeHash = StarBoundHash2(hexCenter + 5.1).x;
                float speedHash = StarBoundHash2(hexCenter + 33.1).x;
                float phaseHash = StarBoundHash2(hexCenter + 27.9).x;
                float panelAngleHash = StarBoundHash2(hexCenter + 71.7).x;
                // Offsets the fine-grain noise field per hex (see its use
                // below) so every Starport doesn't show the identical
                // speckle pattern.
                float grainSeed = StarBoundHash2(hexCenter + 111.0).x * 50.0;

                float hubRadius = maxPlanetRadius * lerp(0.32, 0.42, sizeHash);
                // Panels are the widest-reaching element (see below) —
                // the footprint has to clear their full extent, not just
                // the hub, or they'd get clipped by the sentinel cutoff.
                float footprint = hubRadius * 2.9;

                float dist = length(localPos);
                if (dist > footprint)
                    return float3(-1.0, -1.0, -1.0); // sentinel: outside the structure

                // Paneled boxy hub — Chebyshev distance gives a rounded-
                // square footprint rather than a circular one, reading as
                // built rather than grown.
                float hubDist = max(abs(localPos.x), abs(localPos.y));
                float hubMask = 1.0 - smoothstep(hubRadius - 0.02, hubRadius, hubDist);
                float2 gridUV = localPos * 9.0;
                float2 gridFrac = abs(frac(gridUV) - 0.5);
                float gridLine = 1.0 - smoothstep(0.0, 0.05, min(gridFrac.x, gridFrac.y));
                // A small bright rivet at every panel-seam intersection —
                // barely visible at a normal zoom, but this is exactly the
                // kind of close-up-only detail that keeps the hub from
                // reading as a flat painted grid once a player zooms in.
                float rivet = 1.0 - smoothstep(0.02, 0.06, length(gridFrac - 0.5));
                float hubBevel = StarBoundBevelLight(localPos, float2(hubRadius, hubRadius));
                float3 color = structureColor * lerp(1.0, 0.7, gridLine) * lerp(0.6, 1.3, hubBevel * 0.5 + 0.5) * hubMask;
                color += structureColor * rivet * 0.6 * hubMask;

                // A handful of small offset surface blocks near the hub
                // ("greebles") purely for visual texture/interest — each
                // one a different size/position/shade per hex.
                const int greebleCount = 4;
                [unroll]
                for (int g = 0; g < greebleCount; g++)
                {
                    float angleHash = StarBoundHash2(hexCenter + float(g) * 13.0 + 501.0).x;
                    float distHash = StarBoundHash2(hexCenter + float(g) * 17.0 + 601.0).x;
                    float sizeHashG = StarBoundHash2(hexCenter + float(g) * 23.0 + 701.0).x;
                    float2 greeblePos = float2(cos(angleHash * STARBOUND_TWO_PI), sin(angleHash * STARBOUND_TWO_PI)) * hubRadius * lerp(0.5, 0.85, distHash);
                    float greebleSize = hubRadius * lerp(0.06, 0.13, sizeHashG);
                    float2 greebleCoord = localPos - greeblePos;
                    float greebleMask = 1.0 - smoothstep(greebleSize * 0.85, greebleSize, max(abs(greebleCoord.x), abs(greebleCoord.y)));
                    float greebleBevel = StarBoundBevelLight(greebleCoord, float2(greebleSize, greebleSize));
                    color = lerp(color, structureColor * lerp(0.7, 1.15, angleHash) * lerp(0.65, 1.3, greebleBevel * 0.5 + 0.5), greebleMask);
                }

                // Two flat, grid-textured solar panel arrays on opposite
                // sides of the hub, at a per-hex angle — a static, clearly
                // man-made detail with no spin at all, unlike the
                // gyroscope/docking-ring look this replaced.
                float panelAngle = panelAngleHash * STARBOUND_TWO_PI;
                float pc = cos(-panelAngle);
                float ps = sin(-panelAngle);
                float2 panelAxis = float2(localPos.x * pc - localPos.y * ps, localPos.x * ps + localPos.y * pc);

                float panelGap = hubRadius * 1.1;
                float panelHalfLength = hubRadius * 1.5;
                float panelHalfWidth = hubRadius * 0.45;
                float panelCoordX = abs(panelAxis.x) - panelGap;

                float panelMask = step(0.0, panelCoordX) *
                    (1.0 - smoothstep(panelHalfLength - 0.02, panelHalfLength, panelCoordX)) *
                    (1.0 - smoothstep(panelHalfWidth - 0.02, panelHalfWidth, abs(panelAxis.y)));
                float2 cellUV = float2(panelCoordX, panelAxis.y) * 7.0;
                float2 cellFrac = abs(frac(cellUV) - 0.5);
                float cellLine = 1.0 - smoothstep(0.0, 0.06, min(cellFrac.x, cellFrac.y));
                // Each photovoltaic cell gets its own slight brightness —
                // a uniform two-tone grid reads as a printed texture up
                // close; real solar cells are never perfectly uniform.
                float cellHash = StarBoundHash2(floor(cellUV)).x;
                // A darker bezel frame right at the panel's own outer
                // edge, on top of everything else — reads as an actual
                // mounted plate with a border, not a texture that just
                // stops.
                float bezelDist = min(panelHalfLength - panelCoordX, panelHalfWidth - abs(panelAxis.y));
                float bezel = 1.0 - smoothstep(0.0, hubRadius * 0.06, bezelDist);
                // Bevel across the panel's own extent (not the whole hex)
                // — reads as one flat plate tilted toward the light,
                // rather than a texture painted flush with the hub.
                float2 panelCenterCoord = float2(panelCoordX - panelHalfLength * 0.5, panelAxis.y);
                float panelBevel = StarBoundBevelLight(panelCenterCoord, float2(panelHalfLength * 0.5, panelHalfWidth));
                float3 panelColor = lerp(float3(0.12, 0.2, 0.42), float3(0.22, 0.32, 0.58), cellLine)
                    * lerp(0.55, 1.35, panelBevel * 0.5 + 0.5)
                    * lerp(0.8, 1.2, cellHash);
                panelColor = lerp(panelColor, panelColor * 0.45, bezel);
                color = lerp(color, panelColor, panelMask);

                // Thin struts connecting the hub to each panel — shaded
                // like a rounded rod (bright centerline, dimmer toward
                // its own edges) rather than a flat-colored bar, the same
                // cross-section trick TradelaneConnector's tube rails use.
                float strutHalfWidth = hubRadius * 0.1;
                float strutMask = step(hubRadius * 0.9, abs(panelAxis.x)) *
                    (1.0 - smoothstep(panelGap - 0.02, panelGap, abs(panelAxis.x))) *
                    (1.0 - smoothstep(hubRadius * 0.08, strutHalfWidth, abs(panelAxis.y)));
                float strutRodShade = lerp(1.25, 0.65, saturate(abs(panelAxis.y) / strutHalfWidth));
                // Periodic darker bands along its length — reads as a
                // strut assembled from pipe segments rather than one
                // smooth extruded bar, another detail that only earns its
                // keep once a player is zoomed in close.
                float strutJoint = 1.0 - 0.35 * step(0.8, frac(abs(panelAxis.x) / (hubRadius * 0.12)));
                color = lerp(color, structureColor * strutRodShade * strutJoint, strutMask * 0.9);

                // Alternating red/green nav lights at the two panel tips
                // — real running-light convention (port/starboard), and
                // phase-shifted by half a cycle (+pi) so they blink
                // opposite each other rather than together.
                float2 tipA = float2(panelGap + panelHalfLength, 0.0);
                float2 tipB = float2(-(panelGap + panelHalfLength), 0.0);
                float tipDistA = length(panelAxis - tipA);
                float tipDistB = length(panelAxis - tipB);
                float navPulse = _Time.y * 5.0 + phaseHash * STARBOUND_TWO_PI;
                float navGlowA = exp(-tipDistA * tipDistA * 250.0) * (0.6 + 0.4 * sin(navPulse));
                float navGlowB = exp(-tipDistB * tipDistB * 250.0) * (0.6 + 0.4 * sin(navPulse + 3.14159));
                color += float3(1.0, 0.15, 0.15) * navGlowA * 0.8;
                color += float3(0.2, 1.0, 0.3) * navGlowB * 0.8;

                // A slowly sweeping radar antenna — a single rotating arm
                // (not a closed ring) with a small dish at the tip, so
                // the one piece of rotating motion here reads as a
                // sensor sweeping rather than a wheel spinning.
                float antennaAngle = _Time.y * lerp(0.22, 0.35, speedHash) + phaseHash * STARBOUND_TWO_PI;
                float2 antennaDir = float2(cos(antennaAngle), sin(antennaAngle));
                float antennaLength = hubRadius * 1.3;
                float antennaT = clamp(dot(localPos, antennaDir), 0.0, antennaLength);
                float2 antennaClosest = antennaDir * antennaT;
                float antennaDist = length(localPos - antennaClosest);
                float antennaMask = 1.0 - smoothstep(hubRadius * 0.025, hubRadius * 0.05, antennaDist);
                // Same rounded-rod cross-section shading and segment
                // joints as the struts.
                float antennaRodShade = lerp(1.3, 0.7, saturate(antennaDist / (hubRadius * 0.05)));
                float antennaJoint = 1.0 - 0.3 * step(0.8, frac(antennaT / (hubRadius * 0.15)));
                color = lerp(color, structureColor * 1.3 * antennaRodShade * antennaJoint, antennaMask);

                float2 dishCenter = antennaDir * antennaLength;
                float2 dishCoord = localPos - dishCenter;
                float dishDist = length(dishCoord);
                float dishMask = 1.0 - smoothstep(hubRadius * 0.09, hubRadius * 0.13, dishDist);
                float dishBevel = StarBoundBevelLight(dishCoord, float2(hubRadius * 0.13, hubRadius * 0.13));
                // A few radial ribs across the dish face — a plain lit
                // disc reads as a coin, not a dish, once you're close
                // enough to see it isn't just a soft-edged circle. Same
                // "angular wedge" technique as StarBoundHexFacetShade:
                // wedge is 0 at each rib's own center angle, 1 at the
                // midpoint between two ribs.
                float dishAngle = atan2(dishCoord.y, dishCoord.x);
                float dishWedge = abs(frac(dishAngle / STARBOUND_TWO_PI * 6.0) - 0.5) * 2.0;
                float dishRib = 1.0 - smoothstep(0.0, 0.1, dishWedge);
                float3 dishColor = structureColor * 1.4 * lerp(0.6, 1.3, dishBevel * 0.5 + 0.5) * lerp(1.0, 0.7, dishRib);
                color = lerp(color, dishColor, dishMask);
                float dishGlow = exp(-dishDist * dishDist * 300.0) * (0.5 + 0.5 * sin(_Time.y * 4.0));
                color += lightColor * dishGlow * 0.5;

                // A pair of short docking arms with blinking tip lights —
                // same light language as every module/beacon tip
                // elsewhere in this project.
                const int armCount = 2;
                const float armBaseAngles[2] = { 1.1781, 4.3197 }; // roughly perpendicular to the panel axis

                [unroll]
                for (int arm = 0; arm < armCount; arm++)
                {
                    float armHash = StarBoundHash2(hexCenter + float(arm) * 7.0 + 9.0).x;
                    float armAngle = panelAngle + armBaseAngles[arm] + (armHash - 0.5) * 0.2;
                    float armLength = hubRadius * 1.3 + armHash * hubRadius * 0.2;

                    float c = cos(-armAngle);
                    float s = sin(-armAngle);
                    float2 rotated = float2(localPos.x * c - localPos.y * s, localPos.x * s + localPos.y * c);

                    float beamHalfWidth = hubRadius * 0.1;
                    float inBeam = smoothstep(hubRadius * 0.9, hubRadius, rotated.x) *
                        (1.0 - smoothstep(armLength - hubRadius * 0.1, armLength, rotated.x)) *
                        (1.0 - smoothstep(beamHalfWidth, beamHalfWidth + hubRadius * 0.06, abs(rotated.y)));
                    // Same rounded-rod cross-section shading and segment
                    // joints as the struts/antenna.
                    float armRodShade = lerp(1.25, 0.65, saturate(abs(rotated.y) / beamHalfWidth));
                    float armJoint = 1.0 - 0.35 * step(0.8, frac(rotated.x / (hubRadius * 0.12)));
                    color = lerp(color, structureColor * armRodShade * armJoint, inBeam * 0.9);

                    // Small module block at the tip — an anchor for the
                    // light, so it reads as a fixture bolted to the arm
                    // rather than a glow floating just past its end.
                    float2 tip = float2(armLength, 0.0);
                    float2 tipLocal = rotated - tip;
                    float moduleBlock = smoothstep(hubRadius * 0.09, hubRadius * 0.06, abs(tipLocal.x)) * smoothstep(hubRadius * 0.08, hubRadius * 0.05, abs(tipLocal.y));
                    color = lerp(color, structureColor * 1.15, moduleBlock);

                    float tipDist = length(tipLocal);
                    float lightBlink = 0.4 + 0.3 * sin(_Time.y * 2.0 + armHash * STARBOUND_TWO_PI);
                    float softLight = exp(-tipDist * tipDist * 350.0);
                    color += lightColor * softLight * lightBlink * 0.7;
                }

                // Fine per-pixel grain across the whole structure — the
                // "brushed/weathered metal" micro-detail every other
                // surface here now has via per-cell/per-rivet variation,
                // applied broadly so even the plain hub-top areas between
                // grid lines don't look perfectly uniform up close.
                float grain = StarBoundHash2(floor(localPos * 40.0) + grainSeed).x;
                color *= lerp(0.9, 1.08, grain);

                return color;
            }

            float4 Frag(GlassHexVaryings IN) : SV_Target
            {
                float rim = StarBoundHexRim(IN.localPos, _RimPower);
                float facetShade = StarBoundHexFacetShade(IN.localPos, _FacetStrength);

                float3 starfield = StarBoundStarfield(IN.worldPos);
                float3 featureColor = _IsStarport > 0.5
                    ? StarBoundStarportStructure(IN.localPos, IN.hexCenter, _MaxPlanetRadius, _StarportStructureColor.rgb, _StarportLightColor.rgb)
                    : StarBoundPlanetSurface(IN.localPos, IN.hexCenter, _MaxPlanetRadius, _RotationSpeed, _BandFrequency, _HueVariation, _Color.rgb, _BandColor.rgb, _AtmosphereColor.rgb, _AtmosphereStrength);

                // Sentinel from either function above — this pixel is in
                // the margin between the feature's own edge and the hex's
                // edge, so it shows the starfield straight through, same
                // as empty space on an Asteroids hex.
                float3 fieldColor = featureColor.r < 0.0 ? starfield : featureColor;

                float3 finalColor = StarBoundGlassColor(fieldColor, _RimColor.rgb, rim, facetShade);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
