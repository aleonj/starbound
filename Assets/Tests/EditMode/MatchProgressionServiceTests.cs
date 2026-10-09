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
        public void PickVariable_AlwaysPicksAKnownVariable()
        {
            var known = Enum.GetValues(typeof(MatchVariable)).Cast<MatchVariable>().Where(v => v != MatchVariable.None).ToArray();

            for (var seed = 0; seed < 30; seed++)
            {
                var variable = MatchProgressionService.PickVariable(new Random(seed), MatchVariable.None);
                CollectionAssert.Contains(known, variable);
            }
        }

        [Test]
        public void PickVariable_NeverRepicksTheCurrentlyActiveOne()
        {
            // Replacing an event with itself wouldn't read as a new event
            // at all — the draw pool excludes whatever's currently active.
            for (var seed = 0; seed < 50; seed++)
            {
                var variable = MatchProgressionService.PickVariable(new Random(seed), MatchVariable.MinefieldDamage);
                Assert.AreNotEqual(MatchVariable.MinefieldDamage, variable);
            }
        }

        [Test]
        public void FireGoalEvent_GoalNeverTargetsEitherPlayersCurrentHex()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);

            for (var seed = 0; seed < 30; seed++)
            {
                var goal = MatchProgressionService.FireGoalEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                Assert.AreNotEqual(p1, goal.TargetHex);
                Assert.AreNotEqual(p2, goal.TargetHex);
            }
        }

        [Test]
        public void FireGoalEvent_RewardIsAlwaysPositive()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);

            for (var seed = 0; seed < 30; seed++)
            {
                var goal = MatchProgressionService.FireGoalEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                Assert.Greater(goal.RewardMoney, 0);
            }
        }

        [Test]
        public void FireGoalEvent_DefeatNamedTargetGoal_PlacesARealMarkerAtThePlayersCurrentTier()
        {
            var map = BuildOpenMap();
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);
            var sawDefeatNamedTarget = false;

            for (var seed = 0; seed < 30; seed++)
            {
                var goal = MatchProgressionService.FireGoalEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
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
        public void FireGoalEvent_NeverTargetsTradelaneOrWormhole()
        {
            // User-requested: those exist for passing through, not for
            // being a destination — neither goal type should ever send a
            // player there. Map deliberately weighted so Tradelane/
            // Wormhole would get picked constantly if the exclusion
            // weren't actually wired in.
            var map = new GameMap(radius: 3, Difficulty.Medium);
            for (var q = -3; q <= 3; q++)
            {
                for (var r = -3; r <= 3; r++)
                {
                    var coordinate = new HexCoordinate(q, r);
                    var terrain = (q + r) % 2 == 0 ? TerrainType.Tradelane : TerrainType.Wormhole;
                    map.SetHex(new Hex(coordinate, terrain));
                }
            }
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));
            // The one legal target — without this, every candidate would
            // be excluded (either a player's own hex or Tradelane/
            // Wormhole), which is a pre-existing "nowhere left to send
            // them" edge case unrelated to what this test is checking.
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.ClearSpace));
            var p1 = new HexCoordinate(0, 0);
            var p2 = new HexCoordinate(1, 0);

            for (var seed = 0; seed < 50; seed++)
            {
                var goal = MatchProgressionService.FireGoalEvent(map, p1, p2, EngagementTier.Medium, new Random(seed));
                map.TryGetHex(goal.TargetHex, out var targetHex);
                Assert.AreNotEqual(TerrainType.Tradelane, targetHex.Terrain);
                Assert.AreNotEqual(TerrainType.Wormhole, targetHex.Terrain);
            }
        }

        [Test]
        public void FireGoalEvent_NoHexAvailableForANamedTarget_FallsBackToTravelAndPay()
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
                var goal = MatchProgressionService.FireGoalEvent(map, p1, p2, EngagementTier.Hard, new Random(seed));
                Assert.AreEqual(MatchGoalType.TravelAndPay, goal.Type);
            }
        }
    }
}
