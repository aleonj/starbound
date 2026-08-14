using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class MapGeneratorTests
    {
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(6)]
        public void Generate_ProducesExpectedHexCount(int radius)
        {
            var map = MapGenerator.Generate(radius, Difficulty.Medium, seed: 1);

            var expectedCount = 3 * radius * (radius + 1) + 1;
            Assert.AreEqual(expectedCount, map.Hexes.Count);
        }

        [Test]
        public void Generate_IsDeterministicForSameSeed()
        {
            var mapA = MapGenerator.Generate(radius: 5, Difficulty.Hard, seed: 42);
            var mapB = MapGenerator.Generate(radius: 5, Difficulty.Hard, seed: 42);

            var terrainA = mapA.Hexes.OrderBy(h => h.Coordinate.Q).ThenBy(h => h.Coordinate.R)
                .Select(h => h.Terrain).ToList();
            var terrainB = mapB.Hexes.OrderBy(h => h.Coordinate.Q).ThenBy(h => h.Coordinate.R)
                .Select(h => h.Terrain).ToList();

            CollectionAssert.AreEqual(terrainA, terrainB);
        }

        [TestCase(TerrainType.Asteroids)]
        [TestCase(TerrainType.Mines)]
        [TestCase(TerrainType.Debris)]
        public void Generate_NeverPlacesAnIsolatedSingleHexForClusterTerrain(TerrainType terrain)
        {
            var map = MapGenerator.Generate(radius: 6, Difficulty.Hard, seed: 7);

            var matchingHexes = map.Hexes.Where(h => h.Terrain == terrain).ToList();

            foreach (var hex in matchingHexes)
            {
                var hasMatchingNeighbor = map.GetNeighborCoordinates(hex.Coordinate)
                    .Any(c => map.TryGetHex(c, out var neighbor) && neighbor.Terrain == terrain);

                Assert.IsTrue(hasMatchingNeighbor, $"{terrain} hex at {hex.Coordinate} has no same-terrain neighbor.");
            }
        }

        [Test]
        public void Generate_HardDifficultyHasMoreHazardsThanEasy()
        {
            var easyMap = MapGenerator.Generate(radius: 6, Difficulty.Easy, seed: 99);
            var hardMap = MapGenerator.Generate(radius: 6, Difficulty.Hard, seed: 99);

            int HazardCount(GameMap map) => map.Hexes.Count(h =>
                h.Terrain == TerrainType.Asteroids || h.Terrain == TerrainType.Mines || h.Terrain == TerrainType.Debris);

            Assert.Greater(HazardCount(hardMap), HazardCount(easyMap));
        }

        [Test]
        public void Generate_EveryTradelaneHexBordersAnotherTradelaneOrAPlanet()
        {
            // Route length is now distance-driven rather than randomized,
            // so a single connecting hex between two close planets is
            // valid — the invariant is "borders the route/destination",
            // not "is part of a cluster of 2+".
            var map = MapGenerator.Generate(radius: 6, Difficulty.Medium, seed: 3);

            var tradelaneHexes = map.Hexes.Where(h => h.Terrain == TerrainType.Tradelane).ToList();

            foreach (var hex in tradelaneHexes)
            {
                var bordersRouteOrPlanet = map.GetNeighborCoordinates(hex.Coordinate)
                    .Any(c => map.TryGetHex(c, out var neighbor) &&
                        (neighbor.Terrain == TerrainType.Tradelane || neighbor.Terrain == TerrainType.PlanetOrStarport));

                Assert.IsTrue(bordersRouteOrPlanet, $"Tradelane hex at {hex.Coordinate} borders neither a tradelane nor a planet.");
            }
        }

        [Test]
        public void Generate_WithMultiplePlanets_BuildsAtLeastOneTradelaneRoute()
        {
            // Radius 8 + Easy (high tradelane budget, low hazard density)
            // reliably produces enough planets and open space to connect.
            var map = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed: 11);

            var planetCount = map.Hexes.Count(h => h.Terrain == TerrainType.PlanetOrStarport);
            var tradelaneCount = map.Hexes.Count(h => h.Terrain == TerrainType.Tradelane);

            Assert.GreaterOrEqual(planetCount, 2, "Test assumption failed: expected at least 2 planets for this seed.");
            Assert.Greater(tradelaneCount, 0);
        }
    }
}
