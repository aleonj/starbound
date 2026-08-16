using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    public class MapView : MonoBehaviour
    {
        // Generates a fresh map and renders it. For rendering a map that
        // already exists (e.g. one a Match already owns), use Render.
        public void Generate(int radius, Difficulty difficulty, int seed, float hexRadius)
        {
            var map = MapGenerator.Generate(radius, difficulty, seed);
            EngagementPlacer.PlaceEngagements(map, difficulty, seed);
            Render(map, hexRadius);
        }

        public void Render(
            GameMap map,
            float hexRadius,
            IReadOnlyCollection<HexCoordinate> highlighted = null,
            IReadOnlyCollection<HexCoordinate> discoveredEngagementHexes = null)
        {
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            highlighted ??= System.Array.Empty<HexCoordinate>();
            discoveredEngagementHexes ??= System.Array.Empty<HexCoordinate>();

            foreach (var hex in map.Hexes)
            {
                var isHighlighted = highlighted.Contains(hex.Coordinate);
                var visibleTier = EngagementVisibility.GetVisibleTier(hex, discoveredEngagementHexes);
                var tileObject = new GameObject(
                    $"Hex ({hex.Coordinate.Q}, {hex.Coordinate.R})",
                    typeof(HexTileView));
                tileObject.transform.SetParent(transform, false);
                tileObject.transform.localPosition = HexLayout.AxialToWorld(hex.Coordinate, hexRadius);
                tileObject.GetComponent<HexTileView>().Initialize(hex.Terrain, visibleTier, hexRadius, isHighlighted);
            }
        }
    }
}
