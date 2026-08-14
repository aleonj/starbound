using NUnit.Framework;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class ItemPricingTests
    {
        [TestCase(1, 50)]
        [TestCase(2, 100)]
        [TestCase(-1, 50)]
        public void CalculatePrice_ScalesWithMagnitude(int delta, int expectedPrice)
        {
            Assert.AreEqual(expectedPrice, ItemPricing.CalculatePrice(delta));
        }

        [Test]
        public void CalculatePrice_HigherMagnitudeCostsMore()
        {
            Assert.Greater(ItemPricing.CalculatePrice(2), ItemPricing.CalculatePrice(1));
        }
    }
}
