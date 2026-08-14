using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Map
{
    // Overlays Engagement markers onto a subset of hexes, independent of
    // terrain type, scaled by difficulty. Placeholder balance numbers.
    public static class EngagementPlacer
    {
        private static readonly Dictionary<Difficulty, float> DensityByDifficulty = new()
        {
            { Difficulty.Easy, 0.05f },
            { Difficulty.Medium, 0.08f },
            { Difficulty.Hard, 0.12f },
        };

        private static readonly Dictionary<Difficulty, (float easy, float medium, float hard)> TierWeights = new()
        {
            { Difficulty.Easy, (0.70f, 0.25f, 0.05f) },
            { Difficulty.Medium, (0.30f, 0.50f, 0.20f) },
            { Difficulty.Hard, (0.10f, 0.30f, 0.60f) },
        };

        public static void PlaceEngagements(GameMap map, Difficulty difficulty, int seed)
        {
            var rng = new Random(seed);
            var proportion = DensityByDifficulty[difficulty];
            var budget = (int)Math.Round(map.Hexes.Count * proportion, MidpointRounding.AwayFromZero);

            var candidates = map.Hexes.Where(h => h.Engagement == EngagementTier.None).ToList();

            for (var i = 0; i < budget && candidates.Count > 0; i++)
            {
                var index = rng.Next(candidates.Count);
                var hex = candidates[index];
                candidates.RemoveAt(index);

                hex.Engagement = PickTier(difficulty, rng);
            }
        }

        private static EngagementTier PickTier(Difficulty difficulty, Random rng)
        {
            var weights = TierWeights[difficulty];
            var roll = rng.NextDouble();

            if (roll < weights.easy)
                return EngagementTier.Easy;
            if (roll < weights.easy + weights.medium)
                return EngagementTier.Medium;
            return EngagementTier.Hard;
        }
    }
}
