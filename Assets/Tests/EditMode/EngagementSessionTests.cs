using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class EngagementSessionTests
    {
        private static Player CreatePlayer() => new("p1", "Test Player", new Ship(cargoCapacity: 3));

        private static void BoostAllPerformanceStats(Ship ship, int amount)
        {
            ship.ApplyStatDelta(CoreStat.Weapons, amount);
            ship.ApplyStatDelta(CoreStat.Shields, amount);
            ship.ApplyStatDelta(CoreStat.Speed, amount);
        }

        [Test]
        public void ResolveRound_OpponentHullReachesZero_PlayerWinsAndRecordsTierWin()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed win every round regardless of attribute
            var opponent = new Ship(cargoCapacity: 0); // default Hull = 3

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            session.ResolveRound(rng);
            session.ResolveRound(rng);
            session.ResolveRound(rng);

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.AreEqual(1, player.HardEngagementWins);
        }

        [Test]
        public void ResolveRound_PlayerHullReachesZero_PlayerLoses()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            BoostAllPerformanceStats(opponent, 20); // opponent guaranteed to win every round

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            session.ResolveRound(rng);
            session.ResolveRound(rng);
            session.ResolveRound(rng);

            Assert.AreEqual(EngagementOutcome.PlayerLost, session.Outcome);
            Assert.IsTrue(player.Ship.IsIntegrityDepleted);
        }

        [Test]
        public void ResolveRound_AfterOutcomeDecided_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20);
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            session.ResolveRound(rng);
            session.ResolveRound(rng);
            session.ResolveRound(rng); // opponent Hull reaches 0 here

            Assert.Throws<InvalidOperationException>(() => session.ResolveRound(rng));
        }

        [Test]
        public void AttemptEscape_Success_EndsEngagement()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            player.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var result = session.AttemptEscape(new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(EngagementOutcome.PlayerEscaped, session.Outcome);
        }

        [Test]
        public void AttemptEscape_Failure_EnergyLostAndRoundContinues()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape failure

            var session = new EngagementSession(definition, player, opponent);
            var result = session.AttemptEscape(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
            Assert.AreEqual(2, player.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void AttemptEscape_TwiceInSameRound_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // stays in progress after the failed attempt

            var session = new EngagementSession(definition, player, opponent);
            session.AttemptEscape(new Random(1));

            Assert.Throws<InvalidOperationException>(() => session.AttemptEscape(new Random(1)));
        }

        [Test]
        public void AttemptEscape_AgainAfterNewRoundStarts_IsAllowed()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // escape always fails
            opponent.ApplyStatDelta(CoreStat.Hull, 20); // survives the round's attribute check

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            session.AttemptEscape(rng); // round 1 escape attempt (fails)
            session.ResolveRound(rng); // round 1 attribute check, advances to round 2

            Assert.DoesNotThrow(() => session.AttemptEscape(rng));
        }
    }
}
