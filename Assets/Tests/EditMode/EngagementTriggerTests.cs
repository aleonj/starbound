using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class EngagementTriggerTests
    {
        [Test]
        public void TryTrigger_HexHasEngagement_ReturnsSession()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Medium };
            map.SetHex(hex);
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1));

            Assert.IsNotNull(session);
            Assert.AreEqual(EngagementTier.Medium, session.Definition.Tier);
        }

        [Test]
        public void TryTrigger_HexHasNoEngagement_ReturnsNull()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            var player = new Player("p1", "Test", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            var session = EngagementTrigger.TryTrigger(player, map, new Random(1));

            Assert.IsNull(session);
        }

        [Test]
        public void ClearMarker_RemovesEngagementFromHex()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard };
            map.SetHex(hex);

            EngagementTrigger.ClearMarker(map, new HexCoordinate(0, 0));

            Assert.IsFalse(hex.HasEngagement);
        }
    }
}
