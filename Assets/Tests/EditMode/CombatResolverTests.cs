using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class CombatResolverTests
    {
        [Test]
        public void ResolveAttributeCheck_PlayerWins_OpponentLosesHull()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Weapons, 7); // total always >= 11, guarantees a win
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveAttributeCheck(player, opponent, CoreStat.Weapons, new Random(1));

            Assert.IsTrue(result.PlayerWonExchange);
            Assert.AreEqual(2, opponent.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, player.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveAttributeCheck_PlayerLoses_PlayerLosesHull()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Weapons, 20); // player's total can never reach this

            var result = CombatResolver.ResolveAttributeCheck(player, opponent, CoreStat.Weapons, new Random(1));

            Assert.IsFalse(result.PlayerWonExchange);
            Assert.AreEqual(2, player.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, opponent.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveAttributeCheck_RollIsWithinD10Range()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            var rng = new Random(1);

            for (var i = 0; i < 100; i++)
            {
                var result = CombatResolver.ResolveAttributeCheck(player, opponent, CoreStat.Weapons, rng);
                Assert.That(result.Roll, Is.InRange(1, 10));
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
