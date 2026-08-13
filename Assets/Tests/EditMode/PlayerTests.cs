using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class PlayerTests
    {
        [Test]
        public void HasWonMatch_TrueAfterThreeHardWins()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));

            player.RecordEngagementWin(EngagementTier.Hard);
            player.RecordEngagementWin(EngagementTier.Hard);
            Assert.IsFalse(player.HasWonMatch);

            player.RecordEngagementWin(EngagementTier.Hard);
            Assert.IsTrue(player.HasWonMatch);
        }

        [Test]
        public void RecordEngagementWin_EasyAndMediumDoNotCountTowardVictory()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));

            player.RecordEngagementWin(EngagementTier.Easy);
            player.RecordEngagementWin(EngagementTier.Medium);

            Assert.IsFalse(player.HasWonMatch);
            Assert.AreEqual(1, player.EasyEngagementWins);
            Assert.AreEqual(1, player.MediumEngagementWins);
        }
    }
}
