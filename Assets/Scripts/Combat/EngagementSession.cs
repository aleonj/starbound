using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Drives one engagement round by round. A round is: optional escape
    // attempt (if it fails, the round continues as normal), then a Speed
    // check to decide the attacker, then that attacker's Weapons vs. the
    // defender's Shields. Escape can only be attempted once per round.
    public class EngagementSession
    {
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

        public RoundResult ResolveRound(Random rng)
        {
            if (Outcome != EngagementOutcome.InProgress)
                throw new InvalidOperationException("This engagement has already ended.");

            var result = CombatResolver.ResolveRound(PlayerShip, Opponent, rng);

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
                hasAttemptedEscapeThisRound = false;
            }

            return result;
        }
    }
}
