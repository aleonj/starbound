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
        public void Items_OnlyAffectPerformanceStats()
        {
            Assert.IsTrue(ItemPool.Items.All(i => PerformanceStats.Contains(i.AffectedStat)));
        }

        [Test]
        public void Items_PricesMatchPricingFormula()
        {
            Assert.IsTrue(ItemPool.Items.All(i => i.Price == ItemPricing.CalculatePrice(i.StatDelta)));
        }

        [Test]
        public void Items_HasAtLeastOneEntryPerPerformanceStat()
        {
            foreach (var stat in PerformanceStats)
                Assert.IsTrue(ItemPool.Items.Any(i => i.AffectedStat == stat));
        }
    }
}
