using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    public class MapView : MonoBehaviour
    {
        public void Generate(int radius, Difficulty difficulty, int seed, float hexRadius)
        {
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            var map = MapGenerator.Generate(radius, difficulty, seed);
            EngagementPlacer.PlaceEngagements(map, difficulty, seed);

            foreach (var hex in map.Hexes)
            {
                var tileObject = new GameObject(
                    $"Hex ({hex.Coordinate.Q}, {hex.Coordinate.R})",
                    typeof(HexTileView));
                tileObject.transform.SetParent(transform, false);
                tileObject.transform.localPosition = HexLayout.AxialToWorld(hex.Coordinate, hexRadius);
                tileObject.GetComponent<HexTileView>().Initialize(hex.Terrain, hex.Engagement, hexRadius);
            }
        }
    }
}
