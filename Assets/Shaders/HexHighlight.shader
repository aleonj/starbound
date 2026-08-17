// Hex highlight ring — used for "possible move" (LegalTarget) and
// "selected target" (Pending) indicators (see HexTileView.SetState).
//
// The underlying hex tile is left untouched — this draws a separate
// ring, on top of everything (see HexTileView's sortingOrder), that
// continuously throbs in place between roughly half the hex's size and
// its edge, in the hex's own hexagonal shape (not a circle — see
// StarBoundHexDistance below), never extending past the hex boundary.
// Being alpha-based and transparent everywhere it isn't actively
// ringing, it never obscures the tile, the engagement marker, or a
// ship on the same hex.
Shader "StarBound/HexHighlight"
{
    Properties
    {
        _Color("Highlight Color", Color) = (1, 1, 0.3, 1)
        _RingWidth("Ring Width", Range(0.02, 0.3)) = 0.16
        _MinRadius("Throb Min Radius", Range(0, 1)) = 0.5
        _MaxRadius("Throb Max Radius", Range(0.5, 1)) = 0.88
        _ThrobSpeed("Throb Speed", Float) = 2.6
        _MaxAlpha("Max Alpha", Range(0, 1)) = 0.55
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
                float _RingWidth;
                float _MinRadius;
                float _MaxRadius;
                float _ThrobSpeed;
                float _MaxAlpha;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.localPos = (IN.uv - 0.5) * 2.0;
                return OUT;
            }

            // Distance metric whose contour lines are hexagon-shaped
            // (matching HexMeshFactory.CreateFlatTopHex's own vertices
            // at 0/60/120/180/240/300 degrees) instead of circular —
            // reaches exactly 1.0 uniformly all the way around the true
            // hex boundary, at both vertices and edge midpoints alike,
            // so a "ring" traced at distance 1.0 is hexagon-shaped, not
            // a circle inscribed in/circumscribing the hex.
            float StarBoundHexDistance(float2 p)
            {
                const float2 n0 = float2(0.8660254, 0.5);   // 30 degrees — edge-midpoint direction
                const float2 n1 = float2(0.0, 1.0);         // 90 degrees
                const float2 n2 = float2(-0.8660254, 0.5);  // 150 degrees
                const float apothem = 0.8660254;            // cos(30 degrees)

                float d0 = abs(dot(p, n0));
                float d1 = abs(dot(p, n1));
                float d2 = abs(dot(p, n2));
                return max(max(d0, d1), d2) / apothem;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float dist = StarBoundHexDistance(IN.localPos);

                float throb = 0.5 + 0.5 * sin(_Time.y * _ThrobSpeed);
                float ringRadius = lerp(_MinRadius, _MaxRadius, throb);
                float band = 1.0 - smoothstep(0.0, _RingWidth, abs(dist - ringRadius));

                float alpha = saturate(band) * _MaxAlpha;
                return float4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
