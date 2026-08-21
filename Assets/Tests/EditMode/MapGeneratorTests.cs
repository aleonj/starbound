using System.Collections.Generic;
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
            // Every painted route hex must connect back to its chain —
            // a route is only ever painted at all once it's confirmed to
            // meet MinTradelaneRouteLength (see the dedicated minimum-
            // length test below), so this invariant should hold
            // regardless of route length.
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

        [TestCase(3)]
        [TestCase(11)]
        [TestCase(27)]
        public void Generate_TradelaneRoutesAreNeverShorterThanThreeHexes(int seed)
        {
            // A 1-2 hex "route" doesn't read as a lane, so it's never
            // painted at all (see MapGenerator.ConnectPlanetsWithTradelanes)
            // — flood-fill each connected cluster of Tradelane hexes and
            // confirm none of them are that short.
            var map = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed);

            var unvisited = map.Hexes.Where(h => h.Terrain == TerrainType.Tradelane)
                .Select(h => h.Coordinate).ToHashSet();

            while (unvisited.Count > 0)
            {
                var component = new List<HexCoordinate>();
                var queue = new Queue<HexCoordinate>();
                var start = unvisited.First();
                queue.Enqueue(start);
                unvisited.Remove(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);

                    foreach (var neighbor in map.GetNeighborCoordinates(current))
                    {
                        if (unvisited.Remove(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                Assert.GreaterOrEqual(component.Count, 3,
                    $"Tradelane route starting near {start} has only {component.Count} hex(es) — routes shorter than 3 shouldn't be painted.");
            }
        }

        [TestCase(3)]
        [TestCase(11)]
        [TestCase(27)]
        public void Generate_NoTwoPlanetsAreEverNeighbors(int seed)
        {
            // Radius 8 + Easy reliably produces several planets (see
            // Generate_WithMultiplePlanets_BuildsAtLeastOneTradelaneRoute)
            // — a good stress case for this constraint.
            var map = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed);

            var planets = map.Hexes.Where(h => h.Terrain == TerrainType.PlanetOrStarport)
                .Select(h => h.Coordinate).ToList();

            for (var i = 0; i < planets.Count; i++)
            {
                for (var j = i + 1; j < planets.Count; j++)
                {
                    var distance = HexMath.Distance(planets[i], planets[j]);
                    Assert.GreaterOrEqual(distance, 2,
                        $"Planets at {planets[i]} and {planets[j]} are neighbors ({distance} apart).");
                }
            }
        }

        [TestCase(3)]
        [TestCase(11)]
        [TestCase(27)]
        public void Generate_AssignsUniqueNamesToAllPlanets(int seed)
        {
            var map = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed);

            var names = map.Hexes.Where(h => h.Terrain == TerrainType.PlanetOrStarport)
                .Select(h => h.Name).ToList();

            Assert.IsTrue(names.All(name => !string.IsNullOrEmpty(name)), "Every planet should have a name.");
            CollectionAssert.AllItemsAreUnique(names);
        }

        [Test]
        public void Generate_PlanetNamesAreDeterministicForSameSeed()
        {
            var mapA = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed: 3);
            var mapB = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed: 3);

            var namesA = mapA.Hexes.Where(h => h.Terrain == TerrainType.PlanetOrStarport)
                .OrderBy(h => h.Coordinate.Q).ThenBy(h => h.Coordinate.R)
                .Select(h => h.Name).ToList();
            var namesB = mapB.Hexes.Where(h => h.Terrain == TerrainType.PlanetOrStarport)
                .OrderBy(h => h.Coordinate.Q).ThenBy(h => h.Coordinate.R)
                .Select(h => h.Name).ToList();

            CollectionAssert.AreEqual(namesA, namesB);
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(19)]
        public void Generate_EveryPairOfWormholesIsAtLeastEightApart(int seed)
        {
            // Deliberately checks EVERY pair of wormhole hexes on the map
            // against each other, not just "each hex has at least one far
            // partner" — that weaker check is exactly what let a second
            // wormhole pair get placed right next to a first pair without
            // being caught (MinWormholeDistance is enforced against each
            // pair's own two hexes, but a weaker test wouldn't notice it
            // wasn't enforced against every OTHER already-placed wormhole
            // too).
            var map = MapGenerator.Generate(radius: 8, Difficulty.Easy, seed);

            var wormholes = map.Hexes.Where(h => h.Terrain == TerrainType.Wormhole)
                .Select(h => h.Coordinate).ToList();

            Assert.AreEqual(0, wormholes.Count % 2, "Wormholes should always come in pairs.");

            for (var i = 0; i < wormholes.Count; i++)
            {
                for (var j = i + 1; j < wormholes.Count; j++)
                {
                    var distance = HexMath.Distance(wormholes[i], wormholes[j]);
                    Assert.GreaterOrEqual(distance, 8,
                        $"Wormholes at {wormholes[i]} and {wormholes[j]} are only {distance} apart.");
                }
            }
        }

        [TestCase(0)]
        [TestCase(9)]
        public void Generate_OnASmallMap_NeverPlacesWormholesCloserThanEight(int seed)
        {
            // Radius 4 (Small map size) — max possible distance between
            // any two hexes is exactly 8, so satisfying the minimum is
            // tight and depends heavily on where the first hex lands.
            // Wormholes are placed FIRST now, before anything else claims
            // ClearSpace (see MapGenerator.Generate), specifically so the
            // single farthest-apart pair is always still available — and a
            // guaranteed fallback (ForceWormholePair) steps in if the
            // budget-respecting pass's random first pick still comes up
            // empty, so — unlike before — the map should never end up with
            // zero (see Generate_AlwaysProducesAtLeastOneWormholePair).
            // What this test itself checks is narrower: whatever wormholes
            // DO end up placed, no pair or cross-pair distance is ever
            // below the minimum.
            var map = MapGenerator.Generate(radius: 4, Difficulty.Medium, seed);

            var wormholes = map.Hexes.Where(h => h.Terrain == TerrainType.Wormhole)
                .Select(h => h.Coordinate).ToList();

            Assert.AreEqual(0, wormholes.Count % 2, "Wormholes should always come in pairs, even on a small map.");

            for (var i = 0; i < wormholes.Count; i++)
            {
                for (var j = i + 1; j < wormholes.Count; j++)
                {
                    var distance = HexMath.Distance(wormholes[i], wormholes[j]);
                    Assert.GreaterOrEqual(distance, 8,
                        $"Wormholes at {wormholes[i]} and {wormholes[j]} are only {distance} apart.");
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        public void Generate_AlwaysProducesAtLeastOneWormholePair(int seed)
        {
            // Wormhole travel is a real, player-facing feature (see
            // Match.TravelToWormhole) — every map needs somewhere to warp
            // to, on every size, not just the roomier ones. Radius 4
            // (Small) is deliberately the case under test: it's the
            // tightest fit (see the distance test above), so it's the one
            // most likely to expose a regression in the guarantee.
            var map = MapGenerator.Generate(radius: 4, Difficulty.Medium, seed);

            var wormholeCount = map.Hexes.Count(h => h.Terrain == TerrainType.Wormhole);

            Assert.GreaterOrEqual(wormholeCount, 2, "Every map should have at least one wormhole pair.");
        }
    }
}
