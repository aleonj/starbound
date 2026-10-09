using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class EngagementPlacerTests
    {
        [Test]
        public void PlaceEngagements_IsDeterministicForSameSeed()
        {
            var mapA = MapGenerator.Generate(radius: 5, Difficulty.Medium, seed: 3);
            EngagementPlacer.PlaceEngagements(mapA, Difficulty.Medium, seed: 55);

            var mapB = MapGenerator.Generate(radius: 5, Difficulty.Medium, seed: 3);
            EngagementPlacer.PlaceEngagements(mapB, Difficulty.Medium, seed: 55);

            var tiersA = mapA.Hexes.OrderBy(h => h.Coordinate.Q).ThenBy(h => h.Coordinate.R)
                .Select(h => h.Engagement).ToList();
            var tiersB = mapB.Hexes.OrderBy(h => h.Coordinate.Q).ThenBy(h => h.Coordinate.R)
                .Select(h => h.Engagement).ToList();

            CollectionAssert.AreEqual(tiersA, tiersB);
        }

        [Test]
        public void PlaceEngagements_HardDifficultyPlacesMoreThanEasy()
        {
            var easyMap = MapGenerator.Generate(radius: 6, Difficulty.Easy, seed: 12);
            EngagementPlacer.PlaceEngagements(easyMap, Difficulty.Easy, seed: 12);

            var hardMap = MapGenerator.Generate(radius: 6, Difficulty.Hard, seed: 12);
            EngagementPlacer.PlaceEngagements(hardMap, Difficulty.Hard, seed: 12);

            int Count(GameMap map) => map.Hexes.Count(h => h.Engagement != EngagementTier.None);

            Assert.Greater(Count(hardMap), Count(easyMap));
        }

        [Test]
        public void PlaceEngagements_NeverMarksTradelaneOrWormhole()
        {
            // User-requested: those exist for passing through, not for
            // being a destination — placing a marker there (and, since
            // JobOfferGenerator draws Bounty candidates from exactly
            // what this method marks, offering it as a bounty too) would
            // send a player to "go to" a hex whose whole purpose is travel.
            var map = new GameMap(radius: 3, Difficulty.Hard);
            for (var q = -3; q <= 3; q++)
            {
                for (var r = -3; r <= 3; r++)
                {
                    var coordinate = new HexCoordinate(q, r);
                    var terrain = (q + r) % 2 == 0 ? TerrainType.Tradelane : TerrainType.Wormhole;
                    map.SetHex(new Hex(coordinate, terrain));
                }
            }
            // A handful of legal candidates so there's actually something
            // for the budget to place markers onto.
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.ClearSpace));

            EngagementPlacer.PlaceEngagements(map, Difficulty.Hard, seed: 1);

            var markedTerrain = map.Hexes.Where(h => h.Engagement != EngagementTier.None).Select(h => h.Terrain);
            Assert.IsTrue(markedTerrain.All(t => t != TerrainType.Tradelane && t != TerrainType.Wormhole));
        }

        [Test]
        public void PlaceEngagements_OnlyMarksHexesThatExistOnTheMap()
        {
            var map = MapGenerator.Generate(radius: 4, Difficulty.Hard, seed: 8);

            EngagementPlacer.PlaceEngagements(map, Difficulty.Hard, seed: 8);

            var markedCoordinates = map.Hexes.Where(h => h.Engagement != EngagementTier.None).Select(h => h.Coordinate);
            Assert.IsTrue(markedCoordinates.All(c => map.IsWithinBounds(c)));
        }
    }
}
