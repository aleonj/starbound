using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Movement;

namespace StarBound.Tests
{
    public class DiceRollerTests
    {
        private static readonly HexCoordinate[] RadiusOneCoordinates =
        {
            new(0, 0), new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)
        };

        [Test]
        public void Roll_WithMap_NeverRollsTerrainAbsentFromIt()
        {
            // User-reported unfairness: a map generated without, say,
            // Tradelane shouldn't still roll Tradelane faces. ClearSpace-
            // only map is the strictest possible check — every other
            // terrain type must get filtered out of every one of the 5
            // dice, every single roll. GameMap starts with no hexes at
            // all (see its own SetHex-only population model), so every
            // coordinate has to be set explicitly, not just overwritten.
            var map = new GameMap(radius: 1, Difficulty.Medium);
            foreach (var coordinate in RadiusOneCoordinates)
                map.SetHex(new Hex(coordinate, TerrainType.ClearSpace));

            for (var seed = 1; seed <= 100; seed++)
            {
                var hand = DiceRoller.Roll(new Random(seed), map);
                Assert.IsTrue(hand.Dice.All(d => d.Terrain == TerrainType.ClearSpace),
                    $"Seed {seed} rolled a non-ClearSpace terrain on a ClearSpace-only map.");
            }
        }

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
