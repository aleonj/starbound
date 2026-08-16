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
        public void EveryNonPlanetNonWormholeTerrainType_AppearsOnAtLeastOneDie(TerrainType terrain)
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
        public void Wormhole_NeverAppearsOnADieFace()
        {
            // Wormhole travel is exclusively the Wormhole Device's standing
            // movement option (see Match.CanTravelWormhole) — not
            // die-based at all, so it should never come up on a roll.
            var appears = MovementDiceSet.Dice.Any(die =>
                Enumerable.Range(1, 6).Select(die.FaceAt).Contains(TerrainType.Wormhole));

            Assert.IsFalse(appears, "Wormhole is reached via the device's travel option, not a die face.");
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

        [Test]
        public void FaceFrequency_StrictlyDescendsByDifficulty()
        {
            // User-specified ascending difficulty order: ClearSpace < Tradelane
            // < Debris < Asteroids < Mines — so face counts (how often
            // you're able to use that terrain) must strictly descend in
            // the same order. Wormhole is excluded here entirely — it's no
            // longer die-reachable at all (see Wormhole_NeverAppearsOnADieFace),
            // so it doesn't have a meaningful "frequency" to rank.
            var terrainsEasiestFirst = new[]
            {
                TerrainType.ClearSpace, TerrainType.Tradelane, TerrainType.Debris,
                TerrainType.Asteroids, TerrainType.Mines
            };

            int CountFaces(TerrainType terrain) => MovementDiceSet.Dice
                .Sum(die => Enumerable.Range(1, 6).Count(face => die.FaceAt(face) == terrain));

            var counts = terrainsEasiestFirst.Select(CountFaces).ToArray();

            for (var i = 1; i < counts.Length; i++)
                Assert.Less(counts[i], counts[i - 1],
                    $"{terrainsEasiestFirst[i]} ({counts[i]}) should appear less often than {terrainsEasiestFirst[i - 1]} ({counts[i - 1]}).");
        }
    }
}
