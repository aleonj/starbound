// Shared 2D signed-distance-field primitives for the UI-facing shaders
// (GlassPanel, IconGlyph) — kept separate from GlassHexCommon.hlsl since
// none of this math overlaps with the world-space hex shaders that file
// serves.
#ifndef STARBOUND_UI_SDF_COMMON_INCLUDED
#define STARBOUND_UI_SDF_COMMON_INCLUDED

float SdfCircle(float2 p, float radius)
{
    return length(p) - radius;
}

float SdfBox(float2 p, float2 halfSize)
{
    float2 d = abs(p) - halfSize;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

// Inigo Quilez's standard rounded-box formula. halfSize is the box's
// TRUE visual half-extent (the box spans -halfSize..halfSize) — radius
// is cut inward from that boundary, not added on top of it.
float SdfRoundedBox(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

// Unsigned distance from p to the segment [a, b] — used as a capsule/
// stroke primitive (subtract half stroke width from the result to turn
// it into an outline).
float SdfSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

// Isosceles triangle, apex at the origin pointing away from (0, q.y) —
// q = (halfWidth, height). Same formula shape as IQ's sdTriangleIsosceles.
float SdfTriangleIsosceles(float2 p, float2 q)
{
    p.x = abs(p.x);
    float2 a = p - q * saturate(dot(p, q) / dot(q, q));
    float2 b = p - q * float2(saturate(p.x / q.x), 1.0);
    float s = -sign(q.y);
    float2 d = min(float2(dot(a, a), s * (p.x * q.y - p.y * q.x)),
        float2(dot(b, b), s * (p.y - q.y)));
    return -sqrt(d.x) * sign(d.y);
}

// Flat-top hexagon, matching the map's own hex tile shape (see
// HexHighlight.shader's StarBoundHexDistance, which uses the same
// three-projection technique but returns a normalized ratio rather than
// a true signed distance) — radius is the circumradius (center to
// vertex), matching how every other Sdf* primitive here takes a
// "visual size" parameter rather than an inradius/apothem.
float SdfHexagon(float2 p, float radius)
{
    const float2 n0 = float2(0.8660254, 0.5);
    const float2 n1 = float2(0.0, 1.0);
    const float2 n2 = float2(-0.8660254, 0.5);
    float apothem = radius * 0.8660254;
    return max(max(abs(dot(p, n0)), abs(dot(p, n1))), abs(dot(p, n2))) - apothem;
}

#endif
