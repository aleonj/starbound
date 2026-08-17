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
        private const int MinWormholeDistance = 8;
        private const int MinTradelaneRouteLength = 3;

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
            // minDistance: 2 keeps planets from ever landing as neighbors
            // of each other (distance 1) — same hard-rule pattern as
            // PlaceWormholePairs below, not a best-effort fallback.
            var planets = PlaceSingles(map, rng, TerrainType.PlanetOrStarport, Budget(totalHexes, distribution.PlanetOrStarport), minDistance: 2);
            PlaceWormholePairs(map, rng, Budget(totalHexes, distribution.Wormhole));

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

                // A 1-2 hex "route" doesn't read as a lane — skip painting
                // it (the planets stay graph-connected for the walk below
                // regardless, same as when a route fails to reach its
                // target at all).
                if (path.Count >= MinTradelaneRouteLength)
                {
                    foreach (var coordinate in path)
                    {
                        if (map.TryGetHex(coordinate, out var hex) && hex.Terrain == TerrainType.ClearSpace)
                            hex.Terrain = TerrainType.Tradelane;
                    }
                    remaining -= path.Count;
                }

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

        // minDistance <= 1 (the default) means "no constraint" — any
        // ClearSpace hex is fair game, same as before this was
        // parameterized. minDistance >= 2 enforces that every placed hex
        // stays that far from every OTHER hex placed in this same call,
        // as a hard rule: if no valid spot exists, placement just stops
        // rather than violating it (see PlaceWormholePairs for the same
        // reasoning applied to wormhole pairs).
        private static List<HexCoordinate> PlaceSingles(GameMap map, Random rng, TerrainType terrain, int budget, int minDistance = 1)
        {
            var placed = new List<HexCoordinate>();

            for (var i = 0; i < budget; i++)
            {
                var coordinate = placed.Count == 0 || minDistance <= 1
                    ? PickRandomClearSpace(map, rng)
                    : PickFarClearSpace(map, rng, placed, minDistance);
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

        // Wormholes only function in gameplay as a pair (see
        // Match.OtherWormholeDestinations) — a lone wormhole has nowhere
        // to travel to — so they're placed two at a time. Every wormhole
        // hex placed (not just the two within one pair) must stay at
        // least MinWormholeDistance from every OTHER wormhole hex already
        // on the map — a second pair placed near a first pair is exactly
        // as wrong as the two hexes within one pair being close. The
        // minimum distance is a hard rule, not best-effort: if no valid
        // spot exists (map too small, or already too crowded with earlier
        // pairs), placement just stops rather than placing a wormhole
        // that violates it — fewer wormholes than the nominal budget
        // (possibly zero) is the correct outcome, not a violation. Budget
        // is a hex count, so an odd budget just leaves the last hex
        // unspent rather than placing an unpaired wormhole.
        private static void PlaceWormholePairs(GameMap map, Random rng, int budget)
        {
            var pairCount = budget / 2;
            var placedWormholes = new List<HexCoordinate>();

            for (var i = 0; i < pairCount; i++)
            {
                var first = placedWormholes.Count == 0
                    ? PickRandomClearSpace(map, rng)
                    : PickFarClearSpace(map, rng, placedWormholes, MinWormholeDistance);
                if (first == null)
                    break;

                if (map.TryGetHex(first.Value, out var firstHex))
                    firstHex.Terrain = TerrainType.Wormhole;
                placedWormholes.Add(first.Value);

                var second = PickFarClearSpace(map, rng, placedWormholes, MinWormholeDistance);
                if (second == null)
                {
                    // No valid partner for this hex either — undo placing
                    // it rather than leave an unpaired wormhole sitting
                    // on the map.
                    if (map.TryGetHex(first.Value, out var revertHex))
                        revertHex.Terrain = TerrainType.ClearSpace;
                    placedWormholes.RemoveAt(placedWormholes.Count - 1);
                    break;
                }

                if (map.TryGetHex(second.Value, out var secondHex))
                    secondHex.Terrain = TerrainType.Wormhole;
                placedWormholes.Add(second.Value);
            }
        }

        // A random ClearSpace hex at least minDistance from EVERY hex in
        // origins — null if nothing on the map satisfies that. No
        // violating fallback: the minimum distance is a hard rule (see
        // PlaceWormholePairs).
        private static HexCoordinate? PickFarClearSpace(GameMap map, Random rng, IReadOnlyList<HexCoordinate> origins, int minDistance)
        {
            var farEnough = map.Hexes
                .Where(h => h.Terrain == TerrainType.ClearSpace && origins.All(o => HexMath.Distance(h.Coordinate, o) >= minDistance))
                .ToList();

            return farEnough.Count > 0 ? farEnough[rng.Next(farEnough.Count)].Coordinate : null;
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
