using System;

namespace StarBound.Core
{
    // Basic shape only — NPC stat-range tables and round-order/escape
    // overrides belong to the later [Combat] data-driven definitions story.
    public class Engagement
    {
        public EngagementTier Tier { get; }
        public Ship Opponent { get; }

        public Engagement(EngagementTier tier, Ship opponent)
        {
            if (tier == EngagementTier.None)
                throw new ArgumentOutOfRangeException(nameof(tier), "An engagement must have a real tier.");

            Tier = tier;
            Opponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
        }
    }
}
