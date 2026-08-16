using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Shop
{
    // The full pool of purchasable items. Permanent upgrades only affect
    // Performance stats (Weapons/Shields/Speed) — restoring Integrity
    // stats (Hull/Energy) permanently is RepairService's job, not an
    // item's. Consumables are the exception: a one-time repair kit is a
    // Consumable specifically because its effect is a single use, not a
    // standing item-driven ceiling on Hull. Placeholder content/magnitudes,
    // easy to extend.
    public static class ItemPool
    {
        public static readonly IReadOnlyList<ItemDefinition> Items = BuildPool();

        // Deliberately not part of Items/ShopOfferGenerator's random
        // sampling — a mechanic-unlocking purchase shouldn't be gated by
        // luck. Always shown as its own standing offer in the shop. Price
        // is a placeholder, ~5x the priciest stat item, matching "not
        // cheap" per the story.
        public static readonly ItemDefinition WormholeDevice =
            new("Wormhole Device", null, null, 500, ItemKind.Unlock);

        private static List<ItemDefinition> BuildPool()
        {
            var items = new List<ItemDefinition>();

            foreach (var stat in new[] { CoreStat.Weapons, CoreStat.Shields, CoreStat.Speed })
            {
                foreach (var delta in new[] { 1, 2 })
                {
                    var name = $"{DescribeStat(stat)} Upgrade +{delta}";
                    items.Add(new ItemDefinition(name, stat, delta, ItemPricing.CalculatePrice(delta), ItemKind.Permanent));
                }
            }

            items.Add(new ItemDefinition("Repair Kit", CoreStat.Hull, 2, ItemPricing.CalculatePrice(2), ItemKind.Consumable));
            items.Add(new ItemDefinition("Energy Cell", CoreStat.Energy, 2, ItemPricing.CalculatePrice(2), ItemKind.Consumable));

            return items;
        }

        private static string DescribeStat(CoreStat stat) => stat switch
        {
            CoreStat.Weapons => "Weapons",
            CoreStat.Shields => "Shield",
            CoreStat.Speed => "Thruster",
            _ => stat.ToString()
        };
    }
}
