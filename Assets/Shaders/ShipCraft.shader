// Player ship silhouettes — two distinct hull archetypes (not just a
// recolor) selected via _HullStyle, drawn as a set of straight-edged
// convex polygon pieces (half-plane distance tests, unioned) directly
// in the small hex-canvas quad's local space (see HexMeshFactory /
// ShipMarkerView) — same "procedural shapes in local UV space, no
// textures" approach as the terrain shaders (DebrisField etc.), but
// with a transparent background instead of a starfield fallback, since
// ships sit on top of a terrain hex rather than being terrain
// themselves.
//
// Style 0 ("Interceptor", Player One): narrow pointed nose, swept-back
// wings, single rear engine.
// Style 1 ("Cruiser", Player Two): blunt wide nose, twin outrigger
// hulls each with their own rear engine, joined by a center spar.
//
// Both fixed nose-up (+Y) — there's no ship heading/rotation data
// anywhere in the game (see ShipMarkerView), and the camera is a fixed
// top-down orthographic view that never tilts or rotates, so a fixed
// orientation is correct, not a placeholder.
Shader "StarBound/ShipCraft"
{
    Properties
    {
        _Color("Hull Color", Color) = (0.8, 0.8, 0.85, 1)
        _HullStyle("Hull Style (0=Interceptor, 1=Cruiser)", Float) = 0
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimWidth("Rim Width", Range(0.01, 0.2)) = 0.05
        _PanelColor("Panel Line Color", Color) = (0, 0, 0, 1)
        _EngineColor("Engine Core Color", Color) = (1, 0.95, 0.8, 1)
        _PulseSpeed("Engine Pulse Speed", Float) = 1.6
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
                float _HullStyle;
                float4 _RimColor;
                float _RimWidth;
                float4 _PanelColor;
                float4 _EngineColor;
                float _PulseSpeed;
            CBUFFER_END

            static const float AA = 0.015;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                return OUT;
            }

            // Signed perpendicular distance from p to the infinite line
            // through a->b, positive on the left side of the directed
            // edge (a->b). A convex polygon wound counter-clockwise is
            // then "every edge reports a positive distance."
            float StarBoundHalfPlane(float2 p, float2 a, float2 b)
            {
                float2 edge = b - a;
                float2 toP = p - a;
                return (edge.x * toP.y - edge.y * toP.x) / max(length(edge), 0.0001);
            }

            // Soft interior mask for a convex polygon, eroded inward by
            // `inset` along every edge — inset=0 is the true silhouette;
            // a positive inset shrinks it, used to carve a thin rim band
            // (silhouette minus its own eroded core) without a full SDF.
            float StarBoundConvexMask3(float2 p, float2 a, float2 b, float2 c, float inset)
            {
                float d0 = StarBoundHalfPlane(p, a, b) - inset;
                float d1 = StarBoundHalfPlane(p, b, c) - inset;
                float d2 = StarBoundHalfPlane(p, c, a) - inset;
                return smoothstep(-AA, AA, min(d0, min(d1, d2)));
            }

            float StarBoundConvexMask4(float2 p, float2 a, float2 b, float2 c, float2 d, float inset)
            {
                float d0 = StarBoundHalfPlane(p, a, b) - inset;
                float d1 = StarBoundHalfPlane(p, b, c) - inset;
                float d2 = StarBoundHalfPlane(p, c, d) - inset;
                float d3 = StarBoundHalfPlane(p, d, a) - inset;
                return smoothstep(-AA, AA, min(min(d0, d1), min(d2, d3)));
            }

            float StarBoundConvexMask6(float2 p, float2 a, float2 b, float2 c, float2 d, float2 e, float2 f, float inset)
            {
                float d0 = StarBoundHalfPlane(p, a, b) - inset;
                float d1 = StarBoundHalfPlane(p, b, c) - inset;
                float d2 = StarBoundHalfPlane(p, c, d) - inset;
                float d3 = StarBoundHalfPlane(p, d, e) - inset;
                float d4 = StarBoundHalfPlane(p, e, f) - inset;
                float d5 = StarBoundHalfPlane(p, f, a) - inset;
                return smoothstep(-AA, AA, min(min(min(d0, d1), min(d2, d3)), min(d4, d5)));
            }

            // A thick line segment (a structural spar) — distance from p
            // to the nearest point on segment a->b, thresholded against
            // halfWidth.
            float StarBoundBeamMask(float2 p, float2 a, float2 b, float halfWidth)
            {
                float2 edge = b - a;
                float len = length(edge);
                float2 dir = edge / max(len, 0.0001);
                float along = clamp(dot(p - a, dir), 0.0, len);
                float2 closest = a + dir * along;
                return 1.0 - smoothstep(halfWidth - AA, halfWidth + AA, length(p - closest));
            }

            // Core+halo pulsing engine light, same on/off-feeling duty
            // technique as MinesHazard's warning light / TradelaneConnector's
            // rail lights — a hot core plus a softer breathing halo, not a
            // flat glow blob.
            void StarBoundEngineGlow(float2 p, float2 enginePos, float radius, float phase, float3 engineColor, out float3 glow, out float alpha)
            {
                float dist = length(p - enginePos);
                float core = 1.0 - smoothstep(radius * 0.25, radius * 0.5, dist);
                float halo = 1.0 - smoothstep(radius * 0.4, radius, dist);
                float pulse = 0.55 + 0.45 * sin(_Time.y * _PulseSpeed + phase);
                glow = engineColor * core * 1.5 + engineColor * halo * pulse * 0.6;
                alpha = saturate(core + halo * pulse * 0.8);
            }

            float StarBoundInterceptorHull(float2 p, float inset)
            {
                float2 nose = float2(0.0, 0.70);
                float2 leftShoulder = float2(-0.15, 0.18);
                float2 tail = float2(0.0, -0.55);
                float2 rightShoulder = float2(0.15, 0.18);
                float fuselage = StarBoundConvexMask4(p, nose, leftShoulder, tail, rightShoulder, inset);

                float2 wingRootBack = float2(0.10, -0.30);
                float2 wingTip = float2(0.62, -0.10);
                float2 wingRootFront = float2(0.13, 0.20);
                float rightWing = StarBoundConvexMask3(p, wingRootBack, wingTip, wingRootFront, inset);
                float2 mirrored = float2(-p.x, p.y);
                float leftWing = StarBoundConvexMask3(mirrored, wingRootBack, wingTip, wingRootFront, inset);

                return max(fuselage, max(rightWing, leftWing));
            }

            float StarBoundCruiserHull(float2 p, float inset)
            {
                float2 frontLeft = float2(-0.14, 0.55);
                float2 midLeft = float2(-0.20, 0.05);
                float2 backLeft = float2(-0.12, -0.45);
                float2 backRight = float2(0.12, -0.45);
                float2 midRight = float2(0.20, 0.05);
                float2 frontRight = float2(0.14, 0.55);
                float central = StarBoundConvexMask6(p, frontLeft, midLeft, backLeft, backRight, midRight, frontRight, inset);

                float2 podFront = float2(0.36, 0.15);
                float2 podBack = float2(0.34, -0.30);
                float2 podBackOuter = float2(0.52, -0.35);
                float2 podFrontOuter = float2(0.55, 0.05);
                float rightPod = StarBoundConvexMask4(p, podFront, podBack, podBackOuter, podFrontOuter, inset);

                float2 sparA = float2(0.19, 0.05);
                float2 sparB = float2(0.38, 0.0);
                float rightSpar = StarBoundBeamMask(p, sparA, sparB, 0.05 - inset * 0.5);

                float2 mirrored = float2(-p.x, p.y);
                float leftPod = StarBoundConvexMask4(mirrored, podFront, podBack, podBackOuter, podFrontOuter, inset);
                float leftSpar = StarBoundBeamMask(mirrored, sparA, sparB, 0.05 - inset * 0.5);

                return max(central, max(max(rightPod, leftPod), max(rightSpar, leftSpar)));
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 p = IN.localPos;
                bool isCruiser = _HullStyle > 0.5;

                float mask = isCruiser ? StarBoundCruiserHull(p, 0.0) : StarBoundInterceptorHull(p, 0.0);
                float rimCore = isCruiser ? StarBoundCruiserHull(p, _RimWidth) : StarBoundInterceptorHull(p, _RimWidth);
                float aoCore = isCruiser ? StarBoundCruiserHull(p, _RimWidth * 3.2) : StarBoundInterceptorHull(p, _RimWidth * 3.2);
                float rim = saturate(mask - rimCore);
                float aoBand = saturate(rimCore - aoCore);

                // Rounded cross-section shading — the same analytic
                // fake-3D-normal trick every other living-galaxy effect
                // uses (AsteroidFlow/MinesHazard/DebrisField dome-light a
                // circular scatter point; here the same idea is applied
                // across the hull's width/length instead), so the
                // fuselage reads as a lit, curved surface instead of a
                // flat painted silhouette.
                float3 lightDir = normalize(float3(0.45, 0.6, 0.55));
                float crossX = clamp(p.x / 0.5, -1.0, 1.0);
                float lengthwise = clamp(p.y / 0.65, -1.0, 1.0) * 0.35;
                float3 normal = normalize(float3(crossX, lengthwise, sqrt(saturate(1.0 - crossX * crossX - lengthwise * lengthwise))));
                float lightFactor = saturate(dot(normal, lightDir));
                float3 baseColor = _Color.rgb * lerp(0.45, 1.35, lightFactor);
                baseColor += pow(lightFactor, 10.0) * 0.5; // small glossy specular streak

                // A single etched centerline seam for a "built structure"
                // read, same spirit as DebrisField's panel grid.
                float panelLine = 1.0 - smoothstep(0.0, 0.02, abs(p.x));
                baseColor = lerp(baseColor, _PanelColor.rgb, panelLine * mask * 0.35);

                // Cockpit canopy highlight near the nose.
                float2 cockpitCenter = isCruiser ? float2(0.0, 0.32) : float2(0.0, 0.42);
                float2 cockpitRadii = isCruiser ? float2(0.09, 0.13) : float2(0.07, 0.12);
                float cockpitMask = 1.0 - smoothstep(0.8, 1.0, length((p - cockpitCenter) / cockpitRadii));
                baseColor = lerp(baseColor, float3(0.85, 0.95, 1.0), cockpitMask * mask * 0.85);

                // A shadowed bevel band just inside the bright rim —
                // reads as edge thickness/depth instead of a flat cutout.
                baseColor = lerp(baseColor, baseColor * 0.5, aoBand);

                float3 withRim = lerp(baseColor, _RimColor.rgb, rim);

                float3 engineGlow = float3(0, 0, 0);
                float engineAlpha = 0.0;
                if (isCruiser)
                {
                    float3 glowA, glowB;
                    float alphaA, alphaB;
                    StarBoundEngineGlow(p, float2(0.43, -0.32), 0.11, 0.0, _EngineColor.rgb, glowA, alphaA);
                    StarBoundEngineGlow(p, float2(-0.43, -0.32), 0.11, 3.14159, _EngineColor.rgb, glowB, alphaB);
                    engineGlow = glowA + glowB;
                    engineAlpha = saturate(alphaA + alphaB);
                }
                else
                {
                    StarBoundEngineGlow(p, float2(0.0, -0.52), 0.14, 0.0, _EngineColor.rgb, engineGlow, engineAlpha);
                }

                float3 finalColor = withRim + engineGlow;
                float alpha = saturate(mask + engineAlpha * 0.5);

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
