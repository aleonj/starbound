using System;
using StarBound.Core;

namespace StarBound.Economy
{
    // Placeholder balance numbers, tunable here. Mining/Transport scale
    // with how far the cargo has to travel; bounty rewards scale with the
    // tier of the marked engagement being hunted.
    public static class JobPricing
    {
        public const int MiningRewardPerHexDistance = 15;
        public const int TransportRewardPerHexDistance = 20;

        public static int BountyReward(EngagementTier tier) => tier switch
        {
            EngagementTier.Easy => 50,
            EngagementTier.Medium => 100,
            EngagementTier.Hard => 200,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), "Cannot price a bounty for EngagementTier.None.")
        };
    }
}
