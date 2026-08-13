namespace StarBound.Core
{
    public class Hex
    {
        public HexCoordinate Coordinate { get; }
        public TerrainType Terrain { get; set; }
        public EngagementTier Engagement { get; set; } = EngagementTier.None;

        public bool HasEngagement => Engagement != EngagementTier.None;

        public Hex(HexCoordinate coordinate, TerrainType terrain)
        {
            Coordinate = coordinate;
            Terrain = terrain;
        }
    }
}
