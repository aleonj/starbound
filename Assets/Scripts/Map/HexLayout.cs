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
    }
}
