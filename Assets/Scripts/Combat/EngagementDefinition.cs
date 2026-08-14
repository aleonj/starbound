using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Combat
{
    // Data-driven per-tier NPC stat ranges plus optional round-order/escape
    // overrides, per the [Combat] data-driven engagement definitions story.
    public class EngagementDefinition
    {
        public static readonly IReadOnlyList<CoreStat> DefaultRoundOrder =
            new[] { CoreStat.Weapons, CoreStat.Shields, CoreStat.Speed };

        public EngagementTier Tier { get; }
        public (int Min, int Max) HullRange { get; }
        public (int Min, int Max) WeaponsRange { get; }
        public (int Min, int Max) ShieldsRange { get; }
        public (int Min, int Max) SpeedRange { get; }
        public IReadOnlyList<CoreStat> RoundOrder { get; }
        public bool EscapeAllowed { get; }

        public EngagementDefinition(
            EngagementTier tier,
            (int Min, int Max) hullRange,
            (int Min, int Max) weaponsRange,
            (int Min, int Max) shieldsRange,
            (int Min, int Max) speedRange,
            IReadOnlyList<CoreStat> roundOrder = null,
            bool escapeAllowed = true)
        {
            Tier = tier;
            HullRange = hullRange;
            WeaponsRange = weaponsRange;
            ShieldsRange = shieldsRange;
            SpeedRange = speedRange;
            RoundOrder = roundOrder ?? DefaultRoundOrder;
            EscapeAllowed = escapeAllowed;
        }
    }
}
