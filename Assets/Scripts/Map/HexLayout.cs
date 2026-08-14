using System;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    // Flat-top axial-to-world conversion, matching HexMath's axial adjacency.
    public static class HexLayout
    {
        public static Vector3 AxialToWorld(HexCoordinate coordinate, float hexRadius)
        {
            var x = hexRadius * 1.5f * coordinate.Q;
            var y = hexRadius * Mathf.Sqrt(3f) * (coordinate.R + coordinate.Q / 2f);
            return new Vector3(x, y, 0f);
        }

        // Inverse of AxialToWorld, for turning a mouse click's world position
        // back into the hex coordinate it landed in. Uses fractional axial
        // coordinates plus cube rounding (the standard technique) rather
        // than nearest-neighbor search, so it's exact and O(1).
        public static HexCoordinate WorldToAxial(Vector3 world, float hexRadius)
        {
            var qFrac = (2.0 / 3.0 * world.x) / hexRadius;
            var rFrac = (-1.0 / 3.0 * world.x + Math.Sqrt(3) / 3.0 * world.y) / hexRadius;
            return CubeRound(qFrac, rFrac);
        }

        private static HexCoordinate CubeRound(double qFrac, double rFrac)
        {
            var xFrac = qFrac;
            var zFrac = rFrac;
            var yFrac = -xFrac - zFrac;

            var rx = Math.Round(xFrac);
            var ry = Math.Round(yFrac);
            var rz = Math.Round(zFrac);

            var xDiff = Math.Abs(rx - xFrac);
            var yDiff = Math.Abs(ry - yFrac);
            var zDiff = Math.Abs(rz - zFrac);

            if (xDiff > yDiff && xDiff > zDiff)
                rx = -ry - rz;
            else if (yDiff > zDiff)
                ry = -rx - rz;
            else
                rz = -rx - ry;

            return new HexCoordinate((int)rx, (int)rz);
        }
    }
}
