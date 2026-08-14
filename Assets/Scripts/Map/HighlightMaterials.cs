using UnityEngine;

namespace StarBound.Map
{
    public static class HighlightMaterials
    {
        private static Material targetHighlight;

        public static Material TargetHighlight => targetHighlight ??= CreateMaterial(new Color(1f, 1f, 0.3f));

        private static Material CreateMaterial(Color color) => new(Shader.Find("Sprites/Default"))
        {
            color = color,
            mainTexture = Texture2D.whiteTexture
        };
    }
}
