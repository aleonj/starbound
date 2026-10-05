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
    // World-space position of this tile's own center — constant across
    // every vertex of the tile (it's the object-space origin transformed
    // to world space, not the vertex position), so every pixel of one
    // hex hashes to the same value. Effects that want per-hex-instance
    // variation (e.g. PlanetSpin's size/color/texture) hash this instead
    // of worldPos, which varies per-pixel and would smear the variation
    // across a single tile instead of giving it one consistent identity.
    float2 hexCenter : TEXCOORD2;
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
    // Two earlier attempts at "make the edge read consistently across
    // terrain types" (flat additive, then screen blend) tried to fix
    // this by brightening the blend, which just made every hex look
    // more washed-out/gray. The real issue was never brightness — with
    // _RimPower around 6, StarBoundHexRim is non-negligible over a wide
    // swath of the tile (not just right at the true edge), so the glow
    // blends into and interacts with whatever per-terrain content is
    // underneath it over a large area, which is exactly why it reads
    // differently hex to hex. Each shader now uses a much higher
    // _RimPower (a thin, crisp border confined to right at the mesh
    // edge, barely overlapping the interior content at all), so this
    // can go back to the plain, restrained lerp and still read as a
    // consistent frame regardless of what's behind it.
    return lerp(color, rimColor, rim * 0.5);
}

float StarBoundGlassAlpha(float glassAlpha, float rim, float colorAlpha)
{
    // Tint opacity leans heavily on the rim term (was 0.1) — most of a
    // hex's fill should stay a faint wash over the starfield, with the
    // tint concentrating into a solid-looking outline near the edge.
    return saturate(glassAlpha + rim * 0.4) * colorAlpha;
}

