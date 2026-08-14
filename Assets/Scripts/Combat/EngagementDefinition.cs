using StarBound.Core;

namespace StarBound.Combat
{
    // Data-driven per-tier NPC stat ranges plus an optional escape-allowed
    // override, per the [Combat] data-driven engagement definitions story.
    // Round order is no longer configurable here — every round is now a
    // Speed check followed by Weapons-vs-Shields, not a cycle of
    // independent attribute checks.
    public class EngagementDefinition
    {
        public EngagementTier Tier { get; }
        public (int Min, int Max) HullRange { get; }
        public (int Min, int Max) WeaponsRange { get; }
        public (int Min, int Max) ShieldsRange { get; }
        public (int Min, int Max) SpeedRange { get; }
        public bool EscapeAllowed { get; }

        public EngagementDefinition(
            EngagementTier tier,
            (int Min, int Max) hullRange,
            (int Min, int Max) weaponsRange,
            (int Min, int Max) shieldsRange,
            (int Min, int Max) speedRange,
            bool escapeAllowed = true)
        {
            Tier = tier;
            HullRange = hullRange;
            WeaponsRange = weaponsRange;
            ShieldsRange = shieldsRange;
            SpeedRange = speedRange;
            EscapeAllowed = escapeAllowed;
        }
    }
}
