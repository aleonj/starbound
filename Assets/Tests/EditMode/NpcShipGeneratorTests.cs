using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class NpcShipGeneratorTests
    {
        [Test]
        public void Generate_StatsFallWithinDefinedRanges()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Medium);
            var rng = new Random(1);

            for (var i = 0; i < 50; i++)
            {
                var ship = NpcShipGenerator.Generate(definition, rng);

                Assert.That(ship.GetStat(CoreStat.Hull), Is.InRange(definition.HullRange.Min, definition.HullRange.Max));
                Assert.That(ship.GetStat(CoreStat.Weapons), Is.InRange(definition.WeaponsRange.Min, definition.WeaponsRange.Max));
                Assert.That(ship.GetStat(CoreStat.Shields), Is.InRange(definition.ShieldsRange.Min, definition.ShieldsRange.Max));
                Assert.That(ship.GetStat(CoreStat.Speed), Is.InRange(definition.SpeedRange.Min, definition.SpeedRange.Max));
            }
        }

        [Test]
        public void Generate_IsDeterministicForSameRandomSequence()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);

            var shipA = NpcShipGenerator.Generate(definition, new Random(42));
            var shipB = NpcShipGenerator.Generate(definition, new Random(42));

            Assert.AreEqual(shipA.GetStat(CoreStat.Hull), shipB.GetStat(CoreStat.Hull));
            Assert.AreEqual(shipA.GetStat(CoreStat.Weapons), shipB.GetStat(CoreStat.Weapons));
            Assert.AreEqual(shipA.GetStat(CoreStat.Shields), shipB.GetStat(CoreStat.Shields));
            Assert.AreEqual(shipA.GetStat(CoreStat.Speed), shipB.GetStat(CoreStat.Speed));
        }
    }
}
