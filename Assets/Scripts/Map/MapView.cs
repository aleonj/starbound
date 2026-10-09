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
            HexCoordinate? pendingTarget = null,
            IReadOnlyCollection<HexCoordinate> waypoints = null,
            HexCoordinate? goalTargetHex = null)
        {
            highlighted ??= System.Array.Empty<HexCoordinate>();
            discoveredEngagementHexes ??= System.Array.Empty<HexCoordinate>();
            waypoints ??= System.Array.Empty<HexCoordinate>();

            var seenCoordinates = new HashSet<HexCoordinate>();

            foreach (var hex in map.Hexes)
            {
                seenCoordinates.Add(hex.Coordinate);

                // This-turn movement state only — Waypoint/GoalTarget used
                // to live here too (see HexHighlightState's own comment),
                // which made a job destination or the race goal visually
                // indistinguishable from "you can move here this turn"
                // and got repeatedly misreported as a stuck highlight.
                // They're independent marker-channel state now (below),
                // not mutually exclusive with this any more — a hex can
                // show both at once (e.g. a job waypoint that also
                // happens to be a legal move target this turn).
                var highlightState = pendingTarget.HasValue && hex.Coordinate == pendingTarget.Value
                    ? HexHighlightState.Pending
                    : highlighted.Contains(hex.Coordinate) ? HexHighlightState.LegalTarget
                    : HexHighlightState.None;

                // GoalTarget takes priority over Waypoint on the rare hex
                // where both would otherwise apply (see HexMarkerOverride).
                var markerOverride = goalTargetHex.HasValue && hex.Coordinate == goalTargetHex.Value
                    ? HexMarkerOverride.GoalTarget
                    : waypoints.Contains(hex.Coordinate) ? HexMarkerOverride.Waypoint
                    : HexMarkerOverride.None;

                var visibleTier = EngagementVisibility.GetVisibleTier(hex, discoveredEngagementHexes);

                if (!tiles.TryGetValue(hex.Coordinate, out var tileView))
                {
                    var tileObject = new GameObject($"Hex ({hex.Coordinate.Q}, {hex.Coordinate.R})", typeof(HexTileView));
                    tileObject.transform.SetParent(transform, false);
                    tileObject.transform.localPosition = HexLayout.AxialToWorld(hex.Coordinate, hexRadius);
                    tileView = tileObject.GetComponent<HexTileView>();
                    tileView.Initialize(hex.Terrain, hexRadius);
                    tiles[hex.Coordinate] = tileView;

                    // Terrain never changes after generation, so connector
                    // connectivity (see TradelaneConnector.shader) only
                    // needs computing once, here, not on every Render pass.
                    if (hex.Terrain == TerrainType.Tradelane)
                        tileView.SetConnectionMask(ComputeTradelaneConnectionMask(map, hex.Coordinate));

                    // Same one-time reasoning as above — a hex's Planet/
                    // Starport role (see Hex.IsStarport) is fixed at
                    // generation time too.
                    if (hex.Terrain == TerrainType.PlanetOrStarport)
                        tileView.SetIsStarport(hex.IsStarport);
                }

                tileView.SetState(hex.Terrain, visibleTier, highlightState, markerOverride);
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

        // One-shot attention pulse on a specific hex (e.g. "locate my
        // ship") — a no-op if the coordinate isn't a known tile. See
        // HexTileView.Flash for why this is independent of the
        // highlighted/pendingTarget/waypoints state Render manages above.
        public void FlashHex(HexCoordinate coordinate, Color color, float duration)
        {
            if (tiles.TryGetValue(coordinate, out var tileView))
                tileView.Flash(color, duration);
        }

        // Bit `dir` set means HexMath.Neighbor(coordinate, dir) is also
        // Tradelane. Direction indices correspond to fixed world-space
        // edge angles on the mesh (0->30deg, 1->330deg, 2->270deg,
        // 3->210deg, 4->150deg, 5->90deg — verified against
        // HexLayout.AxialToWorld's formula), which TradelaneConnector.shader
        // relies on to point each connector at the correct edge.
        private static int ComputeTradelaneConnectionMask(GameMap map, HexCoordinate coordinate)
        {
            var mask = 0;
            for (var direction = 0; direction < 6; direction++)
            {
                var neighborCoordinate = HexMath.Neighbor(coordinate, direction);
                if (map.TryGetHex(neighborCoordinate, out var neighborHex) && neighborHex.Terrain == TerrainType.Tradelane)
                    mask |= 1 << direction;
            }

            return mask;
        }
    }
}
