using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Map
{
    // Procedurally fills a GameMap with terrain, clustering hazard/road
    // terrain types so they never appear as isolated single hexes.
    public static class MapGenerator
    {
        private const int MinClusterSize = 2;
        private const int MaxClusterSize = 4;
        private const int MinTradelaneLength = 2;
        private const int MaxTradelaneLength = 5;
        private const int MaxPlacementAttempts = 200;

        public static GameMap Generate(int radius, Difficulty difficulty, int seed)
        {
            var map = new GameMap(radius, difficulty);
            var rng = new Random(seed);

            foreach (var coordinate in EnumerateCoordinates(radius))
                map.SetHex(new Hex(coordinate, TerrainType.ClearSpace));

            var totalHexes = map.Hexes.Count;
            var distribution = TerrainDistributionTable.For(difficulty);

            PlaceClusters(map, rng, TerrainType.Asteroids, Budget(totalHexes, distribution.Asteroids));
            PlaceClusters(map, rng, TerrainType.Mines, Budget(totalHexes, distribution.Mines));
            PlaceClusters(map, rng, TerrainType.Debris, Budget(totalHexes, distribution.Debris));
            PlaceTradelanes(map, rng, Budget(totalHexes, distribution.Tradelane));
            PlaceSingles(map, rng, TerrainType.PlanetOrStarport, Budget(totalHexes, distribution.PlanetOrStarport));
            PlaceSingles(map, rng, TerrainType.Wormhole, Budget(totalHexes, distribution.Wormhole));

            return map;
        }

        private static int Budget(int totalHexes, float proportion) =>
            (int)Math.Round(totalHexes * proportion, MidpointRounding.AwayFromZero);

        private static IEnumerable<HexCoordinate> EnumerateCoordinates(int radius)
        {
            for (var q = -radius; q <= radius; q++)
            {
                var rMin = Math.Max(-radius, -q - radius);
                var rMax = Math.Min(radius, -q + radius);
                for (var r = rMin; r <= rMax; r++)
                    yield return new HexCoordinate(q, r);
            }
        }

        private static void PlaceClusters(GameMap map, Random rng, TerrainType terrain, int budget)
        {
            var remaining = budget;
            var attempts = 0;

            while (remaining > 0 && attempts < MaxPlacementAttempts)
            {
                attempts++;
                var seedCoordinate = PickRandomClearSpace(map, rng);
                if (seedCoordinate == null)
                    break;

                var targetSize = Math.Min(remaining, rng.Next(MinClusterSize, MaxClusterSize + 1));
                var cluster = GrowCluster(map, seedCoordinate.Value, targetSize, rng);

                // Couldn't grow big enough from this seed — leave as
                // ClearSpace rather than create an isolated single hex.
                if (cluster.Count < MinClusterSize)
                    continue;

                foreach (var coordinate in cluster)
                {
                    if (map.TryGetHex(coordinate, out var hex))
                        hex.Terrain = terrain;
                }

                remaining -= cluster.Count;
            }
        }

        private static List<HexCoordinate> GrowCluster(GameMap map, HexCoordinate seed, int targetSize, Random rng)
        {
            var cluster = new List<HexCoordinate> { seed };
            var frontier = new List<HexCoordinate> { seed };

            while (cluster.Count < targetSize && frontier.Count > 0)
            {
                var index = rng.Next(frontier.Count);
                var current = frontier[index];
                frontier.RemoveAt(index);

                var candidates = map.GetNeighborCoordinates(current)
                    .Where(c => IsClearSpace(map, c) && !cluster.Contains(c))
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                var next = candidates[rng.Next(candidates.Count)];
                cluster.Add(next);
                frontier.Add(next);
                frontier.Add(current);
            }

            return cluster;
        }

        private static void PlaceTradelanes(GameMap map, Random rng, int budget)
        {
            var remaining = budget;
            var attempts = 0;

            while (remaining > 0 && attempts < MaxPlacementAttempts)
            {
                attempts++;
                var start = PickRandomClearSpace(map, rng);
                if (start == null)
                    break;

                var targetLength = Math.Min(remaining, rng.Next(MinTradelaneLength, MaxTradelaneLength + 1));
                var path = GrowPath(map, start.Value, targetLength, rng);

                if (path.Count < MinTradelaneLength)
                    continue;

                foreach (var coordinate in path)
                {
                    if (map.TryGetHex(coordinate, out var hex))
                        hex.Terrain = TerrainType.Tradelane;
                }

                remaining -= path.Count;
            }
        }

        private static List<HexCoordinate> GrowPath(GameMap map, HexCoordinate start, int targetLength, Random rng)
        {
            var path = new List<HexCoordinate> { start };
            var current = start;

            while (path.Count < targetLength)
            {
                var candidates = map.GetNeighborCoordinates(current)
                    .Where(c => IsClearSpace(map, c) && !path.Contains(c))
                    .ToList();

                if (candidates.Count == 0)
                    break;

                current = candidates[rng.Next(candidates.Count)];
                path.Add(current);
            }

            return path;
        }

        private static void PlaceSingles(GameMap map, Random rng, TerrainType terrain, int budget)
        {
            for (var i = 0; i < budget; i++)
            {
                var coordinate = PickRandomClearSpace(map, rng);
                if (coordinate == null)
                    break;

                if (map.TryGetHex(coordinate.Value, out var hex))
                    hex.Terrain = terrain;
            }
        }

        private static bool IsClearSpace(GameMap map, HexCoordinate coordinate) =>
            map.TryGetHex(coordinate, out var hex) && hex.Terrain == TerrainType.ClearSpace;

        private static HexCoordinate? PickRandomClearSpace(GameMap map, Random rng)
        {
            var candidates = map.Hexes.Where(h => h.Terrain == TerrainType.ClearSpace).ToList();
            if (candidates.Count == 0)
                return null;

            return candidates[rng.Next(candidates.Count)].Coordinate;
        }
    }
}
