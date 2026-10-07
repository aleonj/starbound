using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Drives one engagement round by round. A round is: optional escape
    // attempt (only before initiative is rolled), then ResolveInitiative
    // (a Speed check decides the attacker), then ResolveAttack (the
    // attacker's Weapons vs. the defender's Shields) — split into two
    // calls so a caller can reveal initiative and, if the opponent won,
    // offer a Brace/Hold choice before the attack resolves. NPC fights
    // use exactly this — ResolveInitiative and the combined ResolveAttack.
    //
    // PvP fights are shared-screen throughout (both sides watching the
    // same device, no hand-off) and use split counterparts for anything
    // that's a genuine per-side DECISION or needs each side's own tap for
    // fairness — DeclareDefense/ExecuteAttack for the attack (the
    // defender commits to Brace/Hold first, then the attacker's own
    // ExecuteAttack tap actually runs the roll); CanOpponentDecideDefense/
    // AttemptOpponentEscape give the opponent their own symmetric moment
    // when they're the one being targeted; Escape (either side's — the
    // player's own pre-initiative attempt, or the opponent's post-
    // initiative one) splits the same way: BeginEscapeAttempt/
    // BeginOpponentEscapeAttempt only roll the escapee's own half,
    // ResolveEscapeIntercept is the OTHER side's own tap that rolls their
    // half and actually decides the outcome.
    //
    // Initiative is the one exception: it's PURE forced RNG with no
    // decision on either side (nobody chooses anything, nothing to hide),
    // so PvP uses the exact same single-call ResolveInitiative NPC fights
    // do, rather than a split pair of taps — there's no fairness reason
    // to make the second side tap their own "roll" button for a result
    // that's already fully determined by both ships' own stats the
    // instant it's called.
    public class EngagementSession
    {
        private bool hasAttemptedEscapeThisRound;
        private RoundAttacker? pendingAttacker;
        private bool? pendingDefenderWantsBrace;
        private RoundAttacker? pendingEscapee;
        private int? pendingEscapeeSpeedRoll;
        private int? pendingEscapeeSpeedTotal;

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

        // Narrative intro line for NPC encounters (see
        // EngagementTrigger.TryTrigger/EngagementFlavorText) — null for
        // PvP sessions, which don't pass one (see Match.AttackOpponent).
        // Purely for display; never read by resolution logic.
        public string FlavorText { get; }

        public EngagementSession(EngagementDefinition definition, Player player, Ship opponent, bool isPvP = false, string flavorText = null)
        {
            Definition = definition;
            Player = player;
            Opponent = opponent;
            IsPvP = isPvP;
            FlavorText = flavorText;
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
            ApplyEscapeOutcome(RoundAttacker.Player, PlayerShip, result);
            return result;
        }

        // PvP-only split of the above (and of AttemptOpponentEscape
        // below): the escapee's own tap only rolls their own half: the
        // OTHER side then gets a real tap of their own — via
        // ResolveEscapeIntercept — to roll their half and actually decide
        // whether the escape succeeds, rather than that roll happening
        // silently as part of the escapee's own call. NPC fights never
        // touch any of these three; they use the combined AttemptEscape/
        // AttemptOpponentEscape above/below.
        public bool IsAwaitingEscapeIntercept => pendingEscapee.HasValue;
        public RoundAttacker? PendingEscapee => pendingEscapee;

        public (int Roll, int Total) BeginEscapeAttempt(Random rng)
        {
            if (!CanAttemptEscape)
                throw new InvalidOperationException("Escape can't be attempted right now.");

            hasAttemptedEscapeThisRound = true;
            pendingEscapee = RoundAttacker.Player;
            var (roll, total) = CombatResolver.RollSpeedCheck(PlayerShip, rng);
            pendingEscapeeSpeedRoll = roll;
            pendingEscapeeSpeedTotal = total;
            return (roll, total);
        }

        // Single call for both PvE and PvP alike — see this class's own
        // doc comment for why Initiative doesn't get the split-tap
        // treatment the other PvP mechanics do.
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

        // wantsBrace applies to whichever side is actually defending this
        // round (see CombatResolver.ResolveAttack) — symmetric for PvP:
        // when the opponent is defending, this is THEIR brace choice, not
        // the player's. Ignored (no effect) when there's nothing to brace
        // against on the defending side's Energy. Declares AND executes
        // in one call — used as-is for NPC fights (there's no second real
        // player to hand a separate "attack" tap to). PvP uses the split
        // DeclareDefense/ExecuteAttack pair below instead, so the
        // attacker's own tap is what triggers the roll, not a side
        // effect of the defender's choice.
        public RoundResult ResolveAttack(Random rng, bool wantsBrace = false)
        {
            if (Outcome != EngagementOutcome.InProgress)
                throw new InvalidOperationException("This engagement has already ended.");
            if (!IsAwaitingAttackResolution)
                throw new InvalidOperationException("Call ResolveInitiative before ResolveAttack.");

            var attacker = pendingAttacker!.Value;
            var result = CombatResolver.ResolveAttack(PlayerShip, Opponent, attacker, rng, wantsBrace);
            pendingAttacker = null;
            ApplyAttackOutcome();
            return result;
        }

        // PvP-only split of the above: the defender declares their
        // Brace/Hold choice first (without yet rolling), then whichever
        // side actually won initiative taps their own ExecuteAttack to
        // run the roll — a real action for the attacker too, not
        // something that just happens as a consequence of the
        // defender's tap. NPC fights never touch either of these; they
        // use the combined ResolveAttack above.
        public bool IsAwaitingDefenseDeclaration => IsAwaitingAttackResolution && !pendingDefenderWantsBrace.HasValue;
        public bool IsAwaitingAttackExecution => IsAwaitingAttackResolution && pendingDefenderWantsBrace.HasValue;

        public void DeclareDefense(bool wantsBrace)
        {
            if (!IsAwaitingDefenseDeclaration)
                throw new InvalidOperationException("There's no pending defense decision right now.");

            pendingDefenderWantsBrace = wantsBrace;
        }

        public RoundResult ExecuteAttack(Random rng)
        {
            if (!IsAwaitingAttackExecution)
                throw new InvalidOperationException("Call DeclareDefense before ExecuteAttack.");

            var attacker = pendingAttacker!.Value;
            var wantsBrace = pendingDefenderWantsBrace!.Value;
            var result = CombatResolver.ResolveAttack(PlayerShip, Opponent, attacker, rng, wantsBrace);
            pendingAttacker = null;
            pendingDefenderWantsBrace = null;
            ApplyAttackOutcome();
            return result;
        }

        // Shared by ResolveAttack and ExecuteAttack — both need to check
        // the same post-attack Hull thresholds and advance the round the
        // same way; the only difference between them is how the brace
        // choice gets in (one call vs. declare-then-execute).
        private void ApplyAttackOutcome()
        {
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
        }

        // True exactly when it's the opponent's moment to decide their
        // own defense in a PvP fight — they've just been targeted
        // (PendingAttacker == Player), haven't declared yet, and the
        // fight is still live. The symmetric counterpart to the player's
        // own pre-initiative AttemptEscape and Brace/Hold choice —
        // Brace/Hold themselves need no separate opponent-side method
        // (DeclareDefense above is already symmetric), only Escape does,
        // since the player's AttemptEscape is hardcoded to PlayerShip as
        // the escapee. Checks IsAwaitingDefenseDeclaration rather than
        // the broader IsAwaitingAttackResolution so this correctly turns
        // off the instant they've declared, even though PendingAttacker
        // itself doesn't change until ExecuteAttack runs.
        public bool CanOpponentDecideDefense =>
            Outcome == EngagementOutcome.InProgress && IsPvP &&
            IsAwaitingDefenseDeclaration && PendingAttacker == RoundAttacker.Player;

        // Single source of truth for "whose moment is this" in a PvP
        // fight — used by EngagementScreen.RebuildActionRow (UI/
        // EngagementScreen.cs) to decide which side's controls to show
        // on the now-shared screen, and by MatchHud.RefreshEngagementScreen
        // for the panel's accent-color tint. Null for a non-PvP or
        // already-finished engagement (callers fall back to a neutral
        // treatment). Deliberately written in the SAME order as
        // RebuildActionRow's own dispatch chain — keep the two in
        // lockstep if either ever grows a new case.
        public RoundAttacker? ActiveDecisionMaker
        {
            get
            {
                if (!IsPvP || Outcome != EngagementOutcome.InProgress)
                    return null;
                if (IsAwaitingEscapeIntercept)
                    return PendingEscapee == RoundAttacker.Player ? RoundAttacker.Opponent : RoundAttacker.Player;
                // Pre-initiative is always the player's own moment now —
                // Initiative resolves in a single call (see
                // ResolveInitiative's own comment), so there's no longer
                // a separate opponent-side sub-phase here to detect.
                if (!IsAwaitingAttackResolution)
                    return RoundAttacker.Player;
                if (IsAwaitingAttackExecution)
                    return PendingAttacker;
                if (PendingAttacker == RoundAttacker.Opponent)
                    return RoundAttacker.Player; // still declaring Brace/Hold
                return RoundAttacker.Opponent; // CanOpponentDecideDefense — the only remaining case
            }
        }

        // Deliberately its own independent gate (CanOpponentDecideDefense,
        // not hasAttemptedEscapeThisRound) — this is a structurally
        // different decision point (post-initiative, only when the
        // opponent is the one targeted) from the player's own
        // pre-initiative escape window, which this doesn't touch at all.
        public EscapeAttemptResult AttemptOpponentEscape(Random rng)
        {
            if (!CanOpponentDecideDefense)
                throw new InvalidOperationException("The opponent can't decide their defense right now.");

            var result = CombatResolver.ResolveEscapeAttempt(Opponent, PlayerShip, rng);
            // Choosing to flee replaces this round's attack entirely,
            // succeed or fail — same as the player's own AttemptEscape.
            pendingAttacker = null;
            ApplyEscapeOutcome(RoundAttacker.Opponent, Opponent, result);
            return result;
        }

        // PvP-only split counterpart to AttemptOpponentEscape — see
        // BeginEscapeAttempt's own comment above for why.
        public (int Roll, int Total) BeginOpponentEscapeAttempt(Random rng)
        {
            if (!CanOpponentDecideDefense)
                throw new InvalidOperationException("The opponent can't decide their defense right now.");

            pendingAttacker = null;
            pendingEscapee = RoundAttacker.Opponent;
            var (roll, total) = CombatResolver.RollSpeedCheck(Opponent, rng);
            pendingEscapeeSpeedRoll = roll;
            pendingEscapeeSpeedTotal = total;
            return (roll, total);
        }

        // The OTHER side's own tap, regardless of which Begin* method
        // started this — pendingEscapee records which side is actually
        // fleeing, so this works symmetrically for either direction.
        // Rolls that side's half of the contested check and is what
        // actually decides whether the escape succeeds.
        public EscapeAttemptResult ResolveEscapeIntercept(Random rng)
        {
            if (!IsAwaitingEscapeIntercept)
                throw new InvalidOperationException("Call BeginEscapeAttempt or BeginOpponentEscapeAttempt before ResolveEscapeIntercept.");

            var escapee = pendingEscapee!.Value;
            var escapeeShip = escapee == RoundAttacker.Player ? PlayerShip : Opponent;
            var otherShip = escapee == RoundAttacker.Player ? Opponent : PlayerShip;

            var (otherRoll, otherTotal) = CombatResolver.RollSpeedCheck(otherShip, rng);
            var result = CombatResolver.DetermineEscapeOutcome(
                escapeeShip, pendingEscapeeSpeedRoll!.Value, pendingEscapeeSpeedTotal!.Value, otherRoll, otherTotal);

            pendingEscapee = null;
            pendingEscapeeSpeedRoll = null;
            pendingEscapeeSpeedTotal = null;

            ApplyEscapeOutcome(escapee, escapeeShip, result);
            return result;
        }

        // Shared by AttemptEscape/AttemptOpponentEscape (combined, NPC
        // path) and ResolveEscapeIntercept (split, PvP path) — both need
        // the same success/failure-cost outcome mapping, just reached via
        // different call shapes.
        private void ApplyEscapeOutcome(RoundAttacker escapee, Ship escapeeShip, EscapeAttemptResult result)
        {
            if (result.Success)
            {
                Outcome = escapee == RoundAttacker.Player ? EngagementOutcome.PlayerEscaped : EngagementOutcome.OpponentEscaped;
            }
            else if (escapeeShip.IsIntegrityDepleted)
            {
                // A failed attempt costs Energy — if that was the last
                // point, the escapee is beaten, same as a lost attack round.
                Outcome = escapee == RoundAttacker.Player ? EngagementOutcome.PlayerLost : EngagementOutcome.PlayerWon;
            }
        }
    }
}
