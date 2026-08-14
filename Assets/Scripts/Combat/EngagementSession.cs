using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Drives one engagement round by round. A round is: optional escape
    // attempt (if it fails, the round continues as normal), then the
    // attribute check for the round's current attribute, then the round
    // advances to the next attribute in the cycle. Escape can only be
    // attempted once per round.
    public class EngagementSession
    {
        private int roundIndex;
        private bool hasAttemptedEscapeThisRound;

        public EngagementDefinition Definition { get; }
        public Player Player { get; }
        public Ship PlayerShip => Player.Ship;
        public Ship Opponent { get; }
        public EngagementOutcome Outcome { get; private set; } = EngagementOutcome.InProgress;

        public EngagementSession(EngagementDefinition definition, Player player, Ship opponent)
        {
            Definition = definition;
            Player = player;
            Opponent = opponent;
        }

        public CoreStat CurrentAttribute => Definition.RoundOrder[roundIndex % Definition.RoundOrder.Count];

        public bool CanAttemptEscape =>
            Outcome == EngagementOutcome.InProgress && Definition.EscapeAllowed && !hasAttemptedEscapeThisRound;

        public EscapeAttemptResult AttemptEscape(Random rng)
        {
            if (!CanAttemptEscape)
                throw new InvalidOperationException("Escape can't be attempted right now.");

            hasAttemptedEscapeThisRound = true;
            var result = CombatResolver.ResolveEscapeAttempt(PlayerShip, Opponent, rng);

            if (result.Success)
                Outcome = EngagementOutcome.PlayerEscaped;

            return result;
        }

        public AttributeCheckResult ResolveRound(Random rng)
        {
            if (Outcome != EngagementOutcome.InProgress)
                throw new InvalidOperationException("This engagement has already ended.");

            var attribute = CurrentAttribute;
            var result = CombatResolver.ResolveAttributeCheck(PlayerShip, Opponent, attribute, rng);

            if (Opponent.GetStat(CoreStat.Hull) <= 0)
            {
                Outcome = EngagementOutcome.PlayerWon;
                Player.RecordEngagementWin(Definition.Tier);
            }
            else if (PlayerShip.IsIntegrityDepleted)
            {
                Outcome = EngagementOutcome.PlayerLost;
            }
            else
            {
                roundIndex++;
                hasAttemptedEscapeThisRound = false;
            }

            return result;
        }
    }
}
