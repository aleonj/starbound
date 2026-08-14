using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Placeholder balance numbers, tunable here.
    public static class EngagementDefinitionTable
    {
        public static EngagementDefinition For(EngagementTier tier) => tier switch
        {
            EngagementTier.Easy => new EngagementDefinition(
                EngagementTier.Easy,
                hullRange: (2, 3), weaponsRange: (2, 3), shieldsRange: (2, 3), speedRange: (2, 3)),
            EngagementTier.Medium => new EngagementDefinition(
                EngagementTier.Medium,
                hullRange: (3, 5), weaponsRange: (3, 5), shieldsRange: (3, 5), speedRange: (3, 5)),
            EngagementTier.Hard => new EngagementDefinition(
                EngagementTier.Hard,
                hullRange: (4, 7), weaponsRange: (4, 7), shieldsRange: (4, 7), speedRange: (4, 7)),
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }
}
