using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;
using StarBound.Multiplayer;

namespace StarBound.Tests
{
    public class MatchFactoryTests
    {
        [Test]
        public void CreateMatch_StartsBothPlayersAtASafeOrigin()
        {
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3));
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3));

            var match = MatchFactory.CreateMatch(MapSize.Small, Difficulty.Hard, seed: 5, p1, p2);

            var origin = new HexCoordinate(0, 0);
            Assert.AreEqual(origin, p1.Position);
            Assert.AreEqual(origin, p2.Position);
            Assert.IsTrue(match.Map.TryGetHex(origin, out var hex));
            Assert.AreEqual(TerrainType.ClearSpace, hex.Terrain);
            Assert.IsFalse(hex.HasEngagement);
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
