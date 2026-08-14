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
        public void PlaceEngagements_OnlyMarksHexesThatExistOnTheMap()
        {
            var map = MapGenerator.Generate(radius: 4, Difficulty.Hard, seed: 8);

            EngagementPlacer.PlaceEngagements(map, Difficulty.Hard, seed: 8);

            var markedCoordinates = map.Hexes.Where(h => h.Engagement != EngagementTier.None).Select(h => h.Coordinate);
            Assert.IsTrue(markedCoordinates.All(c => map.IsWithinBounds(c)));
        }
    }
}
