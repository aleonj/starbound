using NUnit.Framework;
using StarBound.Core;
using StarBound.Movement;

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

        [Test]
        public void TryMove_MatchingTerrain_Succeeds()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.Asteroids);

            var result = ShipMover.TryMove(map, die, from: new HexCoordinate(0, 0), to: new HexCoordinate(1, 0));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(new HexCoordinate(1, 0), result.NewPosition);
            Assert.IsTrue(die.IsSpent);
        }

        [Test]
        public void TryMove_MismatchedTerrain_Fails()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.Mines);

            var result = ShipMover.TryMove(map, die, from: new HexCoordinate(0, 0), to: new HexCoordinate(1, 0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.TargetTerrainMismatch, result.FailureReason);
            Assert.IsFalse(die.IsSpent);
        }

        [Test]
        public void TryMove_AnyDieCanLandOnPlanetOrStarport()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.Mines); // deliberately mismatched type

            var result = ShipMover.TryMove(map, die, from: new HexCoordinate(0, 0), to: new HexCoordinate(1, -1));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(die.IsSpent);
        }

        [Test]
        public void TryMove_AlreadySpentDie_Fails()
        {
            var map = BuildMap();
            var die = new RolledDie(0, TerrainType.ClearSpace);
            die.MarkSpent();

            var result = ShipMover.TryMove(map, die, from: new HexCoordinate(0, 0), to: new HexCoordinate(0, -1));

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

            var result = ShipMover.TryMove(map, die, from: new HexCoordinate(0, 0), to: new HexCoordinate(3, 0));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.TargetNotAdjacent, result.FailureReason);
        }
    }
}
