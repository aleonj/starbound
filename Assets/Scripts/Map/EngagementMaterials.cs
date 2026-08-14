using System.Collections.Generic;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    // Color-coded placeholder materials per engagement tier (easy/medium/hard).
    public static class EngagementMaterials
    {
        private static readonly Dictionary<EngagementTier, Color> Colors = new()
        {
            { EngagementTier.Easy, new Color(0.25f, 0.85f, 0.30f) },
            { EngagementTier.Medium, new Color(0.95f, 0.60f, 0.10f) },
            { EngagementTier.Hard, new Color(0.90f, 0.10f, 0.10f) },
        };

        private static readonly Dictionary<EngagementTier, Material> Cache = new();

        public static Material Get(EngagementTier tier)
        {
            if (!Cache.TryGetValue(tier, out var material))
            {
                material = CreateMaterial(Colors[tier]);
                Cache[tier] = material;
            }

            return material;
        }

        private static Material CreateMaterial(Color color) => new(Shader.Find("Sprites/Default"))
        {
            color = color,
            mainTexture = Texture2D.whiteTexture
        };
    }
}
