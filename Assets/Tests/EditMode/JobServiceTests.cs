using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;

namespace StarBound.Tests
{
    public class JobServiceTests
    {
        private static Player CreatePlayer() => new("p1", "Adam", new Ship(cargoCapacity: 3, startingMoney: 100));

        [Test]
        public void TryAcceptJob_Succeeds_SetsActiveJob()
        {
            var player = CreatePlayer();
            var job = new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50);

            var result = JobService.TryAcceptJob(player, job);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(job, player.ActiveJob);
        }

        [Test]
        public void TryAcceptJob_FailsWhenAlreadyHasActiveJob()
        {
            var player = CreatePlayer();
            JobService.TryAcceptJob(player, new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));

            var result = JobService.TryAcceptJob(player, new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 50));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(AcceptJobFailureReason.AlreadyHasActiveJob, result.FailureReason);
        }

        [Test]
        public void ResolveBountyOutcome_NoActiveJob_DoesNothing()
        {
            var player = CreatePlayer();

            JobService.ResolveBountyOutcome(player, new HexCoordinate(0, -1), EngagementOutcome.PlayerWon);

            Assert.AreEqual(100, player.Ship.Money);
        }

        [Test]
        public void ResolveBountyOutcome_ActiveJobTargetsADifferentHex_DoesNothing()
        {
            var player = CreatePlayer();
            var job = new JobDefinition(JobType.BountyHunting, new HexCoordinate(0, -1), 100, EngagementTier.Easy);
            player.AcceptJob(job);

            JobService.ResolveBountyOutcome(player, new HexCoordinate(5, 5), EngagementOutcome.PlayerWon);

            Assert.AreEqual(100, player.Ship.Money);
            Assert.AreEqual(job, player.ActiveJob);
        }

        [Test]
        public void ResolveBountyOutcome_ActiveJobIsNotABounty_DoesNothing()
        {
            var player = CreatePlayer();
            var hex = new HexCoordinate(0, -1);
            var job = new JobDefinition(JobType.Mining, hex, 50);
            player.AcceptJob(job);

            JobService.ResolveBountyOutcome(player, hex, EngagementOutcome.PlayerWon);

            Assert.AreEqual(100, player.Ship.Money);
            Assert.AreEqual(job, player.ActiveJob);
        }

        [Test]
        public void ResolveBountyOutcome_PlayerWon_AwardsRewardAndClearsJob()
        {
            var player = CreatePlayer();
            var hex = new HexCoordinate(0, -1);
            player.AcceptJob(new JobDefinition(JobType.BountyHunting, hex, 200, EngagementTier.Hard));

            JobService.ResolveBountyOutcome(player, hex, EngagementOutcome.PlayerWon);

            Assert.AreEqual(300, player.Ship.Money); // 100 + 200
            Assert.IsNull(player.ActiveJob);
        }

        [Test]
        public void ResolveBountyOutcome_PlayerEscaped_AppliesTenPercentPenaltyAndClearsJob()
        {
            var player = CreatePlayer();
            var hex = new HexCoordinate(0, -1);
            player.AcceptJob(new JobDefinition(JobType.BountyHunting, hex, 200, EngagementTier.Hard));

            JobService.ResolveBountyOutcome(player, hex, EngagementOutcome.PlayerEscaped);

            Assert.AreEqual(80, player.Ship.Money); // 100 - (200 / 10)
            Assert.IsNull(player.ActiveJob);
        }

        [Test]
        public void ResolveBountyOutcome_PlayerLost_ClearsJobWithoutExtraPenalty()
        {
            var player = CreatePlayer();
            var hex = new HexCoordinate(0, -1);
            player.AcceptJob(new JobDefinition(JobType.BountyHunting, hex, 200, EngagementTier.Hard));

            JobService.ResolveBountyOutcome(player, hex, EngagementOutcome.PlayerLost);

            Assert.AreEqual(100, player.Ship.Money); // unchanged here — IntegrityPenaltyService handles the wipe elsewhere
            Assert.IsNull(player.ActiveJob);
        }
    }
}
