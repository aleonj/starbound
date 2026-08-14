using System;
using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Shop
{
    public static class ShopOfferGenerator
    {
        public const int OfferSize = 3;

        // Fresh random subset each call — landing again generates a new offer.
        public static IReadOnlyList<ItemDefinition> GenerateOffer(Random rng)
        {
            var pool = new List<ItemDefinition>(ItemPool.Items);
            var offerSize = Math.Min(OfferSize, pool.Count);
            var offer = new List<ItemDefinition>(offerSize);

            for (var i = 0; i < offerSize; i++)
            {
                var index = rng.Next(pool.Count);
                offer.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return offer;
        }
    }
}
