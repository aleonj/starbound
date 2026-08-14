using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class IntegrityPenaltyServiceTests
    {
        [Test]
        public void ApplyIfDepleted_HullZero_ClearsMoneyItemsAndRelocatesToNearestPlanet()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.PlanetOrStarport));

            var ship = new Ship(cargoCapacity: 3, startingMoney: 100);
            ship.TryAddItem(new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50));
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, ship.Money);
            Assert.AreEqual(0, ship.HeldItems.Count);
            Assert.AreEqual(new HexCoordinate(2, 0), player.Position);
        }

        [Test]
        public void ApplyIfDepleted_NotDepleted_DoesNothing()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            var ship = new Ship(cargoCapacity: 3, startingMoney: 100);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map);

            Assert.IsFalse(applied);
            Assert.AreEqual(100, ship.Money);
            Assert.AreEqual(new HexCoordinate(0, 0), player.Position);
        }

        [Test]
        public void ApplyIfDepleted_EnergyZero_AlsoTriggersPenalty()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            var ship = new Ship(cargoCapacity: 3, startingMoney: 50);
            ship.ApplyStatDelta(CoreStat.Energy, -3);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, ship.Money);
        }
    }
}
