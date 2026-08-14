using System;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class RepairServiceTests
    {
        [Test]
        public void TryRepairOnePoint_RestoresOnePointAndDeductsMoney()
        {
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100);
            ship.ApplyStatDelta(CoreStat.Hull, -2);

            var result = RepairService.TryRepairOnePoint(ship, CoreStat.Hull);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(100 - RepairService.CostPerPoint, ship.Money);
        }

        [Test]
        public void TryRepairOnePoint_AlreadyAtMax_Fails()
        {
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100);

            var result = RepairService.TryRepairOnePoint(ship, CoreStat.Hull);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RepairFailureReason.AlreadyAtMax, result.FailureReason);
            Assert.AreEqual(100, ship.Money);
        }

        [Test]
        public void TryRepairOnePoint_InsufficientFunds_Fails()
        {
            var ship = new Ship(cargoCapacity: 0, startingMoney: 0);
            ship.ApplyStatDelta(CoreStat.Energy, -1);

            var result = RepairService.TryRepairOnePoint(ship, CoreStat.Energy);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RepairFailureReason.InsufficientFunds, result.FailureReason);
        }

        [Test]
        public void TryRepairOnePoint_NonIntegrityStat_Throws()
        {
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100);

            Assert.Throws<ArgumentException>(() => RepairService.TryRepairOnePoint(ship, CoreStat.Weapons));
        }
    }
}
