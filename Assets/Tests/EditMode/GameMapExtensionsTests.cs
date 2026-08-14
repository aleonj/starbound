using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class GameMapExtensionsTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void IsWithinBounds_OriginIsAlwaysInBounds(int radius)
        {
            var map = new GameMap(radius, Difficulty.Medium);

            Assert.IsTrue(map.IsWithinBounds(new HexCoordinate(0, 0)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void IsWithinBounds_JustOutsideRadius_IsFalse(int radius)
        {
            var map = new GameMap(radius, Difficulty.Medium);
            var justOutside = new HexCoordinate(radius + 1, 0);

            Assert.IsFalse(map.IsWithinBounds(justOutside));
        }

        [Test]
        public void GetNeighborCoordinates_CenterOfLargeMap_ReturnsAllSix()
        {
            var map = new GameMap(radius: 3, difficulty: Difficulty.Medium);

            var neighbors = map.GetNeighborCoordinates(new HexCoordinate(0, 0)).ToList();

            Assert.AreEqual(6, neighbors.Count);
        }

        [Test]
        public void GetNeighborCoordinates_AtMapEdge_ReturnsFewerThanSixAndAllInBounds()
        {
            var map = new GameMap(radius: 2, difficulty: Difficulty.Medium);
            var edgeCoordinate = new HexCoordinate(2, 0);

            var neighbors = map.GetNeighborCoordinates(edgeCoordinate).ToList();

            Assert.Less(neighbors.Count, 6);
            Assert.IsTrue(neighbors.All(map.IsWithinBounds));
        }

        [Test]
        public void GetNeighborCoordinates_OnRadiusZeroMap_ReturnsNone()
        {
            var map = new GameMap(radius: 0, difficulty: Difficulty.Easy);

            var neighbors = map.GetNeighborCoordinates(new HexCoordinate(0, 0)).ToList();

            Assert.IsEmpty(neighbors);
        }
    }
}
