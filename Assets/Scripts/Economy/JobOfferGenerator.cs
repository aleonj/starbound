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
        // MatchVariable.BountySeason — applies to every job type in a
        // freshly generated offer, not just bounties despite the name
        // (the event boosts "job board rewards" generally, per its own
        // description). Only affects offers generated WHILE active;
        // already-accepted jobs keep whatever reward they were offered
        // at.
        public const double BountySeasonRewardMultiplier = 1.5;

        public static IReadOnlyList<JobDefinition> GenerateOffer(Random rng, HexCoordinate currentPlanet, GameMap map, EngagementTier maxUnlockedTier, double rewardMultiplier = 1.0)
        {
            var candidates = BuildCandidates(currentPlanet, map, maxUnlockedTier, rewardMultiplier);
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

        private static List<JobDefinition> BuildCandidates(HexCoordinate currentPlanet, GameMap map, EngagementTier maxUnlockedTier, double rewardMultiplier)
        {
            var candidates = new List<JobDefinition>();

            foreach (var hex in map.Hexes)
            {
                if (hex.Terrain == TerrainType.PlanetOrStarport && hex.Coordinate != currentPlanet)
                {
                    var distance = HexMath.Distance(currentPlanet, hex.Coordinate);
                    candidates.Add(new JobDefinition(JobType.Mining, hex.Coordinate, (int)(distance * JobPricing.MiningRewardPerHexDistance * rewardMultiplier)));
                    candidates.Add(new JobDefinition(JobType.Transport, hex.Coordinate, (int)(distance * JobPricing.TransportRewardPerHexDistance * rewardMultiplier)));
                }
                // A bounty at a tier above the current phase can't actually
                // be triggered on arrival — EngagementTrigger.TryTrigger
                // silently no-ops for hex.Engagement > MaxUnlockedTier, and
                // the marker itself stays hidden (see Match.HandleArrival).
                // Excluding those here keeps the job board from offering a
                // job the player has no way to complete yet.
                else if (hex.HasEngagement && hex.Engagement <= maxUnlockedTier)
                {
                    candidates.Add(new JobDefinition(JobType.BountyHunting, hex.Coordinate, (int)(JobPricing.BountyReward(hex.Engagement) * rewardMultiplier), hex.Engagement));
                }
            }

            return candidates;
        }
    }
}
