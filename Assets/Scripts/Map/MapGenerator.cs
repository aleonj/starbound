using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Map
{
    // Procedurally fills a GameMap with terrain, clustering hazard terrain
    // types so they never appear as isolated single hexes, and routing
    // tradelanes between planets so they read as roads, not random squiggles.
    public static class MapGenerator
    {
        private const int MinClusterSize = 2;
        private const int MaxClusterSize = 4;
        private const int MaxPlacementAttempts = 200;
        private const int MaxPathSteps = 200;

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

            // Planets are placed before tradelanes so routes have targets.
            var planets = PlaceSingles(map, rng, TerrainType.PlanetOrStarport, Budget(totalHexes, distribution.PlanetOrStarport));
            PlaceSingles(map, rng, TerrainType.Wormhole, Budget(totalHexes, distribution.Wormhole));

            ConnectPlanetsWithTradelanes(map, planets, rng, Budget(totalHexes, distribution.Tradelane));

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

        // Connects planets in a nearest-neighbor chain, routing a path
        // through ClearSpace toward each target. Budget-limited: not every
        // planet is guaranteed to end up connected, and a route can fail
        // to reach its target if hazards block every way through.
        private static void ConnectPlanetsWithTradelanes(GameMap map, IReadOnlyList<HexCoordinate> planets, Random rng, int budget)
        {
            if (planets.Count < 2 || budget <= 0)
                return;

            var remaining = budget;
            var connected = new List<HexCoordinate> { planets[0] };
            var unconnected = new List<HexCoordinate>(planets.Skip(1));

            while (unconnected.Count > 0 && remaining > 0)
            {
                var (from, to) = FindNearestPair(connected, unconnected);
                var path = BuildPathTowards(map, from, to, rng, remaining);

                foreach (var coordinate in path)
                {
                    if (map.TryGetHex(coordinate, out var hex) && hex.Terrain == TerrainType.ClearSpace)
                        hex.Terrain = TerrainType.Tradelane;
                }
                remaining -= path.Count;

                connected.Add(to);
                unconnected.Remove(to);
            }
        }

        private static (HexCoordinate From, HexCoordinate To) FindNearestPair(
            IReadOnlyList<HexCoordinate> connected, IReadOnlyList<HexCoordinate> unconnected)
        {
            var bestFrom = connected[0];
            var bestTo = unconnected[0];
            var bestDistance = int.MaxValue;

            foreach (var from in connected)
            {
                foreach (var to in unconnected)
                {
                    var distance = HexMath.Distance(from, to);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestFrom = from;
                        bestTo = to;
                    }
                }
            }

            return (bestFrom, bestTo);
        }

        // Greedy walk from 'from' toward 'to', stepping to whichever
        // ClearSpace neighbor is closest to the target each time. Doesn't
        // include the target hex itself, so the planet it's routing to is
        // never overwritten. Returns an empty path if it gets stuck before
        // reaching the target or exceeds the budget.
        private static List<HexCoordinate> BuildPathTowards(GameMap map, HexCoordinate from, HexCoordinate to, Random rng, int maxLength)
        {
            var path = new List<HexCoordinate>();
            var current = from;
            var steps = 0;

            while (!current.Equals(to) && path.Count < maxLength && steps < MaxPathSteps)
            {
                steps++;
                var candidates = map.GetNeighborCoordinates(current)
                    .Where(c => c.Equals(to) || IsClearSpace(map, c))
                    .OrderBy(c => HexMath.Distance(c, to))
                    .ThenBy(_ => rng.Next())
                    .ToList();

                if (candidates.Count == 0)
                    return new List<HexCoordinate>(); // stuck — abort this connection

                current = candidates[0];
                if (!current.Equals(to))
                    path.Add(current);
            }

            return current.Equals(to) ? path : new List<HexCoordinate>();
        }

        private static List<HexCoordinate> PlaceSingles(GameMap map, Random rng, TerrainType terrain, int budget)
        {
            var placed = new List<HexCoordinate>();

            for (var i = 0; i < budget; i++)
            {
                var coordinate = PickRandomClearSpace(map, rng);
                if (coordinate == null)
                    break;

                if (map.TryGetHex(coordinate.Value, out var hex))
                {
                    hex.Terrain = terrain;
                    placed.Add(coordinate.Value);
                }
            }

            return placed;
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
