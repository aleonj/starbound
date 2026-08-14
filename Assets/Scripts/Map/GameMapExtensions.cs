using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Map
{
    // Grid-shape queries over a GameMap: a hexagonal map of hexagons
    // centered on (0,0), out to its configured radius.
    public static class GameMapExtensions
    {
        private static readonly HexCoordinate Origin = new(0, 0);

        public static bool IsWithinBounds(this GameMap map, HexCoordinate coordinate) =>
            HexMath.Distance(Origin, coordinate) <= map.Radius;

        // Only the neighbors that fall within the map's radius — fewer
        // than 6 at the edge, per the [Map] Hex grid AC.
        public static IEnumerable<HexCoordinate> GetNeighborCoordinates(this GameMap map, HexCoordinate origin) =>
            HexMath.Neighbors(origin).Where(map.IsWithinBounds);
    }
}
