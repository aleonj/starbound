using System;
using System.Collections.Generic;

namespace StarBound.Core
{
    // Holds generated hexes for a match. Generation/clustering algorithms
    // belong to the later [Map] stories, not here.
    public class GameMap
    {
        private readonly Dictionary<HexCoordinate, Hex> hexes = new();

        public Difficulty Difficulty { get; }
        public int Radius { get; }
        public IReadOnlyCollection<Hex> Hexes => hexes.Values;

        // The generation seed this map was built with (see
        // MapGenerator.Generate) — exposed so per-hex derived content can
        // stay deterministic for the lifetime of one match (same map,
        // same seed) while still varying across different matches, same
        // as generation itself. Optional/defaulted so every existing
        // direct-construction call site (tests mainly, which build hexes
        // by hand rather than via MapGenerator) keeps compiling unchanged
        // — those just don't care what Seed resolves to.
        public int Seed { get; }

        public GameMap(int radius, Difficulty difficulty, int seed = 0)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));

            Radius = radius;
            Difficulty = difficulty;
            Seed = seed;
        }

        public void SetHex(Hex hex)
        {
            hexes[hex.Coordinate] = hex;
        }

        public bool TryGetHex(HexCoordinate coordinate, out Hex hex) => hexes.TryGetValue(coordinate, out hex);
    }
}
