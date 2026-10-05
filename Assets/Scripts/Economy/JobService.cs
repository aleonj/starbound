using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Economy
{
    public static class JobService
    {
        public static AcceptJobResult TryAcceptJob(Player player, JobDefinition job)
        {
            if (player.ActiveJob != null)
                return AcceptJobResult.Failed(AcceptJobFailureReason.AlreadyHasActiveJob);

            player.AcceptJob(job);
            return AcceptJobResult.Succeeded();
        }

        // No-ops unless the player has an active bounty job targeting the
        // hex that just resolved. Win-tracking, Hull damage, and integrity
        // penalties are already handled by the normal engagement
        // resolution path (EngagementSession/IntegrityPenaltyService) —
        // this only layers the job-specific consequences on top: the
        // reward on a win, or a 10% penalty fee for backing out via
        // escape (a loss needs no extra penalty, since
        // ClearMoneyAndNonPermanentItems already wipes the player's money
        // and consumables).
        public static void ResolveBountyOutcome(Player player, HexCoordinate resolvedHex, EngagementOutcome outcome)
        {
            var job = player.ActiveJob;
            if (job == null || job.Type != JobType.BountyHunting || job.Destination != resolvedHex)
                return;

            switch (outcome)
            {
                case EngagementOutcome.PlayerWon:
                    player.Ship.AddMoney(job.Reward);
                    player.ClearActiveJob();
                    break;
                case EngagementOutcome.PlayerEscaped:
                    player.Ship.ApplyMoneyPenalty(job.Reward / 10);
                    player.ClearActiveJob();
                    break;
                case EngagementOutcome.PlayerLost:
                    player.ClearActiveJob();
                    break;
            }
        }
    }
}
