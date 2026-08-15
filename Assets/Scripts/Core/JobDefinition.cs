using System;

namespace StarBound.Core
{
    // A job accepted from an on-planet job board. Destination means a
    // delivery planet for Mining/Transport, or the marked engagement hex
    // to defeat for BountyHunting. BountyTier is only meaningful for
    // BountyHunting jobs (EngagementTier.None otherwise) — same
    // "not every field applies to every case" shape as EngagementDefinition.
    public class JobDefinition
    {
        public JobType Type { get; }
        public HexCoordinate Destination { get; }
        public int Reward { get; }
        public EngagementTier BountyTier { get; }

        public JobDefinition(JobType type, HexCoordinate destination, int reward, EngagementTier bountyTier = EngagementTier.None)
        {
            if (reward <= 0)
                throw new ArgumentOutOfRangeException(nameof(reward), "Job must pay a positive reward.");
            if (type == JobType.BountyHunting && bountyTier == EngagementTier.None)
                throw new ArgumentException("A bounty job must target a real EngagementTier.", nameof(bountyTier));

            Type = type;
            Destination = destination;
            Reward = reward;
            BountyTier = bountyTier;
        }
    }
}
