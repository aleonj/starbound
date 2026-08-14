using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Movement;

namespace StarBound.Tests
{
    public class MovementDiceSetTests
    {
        [Test]
        public void MovementDiceSet_HasExactlyFiveDice()
        {
            Assert.AreEqual(5, MovementDiceSet.Dice.Length);
        }

        [Test]
        public void EveryDie_HasExactlySixValidFaces()
        {
            foreach (var die in MovementDiceSet.Dice)
                for (var face = 1; face <= 6; face++)
                    Assert.DoesNotThrow(() => die.FaceAt(face));
        }

        [TestCase(TerrainType.ClearSpace)]
        [TestCase(TerrainType.Asteroids)]
        [TestCase(TerrainType.Tradelane)]
        [TestCase(TerrainType.Mines)]
        [TestCase(TerrainType.Debris)]
        [TestCase(TerrainType.Wormhole)]
        public void EveryNonPlanetTerrainType_AppearsOnAtLeastOneDie(TerrainType terrain)
        {
            var appears = MovementDiceSet.Dice.Any(die =>
                Enumerable.Range(1, 6).Select(die.FaceAt).Contains(terrain));

            Assert.IsTrue(appears, $"{terrain} never appears on any of the 5 movement dice.");
        }

        [Test]
        public void PlanetOrStarport_NeverAppearsOnADieFace()
        {
            var appears = MovementDiceSet.Dice.Any(die =>
                Enumerable.Range(1, 6).Select(die.FaceAt).Contains(TerrainType.PlanetOrStarport));

            Assert.IsFalse(appears, "Planet/starport is reached via the any-die exception, not a die face.");
        }

        [Test]
        public void Wormhole_AppearsOnAtMostTwoDice()
        {
            var diceWithWormhole = MovementDiceSet.Dice.Count(die =>
                Enumerable.Range(1, 6).Select(die.FaceAt).Contains(TerrainType.Wormhole));

            Assert.LessOrEqual(diceWithWormhole, 2, "Wormhole should be rare — on at most 2 of the 5 dice.");
        }

        [Test]
        public void Dice_AreNotAllIdentical()
        {
            var faceSets = MovementDiceSet.Dice
                .Select(die => string.Join(",", Enumerable.Range(1, 6).Select(f => die.FaceAt(f))))
                .Distinct()
                .Count();

            Assert.Greater(faceSets, 1, "The 5 dice should be individually weighted, not 5 copies of the same die.");
        }

        [Test]
        public void FaceAt_InvalidRollValue_Throws()
        {
            var die = MovementDiceSet.Dice[0];

            Assert.Throws<ArgumentOutOfRangeException>(() => die.FaceAt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => die.FaceAt(7));
        }

        [Test]
        public void MovementDie_Constructor_RequiresExactlySixFaces()
        {
            Assert.Throws<ArgumentException>(() => new MovementDie(TerrainType.ClearSpace, TerrainType.Asteroids));
        }
    }
}
