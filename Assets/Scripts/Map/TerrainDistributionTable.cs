using System;
using StarBound.Core;

namespace StarBound.Map
{
    public readonly struct TerrainDistribution
    {
        public float Asteroids { get; }
        public float Mines { get; }
        public float Debris { get; }
        public float Tradelane { get; }
        public float PlanetOrStarport { get; }
        public float Wormhole { get; }

        public TerrainDistribution(
            float asteroids, float mines, float debris,
            float tradelane, float planetOrStarport, float wormhole)
        {
            Asteroids = asteroids;
            Mines = mines;
            Debris = debris;
            Tradelane = tradelane;
            PlanetOrStarport = planetOrStarport;
            Wormhole = wormhole;
        }
    }

    // Proportions of total hexes to allocate to each featured terrain type,
    // per difficulty. The remainder is left as ClearSpace. These are
    // placeholder balance numbers, not final game balance — tune later.
    public static class TerrainDistributionTable
    {
        public static TerrainDistribution For(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => new TerrainDistribution(
                asteroids: 0.08f, mines: 0.02f, debris: 0.03f,
                tradelane: 0.18f, planetOrStarport: 0.06f, wormhole: 0.03f),
            Difficulty.Medium => new TerrainDistribution(
                asteroids: 0.14f, mines: 0.06f, debris: 0.07f,
                tradelane: 0.12f, planetOrStarport: 0.05f, wormhole: 0.04f),
            Difficulty.Hard => new TerrainDistribution(
                asteroids: 0.20f, mines: 0.12f, debris: 0.12f,
                tradelane: 0.06f, planetOrStarport: 0.03f, wormhole: 0.05f),
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
        };
    }
}
