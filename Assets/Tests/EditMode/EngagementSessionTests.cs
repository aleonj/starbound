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
        public void ResolveInitiative_SetsIsAwaitingAttackResolutionAndPendingAttacker()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var initiative = session.ResolveInitiative(new Random(2));

            Assert.IsTrue(session.IsAwaitingAttackResolution);
            Assert.AreEqual(initiative.Attacker, session.PendingAttacker);
        }

        [Test]
        public void ResolveInitiative_CalledTwiceInSameRound_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            session.ResolveInitiative(new Random(1));

            Assert.Throws<InvalidOperationException>(() => session.ResolveInitiative(new Random(1)));
        }

        [Test]
        public void ResolveAttack_WithoutResolvingInitiativeFirst_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);

            Assert.Throws<InvalidOperationException>(() => session.ResolveAttack(new Random(1)));
        }

        [Test]
        public void ResolveAttack_OpponentHullReachesZero_PlayerWinsAndRecordsTierWin()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed hit whenever player has initiative
            var opponent = new Ship(cargoCapacity: 0); // default Hull = 3

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            // Loop rather than a fixed 3 calls: an occasional natural-1
            // fumble can hand the opponent a (harmless, since Shields=23)
            // wasted round, so the exact round count isn't fixed.
            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.AreEqual(1, player.HardEngagementWins);
        }

        [Test]
        public void ResolveAttack_PvPOpponentHullReachesZero_PlayerWinsButNoTierWinIsRecorded()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed hit whenever player has initiative
            var opponentShip = new Ship(cargoCapacity: 0); // default Hull = 3, stands in for the other player's real ship

            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);
            var rng = new Random(1);

            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.AreEqual(0, player.HardEngagementWins);
            Assert.IsFalse(player.HasWonMatch);
        }

        [Test]
        public void ResolveAttack_PlayerHullReachesZero_PlayerLoses()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            BoostAllPerformanceStats(opponent, 20); // opponent guaranteed to win initiative and hit every round

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            for (var i = 0; i < 3; i++)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
            }

            Assert.AreEqual(EngagementOutcome.PlayerLost, session.Outcome);
            Assert.IsTrue(player.Ship.IsIntegrityDepleted);
        }

        [Test]
        public void ResolveInitiative_AfterOutcomeDecided_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20);
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.Throws<InvalidOperationException>(() => session.ResolveInitiative(rng));
            Assert.Throws<InvalidOperationException>(() => session.ResolveAttack(rng));
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
        public void AttemptEscape_FailureDepletesLastEnergy_PlayerLoses()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            player.Ship.ApplyStatDelta(CoreStat.Energy, -2); // Energy = 1, one failed attempt from depleted
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape failure

            var session = new EngagementSession(definition, player, opponent);
            var result = session.AttemptEscape(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, player.Ship.GetStat(CoreStat.Energy));
            Assert.AreEqual(EngagementOutcome.PlayerLost, session.Outcome);
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
        public void AttemptEscape_WhileAwaitingAttackResolution_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            session.ResolveInitiative(new Random(1));

            Assert.IsTrue(session.IsAwaitingAttackResolution);
            Assert.IsFalse(session.CanAttemptEscape);
            Assert.Throws<InvalidOperationException>(() => session.AttemptEscape(new Random(1)));
        }

        [Test]
        public void AttemptEscape_AgainAfterNewRoundStarts_IsAllowed()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // escape always fails, opponent always wins initiative
            opponent.ApplyStatDelta(CoreStat.Hull, 20); // survives the round's attack

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            session.AttemptEscape(rng); // round 1 escape attempt (fails)
            session.ResolveInitiative(rng);
            session.ResolveAttack(rng); // round 1 attack resolves, advances to round 2

            Assert.DoesNotThrow(() => session.AttemptEscape(rng));
        }
    }
}
