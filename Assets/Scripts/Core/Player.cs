using System;

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
    }
}
