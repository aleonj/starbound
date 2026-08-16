using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class ItemPoolTests
    {
        private static readonly CoreStat[] PerformanceStats = { CoreStat.Weapons, CoreStat.Shields, CoreStat.Speed };

        [Test]
        public void PermanentItems_OnlyAffectPerformanceStats()
        {
            Assert.IsTrue(ItemPool.Items.Where(i => i.Kind == ItemKind.Permanent).All(i => PerformanceStats.Contains(i.AffectedStat!.Value)));
        }

        [Test]
        public void ConsumableItems_OnlyAffectIntegrityStats()
        {
            var integrityStats = new[] { CoreStat.Hull, CoreStat.Energy };
            Assert.IsTrue(ItemPool.Items.Where(i => i.Kind == ItemKind.Consumable).All(i => integrityStats.Contains(i.AffectedStat!.Value)));
        }

        [Test]
        public void Items_PricesMatchPricingFormula()
        {
            Assert.IsTrue(ItemPool.Items.All(i => i.Price == ItemPricing.CalculatePrice(i.StatDelta!.Value)));
        }

        [Test]
        public void Items_HasAtLeastOneEntryPerPerformanceStat()
        {
            foreach (var stat in PerformanceStats)
                Assert.IsTrue(ItemPool.Items.Any(i => i.AffectedStat == stat && i.Kind == ItemKind.Permanent));
        }

        [Test]
        public void Items_HasAtLeastOneConsumable()
        {
            Assert.IsTrue(ItemPool.Items.Any(i => i.Kind == ItemKind.Consumable));
        }

        [Test]
        public void WormholeDevice_IsAnUnlockItemWithNoStatEffect()
        {
            Assert.AreEqual(ItemKind.Unlock, ItemPool.WormholeDevice.Kind);
            Assert.IsNull(ItemPool.WormholeDevice.AffectedStat);
            Assert.IsNull(ItemPool.WormholeDevice.StatDelta);
        }

        [Test]
        public void WormholeDevice_IsNotPartOfTheRandomizedOffer()
        {
            // A mechanic-unlocking purchase shouldn't be gated by luck —
            // it's always shown separately in the shop instead.
            Assert.IsFalse(ItemPool.Items.Contains(ItemPool.WormholeDevice));
        }
    }
}
