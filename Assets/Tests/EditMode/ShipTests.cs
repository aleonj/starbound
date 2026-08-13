using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class ShipTests
    {
        [Test]
        public void NewShip_DefaultsAllCoreStatsToThree()
        {
            var ship = new Ship(cargoCapacity: 3);

            Assert.AreEqual(3, ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(3, ship.GetStat(CoreStat.Energy));
            Assert.AreEqual(3, ship.GetStat(CoreStat.Weapons));
            Assert.AreEqual(3, ship.GetStat(CoreStat.Shields));
            Assert.AreEqual(3, ship.GetStat(CoreStat.Speed));
        }

        [Test]
        public void ApplyStatDelta_ClampsAtZero()
        {
            var ship = new Ship(cargoCapacity: 3);

            ship.ApplyStatDelta(CoreStat.Hull, -10);

            Assert.AreEqual(0, ship.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ApplyStatDelta_AllowsIncrease()
        {
            var ship = new Ship(cargoCapacity: 3);

            ship.ApplyStatDelta(CoreStat.Weapons, 2);

            Assert.AreEqual(5, ship.GetStat(CoreStat.Weapons));
        }

        [Test]
        public void IsIntegrityDepleted_TrueWhenHullReachesZero()
        {
            var ship = new Ship(cargoCapacity: 3);

            ship.ApplyStatDelta(CoreStat.Hull, -3);

            Assert.IsTrue(ship.IsIntegrityDepleted);
        }

        [Test]
        public void IsIntegrityDepleted_TrueWhenEnergyReachesZero()
        {
            var ship = new Ship(cargoCapacity: 3);

            ship.ApplyStatDelta(CoreStat.Energy, -3);

            Assert.IsTrue(ship.IsIntegrityDepleted);
        }

        [Test]
        public void IsIntegrityDepleted_FalseWhileHullAndEnergyPositive()
        {
            var ship = new Ship(cargoCapacity: 3);

            Assert.IsFalse(ship.IsIntegrityDepleted);
        }

        [Test]
        public void TrySpendMoney_FailsWhenInsufficientFunds()
        {
            var ship = new Ship(cargoCapacity: 3, startingMoney: 10);

            var result = ship.TrySpendMoney(20);

            Assert.IsFalse(result);
            Assert.AreEqual(10, ship.Money);
        }

        [Test]
        public void TrySpendMoney_SucceedsAndDeductsWhenAffordable()
        {
            var ship = new Ship(cargoCapacity: 3, startingMoney: 10);

            var result = ship.TrySpendMoney(4);

            Assert.IsTrue(result);
            Assert.AreEqual(6, ship.Money);
        }

        [Test]
        public void TryAddItem_FailsWhenCargoFull()
        {
            var ship = new Ship(cargoCapacity: 1);
            var item1 = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 100);
            var item2 = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 100);

            Assert.IsTrue(ship.TryAddItem(item1));
            Assert.IsFalse(ship.TryAddItem(item2));
            Assert.AreEqual(1, ship.HeldItems.Count);
        }

        [Test]
        public void ClearMoneyAndItems_ResetsBothToEmpty()
        {
            var ship = new Ship(cargoCapacity: 2, startingMoney: 50);
            ship.TryAddItem(new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 100));

            ship.ClearMoneyAndItems();

            Assert.AreEqual(0, ship.Money);
            Assert.AreEqual(0, ship.HeldItems.Count);
        }
    }
}
