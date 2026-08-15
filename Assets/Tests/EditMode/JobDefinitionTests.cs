using System;
using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class JobDefinitionTests
    {
        [Test]
        public void Constructor_ThrowsWhenRewardIsNotPositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 0));
        }

        [Test]
        public void Constructor_ThrowsWhenBountyHasNoTier()
        {
            Assert.Throws<ArgumentException>(() =>
                new JobDefinition(JobType.BountyHunting, new HexCoordinate(1, 0), 100));
        }

        [Test]
        public void Constructor_AllowsBountyWithARealTier()
        {
            var job = new JobDefinition(JobType.BountyHunting, new HexCoordinate(1, 0), 100, EngagementTier.Hard);

            Assert.AreEqual(EngagementTier.Hard, job.BountyTier);
        }

        [Test]
        public void Constructor_NonBountyJobsDefaultToNoTier()
        {
            var job = new JobDefinition(JobType.Transport, new HexCoordinate(1, 0), 50);

            Assert.AreEqual(EngagementTier.None, job.BountyTier);
        }
    }
}
