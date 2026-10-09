using StarBound.Core;
using StarBound.Map;

namespace StarBound.Multiplayer
{
    // Shared between the persistent status label (MatchHud) and the
    // one-time "New Goal" announcement (Match.HandleProgressionOnEngagementWin)
    // so they can never drift out of sync — same reasoning as
    // MatchVariableDescriptions.
    public static class MatchGoalDescriptions
    {
        // Takes the map (not just the goal) so it can resolve TargetHex
        // through HexNavigationDescriptions instead of printing raw
        // (Q, R) — the same meaningless-to-a-player coordinates the user
        // flagged, now replaced everywhere via one shared description.
        public static string Describe(GameMap map, MatchGoal goal) => goal.Type switch
        {
            MatchGoalType.TravelAndPay =>
                $"Courier Run — a contract's open at {HexNavigationDescriptions.Describe(map, goal.TargetHex)}: deliver {goal.MoneyRequired} credits there and net {goal.RewardMoney}.",
            MatchGoalType.DefeatNamedTarget =>
                $"Marked Target — a hostile is holed up at {HexNavigationDescriptions.Describe(map, goal.TargetHex)}. Take them down for {goal.RewardMoney}.",
            _ => goal.Type.ToString()
        };
    }
}
