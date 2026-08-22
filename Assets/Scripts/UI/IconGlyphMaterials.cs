using System.Collections.Generic;
using UnityEngine;

namespace StarBound.UI
{
    // Cached Materials for IconGlyph.shader (see Assets/Shaders/IconGlyph.shader)
    // — unlike GlassPanelMaterials, icon tint is fixed and shared rather
    // than per-instance runtime-arbitrary, so this follows the same
    // shared-cache convention as HighlightMaterials/TerrainMaterials.
    public static class IconGlyphMaterials
    {
        // Must stay in sync with the glyph index switch in IconGlyph.shader's
        // GlyphDistances function.
        public enum Glyph
        {
            Hull,
            Energy,
            Weapons,
            Shields,
            Speed,
            Money,
            Cargo,
            ChevronUp,
            ChevronDown,
            LocatePin,
            ClearSpace,
            Tradelane,
            Asteroids,
            Debris,
            Mines,
            Wormhole,
            Pause
        }

        private static readonly Dictionary<Glyph, Material> Cache = new();

        // The `|| material == null` check (not `??=`) matters here: with
        // Domain Reload disabled in Enter Play Mode Settings, this static
        // cache survives Stop/Play, but Unity destroys the actual
        // Material objects it created during the previous session — see
        // TerrainMaterials.Get for the same pattern and reasoning.
        public static Material Get(Glyph glyph)
        {
            if (!Cache.TryGetValue(glyph, out var material) || material == null)
            {
                material = new Material(Shader.Find("StarBound/IconGlyph"));
                material.SetFloat("_Glyph", (float)glyph);
                Cache[glyph] = material;
            }

            return material;
        }
    }
}
