using System;
using System.Linq;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Multiplayer
{
    // Each player starts at a different, randomly chosen planet/starport,
    // deterministic from the match seed. Falls back to sharing the map
    // center if the generated map doesn't have at least two planets (e.g.
    // a tiny map on Hard, where the planet budget can drop to 0 or 1).
    public static class MatchFactory
    {
        private static readonly HexCoordinate FallbackPosition = new(0, 0);

        public static Match CreateMatch(MapSize mapSize, Difficulty difficulty, int seed, Player playerOne, Player playerTwo)
        {
            var map = MapGenerator.Generate(mapSize.ToRadius(), difficulty, seed);
            EngagementPlacer.PlaceEngagements(map, difficulty, seed);

            var rng = new Random(seed);
            var (startOne, startTwo) = PickStartingPositions(map, rng);

            playerOne.Position = startOne;
            playerTwo.Position = startTwo;

            return new Match(map, playerOne, playerTwo);
        }

        private static (HexCoordinate One, HexCoordinate Two) PickStartingPositions(GameMap map, Random rng)
        {
            var planets = map.Hexes
                .Where(hex => hex.Terrain == TerrainType.PlanetOrStarport)
                .Select(hex => hex.Coordinate)
                .ToList();

            HexCoordinate one, two;
            if (planets.Count >= 2)
            {
                one = planets[rng.Next(planets.Count)];
                do
                {
                    two = planets[rng.Next(planets.Count)];
                } while (two.Equals(one));
            }
            else
            {
                one = two = FallbackPosition;
                if (map.TryGetHex(FallbackPosition, out var fallbackHex))
                    fallbackHex.Terrain = TerrainType.ClearSpace;
            }

            // A hidden engagement marker right under a starting planet
            // would ambush a player before they've made a single move —
            // clear it, same safety guarantee the old fixed-center start had.
            ClearEngagementMarker(map, one);
            ClearEngagementMarker(map, two);

            return (one, two);
        }

        private static void ClearEngagementMarker(GameMap map, HexCoordinate coordinate)
        {
            if (map.TryGetHex(coordinate, out var hex))
                hex.Engagement = EngagementTier.None;
        }
    }
}
