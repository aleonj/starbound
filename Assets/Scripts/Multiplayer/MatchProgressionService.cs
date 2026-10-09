using System;
using System.Linq;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Multiplayer
{
    // Builds goal events and picks variable events — see Match for when
    // each actually fires (two INDEPENDENT win-counters now, see
    // Match.HandleProgressionOnEngagementWin — goal and variable events
    // used to be bundled into one combined event, firing and replacing
    // together; decoupled so events turn over on their own cadence
    // rather than being tied to goal pacing, and so they keep cycling
    // for the whole match instead of stopping once Hard tier unlocks,
    // same as goal-firing does). Placeholder balance numbers, tunable
    // here, same convention as EngagementDefinitionTable/EngagementPlacer.
    public static class MatchProgressionService
    {
        private static readonly MatchVariable[] Variables =
        {
            MatchVariable.MinefieldDamage,
            MatchVariable.AsteroidStorm,
            MatchVariable.FuelShortage,
            MatchVariable.PirateSurge,
            MatchVariable.IonStorm,
            MatchVariable.TradeBoom,
            MatchVariable.CalmSpace,
            MatchVariable.MarketCrash,
            MatchVariable.RepairDiscount,
            MatchVariable.SalvageRush,
            MatchVariable.BountySeason
        };

        private const int TravelAndPayMoneyRequired = 40;
        private const int TravelAndPayReward = 75;
        private const int DefeatNamedTargetReward = 100;

        // User-requested: Tradelane/Wormhole hexes exist for passing
        // through, not for being a destination — neither goal type should
        // ever target one. Same exclusion EngagementPlacer applies to its
        // own marker candidates, kept in sync deliberately (a
        // DefeatNamedTarget goal creates a real engagement marker on its
        // target hex, so it has to honor the same rule that governs every
        // other marker).
        private static readonly TerrainType[] IneligibleTargetTerrain = { TerrainType.Tradelane, TerrainType.Wormhole };

        // Picks a variable event — independent of FireGoalEvent below,
        // and deliberately never re-picks the CURRENTLY active one
        // (replacing an event with itself wouldn't read as a new event
        // at all) by excluding it from the draw pool, falling back to
        // the full pool only if excluding it would leave nothing to
        // pick from.
        public static MatchVariable PickVariable(Random rng, MatchVariable currentlyActive)
        {
            var pool = Variables.Where(v => v != currentlyActive).ToArray();
            if (pool.Length == 0)
                pool = Variables;

            return pool[rng.Next(pool.Length)];
        }

        public static MatchGoal FireGoalEvent(
            GameMap map, HexCoordinate playerOnePosition, HexCoordinate playerTwoPosition,
            EngagementTier currentMaxUnlockedTier, Random rng) =>
            rng.NextDouble() < 0.5
                ? BuildTravelAndPayGoal(map, playerOnePosition, playerTwoPosition, rng)
                : BuildDefeatNamedTargetGoal(map, playerOnePosition, playerTwoPosition, currentMaxUnlockedTier, rng);

        private static MatchGoal BuildTravelAndPayGoal(
            GameMap map, HexCoordinate playerOnePosition, HexCoordinate playerTwoPosition, Random rng)
        {
            var candidates = map.Hexes
                .Where(h => h.Coordinate != playerOnePosition && h.Coordinate != playerTwoPosition &&
                    !IneligibleTargetTerrain.Contains(h.Terrain))
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
                    h.Coordinate != playerOnePosition && h.Coordinate != playerTwoPosition &&
                    !IneligibleTargetTerrain.Contains(h.Terrain))
                .ToList();

            if (candidates.Count == 0)
                return BuildTravelAndPayGoal(map, playerOnePosition, playerTwoPosition, rng);

            var hex = candidates[rng.Next(candidates.Count)];
            hex.Engagement = currentMaxUnlockedTier;
            return new MatchGoal(MatchGoalType.DefeatNamedTarget, hex.Coordinate, moneyRequired: 0, DefeatNamedTargetReward);
        }
    }
}
