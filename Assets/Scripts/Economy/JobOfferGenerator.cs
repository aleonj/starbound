using System;
using System.Collections.Generic;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Economy
{
    // Fresh random subset each call, same pattern as ShopOfferGenerator —
    // landing again generates a new offer. Mining/Transport candidates are
    // every other planet on the map (reward scales with hex distance from
    // the current planet); bounty candidates are every currently-marked
    // engagement hex (reward scales with its tier).
    public static class JobOfferGenerator
    {
        public const int OfferSize = 3;

        public static IReadOnlyList<JobDefinition> GenerateOffer(Random rng, HexCoordinate currentPlanet, GameMap map, EngagementTier maxUnlockedTier)
        {
            var candidates = BuildCandidates(currentPlanet, map, maxUnlockedTier);
            var offerSize = Math.Min(OfferSize, candidates.Count);
            var offer = new List<JobDefinition>(offerSize);

            for (var i = 0; i < offerSize; i++)
            {
                var index = rng.Next(candidates.Count);
                offer.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            return offer;
        }

        private static List<JobDefinition> BuildCandidates(HexCoordinate currentPlanet, GameMap map, EngagementTier maxUnlockedTier)
        {
            var candidates = new List<JobDefinition>();

            foreach (var hex in map.Hexes)
            {
                if (hex.Terrain == TerrainType.PlanetOrStarport && hex.Coordinate != currentPlanet)
                {
                    var distance = HexMath.Distance(currentPlanet, hex.Coordinate);
                    candidates.Add(new JobDefinition(JobType.Mining, hex.Coordinate, distance * JobPricing.MiningRewardPerHexDistance));
                    candidates.Add(new JobDefinition(JobType.Transport, hex.Coordinate, distance * JobPricing.TransportRewardPerHexDistance));
                }
                // A bounty at a tier above the current phase can't actually
                // be triggered on arrival — EngagementTrigger.TryTrigger
                // silently no-ops for hex.Engagement > MaxUnlockedTier, and
                // the marker itself stays hidden (see Match.HandleArrival).
                // Excluding those here keeps the job board from offering a
                // job the player has no way to complete yet.
                else if (hex.HasEngagement && hex.Engagement <= maxUnlockedTier)
                {
                    candidates.Add(new JobDefinition(JobType.BountyHunting, hex.Coordinate, JobPricing.BountyReward(hex.Engagement), hex.Engagement));
                }
            }

            return candidates;
        }
    }
}
