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
        // tollPerHex replaces an earlier plain waive/don't-waive bool —
        // generalized to an actual amount so a caller can pass 0 (waived,
        // see MatchVariable.TradeBoom), the normal TollPricing default,
        // or a raised amount (see MatchVariable.FuelShortage) through the
        // same parameter rather than needing a second on/off flag.
        public static MoveResult TryMove(GameMap map, RolledDie die, Player player, HexCoordinate to, int tollPerHex = TollPricing.TradelaneTollPerHex)
        {
            if (die.IsSpent)
                return MoveResult.Failed(MoveFailureReason.DieAlreadySpent);

            // Energy caps how many of this turn's rolled dice are usable
            // (see [Combat] Energy overhaul) — a die's own fixed index
            // (0-4, see MovementDiceSet) beyond the ship's current
            // Energy is locked out regardless of which die it is. The UI
            // already disables these in the dice tray (see
            // MatchHudChrome.RefreshDiceTray), but this is the real gate
            // — the synthetic Wormhole-device die (DieIndex -1) never
            // reaches this method, see Match.TravelToWormhole.
            if (die.DieIndex >= player.Ship.GetStat(CoreStat.Energy))
                return MoveResult.Failed(MoveFailureReason.NotEnoughEnergy);

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

            if (targetHex.Terrain == TerrainType.Tradelane && tollPerHex > 0)
            {
                if (player.Ship.Money < tollPerHex)
                    return MoveResult.Failed(MoveFailureReason.InsufficientFundsForToll);

                player.Ship.TrySpendMoney(tollPerHex);
            }

            die.MarkSpent();
            return MoveResult.Succeeded(to);
        }
    }
}
