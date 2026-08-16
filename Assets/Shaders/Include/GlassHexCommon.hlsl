// Shared glass-hex look (see GlassHex.shader) — factored out so
// per-terrain effect shaders (AsteroidFlow.shader, and future
// mines/wormhole/tradelane/planet shaders) can reuse the same base
// translucent/faceted look instead of duplicating the math.
#ifndef STARBOUND_GLASS_HEX_COMMON_INCLUDED
#define STARBOUND_GLASS_HEX_COMMON_INCLUDED

#define STARBOUND_TWO_PI 6.28318530718

struct GlassHexVaryings
{
    float4 positionHCS : SV_POSITION;
    float2 localPos : TEXCOORD0; // (uv - 0.5) * 2 — see HexMeshFactory
    float2 worldPos : TEXCOORD1; // for effects that need continuity across tiles (e.g. flow)
};

// Fake fresnel for a flat top-down tile: brightens/tints toward the
// hex's edge based on distance from center rather than surface normal.
float StarBoundHexRim(float2 localPos, float rimPower)
{
    float dist = length(localPos);
    return pow(saturate(dist), rimPower);
}

// Six wedges matching the mesh's own triangle fan — alternating shading
// gives a faceted "cut glass" look.
float StarBoundHexFacetShade(float2 localPos, float facetStrength)
{
    float angle = atan2(localPos.y, localPos.x);
    float wedge = frac((angle / STARBOUND_TWO_PI) * 6.0);
    float facet = abs(wedge - 0.5) * 2.0;
    return lerp(1.0, 0.85, facet * facetStrength);
}

float3 StarBoundGlassColor(float3 baseColor, float3 rimColor, float rim, float facetShade)
{
    float3 color = baseColor * facetShade;
    // Strong edge glint (was 0.2) — this is the visual cue that reads as
    // a HUD-style outline around each hex, not just a colored panel.
    return lerp(color, rimColor, rim * 0.4);
}

float StarBoundGlassAlpha(float glassAlpha, float rim, float colorAlpha)
{
    // Tint opacity leans heavily on the rim term (was 0.1) — most of a
    // hex's fill should stay a faint wash over the starfield, with the
    // tint concentrating into a solid-looking outline near the edge.
    return saturate(glassAlpha + rim * 0.4) * colorAlpha;
}

// No texture assets exist in the project (see TerrainMaterials) — a
// hashed pseudo-random point stands in for noise/scatter everywhere a
// per-terrain effect shader needs one (Worley cell jitter, starfield
// placement, etc).
float2 StarBoundHash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

// Sparse procedural starfield, sampled purely from world position with
// no tunable parameters — every hex on the map calls this identically,
// so the field is genuinely one continuous galaxy behind the glass, not
// a per-tile pattern that happens to look similar. This is what makes
// the map read as "glass overlaid on a living galaxy" rather than each
// hex being its own self-contained scene — see GlassHex.shader and
// AsteroidFlow.shader, which both composite this in as their backdrop.
float3 StarBoundStarfield(float2 worldPos)
{
    const float scale = 6.0;
    const float presentThreshold = 0.72; // roughly 28% of cells have a star

    float2 cellPos = worldPos * scale;
    float2 cell = floor(cellPos);
    float2 localPos = frac(cellPos);

    float2 jitter = StarBoundHash2(cell);
    float present = step(presentThreshold, StarBoundHash2(cell + 91.7).x);
    float dist = length(localPos - jitter);

    float sizeHash = StarBoundHash2(cell + 3.1).x;
    float brightnessHash = StarBoundHash2(cell + 7.7).x;
    float flickerHash = StarBoundHash2(cell + 12.3).x;
    float tintHash = StarBoundHash2(cell + 21.9).x;

    float size = lerp(0.015, 0.06, sizeHash);
    float brightness = lerp(0.35, 1.0, brightnessHash);
    brightness *= lerp(0.75, 1.0, 0.5 + 0.5 * sin(_Time.y * lerp(0.4, 1.6, flickerHash) + flickerHash * STARBOUND_TWO_PI));

    float glow = smoothstep(size, 0.0, dist) * brightness * present;

    float3 tint = lerp(float3(0.7, 0.82, 1.0), float3(1.0, 0.88, 0.72), tintHash);
    return glow * tint;
}

#endif
