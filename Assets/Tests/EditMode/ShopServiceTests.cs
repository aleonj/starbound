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
        public void TryPurchase_WithPriceOverride_ChargesTheOverrideNotItemPrice()
        {
            // MatchVariable.MarketCrash — Match.BuyItem computes a
            // discounted price and passes it in rather than this
            // service reading item.Price directly, so the amount
            // actually charged matches what the purchase confirmation
            // shows.
            var ship = new Ship(cargoCapacity: 3, startingMoney: 200);
            var item = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);

            var result = ShopService.TryPurchase(ship, item, price: 25);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(175, ship.Money);
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

        [Test]
        public void TryPurchase_DoesNotApplyTheBonusUntilTheItemIsActuallyHeld()
        {
            // Regression guard for the old "bake the delta in at purchase
            // time" behavior — the bonus must come from GetStat reading
            // HeldItems live, not from ApplyStatDelta at purchase.
            var ship = new Ship(cargoCapacity: 3, startingMoney: 200);
            var item = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);

            ShopService.TryPurchase(ship, item);
            ship.TryRemoveItem(item);

            Assert.AreEqual(3, ship.GetStat(CoreStat.Weapons));
        }

        [Test]
        public void TrySell_Succeeds_RefundsHalfPriceAndRemovesItem()
        {
            var ship = new Ship(cargoCapacity: 3, startingMoney: 150);
            var item = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);
            ShopService.TryPurchase(ship, item);

            var result = ShopService.TrySell(ship, item);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(25, result.Refund);
            Assert.AreEqual(125, ship.Money); // 150 - 50 + 25
            Assert.AreEqual(0, ship.HeldItems.Count);
            Assert.AreEqual(3, ship.GetStat(CoreStat.Weapons)); // bonus gone
        }

        [Test]
        public void TrySell_FailsWhenItemNotHeld()
        {
            var ship = new Ship(cargoCapacity: 3, startingMoney: 150);
            var item = new ItemDefinition("Weapons Upgrade +1", CoreStat.Weapons, 1, 50);

            var result = ShopService.TrySell(ship, item);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellFailureReason.ItemNotHeld, result.FailureReason);
            Assert.AreEqual(150, ship.Money);
        }
    }
}
