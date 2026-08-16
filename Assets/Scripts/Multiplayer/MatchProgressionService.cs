using System;
using System.Linq;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Multiplayer
{
    // Builds the (variable, goal) pair for one progression event — see
    // Match for when this fires (every EngagementsPerProgressionEvent
    // successful engagements) and how each goal type's completion is
    // actually checked. Placeholder balance numbers, tunable here, same
    // convention as EngagementDefinitionTable/EngagementPlacer.
    public static class MatchProgressionService
    {
        private static readonly MatchVariable[] Variables =
        {
            MatchVariable.MinefieldDamage,
            MatchVariable.TradeBoom
        };

        private const int TravelAndPayMoneyRequired = 40;
        private const int TravelAndPayReward = 75;
        private const int DefeatNamedTargetReward = 100;

        public static (MatchVariable Variable, MatchGoal Goal) FireEvent(
            GameMap map, HexCoordinate playerOnePosition, HexCoordinate playerTwoPosition,
            EngagementTier currentMaxUnlockedTier, Random rng)
        {
            var variable = Variables[rng.Next(Variables.Length)];
            var goal = rng.NextDouble() < 0.5
                ? BuildTravelAndPayGoal(map, playerOnePosition, playerTwoPosition, rng)
                : BuildDefeatNamedTargetGoal(map, playerOnePosition, playerTwoPosition, currentMaxUnlockedTier, rng);

            return (variable, goal);
        }

        private static MatchGoal BuildTravelAndPayGoal(
            GameMap map, HexCoordinate playerOnePosition, HexCoordinate playerTwoPosition, Random rng)
        {
            var candidates = map.Hexes
                .Where(h => h.Coordinate != playerOnePosition && h.Coordinate != playerTwoPosition)
                .ToList();

            var target = candidates[rng.Next(candidates.Count)].Coordinate;
            return new MatchGoal(MatchGoalType.TravelAndPay, target, TravelAndPayMoneyRequired, TravelAndPayReward);
        }

        // Places a fresh marker at an otherwise-unmarked hex, at the
        // player's CURRENT max-unlocked tier — deliberately NOT the tier
        // being unlocked. An earlier version placed it at the next tier
        // ("prove you're ready for Medium by beating a Medium NPC"), but
        // that requires already being strong enough for the tier you're
        // trying to unlock, which is self-defeating (see the
        // [Multiplayer] game-progression story's balance finding). A
        // marker at the player's current strength is still a genuine,
        // specific race — just a winnable one — and needs no exemption
        // from the usual phase-gating check, since it's never above the
        // ceiling in the first place.
        private static MatchGoal BuildDefeatNamedTargetGoal(
            GameMap map, HexCoordinate playerOnePosition, HexCoordinate playerTwoPosition,
            EngagementTier currentMaxUnlockedTier, Random rng)
        {
            var candidates = map.Hexes
                .Where(h => h.Engagement == EngagementTier.None &&
                    h.Coordinate != playerOnePosition && h.Coordinate != playerTwoPosition)
                .ToList();

            if (candidates.Count == 0)
                return BuildTravelAndPayGoal(map, playerOnePosition, playerTwoPosition, rng);

            var hex = candidates[rng.Next(candidates.Count)];
            hex.Engagement = currentMaxUnlockedTier;
            return new MatchGoal(MatchGoalType.DefeatNamedTarget, hex.Coordinate, moneyRequired: 0, DefeatNamedTargetReward);
        }
    }
}
