// Rounded-corner, rim-glowing "glass" panel for UGUI Image components —
// the HUD-chrome equivalent of GlassHex.shader's look (translucent tint
// over what's behind it, brightened glow near the edge), so the map's
// glass hexes and the new HUD chrome read as the same material
// language. No 9-slice sprite asset exists (or is planned) for rounded
// corners, so those are done with a signed-distance-field box instead —
// entirely asset-free, and resolution-independent at any panel size.
//
// _RimColor is deliberately a separate channel from the UI mesh's own
// vertex color (Image.color): every existing caller (see MatchHud.cs's
// SetEndTurn) already swaps Image.color for enable/disabled state, and
// this shader multiplies that in unchanged (see the vertex-color
// compatibility note in Frag below) — _RimColor is purely the glow tint
// on top, e.g. the current player's color or a die's selection state.
Shader "StarBound/GlassPanel"
{
    Properties
    {
        // Never sampled — this shader is fully procedural — but UGUI's
        // Image/CanvasRenderer always tries to bind its source texture
        // into a material's _MainTex slot, and warns if the shader
        // doesn't declare one at all.
        _MainTex("Texture", 2D) = "white" {}
        _Color("Glass Tint", Color) = (0.10, 0.11, 0.15, 0.92)
        _GlassAlpha("Glass Alpha", Range(0, 1)) = 0.85
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimWidth("Rim Width", Float) = 6.0
        _RimPower("Rim Power", Range(0.1, 10)) = 2.2
        _RimIntensity("Rim Intensity", Range(0, 3)) = 1.2
        _CornerRadius("Corner Radius", Float) = 14.0
        // The Image's actual point-size (RectTransform.rect.size) — set
        // from C#. Needed so corner rounding reads as a true round
        // regardless of the rect's aspect ratio, and re-synced whenever
        // the rect resizes (see MatchHudChrome.LateUpdate — the chrome
        // panel's height changes at runtime as optional rows show/hide).
        _Size("Rect Size", Vector) = (100, 100, 0, 0)
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
            // entry above. Textures/samplers live outside the constant
            // buffer; an entry in the ShaderLab Properties block alone
            // isn't enough for URP to expose a real bindable property —
            // without this actual HLSL declaration too, the compiler
            // strips it as unused and Material.mainTexture still fails.
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _GlassAlpha;
                float4 _RimColor;
                float _RimWidth;
                float _RimPower;
                float _RimIntensity;
                float _CornerRadius;
                float4 _Size;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * _Size.xy;
                float2 halfSize = _Size.xy * 0.5;
                float dist = SdfRoundedBox(p, halfSize, _CornerRadius);

                // Fixed local-unit AA width — safer for UI than fwidth(),
                // which can misbehave under UI batching/atlasing.
                const float aa = 1.25;
                float shapeMask = 1.0 - smoothstep(-aa, aa, dist);

                // Same rim-falloff shape as GlassHexCommon's StarBoundHexRim
                // / StarBoundGlassColor / StarBoundGlassAlpha — pow-curved,
                // brightens toward the true edge, gated to stay inside the
                // shape (never bleeds outward past the panel's own bounds).
                float rimT = saturate(1.0 - (-dist) / max(_RimWidth, 0.0001));
                float rim = pow(rimT, _RimPower) * shapeMask;

                float3 tintedColor = lerp(_Color.rgb, _RimColor.rgb, saturate(rim * _RimIntensity));
                float alpha = shapeMask * saturate(_GlassAlpha + rim * _RimIntensity * 0.5) * _Color.a;

                // Vertex-color compatibility — see the shader header
                // comment. Matches UI/Default's own vertex-color multiply
                // so existing Image.color-driven enable/disable logic
                // keeps working unchanged after switching to this shader.
                float3 finalColor = tintedColor * IN.color.rgb;
                float finalAlpha = alpha * IN.color.a;

                if (finalAlpha <= 0.001)
                    discard;

                return float4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
}
