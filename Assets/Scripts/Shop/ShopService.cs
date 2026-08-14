using StarBound.Core;

namespace StarBound.Shop
{
    public static class ShopService
    {
        public static PurchaseResult TryPurchase(Ship ship, ItemDefinition item)
        {
            if (!ship.CanHoldAnotherItem)
                return PurchaseResult.Failed(PurchaseFailureReason.CargoFull);

            if (ship.Money < item.Price)
                return PurchaseResult.Failed(PurchaseFailureReason.InsufficientFunds);

            ship.TrySpendMoney(item.Price);
            ship.TryAddItem(item);
            ship.ApplyStatDelta(item.AffectedStat, item.StatDelta);

            return PurchaseResult.Succeeded();
        }
    }
}
