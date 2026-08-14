using StarBound.Core;
using StarBound.Map;

namespace StarBound.Multiplayer
{
    // Placeholder starting-position rule: both players begin at the map
    // center, which is force-cleared to a safe ClearSpace/no-engagement
    // hex for exactly that reason. No stated rule on symmetric starts vs.
    // a shared one — easy to change here if a different design is wanted.
    public static class MatchFactory
    {
        private static readonly HexCoordinate StartingPosition = new(0, 0);

        public static Match CreateMatch(MapSize mapSize, Difficulty difficulty, int seed, Player playerOne, Player playerTwo)
        {
            var map = MapGenerator.Generate(mapSize.ToRadius(), difficulty, seed);
            EngagementPlacer.PlaceEngagements(map, difficulty, seed);

            if (map.TryGetHex(StartingPosition, out var startHex))
            {
                startHex.Terrain = TerrainType.ClearSpace;
                startHex.Engagement = EngagementTier.None;
            }

            playerOne.Position = StartingPosition;
            playerTwo.Position = StartingPosition;

            return new Match(map, playerOne, playerTwo);
        }
    }
}
