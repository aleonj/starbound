using System;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Economy;

namespace StarBound.Tests
{
    public class JobPricingTests
    {
        [TestCase(EngagementTier.Easy, 50)]
        [TestCase(EngagementTier.Medium, 100)]
        [TestCase(EngagementTier.Hard, 200)]
        public void BountyReward_ScalesWithTier(EngagementTier tier, int expectedReward)
        {
            Assert.AreEqual(expectedReward, JobPricing.BountyReward(tier));
        }

        [Test]
        public void BountyReward_ThrowsForNoneTier()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => JobPricing.BountyReward(EngagementTier.None));
        }
    }
}
