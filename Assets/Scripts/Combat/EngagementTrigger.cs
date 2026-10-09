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
        // Default NPC performance-stat boost while MatchVariable.
        // PirateSurge is active — deliberately a plain int here, not the
        // MatchVariable enum itself: this class stays agnostic of match-
        // wide event state, the caller (Match) resolves ActiveVariable
        // into these already-concrete numbers before calling in, same
        // reasoning as rewardMultiplier below.
        public const int PirateSurgeStatBoost = 2;

        // maxTier gates progression phasing (see Match.MaxUnlockedTier): a
        // marker whose tier exceeds it doesn't go off yet and stays on the
        // map for a later visit, once that tier unlocks. Callers that need
        // to bypass the gate for a specific hex (the active progression
        // goal's own target) resolve that before calling in, by passing a
        // maxTier that already covers it — this method stays a dumb
        // ceiling check. statBoost/rewardMultiplier default to "no event
        // active" (0 / 1.0) — see Match.HandleArrival for where
        // ActiveVariable gets resolved into these before calling in.
        // goalTargetHex defaults to null (no goal active, or caller
        // doesn't care) — Match passes ActiveGoal?.TargetHex.
        public static EngagementSession TryTrigger(Player player, GameMap map, Random rng, EngagementTier maxTier, int statBoost = 0,
            double rewardMultiplier = 1.0, HexCoordinate? goalTargetHex = null)
        {
            if (!map.TryGetHex(player.Position, out var hex) || !hex.HasEngagement)
                return null;
            if (hex.Engagement > maxTier)
                return null;

            var definition = EngagementDefinitionTable.For(hex.Engagement);
            if (statBoost != 0)
                definition = definition.WithPerformanceStatBoost(statBoost);
            var opponent = NpcShipGenerator.Generate(definition, rng);
            var flavorText = EngagementFlavorText.PickRandom(hex.Engagement, rng);

            // Display-only — JobService.ResolveBountyOutcome re-derives
            // this independently once the engagement actually concludes
            // (see its own comment), rather than trusting whatever this
            // session was constructed with.
            var bountyJob = player.ActiveJob is { Type: JobType.BountyHunting } job && job.Destination == player.Position
                ? job
                : null;

            // Also display-only — Match.HandleProgressionOnEngagementWin
            // is what actually completes a DefeatNamedTarget goal,
            // independently re-checking ActiveGoal/resolvedHex once the
            // fight concludes, same reasoning as the bounty flag above.
            // A dumb coordinate match here — it's the CALLER's job (see
            // Match.HandleArrival) to only pass a non-null goalTargetHex
            // when that's actually meaningful (i.e. the active goal is
            // DefeatNamedTarget, not a TravelAndPay goal that merely
            // coincides with this hex, which this fight has nothing to
            // do with completing).
            var isGoalTarget = goalTargetHex.HasValue && goalTargetHex.Value == player.Position;

            return new EngagementSession(definition, player, opponent, flavorText: flavorText, rewardMultiplier: rewardMultiplier,
                isBountyTarget: bountyJob != null, bountyRewardMoney: bountyJob?.Reward, isGoalTarget: isGoalTarget);
        }

        public static void ClearMarker(GameMap map, HexCoordinate coordinate)
        {
            if (map.TryGetHex(coordinate, out var hex))
                hex.Engagement = EngagementTier.None;
        }
    }
}
