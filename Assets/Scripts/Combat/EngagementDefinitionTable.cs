using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Placeholder balance numbers, tunable here. Rebalanced after
    // playtesting found opponents far too weak: with the player's default
    // stat at 3, a d10+stat roll ranges 4-13 (avg ~8.5), so the old ranges
    // (Easy 2-3, Medium 3-5, Hard 4-7) meant the player nearly always won.
    public static class EngagementDefinitionTable
    {
        public static EngagementDefinition For(EngagementTier tier) => tier switch
        {
            EngagementTier.Easy => new EngagementDefinition(
                EngagementTier.Easy,
                hullRange: (3, 4), weaponsRange: (4, 6), shieldsRange: (4, 6), speedRange: (4, 6)),
            EngagementTier.Medium => new EngagementDefinition(
                EngagementTier.Medium,
                hullRange: (4, 6), weaponsRange: (7, 9), shieldsRange: (7, 9), speedRange: (7, 9)),
            EngagementTier.Hard => new EngagementDefinition(
                EngagementTier.Hard,
                hullRange: (6, 9), weaponsRange: (10, 13), shieldsRange: (10, 13), speedRange: (10, 13)),
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }
}
