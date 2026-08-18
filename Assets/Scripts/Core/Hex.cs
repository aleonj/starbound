using System.Collections.Generic;

namespace StarBound.Core
{
    public class Hex
    {
        public HexCoordinate Coordinate { get; }
        public TerrainType Terrain { get; set; }
        public EngagementTier Engagement { get; set; } = EngagementTier.None;

        public bool HasEngagement => Engagement != EngagementTier.None;

        // Assigned by MapGenerator (see PlanetNames) — null for every
        // non-planet hex. Not just cosmetic: job/shop UI text falls back
        // to printing the raw coordinate when this is unset.
        public string Name { get; set; }

        // A planet's persistent shop shelf (see PlanetShopService) — null
        // means it's never been generated yet (first visit), as opposed
        // to an empty list (every slot currently sold out, awaiting a
        // turn boundary to refill). Only meaningful for
        // TerrainType.PlanetOrStarport hexes.
        public List<ItemDefinition> ShopOffer { get; set; }

        // The Match.TurnNumber a purchase was last made from this shelf —
        // a refill only happens once the current turn number differs from
        // this. Null when there's nothing pending a refill.
        public int? ShopOfferLastPurchaseTurn { get; set; }

        public Hex(HexCoordinate coordinate, TerrainType terrain)
        {
            Coordinate = coordinate;
            Terrain = terrain;
        }
    }
}
