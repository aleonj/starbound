using System;
using System.Linq;
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

        [Test]
        public void AcceptJob_SetsActiveJob()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            var job = new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50);

            player.AcceptJob(job);

            Assert.AreEqual(job, player.ActiveJob);
        }

        [Test]
        public void AcceptJob_ThrowsWhenAlreadyHasAnActiveJob()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            player.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));

            Assert.Throws<InvalidOperationException>(() =>
                player.AcceptJob(new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 50)));
        }

        [Test]
        public void ClearActiveJob_AllowsAcceptingANewJob()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            player.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));

            player.ClearActiveJob();

            Assert.IsNull(player.ActiveJob);
            Assert.DoesNotThrow(() =>
                player.AcceptJob(new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 50)));
        }

        [Test]
        public void MarkCargoMined_SetsHasMinedCargo()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            player.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));

            player.MarkCargoMined();

            Assert.IsTrue(player.HasMinedCargo);
        }

        [Test]
        public void MarkCargoMined_ThrowsWithoutAnActiveMiningJob()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));

            Assert.Throws<InvalidOperationException>(() => player.MarkCargoMined());
        }

        [Test]
        public void MarkCargoMined_ThrowsForANonMiningJob()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            player.AcceptJob(new JobDefinition(JobType.Transport, new HexCoordinate(1, 0), 50));

            Assert.Throws<InvalidOperationException>(() => player.MarkCargoMined());
        }

        [Test]
        public void AcceptJob_ResetsHasMinedCargoForTheNewJob()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            player.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));
            player.MarkCargoMined();
            player.ClearActiveJob();

            player.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(2, 0), 50));

            Assert.IsFalse(player.HasMinedCargo);
        }

        [Test]
        public void DiscoverHex_AddsToDiscoveredEngagementHexes()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            var coordinate = new HexCoordinate(2, -1);

            player.DiscoverHex(coordinate);

            Assert.Contains(coordinate, player.DiscoveredEngagementHexes.ToList());
        }

        [Test]
        public void DiscoverHex_SameHexTwice_DoesNotDuplicate()
        {
            var player = new Player("p1", "Adam", new Ship(cargoCapacity: 3));
            var coordinate = new HexCoordinate(2, -1);

            player.DiscoverHex(coordinate);
            player.DiscoverHex(coordinate);

            Assert.AreEqual(1, player.DiscoveredEngagementHexes.Count);
        }
    }
}
