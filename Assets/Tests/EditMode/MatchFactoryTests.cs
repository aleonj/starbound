using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;
using StarBound.Multiplayer;

namespace StarBound.Tests
{
    public class MatchFactoryTests
    {
        [Test]
        public void CreateMatch_NeverStartsAPlayerOnAHiddenEngagementMarker()
        {
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3));
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3));

            var match = MatchFactory.CreateMatch(MapSize.Small, Difficulty.Hard, seed: 5, p1, p2);

            Assert.IsTrue(match.Map.TryGetHex(p1.Position, out var p1Hex));
            Assert.IsTrue(match.Map.TryGetHex(p2.Position, out var p2Hex));
            Assert.IsFalse(p1Hex.HasEngagement);
            Assert.IsFalse(p2Hex.HasEngagement);
        }

        [Test]
        public void CreateMatch_WithEnoughPlanets_StartsPlayersAtDifferentPlanets()
        {
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3));
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3));

            // Medium map + Easy difficulty generates a planet budget well
            // above 2 (see TerrainDistributionTable), so this seed is a
            // reliable case for asserting the distinct-planets path rather
            // than the low-planet-count fallback.
            var match = MatchFactory.CreateMatch(MapSize.Medium, Difficulty.Easy, seed: 1, p1, p2);

            Assert.AreNotEqual(p1.Position, p2.Position);
            Assert.IsTrue(match.Map.TryGetHex(p1.Position, out var p1Hex));
            Assert.IsTrue(match.Map.TryGetHex(p2.Position, out var p2Hex));
            Assert.AreEqual(TerrainType.PlanetOrStarport, p1Hex.Terrain);
            Assert.AreEqual(TerrainType.PlanetOrStarport, p2Hex.Terrain);
        }

        [Test]
        public void CreateMatch_SamePlayersAndSeed_ProducesTheSameStartingPositions()
        {
            var p1a = new Player("p1", "One", new Ship(cargoCapacity: 3));
            var p2a = new Player("p2", "Two", new Ship(cargoCapacity: 3));
            var p1b = new Player("p1", "One", new Ship(cargoCapacity: 3));
            var p2b = new Player("p2", "Two", new Ship(cargoCapacity: 3));

            MatchFactory.CreateMatch(MapSize.Small, Difficulty.Medium, seed: 42, p1a, p2a);
            MatchFactory.CreateMatch(MapSize.Small, Difficulty.Medium, seed: 42, p1b, p2b);

            Assert.AreEqual(p1a.Position, p1b.Position);
            Assert.AreEqual(p2a.Position, p2b.Position);
        }

        [Test]
        public void CreateMatch_GeneratesMapMatchingRequestedSize()
        {
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3));
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3));

            var match = MatchFactory.CreateMatch(MapSize.Medium, Difficulty.Easy, seed: 1, p1, p2);

            var expectedRadius = MapSize.Medium.ToRadius();
            var expectedCount = 3 * expectedRadius * (expectedRadius + 1) + 1;
            Assert.AreEqual(expectedCount, match.Map.Hexes.Count);
        }
    }
}
