using System;

namespace StarBound.Core
{
    // A shop item that boosts one core stat — either a Permanent item
    // (passive bonus while held) or a Consumable (one-time use). Pricing
    // formula and shop-offer generation belong to the [Shop] stories.
    public class ItemDefinition
    {
        public string Name { get; }
        public CoreStat AffectedStat { get; }
        public int StatDelta { get; }
        public int Price { get; }
        public ItemKind Kind { get; }

        public ItemDefinition(string name, CoreStat affectedStat, int statDelta, int price, ItemKind kind = ItemKind.Permanent)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Item name is required.", nameof(name));
            if (statDelta == 0)
                throw new ArgumentOutOfRangeException(nameof(statDelta), "Item must have a non-zero stat effect.");
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));

            Name = name;
            AffectedStat = affectedStat;
            StatDelta = statDelta;
            Price = price;
            Kind = kind;
        }
    }
}
