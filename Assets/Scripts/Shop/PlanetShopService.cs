using System;
using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Shop
{
    // Owns a planet hex's persistent shop shelf — one shared offer per
    // planet, not a fresh roll per visit. A purchased slot only refills
    // once at least one EndTurn has passed since it was bought (see
    // Match.TurnNumber), regardless of who's visiting.
    public static class PlanetShopService
    {
        public static IReadOnlyList<ItemDefinition> GetOffer(Hex hex, int currentTurn, Random rng)
        {
            if (hex.ShopOffer == null)
            {
                hex.ShopOffer = new List<ItemDefinition>(ShopOfferGenerator.GenerateOffer(rng));
                return hex.ShopOffer;
            }

            var readyToRefill = hex.ShopOfferLastPurchaseTurn.HasValue &&
                hex.ShopOfferLastPurchaseTurn.Value != currentTurn;

            if (hex.ShopOffer.Count < ShopOfferGenerator.OfferSize && readyToRefill)
            {
                var missing = ShopOfferGenerator.OfferSize - hex.ShopOffer.Count;
                hex.ShopOffer.AddRange(ShopOfferGenerator.GenerateReplacements(rng, missing, hex.ShopOffer));
                hex.ShopOfferLastPurchaseTurn = null;
            }

            return hex.ShopOffer;
        }

        public static void RecordPurchase(Hex hex, ItemDefinition item, int currentTurn)
        {
            hex.ShopOffer?.Remove(item);
            hex.ShopOfferLastPurchaseTurn = currentTurn;
        }
    }
}