// Standard compact RGB<->HSV conversions — used to give per-hex-instance
// hue variety (e.g. PlanetSpin) without needing a whole second/third
// tint color property to hand-author per effect.
float3 StarBoundRgbToHsv(float3 c)
{
    float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = c.g < c.b ? float4(c.bg, k.wz) : float4(c.gb, k.xy);
    float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 StarBoundHsvToRgb(float3 c)
{
    float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
    return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
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

// Bends straight lines into organic curves before they hit a cell grid
// — plain Worley noise on its own has dead-straight cell boundaries
// meeting at sharp points, which reads as cut paving stones/geometric
// shapes rather than something organic or hand-built. Warping the
// sample position first is what breaks that regularity. Shared by
// AsteroidFlow (rocks) and MinesHazard (mine casings).
float2 StarBoundDomainWarp(float2 p, float amount)
{
    float warpX = sin(p.y * 1.7 + p.x * 0.6) + sin(p.y * 3.1 - _Time.y * 0.05);
    float warpY = sin(p.x * 1.9 - p.y * 0.5) + sin(p.x * 2.7 + _Time.y * 0.05);
    return p + float2(warpX, warpY) * amount;
}

// Worley/cellular noise: f1 = distance to the nearest scattered point,
// f2 = distance to the second-nearest, nearestOffset = the vector from
// this sample position to that nearest point (needed by callers that
// derive an angle/direction relative to each point's own center, e.g.
// AsteroidFlow's jagged silhouette).
//
// Each point's jitter is constrained to the central 60% of its cell
// (not the full 0..1 range) — with full-range jitter, two points in
// adjacent cells can occasionally land right next to each other across
// the shared boundary, and whatever's placed there (rocks, mines) fuses
// into one elongated blob. Keeping points away from cell edges
// guarantees a minimum gap between neighbors.
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

// One layer of sparse procedural stars, sampled purely from world
// position with no tunable parameters — used three times below (see
// StarBoundStarfield) at different parallax offsets to fake depth.
float3 StarBoundStarfieldLayer(float2 worldPos)
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

    // Power-curved (not a plain lerp) so most stars stay small/dim and
    // only a rare few land near the top of the range — a flat lerp gave
    // every star roughly the same "medium" look; this gives occasional
    // standout bright/large stars against a field of faint ones, closer
    // to how a real starfield reads.
    float size = lerp(0.01, 0.09, pow(sizeHash, 3.0));
    float brightness = lerp(0.25, 1.5, pow(brightnessHash, 2.5));
    brightness *= lerp(0.75, 1.0, 0.5 + 0.5 * sin(_Time.y * lerp(0.4, 1.6, flickerHash) + flickerHash * STARBOUND_TWO_PI));

    float glow = smoothstep(size, 0.0, dist) * brightness * present;

    float3 tint = lerp(float3(0.7, 0.82, 1.0), float3(1.0, 0.88, 0.72), tintHash);
    return glow * tint;
}

// Set once per frame by MapCameraController (Shader.SetGlobalVector) —
// the map camera's own world position. Used below to make two of the
// three star layers sample a point that lags behind the true worldPos
// by a fraction of how far the camera has panned: parallax, the classic
// "distant things move less" depth cue. Global (not per-material) since
// every terrain/background shader that calls StarBoundStarfield needs
// the same value, without each one having to forward it through as an
// argument.
float4 _StarBoundCameraOffset;

// Sparse procedural starfield — every hex on the map calls this
// identically, so the field is genuinely one continuous galaxy behind
// the glass, not a per-tile pattern that happens to look similar. This
// is what makes the map read as "glass overlaid on a living galaxy"
// rather than each hex being its own self-contained scene — see
// GlassHex.shader and AsteroidFlow.shader, which both composite this in
// as their backdrop, and GalaxyBackground.shader, which samples it
// directly for the gaps outside the hex grid. The parallax star layers
// live in here (rather than compositing them only in
// GalaxyBackground.shader) so a hex's own glass tile shows the same
// depth cue, not a flattened version of what's behind it. A nebula
// layer was tried here too (colored, domain-warped fbm clouds) but
// removed — a 3-octave hash-noise function can only produce smooth
// round-ish blobs, not the filament/wisp structure a real nebula photo
// has, and no amount of tuning color/opacity closed that gap.
float3 StarBoundStarfield(float2 worldPos)
{
    float3 color = StarBoundStarfieldLayer(worldPos);
    color += StarBoundStarfieldLayer(worldPos - _StarBoundCameraOffset.xy * 0.45 + 173.0) * 0.5;
    color += StarBoundStarfieldLayer(worldPos - _StarBoundCameraOffset.xy * 0.75 + 419.0) * 0.3;
    return color;
}

// A ring tilted into a fixed axis, then squashed along that axis by
// cos(spinAngle) so it reads as tumbling in and out of the view plane,
// exactly like a coin (or a gimbal ring) flipping end over end. Clamped
// away from a zero squash so it never vanishes to a zero-thickness line
// — a real ring still shows its rim edge-on. Shared by
// TradelaneConnector.shader (two of these make its gyroscope
// construction) and PlanetSpin.shader (one makes a Starport's docking
// ring) — promoted here once it had its second user, same threshold the
// rest of this file's primitives (e.g. StarBoundDomainWarp) already use.
float3 StarBoundSpinningRing(float2 localPos, float radius, float thickness, float tiltAngle, float spinAngle, float3 ringColor)
{
    float c = cos(tiltAngle);
    float s = sin(tiltAngle);
    float2 tilted = float2(localPos.x * c - localPos.y * s, localPos.x * s + localPos.y * c);

    float squash = max(0.12, abs(cos(spinAngle)));
    tilted.y /= squash;

    float ringDist = abs(length(tilted) - radius);
    float halo = 1.0 - smoothstep(thickness * 0.5, thickness * 0.5 + 0.03, ringDist);
    float core = 1.0 - smoothstep(thickness * 0.12, thickness * 0.3, ringDist);
    return ringColor * (halo * 0.5 + core * 0.7);
}

#endif
