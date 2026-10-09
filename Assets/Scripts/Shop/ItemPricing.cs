using System;

namespace StarBound.Shop
{
    // Price scales with the magnitude of the stat boost. Placeholder
    // balance, tunable here.
    public static class ItemPricing
    {
        public const int BasePricePerPoint = 50;
        // MarketCrash event — applied at purchase time (see
        // Match.BuyItem), not to the generated offer's own listed price,
        // so the discount is visible in the purchase confirmation rather
        // than silently baked into ShopOfferGenerator's output.
        public const double MarketCrashDiscountMultiplier = 0.5;

        public static int CalculatePrice(int statDelta) => Math.Abs(statDelta) * BasePricePerPoint;
    }
}
