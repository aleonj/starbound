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

        [Test]
        public void Constructor_UnlockKind_AllowsNullStatFields()
        {
            var item = new ItemDefinition("Wormhole Device", null, null, 500, ItemKind.Unlock);

            Assert.IsNull(item.AffectedStat);
            Assert.IsNull(item.StatDelta);
        }

        [Test]
        public void Constructor_UnlockKind_ThrowsWhenGivenAStatEffect()
        {
            Assert.Throws<ArgumentException>(() =>
                new ItemDefinition("Wormhole Device", CoreStat.Weapons, 1, 500, ItemKind.Unlock));
        }

        [Test]
        public void Constructor_PermanentKind_ThrowsWhenStatFieldsAreNull()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ItemDefinition("Broken Item", null, null, 100, ItemKind.Permanent));
        }
    }
}
