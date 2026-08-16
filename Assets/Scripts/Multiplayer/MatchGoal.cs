using StarBound.Core;

namespace StarBound.Multiplayer
{
    // The race-to-complete goal introduced by a progression event —
    // shared, not per-player: whichever player satisfies it first claims
    // it (see Match.HandleArrival / Match.ResolveActiveEngagement for the
    // two completion paths). Immutable — a new event replaces the whole
    // goal outright rather than mutating this one.
    public class MatchGoal
    {
        public MatchGoalType Type { get; }
        public HexCoordinate TargetHex { get; }

        // Only meaningful for TravelAndPay — the amount the completing
        // player must have on hand (and pays) to claim the goal.
        public int MoneyRequired { get; }

        public int RewardMoney { get; }

        public MatchGoal(MatchGoalType type, HexCoordinate targetHex, int moneyRequired, int rewardMoney)
        {
            Type = type;
            TargetHex = targetHex;
            MoneyRequired = moneyRequired;
            RewardMoney = rewardMoney;
        }
    }
}
