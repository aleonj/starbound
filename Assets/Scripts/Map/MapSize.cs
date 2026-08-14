using System;

namespace StarBound.Map
{
    public enum MapSize
    {
        Small,
        Medium,
        Large
    }

    public static class MapSizeExtensions
    {
        public static int ToRadius(this MapSize size) => size switch
        {
            MapSize.Small => 4,
            MapSize.Medium => 6,
            MapSize.Large => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };
    }
}
