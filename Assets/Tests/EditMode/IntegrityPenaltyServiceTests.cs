using System;
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
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.PlanetOrStarport) { Name = "Haven" });

            var ship = new Ship(cargoCapacity: 3, startingMoney: 100);
            var upgrade = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            ship.TryAddItem(upgrade);
            ship.TryAddItem(new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 50, ItemKind.Consumable));
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out var moneyLost);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, ship.Money);
            Assert.AreEqual(100, moneyLost, "The caller needs the pre-clear amount to hand it to a PvP winner.");
            // A losing streak resets cash and consumables, but earned
            // Permanent gear survives — see Ship.ClearMoneyAndNonPermanentItems.
            CollectionAssert.AreEqual(new[] { upgrade }, ship.HeldItems);
            Assert.AreEqual(new HexCoordinate(2, 0), player.Position);
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Hull),
                "Depleted Hull must reset, not leave the player stuck at 0 with no money to repair.");
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Energy),
                "Energy resets too, even though only Hull triggered the penalty here — this is a fresh start, not a partial one.");
            // A destroyed player always gets told what happened on their
            // next turn — not necessarily the one looking at the device
            // right now (see Player.PendingTurnStartNotice).
            Assert.IsNotNull(player.PendingTurnStartNotice);
            StringAssert.Contains("Haven", player.PendingTurnStartNotice);
            StringAssert.Contains("money has been lost", player.PendingTurnStartNotice);
            // Tells the map view to materialize the marker instead of
            // gliding it there — a destroyed ship didn't fly to Haven.
            Assert.IsTrue(player.JustTeleported);
        }

        [Test]
        public void ApplyIfDepleted_NotDepleted_DoesNothing()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            var ship = new Ship(cargoCapacity: 3, startingMoney: 100);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out _);

            Assert.IsFalse(applied);
            Assert.AreEqual(100, ship.Money);
            Assert.AreEqual(new HexCoordinate(0, 0), player.Position);
            Assert.IsNull(player.PendingTurnStartNotice);
            Assert.IsFalse(player.JustTeleported);
        }

        [Test]
        public void ApplyIfDepleted_EnergyZero_AlsoTriggersPenalty()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            var ship = new Ship(cargoCapacity: 3, startingMoney: 50);
            ship.ApplyStatDelta(CoreStat.Energy, -3);
            var player = new Player("p1", "Test", ship) { Position = new HexCoordinate(0, 0) };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out _);

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

            IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out _);

            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(Ship.DefaultStatValue, ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void ApplyIfDepleted_DestroyedOnSharedPlanetOrStarport_RelocatesToADifferentOneNotTheSameHex()
        {
            // The scenario this guards against: a PvP loss resolved on a
            // planet/starbase both players are standing on.
            // FindNearestPlanetOrStarport alone would trivially pick that
            // SAME hex right back (distance 0 to itself), reading as if
            // nothing happened. Only one OTHER planet/starport exists
            // here, so which one gets picked is deterministic regardless
            // of the rng seed — this isolates "does it avoid the current
            // hex" from "is the random pick correct."
            var map = new GameMap(radius: 2, Difficulty.Medium);
            var start = new HexCoordinate(0, 0);
            var other = new HexCoordinate(2, 0);
            map.SetHex(new Hex(start, TerrainType.PlanetOrStarport) { Name = "Start Station" });
            map.SetHex(new Hex(other, TerrainType.PlanetOrStarport) { Name = "Other Station" });

            var ship = new Ship(cargoCapacity: 3, startingMoney: 50);
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            var player = new Player("p1", "Test", ship) { Position = start };

            IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out _);

            Assert.AreEqual(other, player.Position);
            Assert.IsNotNull(player.PendingTurnStartNotice);
            StringAssert.Contains("Other Station", player.PendingTurnStartNotice);
            Assert.IsTrue(player.JustTeleported);
        }

        [Test]
        public void ApplyIfDepleted_OnlyPlanetOrStarportOnMapIsCurrentHex_StaysPutWithNoNotice()
        {
            // No OTHER planet/starport exists anywhere to send them to —
            // stats still reset, but position and notice are left alone
            // rather than crashing or silently respawning in place with
            // a notice that would be misleading ("towed to" the hex
            // they're already on).
            var map = new GameMap(radius: 1, Difficulty.Medium);
            var start = new HexCoordinate(0, 0);
            map.SetHex(new Hex(start, TerrainType.PlanetOrStarport));

            var ship = new Ship(cargoCapacity: 3, startingMoney: 50);
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            var player = new Player("p1", "Test", ship) { Position = start };

            var applied = IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out _);

            Assert.IsTrue(applied);
            Assert.AreEqual(start, player.Position);
            Assert.IsNull(player.PendingTurnStartNotice);
            Assert.IsFalse(player.JustTeleported);
        }

        [Test]
        public void ApplyIfDepleted_DestroyedOnSharedPlanetOrStarport_ConsumeTeleportedReturnsTrueOnceThenClears()
        {
            // The map-view side of this contract (see
            // MatchHud.UpdateMarkerOrMaterialize): ConsumeTeleported must
            // report true exactly once per actual teleport, then go back
            // to false, the same one-shot-flag shape ConsumePendingTurnStartNotice
            // already uses for the sibling notice.
            var map = new GameMap(radius: 2, Difficulty.Medium);
            var start = new HexCoordinate(0, 0);
            var other = new HexCoordinate(2, 0);
            map.SetHex(new Hex(start, TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(other, TerrainType.PlanetOrStarport));

            var ship = new Ship(cargoCapacity: 3, startingMoney: 50);
            ship.ApplyStatDelta(CoreStat.Hull, -3);
            var player = new Player("p1", "Test", ship) { Position = start };

            IntegrityPenaltyService.ApplyIfDepleted(player, map, new Random(1), out _);

            Assert.IsTrue(player.ConsumeTeleported());
            Assert.IsFalse(player.ConsumeTeleported());
            Assert.IsFalse(player.JustTeleported);
        }
    }
}
