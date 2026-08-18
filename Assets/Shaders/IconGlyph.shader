// A small fixed set of glowing line-art icons — ship stats (Hull/Energy/
// Weapons/Shields/Speed/Money/Cargo), HUD chrome controls (ChevronUp/
// ChevronDown/LocatePin), and dice-face terrain (ClearSpace/Tradelane/
// Asteroids/Debris/Mines) — selected via _Glyph, drawn as SDF shapes. No
// icon image assets exist or are planned, so every glyph is built from a
// handful of primitives (see Include/UISdfCommon.hlsl) rather than a
// texture. Styled as thin glowing outline strokes (bright core + soft
// halo, matching EngagementBeacon.shader's glow vocabulary) rather than
// filled shapes, since outlines stay legible at small icon sizes while
// filled blobs lose their silhouette — Energy and ClearSpace are the
// deliberate exceptions (a zigzag outline reads as two wobbly lines at
// this size, filled reads as a bolt immediately; scattered stars read
// better as small filled dots than thin outlined circles).
Shader "StarBound/IconGlyph"
{
    Properties
    {
        // Never sampled — this shader is fully procedural — but UGUI's
        // Image/CanvasRenderer always tries to bind its source texture
        // into a material's _MainTex slot, and warns if the shader
        // doesn't declare one at all.
        _MainTex("Texture", 2D) = "white" {}
        _Color("Glyph Color", Color) = (0.85, 0.95, 1.0, 1)
        _GlowColor("Glow Color", Color) = (0.6, 0.85, 1.0, 1)
        // Hull=0, Energy=1, Weapons=2, Shields=3, Speed=4, Money=5, Cargo=6,
        // ChevronUp=7, ChevronDown=8, LocatePin=9, ClearSpace=10,
        // Tradelane=11, Asteroids=12, Debris=13, Mines=14 — see
        // IconGlyphMaterials.Glyph, which this must stay in sync with.
        _Glyph("Glyph Index", Float) = 0
        _StrokeWidth("Stroke Width", Float) = 0.12
        _GlowWidth("Glow Width", Float) = 0.22
        _GlowIntensity("Glow Intensity", Range(0, 3)) = 1.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Include/UISdfCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            // Declared but never sampled — see the _MainTex Properties
            // entry above and GlassPanel.shader's matching comment for
            // why the ShaderLab Properties entry alone isn't enough.
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _GlowColor;
                float _Glyph;
                float _StrokeWidth;
                float _GlowWidth;
                float _GlowIntensity;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            float2 Rotate(float2 p, float angleRadians)
            {
                float s = sin(angleRadians);
                float c = cos(angleRadians);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            // Returns (outlineDist, filledDist) — outlineDist is the
            // unsigned distance to the nearest stroke centerline (already
            // abs'd where the underlying primitive was signed); filledDist
            // is a signed distance for the rare filled region (negative
            // inside), or a large constant when the glyph has none.
            void GlyphDistances(float2 p, int glyph, out float outlineDist, out float filledDist)
            {
                outlineDist = 1e5;
                filledDist = 1e5;

                if (glyph == 0) // Hull — domed rounded-box top, triangular point at the bottom
                {
                    float dome = SdfRoundedBox(p - float2(0, 0.15), float2(0.42, 0.30), 0.28);
                    float point_ = SdfTriangleIsosceles(p - float2(0, -0.55), float2(0.42, 0.35));
                    outlineDist = abs(min(dome, point_));
                }
                else if (glyph == 1) // Energy — two staggered rotated boxes, filled
                {
                    float2 pA = Rotate(p - float2(-0.10, 0.12), 0.35);
                    float2 pB = Rotate(p - float2(0.10, -0.12), -0.35);
                    float boxA = SdfBox(pA, float2(0.09, 0.42));
                    float boxB = SdfBox(pB, float2(0.09, 0.42));
                    filledDist = min(boxA, boxB);
                }
                else if (glyph == 2) // Weapons — ring + 4 ticks + filled center dot
                {
                    float ring = abs(SdfCircle(p, 0.55));
                    float tickN = SdfSegment(p, float2(0, 0.60), float2(0, 0.82));
                    float tickS = SdfSegment(p, float2(0, -0.60), float2(0, -0.82));
                    float tickE = SdfSegment(p, float2(0.60, 0), float2(0.82, 0));
                    float tickW = SdfSegment(p, float2(-0.60, 0), float2(-0.82, 0));
                    outlineDist = min(ring, min(min(tickN, tickS), min(tickE, tickW)));
                    filledDist = SdfCircle(p, 0.09);
                }
                else if (glyph == 3) // Shields — two concentric rings ("forcefield bubble"),
                                     // deliberately not another dome shape — too close to Hull's
                                     // silhouette to tell apart at a glance
                {
                    float outer = abs(SdfCircle(p, 0.55));
                    float inner = abs(SdfCircle(p, 0.32));
                    outlineDist = min(outer, inner);
                }
                else if (glyph == 4) // Speed — right-pointing chevron + trailing motion tick
                {
                    float chevronTop = SdfSegment(p, float2(-0.35, 0.45), float2(0.35, 0));
                    float chevronBottom = SdfSegment(p, float2(-0.35, -0.45), float2(0.35, 0));
                    float motionTick = SdfSegment(p, float2(-0.75, 0), float2(-0.55, 0));
                    outlineDist = min(chevronTop, min(chevronBottom, motionTick));
                }
                else if (glyph == 5) // Money — coin ring + currency bar
                {
                    float coin = abs(SdfCircle(p, 0.60));
                    float bar = SdfSegment(p, float2(0, -0.40), float2(0, 0.40));
                    outlineDist = min(coin, bar);
                }
                else if (glyph == 6) // Cargo — crate outline + lid seam + center seam
                {
                    float box = abs(SdfBox(p, float2(0.55, 0.55)));
                    float lidSeam = SdfSegment(p, float2(-0.55, 0.12), float2(0.55, 0.12));
                    float centerSeam = SdfSegment(p, float2(0, 0.12), float2(0, -0.55));
                    outlineDist = min(box, min(lidSeam, centerSeam));
                }
                else if (glyph == 7) // ChevronUp — collapse-toggle icon while expanded
                {
                    float legLeft = SdfSegment(p, float2(0, 0.4), float2(-0.4, -0.25));
                    float legRight = SdfSegment(p, float2(0, 0.4), float2(0.4, -0.25));
                    outlineDist = min(legLeft, legRight);
                }
                else if (glyph == 8) // ChevronDown — collapse-toggle icon while collapsed
                {
                    float legLeft = SdfSegment(p, float2(0, -0.4), float2(-0.4, 0.25));
                    float legRight = SdfSegment(p, float2(0, -0.4), float2(0.4, 0.25));
                    outlineDist = min(legLeft, legRight);
                }
                else if (glyph == 9) // LocatePin — map-pin/teardrop, for the "find my ship" button
                {
                    float head = SdfCircle(p - float2(0, 0.15), 0.32);
                    float point_ = SdfTriangleIsosceles(p - float2(0, -0.45), float2(0.26, 0.42));
                    outlineDist = abs(min(head, point_));
                    filledDist = SdfCircle(p - float2(0, 0.15), 0.09);
                }
                // Die-face terrain glyphs (10-14) — every die face is one
                // of exactly these five (see MovementDiceSet), used on
                // the dice bar's per-die buttons instead of text labels.
                else if (glyph == 10) // ClearSpace — an empty hex outline, echoing the map's own
                                       // tile shape (a plain hex reads immediately in context,
                                       // clearer than an earlier scattered-stars attempt)
                {
                    outlineDist = abs(SdfHexagon(p, 0.62));
                }
                else if (glyph == 11) // Tradelane — a rail with three cross-ties (railroad-style
                                       // route marker), strengthened from two ties to three so it
                                       // reads clearly as "track" rather than an ambiguous mark —
                                       // deliberately not a chevron, too close to Speed's glyph
                {
                    float rail = SdfSegment(p, float2(-0.55, 0), float2(0.55, 0));
                    float tick1 = SdfSegment(p, float2(-0.28, -0.18), float2(-0.28, 0.18));
                    float tick2 = SdfSegment(p, float2(0, -0.18), float2(0, 0.18));
                    float tick3 = SdfSegment(p, float2(0.28, -0.18), float2(0.28, 0.18));
                    outlineDist = min(rail, min(tick1, min(tick2, tick3)));
                }
                else if (glyph == 12) // Asteroids — a cluster of overlapping rock outlines
                {
                    float rock1 = SdfCircle(p - float2(-0.2, 0.15), 0.22);
                    float rock2 = SdfCircle(p - float2(0.2, 0.1), 0.18);
                    float rock3 = SdfCircle(p - float2(0, -0.25), 0.16);
                    outlineDist = abs(min(rock1, min(rock2, rock3)));
                }
                else if (glyph == 13) // Debris — four small filled chunks scattered toward the
                                       // corners, with nothing crossing the center. Two earlier
                                       // attempts used thin diagonal shards that, unioned together
                                       // near the middle, accidentally read as an X/cancel mark
                                       // instead of wreckage — solid corner-anchored chunks with a
                                       // deliberately empty middle can't form that illusion
                {
                    float chunkA = SdfRoundedBox(Rotate(p - float2(-0.42, 0.36), 0.4), float2(0.14, 0.09), 0.03);
                    float chunkB = SdfRoundedBox(Rotate(p - float2(0.40, 0.34), -0.6), float2(0.12, 0.08), 0.03);
                    float chunkC = SdfRoundedBox(Rotate(p - float2(0.44, -0.32), 0.9), float2(0.13, 0.08), 0.03);
                    float chunkD = SdfRoundedBox(Rotate(p - float2(-0.36, -0.34), -0.3), float2(0.10, 0.07), 0.03);
                    filledDist = min(chunkA, min(chunkB, min(chunkC, chunkD)));
                }
                else if (glyph == 14) // Mines — a naval-mine silhouette: ring body + 6 radiating
                                       // spikes at 60-degree increments
                {
                    float body = abs(SdfCircle(p, 0.32));
                    float spikes = 1e5;
                    spikes = min(spikes, SdfSegment(p, Rotate(float2(0.32, 0), 0.0), Rotate(float2(0.58, 0), 0.0)));
                    spikes = min(spikes, SdfSegment(p, Rotate(float2(0.32, 0), 1.047), Rotate(float2(0.58, 0), 1.047)));
                    spikes = min(spikes, SdfSegment(p, Rotate(float2(0.32, 0), 2.094), Rotate(float2(0.58, 0), 2.094)));
                    spikes = min(spikes, SdfSegment(p, Rotate(float2(0.32, 0), 3.142), Rotate(float2(0.58, 0), 3.142)));
                    spikes = min(spikes, SdfSegment(p, Rotate(float2(0.32, 0), 4.189), Rotate(float2(0.58, 0), 4.189)));
                    spikes = min(spikes, SdfSegment(p, Rotate(float2(0.32, 0), 5.236), Rotate(float2(0.58, 0), 5.236)));
                    outlineDist = min(body, spikes);
                }
                // Any other index: outlineDist/filledDist stay at their
                // 1e5 default, i.e. nothing renders — safer than falling
                // through to an unrelated glyph.
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                int glyph = (int)round(_Glyph);

                float outlineDist, filledDist;
                GlyphDistances(p, glyph, outlineDist, filledDist);

                float strokeDist = outlineDist - _StrokeWidth * 0.5;

                float coreFromStroke = 1.0 - smoothstep(0.0, 0.035, strokeDist);
                float haloFromStroke = 1.0 - smoothstep(0.0, _GlowWidth, strokeDist);
                float coreFromFill = 1.0 - smoothstep(0.0, 0.035, filledDist);
                float haloFromFill = 1.0 - smoothstep(0.0, _GlowWidth, filledDist);

                float core = max(coreFromStroke, coreFromFill);
                float halo = max(haloFromStroke, haloFromFill);
                float mask = saturate(core + halo * _GlowIntensity * 0.6);

                float3 glyphColor = _Color.rgb * core + _GlowColor.rgb * halo * _GlowIntensity * 0.5;

                float3 finalColor = glyphColor * IN.color.rgb;
                float finalAlpha = saturate(mask) * _Color.a * IN.color.a;

                if (finalAlpha <= 0.001)
                    discard;

                return float4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
}
