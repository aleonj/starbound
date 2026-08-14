using NUnit.Framework;
using StarBound.Core;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class ShopServiceTests
    {
        [Test]
        public void TryPurchase_Succeeds_DeductsMoneyAppliesBoostAddsItem()
        {
            var ship = new Ship(cargoCapacity: 3, startingMoney: 200);
            var item = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);

            var result = ShopService.TryPurchase(ship, item);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(150, ship.Money);
            Assert.AreEqual(4, ship.GetStat(CoreStat.Weapons));
            Assert.AreEqual(1, ship.HeldItems.Count);
        }

        [Test]
        public void TryPurchase_InsufficientFunds_Fails()
        {
            var ship = new Ship(cargoCapacity: 3, startingMoney: 10);
            var item = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);

            var result = ShopService.TryPurchase(ship, item);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.InsufficientFunds, result.FailureReason);
            Assert.AreEqual(10, ship.Money);
            Assert.AreEqual(3, ship.GetStat(CoreStat.Weapons));
        }

        [Test]
        public void TryPurchase_CargoFull_Fails()
        {
            var ship = new Ship(cargoCapacity: 1, startingMoney: 500);
            var item1 = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);
            var item2 = new ItemDefinition("Shield Upgrade +1", CoreStat.Shields, 1, 50);
            ShopService.TryPurchase(ship, item1);

            var result = ShopService.TryPurchase(ship, item2);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailureReason.CargoFull, result.FailureReason);
            Assert.AreEqual(3, ship.GetStat(CoreStat.Shields));
        }
    }
}
