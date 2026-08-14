using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Drives one engagement round by round. A round is: optional escape
    // attempt (only before initiative is rolled), then ResolveInitiative
    // (a Speed check decides the attacker), then ResolveAttack (the
    // attacker's Weapons vs. the defender's Shields) — split into two
    // calls so a caller can reveal initiative and, if the opponent won,
    // offer a Brace/Hold choice before the attack resolves.
    public class EngagementSession
    {
        private bool hasAttemptedEscapeThisRound;
        private RoundAttacker? pendingAttacker;

        public EngagementDefinition Definition { get; }
        public Player Player { get; }
        public Ship PlayerShip => Player.Ship;
        public Ship Opponent { get; }
        public EngagementOutcome Outcome { get; private set; } = EngagementOutcome.InProgress;

        // True when Opponent is another real player's ship (landed on their
        // hex and chose to attack) rather than a generated NPC. PvP wins
        // don't count toward match victory yet — whether they should is
        // still an open design question — so RecordEngagementWin is skipped.
        public bool IsPvP { get; }

        public EngagementSession(EngagementDefinition definition, Player player, Ship opponent, bool isPvP = false)
        {
            Definition = definition;
            Player = player;
            Opponent = opponent;
            IsPvP = isPvP;
        }

        public bool IsAwaitingAttackResolution => pendingAttacker.HasValue;
        public RoundAttacker? PendingAttacker => pendingAttacker;

        public bool CanAttemptEscape =>
            Outcome == EngagementOutcome.InProgress && Definition.EscapeAllowed &&
            !hasAttemptedEscapeThisRound && !IsAwaitingAttackResolution;

        public EscapeAttemptResult AttemptEscape(Random rng)
        {
            if (!CanAttemptEscape)
                throw new InvalidOperationException("Escape can't be attempted right now.");

            hasAttemptedEscapeThisRound = true;
            var result = CombatResolver.ResolveEscapeAttempt(PlayerShip, Opponent, rng);

            if (result.Success)
                Outcome = EngagementOutcome.PlayerEscaped;
            else if (PlayerShip.IsIntegrityDepleted)
                // A failed attempt costs Energy — if that was the last
                // point, the player is beaten, same as a lost attack round.
                Outcome = EngagementOutcome.PlayerLost;

            return result;
        }

        public InitiativeResult ResolveInitiative(Random rng)
        {
            if (Outcome != EngagementOutcome.InProgress)
                throw new InvalidOperationException("This engagement has already ended.");
            if (IsAwaitingAttackResolution)
                throw new InvalidOperationException("Initiative has already been resolved this round — call ResolveAttack next.");

            var result = CombatResolver.ResolveInitiative(PlayerShip, Opponent, rng);
            pendingAttacker = result.Attacker;
            return result;
        }

        // wantsBrace only matters when the opponent is the pending
        // attacker — ignored otherwise.
        public RoundResult ResolveAttack(Random rng, bool wantsBrace = false)
        {
            if (Outcome != EngagementOutcome.InProgress)
                throw new InvalidOperationException("This engagement has already ended.");
            if (!IsAwaitingAttackResolution)
                throw new InvalidOperationException("Call ResolveInitiative before ResolveAttack.");

            var attacker = pendingAttacker!.Value;
            var result = CombatResolver.ResolveAttack(PlayerShip, Opponent, attacker, rng, wantsBrace);
            pendingAttacker = null;

            if (Opponent.GetStat(CoreStat.Hull) <= 0)
            {
                Outcome = EngagementOutcome.PlayerWon;
                if (!IsPvP)
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
