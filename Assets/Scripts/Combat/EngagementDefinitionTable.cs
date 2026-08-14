using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Placeholder balance numbers, tunable here. Now that CombatResolver
    // rolls for both sides (a contested d10+stat check both ways), the
    // ranges no longer need to be inflated to compensate for a one-sided
    // roll advantage — matching the player's own baseline of 3 gives a
    // genuinely fair fight by symmetry, so ranges sit near that baseline
    // and scale up/down from there per tier.
    public static class EngagementDefinitionTable
    {
        public static EngagementDefinition For(EngagementTier tier) => tier switch
        {
            EngagementTier.Easy => new EngagementDefinition(
                EngagementTier.Easy,
                hullRange: (2, 3), weaponsRange: (2, 3), shieldsRange: (2, 3), speedRange: (2, 3)),
            EngagementTier.Medium => new EngagementDefinition(
                EngagementTier.Medium,
                hullRange: (3, 5), weaponsRange: (3, 4), shieldsRange: (3, 4), speedRange: (3, 4)),
            EngagementTier.Hard => new EngagementDefinition(
                EngagementTier.Hard,
                hullRange: (5, 8), weaponsRange: (5, 7), shieldsRange: (5, 7), speedRange: (5, 7)),
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }
}
