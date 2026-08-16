using System.Collections.Generic;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    // Solid-color placeholder materials per terrain type, standing in for
    // real art. Colors are cached per terrain, shared across all tiles.
    public static class TerrainMaterials
    {
        private static readonly Dictionary<TerrainType, Color> Colors = new()
        {
            { TerrainType.ClearSpace, new Color(0.08f, 0.08f, 0.14f) },
            { TerrainType.Asteroids, new Color(0.45f, 0.42f, 0.38f) },
            { TerrainType.PlanetOrStarport, new Color(0.20f, 0.70f, 0.35f) },
            { TerrainType.Wormhole, new Color(0.55f, 0.25f, 0.85f) },
            { TerrainType.Tradelane, new Color(0.90f, 0.80f, 0.25f) },
            { TerrainType.Mines, new Color(0.85f, 0.20f, 0.20f) },
            { TerrainType.Debris, new Color(0.80f, 0.50f, 0.20f) },
        };

        private static readonly Dictionary<TerrainType, Material> Cache = new();

        public static Material Get(TerrainType terrain)
        {
            // The `== null` check (not just TryGetValue) matters here: with
            // Domain Reload disabled in Enter Play Mode Settings, this
            // static cache survives Stop/Play, but Unity destroys the
            // actual Material objects it created during the previous
            // session. A destroyed UnityEngine.Object isn't a real C# null
            // reference, so only Unity's overloaded == catches it and lets
            // us recreate the material instead of handing back a dead one.
            if (!Cache.TryGetValue(terrain, out var material) || material == null)
            {
                material = CreateMaterial(Colors[terrain]);
                Cache[terrain] = material;
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
