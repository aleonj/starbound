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

        public GameMap(int radius, Difficulty difficulty)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));

            Radius = radius;
            Difficulty = difficulty;
        }

        public void SetHex(Hex hex)
        {
            hexes[hex.Coordinate] = hex;
        }

        public bool TryGetHex(HexCoordinate coordinate, out Hex hex) => hexes.TryGetValue(coordinate, out hex);
    }
}
