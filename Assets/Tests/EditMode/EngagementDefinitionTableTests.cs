using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class EngagementDefinitionTableTests
    {
        [Test]
        public void HullRanges_IncreaseWithTier()
        {
            var easy = EngagementDefinitionTable.For(EngagementTier.Easy);
            var medium = EngagementDefinitionTable.For(EngagementTier.Medium);
            var hard = EngagementDefinitionTable.For(EngagementTier.Hard);

            Assert.Less(easy.HullRange.Max, medium.HullRange.Max);
            Assert.Less(medium.HullRange.Max, hard.HullRange.Max);
        }

        [Test]
        public void DefaultRoundOrder_IsWeaponsShieldsSpeed()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Medium);

            CollectionAssert.AreEqual(
                new[] { CoreStat.Weapons, CoreStat.Shields, CoreStat.Speed },
                definition.RoundOrder);
        }

        [Test]
        public void EscapeAllowed_DefaultsTrue()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);

            Assert.IsTrue(definition.EscapeAllowed);
        }
    }
}
