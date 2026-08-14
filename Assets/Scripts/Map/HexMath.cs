using System;
using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Map
{
    // Pure axial-coordinate math: neighbor lookup and distance. Grid-bounds
    // aware queries live on GameMapExtensions, not here.
    public static class HexMath
    {
        // The six axial neighbor directions, in a fixed clockwise order.
        private static readonly HexCoordinate[] Directions =
        {
            new(1, 0),
            new(1, -1),
            new(0, -1),
            new(-1, 0),
            new(-1, 1),
            new(0, 1),
        };

        public static HexCoordinate Neighbor(HexCoordinate origin, int direction)
        {
            if (direction < 0 || direction >= Directions.Length)
                throw new ArgumentOutOfRangeException(nameof(direction));

            var offset = Directions[direction];
            return new HexCoordinate(origin.Q + offset.Q, origin.R + offset.R);
        }

        public static IEnumerable<HexCoordinate> Neighbors(HexCoordinate origin)
        {
            for (var direction = 0; direction < Directions.Length; direction++)
                yield return Neighbor(origin, direction);
        }

        public static int Distance(HexCoordinate a, HexCoordinate b)
        {
            var dq = a.Q - b.Q;
            var dr = a.R - b.R;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
        }
    }
}
