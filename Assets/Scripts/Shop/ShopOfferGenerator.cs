using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Shop
{
    public static class ShopOfferGenerator
    {
        public const int OfferSize = 3;

        // Fresh random subset each call — landing again generates a new offer.
        public static IReadOnlyList<ItemDefinition> GenerateOffer(Random rng) =>
            Sample(ItemPool.Items, OfferSize, rng);

        // Picks `count` more items, distinct from each other and from
        // `exclude` — used by PlanetShopService to top up sold-out slots
        // on a persistent per-planet shelf without ever duplicating
        // whatever's still sitting in the offer.
        public static IReadOnlyList<ItemDefinition> GenerateReplacements(
            Random rng, int count, IReadOnlyCollection<ItemDefinition> exclude)
        {
            var pool = ItemPool.Items.Where(item => !exclude.Contains(item)).ToList();
            return Sample(pool, count, rng);
        }

        private static IReadOnlyList<ItemDefinition> Sample(IReadOnlyList<ItemDefinition> source, int count, Random rng)
        {
            var pool = new List<ItemDefinition>(source);
            var sampleSize = Math.Min(count, pool.Count);
            var sample = new List<ItemDefinition>(sampleSize);

            for (var i = 0; i < sampleSize; i++)
            {
                var index = rng.Next(pool.Count);
                sample.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return sample;
        }
    }
}
