using UnityEngine;

namespace StarBound.UI
{
    // Rounded, rim-glowing "glass" Materials for GlassPanel.shader (see
    // Assets/Shaders/GlassPanel.shader) — hands out a FRESH Material per
    // call, unlike HighlightMaterials/TerrainMaterials' shared cache:
    // rim color here is always runtime-arbitrary (the current player's
    // color, a die button's selection state), so a shared cached
    // instance would make recoloring one Image bleed into every other
    // Image using that same instance. Callers store the returned
    // Material and mutate it directly afterward (e.g. SetColor("_RimColor",
    // ...) every time the current player changes) rather than calling
    // Create again.
    public static class GlassPanelMaterials
    {
        public enum Style
        {
            Panel,
            Button,
            DieButton
        }

        // Buttons deliberately get a neutral white base tint, not a
        // baked-in dark glass color — every button call site already
        // drives its actual visible color through the Image's own
        // vertex color (see MatchHudChrome.CreateButton/SetEndTurn), and
        // GlassPanel.shader multiplies its _Color by that vertex color
        // (see the shader's header comment). A dark _Color here would
        // multiply those bright semantic colors (green/red/etc.) down to
        // near-black instead of just adding rounding/glow on top of them.
        // The panel has no such caller swapping its Image.color at
        // runtime, so its own dark glass tint lives directly in _Color.
        private static Color BaseColorFor(Style style) => style switch
        {
            Style.Panel => new Color(0.10f, 0.11f, 0.15f, 0.92f),
            _ => Color.white
        };

        private static float RimWidthFor(Style style) => style switch
        {
            Style.Panel => 6f,
            Style.Button => 5f,
            Style.DieButton => 4f,
            _ => 5f
        };

        private static float CornerRadiusFor(Style style) => style switch
        {
            Style.Panel => 16f,
            Style.Button => 12f,
            Style.DieButton => 10f,
            _ => 10f
        };

        public static Material Create(Style style, Color rimColor)
        {
            var material = new Material(Shader.Find("StarBound/GlassPanel"))
            {
                color = BaseColorFor(style)
            };

            material.SetColor("_RimColor", rimColor);
            material.SetFloat("_GlassAlpha", style == Style.Panel ? 0.85f : 0.9f);
            material.SetFloat("_RimWidth", RimWidthFor(style));
            material.SetFloat("_RimPower", 2.2f);
            material.SetFloat("_RimIntensity", 1.2f);
            material.SetFloat("_CornerRadius", CornerRadiusFor(style));

            return material;
        }
    }
}
