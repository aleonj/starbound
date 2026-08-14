using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class CombatResolverTests
    {
        [Test]
        public void ResolveRound_PlayerWinsInitiativeAndHits_OpponentLosesHull()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed initiative win
            player.ApplyStatDelta(CoreStat.Weapons, 20); // guaranteed hit
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveRound(player, opponent, new Random(1));

            Assert.AreEqual(RoundAttacker.Player, result.Attacker);
            Assert.IsTrue(result.HitLanded);
            Assert.AreEqual(2, opponent.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, player.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveRound_PlayerWinsInitiativeButMisses_NoHullLost()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed initiative win
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Shields, 20); // player's weapons total can never reach this

            var result = CombatResolver.ResolveRound(player, opponent, new Random(1));

            Assert.AreEqual(RoundAttacker.Player, result.Attacker);
            Assert.IsFalse(result.HitLanded);
            Assert.AreEqual(3, opponent.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, player.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveRound_OpponentWinsInitiativeAndHits_PlayerLosesHull_NoAttackRoll()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed initiative win
            opponent.ApplyStatDelta(CoreStat.Weapons, 20); // guaranteed hit vs player's default Shields

            var result = CombatResolver.ResolveRound(player, opponent, new Random(1));

            Assert.AreEqual(RoundAttacker.Opponent, result.Attacker);
            Assert.IsNull(result.AttackRoll); // opponent never rolls
            Assert.IsTrue(result.HitLanded);
            Assert.AreEqual(2, player.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, opponent.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveRound_OpponentWinsInitiativeButMisses_NoHullLost()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Shields, 20); // opponent's flat weapons can never reach this
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed initiative win

            var result = CombatResolver.ResolveRound(player, opponent, new Random(1));

            Assert.AreEqual(RoundAttacker.Opponent, result.Attacker);
            Assert.IsFalse(result.HitLanded);
            Assert.AreEqual(3, player.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, opponent.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveRound_SpeedAndAttackRollsAreWithinD10Range()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            var rng = new Random(1);

            for (var i = 0; i < 100; i++)
            {
                var result = CombatResolver.ResolveRound(player, opponent, rng);
                Assert.That(result.SpeedRoll, Is.InRange(1, 10));
                if (result.AttackRoll.HasValue)
                    Assert.That(result.AttackRoll.Value, Is.InRange(1, 10));
            }
        }

        [Test]
        public void ResolveEscapeAttempt_Success_NoEnergyLoss()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Speed, 7); // total always >= 8, guarantees success
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveEscapeAttempt(player, opponent, new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, player.GetStat(CoreStat.Energy));
        }

        [Test]
        public void ResolveEscapeAttempt_Failure_LosesOneEnergy()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // player's total can never reach this

            var result = CombatResolver.ResolveEscapeAttempt(player, opponent, new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(2, player.GetStat(CoreStat.Energy));
        }
    }
}
