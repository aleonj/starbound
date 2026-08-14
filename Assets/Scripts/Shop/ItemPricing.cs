using System;

namespace StarBound.Shop
{
    // Price scales with the magnitude of the stat boost. Placeholder
    // balance, tunable here.
    public static class ItemPricing
    {
        public const int BasePricePerPoint = 50;

        public static int CalculatePrice(int statDelta) => Math.Abs(statDelta) * BasePricePerPoint;
    }
}
