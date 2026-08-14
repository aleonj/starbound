using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Shop
{
    // The full pool of purchasable items. Only affects Performance stats
    // (Weapons/Shields/Speed) — Integrity stats (Hull/Energy) are handled
    // exclusively via RepairService, matching the spec's separate
    // "augment a stat via items" vs. "restore integrity" offerings.
    // Placeholder content/magnitudes, easy to extend.
    public static class ItemPool
    {
        public static readonly IReadOnlyList<ItemDefinition> Items = BuildPool();

        private static List<ItemDefinition> BuildPool()
        {
            var items = new List<ItemDefinition>();

            foreach (var stat in new[] { CoreStat.Weapons, CoreStat.Shields, CoreStat.Speed })
            {
                foreach (var delta in new[] { 1, 2 })
                {
                    var name = $"{DescribeStat(stat)} Upgrade +{delta}";
                    items.Add(new ItemDefinition(name, stat, delta, ItemPricing.CalculatePrice(delta)));
                }
            }

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
