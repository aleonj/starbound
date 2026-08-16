using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Multiplayer;

namespace StarBound.Tests
{
    public class MatchProgressionServiceTests
    {
        private static GameMap BuildOpenMap(int radius = 2)
        {
            var map = new GameMap(radius, Difficulty.Medium);
            for (var q = -radius; q <= radius; q++)
                for (var r = -radius; r <= radius; r++)
                    map.SetHex(new Hex(new HexCoordinate(q, r), TerrainType.ClearSpace));

            return map;
        }

        [Test]
        public void FireEvent_AlwaysPicksAKnownVariable()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);
            var known = new[] { MatchVariable.MinefieldDamage, MatchVariable.TradeBoom };

            for (var seed = 0; seed < 30; seed++)
            {
                var (variable, _) = MatchProgressionService.FireEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                CollectionAssert.Contains(known, variable);
            }
        }

        [Test]
        public void FireEvent_GoalNeverTargetsEitherPlayersCurrentHex()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);

            for (var seed = 0; seed < 30; seed++)
            {
                var (_, goal) = MatchProgressionService.FireEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                Assert.AreNotEqual(p1, goal.TargetHex);
                Assert.AreNotEqual(p2, goal.TargetHex);
            }
        }

        [Test]
        public void FireEvent_RewardIsAlwaysPositive()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);

            for (var seed = 0; seed < 30; seed++)
            {
                var (_, goal) = MatchProgressionService.FireEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                Assert.Greater(goal.RewardMoney, 0);
            }
        }

        [Test]
        public void FireEvent_DefeatNamedTargetGoal_PlacesARealMarkerAtThePlayersCurrentTier()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);
            var sawDefeatNamedTarget = false;

            for (var seed = 0; seed < 30; seed++)
            {
                var (_, goal) = MatchProgressionService.FireEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                if (goal.Type != MatchGoalType.DefeatNamedTarget)
                    continue;

                sawDefeatNamedTarget = true;
                map.TryGetHex(goal.TargetHex, out var hex);
                Assert.AreEqual(EngagementTier.Medium, hex.Engagement,
                    "The goal's target hex should carry a real marker at the player's current tier — a winnable fight, not a preview of the next one.");
            }

            Assert.IsTrue(sawDefeatNamedTarget, "Expected at least one of the 30 seeds to roll a DefeatNamedTarget goal.");
        }

        [Test]
        public void FireEvent_NoHexAvailableForANamedTarget_FallsBackToTravelAndPay()
        {
            // Every hex is already marked (or occupied), so BuildDefeatNamedTargetGoal
            // can never find a candidate — even seeds that would otherwise
            // roll DefeatNamedTarget must fall back to a valid goal instead
            // of throwing or returning something malformed.
            var map = new GameMap(radius: 1, Difficulty.Medium);
            foreach (var coordinate in new[]
                     {
                         new HexCoordinate(0, 0), new HexCoordinate(1, 0), new HexCoordinate(-1, 0),
                         new HexCoordinate(0, 1), new HexCoordinate(0, -1), new HexCoordinate(1, -1),
                         new HexCoordinate(-1, 1)
                     })
            {
                map.SetHex(new Hex(coordinate, TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            }

            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);

            for (var seed = 0; seed < 30; seed++)
            {
                var (_, goal) = MatchProgressionService.FireEvent(map, p1, p2, EngagementTier.Hard, new Random(seed));
                Assert.AreEqual(MatchGoalType.TravelAndPay, goal.Type);
            }
        }
    }
}
