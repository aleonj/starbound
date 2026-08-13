using System;
using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class ItemDefinitionTests
    {
        [Test]
        public void Constructor_ThrowsWhenStatDeltaIsZero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ItemDefinition("Useless Item", CoreStat.Weapons, 0, 100));
        }

        [Test]
        public void Constructor_ThrowsWhenPriceNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ItemDefinition("Shield Booster", CoreStat.Shields, 1, -5));
        }
    }
}
