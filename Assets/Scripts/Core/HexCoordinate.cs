using System;

namespace StarBound.Core
{
    // Axial coordinates. Neighbor/distance math belongs to the later
    // [Map] Hex grid data structure story, not here.
    public readonly struct HexCoordinate : IEquatable<HexCoordinate>
    {
        public int Q { get; }
        public int R { get; }

        public HexCoordinate(int q, int r)
        {
            Q = q;
            R = r;
        }

        public bool Equals(HexCoordinate other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Q, R);
        public static bool operator ==(HexCoordinate a, HexCoordinate b) => a.Equals(b);
        public static bool operator !=(HexCoordinate a, HexCoordinate b) => !a.Equals(b);
        public override string ToString() => $"({Q}, {R})";
    }
}
