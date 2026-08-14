using StarBound.Core;
using StarBound.Map;

namespace StarBound.Combat
{
    // Applies whenever a player's Hull or Energy reaches 0, in or out of
    // combat — not wired into EngagementSession automatically, since the
    // rule applies more broadly than just combat. Callers should invoke
    // this after anything that can deplete Hull/Energy.
    public static class IntegrityPenaltyService
    {
        public static bool ApplyIfDepleted(Player player, GameMap map)
        {
            if (!player.Ship.IsIntegrityDepleted)
                return false;

            player.Ship.ClearMoneyAndItems();

            var nearestPlanet = FindNearestPlanetOrStarport(map, player.Position);
            if (nearestPlanet != null)
                player.Position = nearestPlanet.Value;

            return true;
        }

        private static HexCoordinate? FindNearestPlanetOrStarport(GameMap map, HexCoordinate from)
        {
            HexCoordinate? nearest = null;
            var nearestDistance = int.MaxValue;

            foreach (var hex in map.Hexes)
            {
                if (hex.Terrain != TerrainType.PlanetOrStarport)
                    continue;

                var distance = HexMath.Distance(from, hex.Coordinate);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = hex.Coordinate;
                }
            }

            return nearest;
        }
    }
}
