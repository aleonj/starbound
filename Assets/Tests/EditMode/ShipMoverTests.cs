using NUnit.Framework;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Movement;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class ShipMoverTests
    {
        private static GameMap BuildMap()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.Asteroids));
            map.SetHex(new Hex(new HexCoordinate(1, -1), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(-1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(-1, 1), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(0, 1), TerrainType.ClearSpace));
            return map;
        }

        private static GameMap BuildMapWithTollAndWormholeHexes()
        {
            var map = new GameMap(radius: 1, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.Tradelane));
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.Wormhole));
            return map;
        }

        private static Player BuildPlayer(HexCoordinate position, int money = 0)
        {
            var player = new Player("p1", "One", new Ship(cargoCapacity: 3, startingMoney: money))
            {
                Position = position
            };
            return player;
        }

        [Test]
        public void TryMove_MatchingTerrain_Succeeds()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.Asteroids);
            var player = BuildPlayer(new HexCoordinate(0, 0));

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(1, 0));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(new HexCoordinate(1, 0), result.NewPosition);
            Assert.IsTrue(die.IsSpent);
        }

        [Test]
        public void TryMove_MismatchedTerrain_Fails()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.Mines);
            var player = BuildPlayer(new HexCoordinate(0, 0));

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(1, 0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.TargetTerrainMismatch, result.FailureReason);
            Assert.IsFalse(die.IsSpent);
        }

        [Test]
        public void TryMove_AnyDieCanLandOnPlanetOrStarport()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.Mines); // deliberately mismatched type
            var player = BuildPlayer(new HexCoordinate(0, 0));

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(1, -1));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(die.IsSpent);
        }

        [Test]
        public void TryMove_AlreadySpentDie_Fails()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.ClearSpace);
            die.MarkSpent();
            var player = BuildPlayer(new HexCoordinate(0, 0));

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(0, -1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.DieAlreadySpent, result.FailureReason);
        }

        [Test]
        public void TryMove_NonAdjacentTarget_Fails()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.ClearSpace));
            var die = new RolledDie(0, TerrainType.ClearSpace);
            var player = BuildPlayer(new HexCoordinate(0, 0));

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(3, 0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.TargetNotAdjacent, result.FailureReason);
        }

        [Test]
        public void TryMove_OntoTradelane_ChargesTheToll()
        {
            var map = BuildMapWithTollAndWormholeHexes();
            var die = new RolledDie(0, TerrainType.Tradelane);
            var player = BuildPlayer(new HexCoordinate(0, 0), money: 10);

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(1, 0));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(die.IsSpent);
            Assert.AreEqual(10 - TollPricing.TradelaneTollPerHex, player.Ship.Money);
        }

        [Test]
        public void TryMove_OntoTradelane_InsufficientFunds_BlocksTheMove()
        {
            var map = BuildMapWithTollAndWormholeHexes();
            var die = new RolledDie(0, TerrainType.Tradelane);
            var player = BuildPlayer(new HexCoordinate(0, 0), money: 0);

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(1, 0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.InsufficientFundsForToll, result.FailureReason);
            Assert.IsFalse(die.IsSpent);
            Assert.AreEqual(0, player.Ship.Money);
        }

        [Test]
        public void TryMove_OntoWormhole_WithoutDevice_Fails()
        {
            var map = BuildMapWithTollAndWormholeHexes();
            var die = new RolledDie(0, TerrainType.Wormhole);
            var player = BuildPlayer(new HexCoordinate(0, 0));

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(0, -1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.WormholeDeviceRequired, result.FailureReason);
            Assert.IsFalse(die.IsSpent);
        }

        [Test]
        public void TryMove_OntoWormhole_WithDevice_Succeeds()
        {
            var map = BuildMapWithTollAndWormholeHexes();
            var die = new RolledDie(0, TerrainType.Wormhole);
            var player = BuildPlayer(new HexCoordinate(0, 0));
            player.Ship.TryAddItem(ItemPool.WormholeDevice);

            var result = ShipMover.TryMove(map, die, player, to: new HexCoordinate(0, -1));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(die.IsSpent);
        }
    }
}
