using System;

namespace StarBound.Core
{
    // A shop item that either boosts one core stat — a Permanent item
    // (passive bonus while held) or a Consumable (one-time use) — or, for
    // Kind == Unlock, gates a capability with no stat effect at all (e.g.
    // the Wormhole Device). Pricing formula and shop-offer generation
    // belong to the [Shop] stories.
    public class ItemDefinition
    {
        public string Name { get; }
        public CoreStat? AffectedStat { get; }
        public int? StatDelta { get; }
        public int Price { get; }
        public ItemKind Kind { get; }

        public ItemDefinition(string name, CoreStat? affectedStat, int? statDelta, int price, ItemKind kind = ItemKind.Permanent)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Item name is required.", nameof(name));
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));

            if (kind == ItemKind.Unlock)
            {
                if (affectedStat != null || statDelta != null)
                    throw new ArgumentException("An Unlock item has no stat effect — pass null for both.", nameof(affectedStat));
            }
            else if (affectedStat == null || statDelta == null || statDelta == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(statDelta), "Item must have a non-zero stat effect.");
            }

            Name = name;
            AffectedStat = affectedStat;
            StatDelta = statDelta;
            Price = price;
            Kind = kind;
        }
    }
}
