using UnityEngine;

namespace StarBound.Map
{
    public static class HighlightMaterials
    {
        private static Material targetHighlight;

        // See TerrainMaterials.Get — `??=` alone isn't enough because a
        // destroyed UnityEngine.Object isn't a real C# null reference, so
        // it survives across Play sessions when Domain Reload is disabled.
        public static Material TargetHighlight =>
            targetHighlight = targetHighlight != null ? targetHighlight : CreateMaterial(new Color(1f, 1f, 0.3f));

        private static Material CreateMaterial(Color color) => new(Shader.Find("Sprites/Default"))
        {
            color = color,
            mainTexture = Texture2D.whiteTexture
        };
    }
}
