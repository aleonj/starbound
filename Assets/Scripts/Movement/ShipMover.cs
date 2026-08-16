using System.Linq;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Shop;

namespace StarBound.Movement
{
    public static class ShipMover
    {
        // Takes the acting Player (not just a HexCoordinate) because the
        // Wormhole-device and Tradelane-toll gates below have to run
        // atomically with the terrain-match check, before the die is
        // marked spent — there's no "unspend" path, so a blocked move
        // must never reach die.MarkSpent().
        public static MoveResult TryMove(GameMap map, RolledDie die, Player player, HexCoordinate to)
        {
            if (die.IsSpent)
                return MoveResult.Failed(MoveFailureReason.DieAlreadySpent);

            if (!map.GetNeighborCoordinates(player.Position).Contains(to))
                return MoveResult.Failed(MoveFailureReason.TargetNotAdjacent);

            if (!map.TryGetHex(to, out var targetHex))
                return MoveResult.Failed(MoveFailureReason.TargetNotAdjacent);

            // Any die can be spent to land on a planet/starport, regardless
            // of the die's own terrain type.
            var isValidLanding = targetHex.Terrain == die.Terrain || targetHex.Terrain == TerrainType.PlanetOrStarport;
            if (!isValidLanding)
                return MoveResult.Failed(MoveFailureReason.TargetTerrainMismatch);

            if (targetHex.Terrain == TerrainType.Wormhole && !player.Ship.HeldItems.Contains(ItemPool.WormholeDevice))
                return MoveResult.Failed(MoveFailureReason.WormholeDeviceRequired);

            if (targetHex.Terrain == TerrainType.Tradelane)
            {
                if (player.Ship.Money < TollPricing.TradelaneTollPerHex)
                    return MoveResult.Failed(MoveFailureReason.InsufficientFundsForToll);

                player.Ship.TrySpendMoney(TollPricing.TradelaneTollPerHex);
            }

            die.MarkSpent();
            return MoveResult.Succeeded(to);
        }
    }
}
