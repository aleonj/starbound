using System.Linq;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Movement
{
    public static class ShipMover
    {
        public static MoveResult TryMove(GameMap map, RolledDie die, HexCoordinate from, HexCoordinate to)
        {
            if (die.IsSpent)
                return MoveResult.Failed(MoveFailureReason.DieAlreadySpent);

            if (!map.GetNeighborCoordinates(from).Contains(to))
                return MoveResult.Failed(MoveFailureReason.TargetNotAdjacent);

            if (!map.TryGetHex(to, out var targetHex))
                return MoveResult.Failed(MoveFailureReason.TargetNotAdjacent);

            // Any die can be spent to land on a planet/starport, regardless
            // of the die's own terrain type.
            var isValidLanding = targetHex.Terrain == die.Terrain || targetHex.Terrain == TerrainType.PlanetOrStarport;
            if (!isValidLanding)
                return MoveResult.Failed(MoveFailureReason.TargetTerrainMismatch);

            die.MarkSpent();
            return MoveResult.Succeeded(to);
        }
    }
}
