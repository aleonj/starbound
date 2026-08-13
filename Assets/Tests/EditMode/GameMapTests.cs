using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class GameMapTests
    {
        [Test]
        public void SetAndGetHex_RoundTrips()
        {
            var map = new GameMap(radius: 2, difficulty: Difficulty.Medium);
            var coordinate = new HexCoordinate(1, 1);
            var hex = new Hex(coordinate, TerrainType.Asteroids);

            map.SetHex(hex);

            Assert.IsTrue(map.TryGetHex(coordinate, out var found));
            Assert.AreSame(hex, found);
        }

        [Test]
        public void TryGetHex_ReturnsFalseForUnknownCoordinate()
        {
            var map = new GameMap(radius: 2, difficulty: Difficulty.Easy);

            Assert.IsFalse(map.TryGetHex(new HexCoordinate(5, 5), out _));
        }
    }
}
