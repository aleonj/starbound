using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class EngagementTriggerTests
    {
        [Test]
        public void TryTrigger_HexHasEngagement_ReturnsSession()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Medium };
            map.SetHex(hex);
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Hard);

            Assert.IsNotNull(session);
            Assert.AreEqual(EngagementTier.Medium, session.Definition.Tier);
        }

        [Test]
        public void TryTrigger_HexHasNoEngagement_ReturnsNull()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Hard);

            Assert.IsNull(session);
        }

        [Test]
        public void TryTrigger_TierAboveMaxTier_ReturnsNullAndLeavesMarkerInPlace()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard };
            map.SetHex(hex);
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Medium);

            Assert.IsNull(session);
            Assert.IsTrue(hex.HasEngagement, "An ungated marker should stay on the map for a later visit once unlocked.");
        }

        [Test]
        public void TryTrigger_TierAtOrBelowMaxTier_Triggers()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Medium });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Medium);

            Assert.IsNotNull(session);
        }

        [Test]
        public void TryTrigger_WithStatBoost_RaisesOnlyPerformanceStatsNotHull()
        {
            // MatchVariable.PirateSurge — statBoost is a plain int, not
            // the MatchVariable enum itself (see TryTrigger's own
            // comment on why), resolved by the caller (Match).
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var unboosted = EngagementDefinitionTable.For(EngagementTier.Easy);

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Easy, statBoost: EngagementTrigger.PirateSurgeStatBoost);

            Assert.IsNotNull(session);
            var opponent = session.Opponent;
            Assert.GreaterOrEqual(opponent.GetStat(CoreStat.Weapons), unboosted.WeaponsRange.Min + EngagementTrigger.PirateSurgeStatBoost);
            Assert.GreaterOrEqual(opponent.GetStat(CoreStat.Shields), unboosted.ShieldsRange.Min + EngagementTrigger.PirateSurgeStatBoost);
            Assert.GreaterOrEqual(opponent.GetStat(CoreStat.Speed), unboosted.SpeedRange.Min + EngagementTrigger.PirateSurgeStatBoost);
            // Hull deliberately untouched — see EngagementDefinition.WithPerformanceStatBoost's own comment.
            Assert.GreaterOrEqual(opponent.GetStat(CoreStat.Hull), unboosted.HullRange.Min);
            Assert.LessOrEqual(opponent.GetStat(CoreStat.Hull), unboosted.HullRange.Max);
        }

        [Test]
        public void TryTrigger_WithRewardMultiplier_ThreadsThroughToTheConstructedSession()
        {
            // MatchVariable.SalvageRush — confirms the multiplier actually
            // reaches the constructed EngagementSession (see its own
            // rewardMultiplier field/constructor param), not just that
            // TryTrigger accepts the parameter.
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            player.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            player.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            player.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Easy, rewardMultiplier: EngagementSession.SalvageRushRewardMultiplier);

            var rng = new Random(1);
            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            // Easy tier's plain reward (15) doubled by SalvageRush.
            Assert.AreEqual(30, session.LastKillRewardMoney);
        }

        [Test]
        public void TryTrigger_HexIsPlayersActiveBountyTarget_SessionCarriesTheRewardForDisplay()
        {
            // User-reported: claiming a bounty was "completely
            // indistinguishable from a normal engagement" — IsBountyTarget/
            // BountyRewardMoney are display-only (see EngagementSession's
            // own comment; JobService.ResolveBountyOutcome re-derives this
            // independently when it actually pays out).
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new HexCoordinate(0, 0);
            map.SetHex(new Hex(hex, TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = hex };
            player.AcceptJob(new JobDefinition(JobType.BountyHunting, hex, reward: 50, bountyTier: EngagementTier.Easy));

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Easy);

            Assert.IsTrue(session.IsBountyTarget);
            Assert.AreEqual(50, session.BountyRewardMoney);
        }

        [Test]
        public void TryTrigger_NoActiveBountyJob_SessionIsNotMarkedAsBountyTarget()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new HexCoordinate(0, 0);
            map.SetHex(new Hex(hex, TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = hex };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Easy);

            Assert.IsFalse(session.IsBountyTarget);
            Assert.IsNull(session.BountyRewardMoney);
        }

        [Test]
        public void TryTrigger_GoalTargetHexMatchesPlayerPosition_SessionIsMarkedAsGoalTarget()
        {
            // User-reported: found the goal's marked engagement but "the
            // engagement screen didn't mention it." goalTargetHex is
            // display-only, resolved by the caller (Match.HandleArrival) —
            // see that call site for why it's only ever passed for a
            // DefeatNamedTarget goal, not just "coordinates happen to match."
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new HexCoordinate(0, 0);
            map.SetHex(new Hex(hex, TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = hex };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Easy, goalTargetHex: hex);

            Assert.IsTrue(session.IsGoalTarget);
        }

        [Test]
        public void TryTrigger_GoalTargetHexDoesNotMatchPlayerPosition_SessionIsNotMarkedAsGoalTarget()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new HexCoordinate(0, 0);
            map.SetHex(new Hex(hex, TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = hex };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1), EngagementTier.Easy, goalTargetHex: new HexCoordinate(5, 5));

            Assert.IsFalse(session.IsGoalTarget);
        }

        [Test]
        public void ClearMarker_RemovesEngagementFromHex()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard };
            map.SetHex(hex);

            EngagementTrigger.ClearMarker(map, new HexCoordinate(0, 0));

            Assert.IsFalse(hex.HasEngagement);
        }
    }
}
