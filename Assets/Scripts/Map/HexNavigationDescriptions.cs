using System;
using System.Linq;
using StarBound.Core;

namespace StarBound.Map
{
    // Replaces raw axial (Q, R) coordinates in player-facing text with a
    // procedurally-generated flavor name plus a landmark-relative bearing
    // — "Kepler's Drift, two hexes rimward of Meridian" instead of
    // "(2, -1)", which players correctly called out as meaningless (see
    // the companion Notion story on hex navigation). Deliberately avoids
    // real-world compass words (N/S/E/W don't fit a space setting) in
    // favor of the spinward/trailing/coreward/rimward convention —
    // directions relative to the map's own galactic core (its center,
    // (0, 0) — see MapGenerator's radius-around-origin layout), not an
    // arbitrary fixed "up is north" frame.
    public static class HexNavigationDescriptions
    {
        // Deliberately a different flavor/pool than PlanetNames — these
        // read as a region/feature of open space, not a settlement.
        private static readonly string[] ProperNames =
        {
            "Kepler", "Vega", "Tycho", "Halley", "Drake", "Sagan", "Ptolemy",
            "Hawking", "Oort", "Chandra", "Hypatia", "Copernicus", "Laika",
            "Armstrong", "Herschel",
        };

        private static readonly string[] FeatureNames =
        {
            "Drift", "Hollow", "Reach", "Expanse", "Belt", "Shoal", "Span",
            "Gulf", "Verge", "Rift", "Shallows", "Wake", "Corridor", "Field",
        };

        // A hex that IS a planet/starport already has its own authored
        // name (MapGenerator/PlanetNames/StarportNames) — use that
        // directly rather than generating a redundant second name for it.
        public static string Describe(GameMap map, HexCoordinate target)
        {
            if (map.TryGetHex(target, out var hex) && !string.IsNullOrEmpty(hex.Name))
                return hex.Name;

            var flavorName = FlavorName(map.Seed, target);
            var landmark = NearestLandmark(map, target);

            if (landmark == null)
                return flavorName; // No named landmark anywhere on this map — flavor name stands alone.

            var distance = HexMath.Distance(target, landmark.Value.Coordinate);
            var hexWord = distance == 1 ? "hex" : "hexes";
            return $"{flavorName}, {distance} {hexWord} {Bearing(target, landmark.Value.Coordinate)} of {landmark.Value.Name}";
        }

        // Pure function of (map seed, coordinate) — no storage needed,
        // stable for the lifetime of one match (same map, same seed),
        // varies across matches (different seed) the same way the rest
        // of generation already does.
        private static string FlavorName(int seed, HexCoordinate coordinate)
        {
            var rng = new Random(HashCode.Combine(seed, coordinate.Q, coordinate.R));
            var proper = ProperNames[rng.Next(ProperNames.Length)];
            var feature = FeatureNames[rng.Next(FeatureNames.Length)];
            return $"{proper}'s {feature}";
        }

        private static (HexCoordinate Coordinate, string Name)? NearestLandmark(GameMap map, HexCoordinate target)
        {
            var landmarks = map.Hexes
                .Where(h => h.Terrain == TerrainType.PlanetOrStarport && !string.IsNullOrEmpty(h.Name))
                .OrderBy(h => HexMath.Distance(target, h.Coordinate))
                .ToList();

            return landmarks.Count == 0 ? null : (landmarks[0].Coordinate, landmarks[0].Name);
        }

        // Axial-to-cartesian projection (pointy-top convention) — the
        // orientation doesn't need to match the actual render layout, any
        // consistent projection yields a correct RELATIVE bearing.
        // "Rimward" at the landmark's own position is "directly away from
        // map center through the landmark"; spinward/trailing are the two
        // directions perpendicular to that, using a fixed rotational
        // sense so the same pair of hexes always gets the same answer.
        private static string Bearing(HexCoordinate target, HexCoordinate landmark)
        {
            var center = ToCartesian(new HexCoordinate(0, 0));
            var landmarkPos = ToCartesian(landmark);
            var targetPos = ToCartesian(target);

            var radial = Normalize(landmarkPos.X - center.X, landmarkPos.Y - center.Y);
            var tangent = (X: -radial.Y, Y: radial.X); // 90° rotation of radial — the "spin" direction at the landmark.

            var offsetX = targetPos.X - landmarkPos.X;
            var offsetY = targetPos.Y - landmarkPos.Y;
            var radialComponent = offsetX * radial.X + offsetY * radial.Y;
            var tangentComponent = offsetX * tangent.X + offsetY * tangent.Y;

            return Math.Abs(radialComponent) >= Math.Abs(tangentComponent)
                ? (radialComponent >= 0 ? "rimward" : "coreward")
                : (tangentComponent >= 0 ? "spinward" : "trailing");
        }

        private static (double X, double Y) ToCartesian(HexCoordinate coordinate) =>
            (coordinate.Q + coordinate.R * 0.5, coordinate.R * 0.8660254037844387);

        private static (double X, double Y) Normalize(double x, double y)
        {
            var length = Math.Sqrt(x * x + y * y);
            // Landmark sits exactly at map center — arbitrary but stable fallback axis.
            return length < 1e-9 ? (0, 1) : (x / length, y / length);
        }
    }
}
