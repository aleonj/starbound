using System;
using System.Collections.Generic;

namespace StarBound.Core
{
    public class Player
    {
        public const int HardWinsToVictory = 3;

        public string Id { get; }
        public string DisplayName { get; }
        public Ship Ship { get; }
        public HexCoordinate Position { get; set; }

        public int EasyEngagementWins { get; private set; }
        public int MediumEngagementWins { get; private set; }
        public int HardEngagementWins { get; private set; }

        public bool HasWonMatch => HardEngagementWins >= HardWinsToVictory;

        // One job at a time — a separate slot from Ship's item cargo,
        // tracking whichever mining/transport/bounty contract the player
        // has accepted from a job board.
        public JobDefinition ActiveJob { get; private set; }

        // Mining jobs are two steps, not just a delivery: the ore has to
        // actually be mined at an Asteroids field before it can be
        // delivered. Tracked here rather than on JobDefinition itself,
        // since JobDefinition is an immutable spec and this is progress
        // toward completing it.
        public bool HasMinedCargo { get; private set; }

        // Per-player discovery: an engagement marker only shows on this
        // player's view of the map once they've personally landed on it.
        // The opponent must discover the same hex independently.
        private readonly HashSet<HexCoordinate> discoveredEngagementHexes = new();
        public IReadOnlyCollection<HexCoordinate> DiscoveredEngagementHexes => discoveredEngagementHexes;
        public void DiscoverHex(HexCoordinate coordinate) => discoveredEngagementHexes.Add(coordinate);

        public Player(string id, string displayName, Ship ship)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Player id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Player display name is required.", nameof(displayName));

            Id = id;
            DisplayName = displayName;
            Ship = ship ?? throw new ArgumentNullException(nameof(ship));
        }

        public void RecordEngagementWin(EngagementTier tier)
        {
            switch (tier)
            {
                case EngagementTier.Easy:
                    EasyEngagementWins++;
                    break;
                case EngagementTier.Medium:
                    MediumEngagementWins++;
                    break;
                case EngagementTier.Hard:
                    HardEngagementWins++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), "Cannot record a win for EngagementTier.None.");
            }
        }

        public void AcceptJob(JobDefinition job)
        {
            if (ActiveJob != null)
                throw new InvalidOperationException("Already have an active job.");

            ActiveJob = job ?? throw new ArgumentNullException(nameof(job));
            HasMinedCargo = false;
        }

        public void ClearActiveJob()
        {
            ActiveJob = null;
            HasMinedCargo = false;
        }

        public void MarkCargoMined()
        {
            if (ActiveJob is not { Type: JobType.Mining })
                throw new InvalidOperationException("No active Mining job to mine cargo for.");

            HasMinedCargo = true;
        }
    }
}
