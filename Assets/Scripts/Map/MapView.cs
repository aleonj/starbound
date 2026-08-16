using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    public class MapView : MonoBehaviour
    {
        // Reused across repeated Render calls instead of destroying and
        // recreating every hex — keeps each tile's GameObject identity
        // stable across moves/turns, which future living-galaxy effects
        // (planet spin, shader time, etc.) need in order to persist.
        private readonly Dictionary<HexCoordinate, HexTileView> tiles = new();

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
            IReadOnlyCollection<HexCoordinate> discoveredEngagementHexes = null,
            HexCoordinate? pendingTarget = null)
        {
            highlighted ??= System.Array.Empty<HexCoordinate>();
            discoveredEngagementHexes ??= System.Array.Empty<HexCoordinate>();

            var seenCoordinates = new HashSet<HexCoordinate>();

            foreach (var hex in map.Hexes)
            {
                seenCoordinates.Add(hex.Coordinate);

                var highlightState = pendingTarget.HasValue && hex.Coordinate == pendingTarget.Value
                    ? HexHighlightState.Pending
                    : highlighted.Contains(hex.Coordinate) ? HexHighlightState.LegalTarget : HexHighlightState.None;
                var visibleTier = EngagementVisibility.GetVisibleTier(hex, discoveredEngagementHexes);

                if (!tiles.TryGetValue(hex.Coordinate, out var tileView))
                {
                    var tileObject = new GameObject($"Hex ({hex.Coordinate.Q}, {hex.Coordinate.R})", typeof(HexTileView));
                    tileObject.transform.SetParent(transform, false);
                    tileObject.transform.localPosition = HexLayout.AxialToWorld(hex.Coordinate, hexRadius);
                    tileView = tileObject.GetComponent<HexTileView>();
                    tileView.Initialize(hex.Terrain, hexRadius);
                    tiles[hex.Coordinate] = tileView;
                }

                tileView.SetState(hex.Terrain, visibleTier, highlightState);
            }

            // Defensive only — a map's hex set doesn't change mid-match in
            // practice (see MapView usage in DemoBootstrap/MatchHud), but
            // keeps `tiles` accurate if Render is ever called against a
            // differently-shaped map.
            var stale = tiles.Keys.Where(coordinate => !seenCoordinates.Contains(coordinate)).ToList();
            foreach (var coordinate in stale)
            {
                Destroy(tiles[coordinate].gameObject);
                tiles.Remove(coordinate);
            }
        }
    }
}
