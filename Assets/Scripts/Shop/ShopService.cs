using StarBound.Core;

namespace StarBound.Shop
{
    public static class ShopService
    {
        // Purchasing only deducts money and adds the item to cargo — a
        // Permanent item's stat bonus is computed live by Ship.GetStat
        // from whatever's currently held, and a Consumable's effect only
        // applies when it's used, so no stat change happens here. price
        // defaults to the item's own listed price — callers pass an
        // event-adjusted value (see Match.BuyItem/MatchVariable.MarketCrash)
        // only when one is actually active, so the charged amount is
        // what actually shows in the purchase confirmation rather than
        // silently differing from item.Price.
        public static PurchaseResult TryPurchase(Ship ship, ItemDefinition item, int? price = null)
        {
            var effectivePrice = price ?? item.Price;

            if (!ship.CanHoldAnotherItem)
                return PurchaseResult.Failed(PurchaseFailureReason.CargoFull);

            if (ship.Money < effectivePrice)
                return PurchaseResult.Failed(PurchaseFailureReason.InsufficientFunds);

            ship.TrySpendMoney(effectivePrice);
            ship.TryAddItem(item);

            return PurchaseResult.Succeeded();
        }

        // Refunds half the item's price — works for any currently-held
        // item, used or unused doesn't apply since a used Consumable is
        // already removed from HeldItems by the time it could be sold.
        public static SellResult TrySell(Ship ship, ItemDefinition item)
        {
            if (!ship.TryRemoveItem(item))
                return SellResult.Failed(SellFailureReason.ItemNotHeld);

            var refund = item.Price / 2;
            ship.AddMoney(refund);

            return SellResult.Succeeded(refund);
        }
    }
}
