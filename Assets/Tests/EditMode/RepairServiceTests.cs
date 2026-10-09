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
        public void TryRepairOnePoint_WithCostOverride_ChargesTheOverrideNotCostPerPoint()
        {
            // MatchVariable.RepairDiscount — Match.RepairStat computes a
            // discounted cost and passes it in.
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100);
            ship.ApplyStatDelta(CoreStat.Hull, -2);

            var result = RepairService.TryRepairOnePoint(ship, CoreStat.Hull, costPerPoint: 5);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(95, ship.Money);
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
        public void TryRepairOnePoint_Energy_RepairsUpToMaxEnergyValueNotDefaultStatValue()
        {
            // Energy's own ceiling is Ship.MaxEnergyValue (5), not the
            // shared DefaultStatValue (3) Hull uses — see [Combat]
            // Energy overhaul.
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100);
            ship.ApplyStatDelta(CoreStat.Energy, -2); // Energy = 3 — at Hull's max, but below Energy's own

            var result = RepairService.TryRepairOnePoint(ship, CoreStat.Energy);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(4, ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void TryRepairOnePoint_Energy_AlreadyAtMaxEnergyValue_Fails()
        {
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100); // Energy starts at MaxEnergyValue already

            var result = RepairService.TryRepairOnePoint(ship, CoreStat.Energy);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(RepairFailureReason.AlreadyAtMax, result.FailureReason);
        }

        [Test]
        public void TryRepairOnePoint_NonIntegrityStat_Throws()
        {
            var ship = new Ship(cargoCapacity: 0, startingMoney: 100);

            Assert.Throws<ArgumentException>(() => RepairService.TryRepairOnePoint(ship, CoreStat.Weapons));
        }
    }
}
