using StarBound.Core;

namespace StarBound.Shop
{
    public static class ShopService
    {
        // Purchasing only deducts money and adds the item to cargo — a
        // Permanent item's stat bonus is computed live by Ship.GetStat
        // from whatever's currently held, and a Consumable's effect only
        // applies when it's used, so no stat change happens here.
        public static PurchaseResult TryPurchase(Ship ship, ItemDefinition item)
        {
            if (!ship.CanHoldAnotherItem)
                return PurchaseResult.Failed(PurchaseFailureReason.CargoFull);

            if (ship.Money < item.Price)
                return PurchaseResult.Failed(PurchaseFailureReason.InsufficientFunds);

            ship.TrySpendMoney(item.Price);
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
