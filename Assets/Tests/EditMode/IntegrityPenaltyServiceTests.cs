using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class IntegrityPenaltyServiceTests
    {
        [Test]
        public void ApplyIfDepleted_HullZero_ClearsMoneyAndConsumablesButKeepsPermanentItemsAndRelocates()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.PlanetOrStarport));

            var ship = new Ship(cargoCapacity: 3, startingMoney: 100);
            var upgrade = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            ship.TryAddItem(upgrade);
            ship.TryAddItem(new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 50, ItemKind.Consumable));
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, ship.Money);
            // A losing streak resets cash and consumables, but earned
            // Permanent gear survives — see Ship.ClearMoneyAndNonPermanentItems.
            CollectionAssert.AreEqual(new[] { upgrade }, ship.HeldItems);
            Assert.AreEqual(new HexCoordinate(2, 0), player.Position);
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Hull),
                "Depleted Hull must reset, not leave the player stuck at 0 with no money to repair.");
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Energy),
                "Energy resets too, even though only Hull triggered the penalty here — this is a fresh start, not a partial one.");
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
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Energy));
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ApplyIfDepleted_OnlyHullZero_StillResetsEnergyToo()
        {
            // Not just whichever stat happened to trigger it — a
            // depleted ship gets a full fresh start, same treatment as
            // the money/items wipe (see ResetIntegrityStats's own
            // comment).
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            var ship = new Ship(cargoCapacity: 3, startingMoney: 50);
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            ship.ApplyStatDelta(CoreStat.Energy, -1); // damaged, but not depleted on its own
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            IntegrityPenaltyService.ApplyIfDepleted(player, map);

            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Energy));
        }
    }
}
