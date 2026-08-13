using System;
using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class EngagementTests
    {
        [Test]
        public void Constructor_ThrowsWhenTierIsNone()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new Engagement(EngagementTier.None, new Ship(cargoCapacity: 3)));
        }

        [Test]
        public void Constructor_StoresTierAndOpponent()
        {
            var opponent = new Ship(cargoCapacity: 0);

            var engagement = new Engagement(EngagementTier.Hard, opponent);

            Assert.AreEqual(EngagementTier.Hard, engagement.Tier);
            Assert.AreSame(opponent, engagement.Opponent);
        }
    }
}
