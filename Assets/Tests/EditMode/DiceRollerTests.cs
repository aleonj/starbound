using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Movement;

namespace StarBound.Tests
{
    public class DiceRollerTests
    {
        [Test]
        public void Roll_ProducesFiveResults()
        {
            var hand = DiceRoller.Roll(new Random(1));

            Assert.AreEqual(5, hand.Dice.Count);
        }

        [Test]
        public void Roll_AllDiceStartUnspent()
        {
            var hand = DiceRoller.Roll(new Random(1));

            Assert.IsTrue(hand.Dice.All(d => !d.IsSpent));
            Assert.IsTrue(hand.HasUnspentDice);
            Assert.AreEqual(5, hand.UnspentDice.Count());
        }

        [Test]
        public void Roll_IsDeterministicForSameRandomSequence()
        {
            var handA = DiceRoller.Roll(new Random(123));
            var handB = DiceRoller.Roll(new Random(123));

            CollectionAssert.AreEqual(
                handA.Dice.Select(d => d.Terrain).ToList(),
                handB.Dice.Select(d => d.Terrain).ToList());
        }

        [Test]
        public void Roll_EachResultComesFromItsOwnDie()
        {
            var hand = DiceRoller.Roll(new Random(1));

            for (var i = 0; i < hand.Dice.Count; i++)
                Assert.AreEqual(i, hand.Dice[i].DieIndex);
        }
    }
}
