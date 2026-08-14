using System;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Combat
{
    // Landing-on-a-marked-hex integration glue. Starting an engagement and
    // clearing its marker are separate calls: the marker should only be
    // resolved once the encounter actually ends, which a caller driving
    // an EngagementSession round by round can only know after the fact.
    public static class EngagementTrigger
    {
        public static EngagementSession TryTrigger(Player player, GameMap map, Random rng)
        {
            if (!map.TryGetHex(player.Position, out var hex) || !hex.HasEngagement)
                return null;

            var definition = EngagementDefinitionTable.For(hex.Engagement);
            var opponent = NpcShipGenerator.Generate(definition, rng);

            return new EngagementSession(definition, player, opponent);
        }

        public static void ClearMarker(GameMap map, HexCoordinate coordinate)
        {
            if (map.TryGetHex(coordinate, out var hex))
                hex.Engagement = EngagementTier.None;
        }
    }
}
