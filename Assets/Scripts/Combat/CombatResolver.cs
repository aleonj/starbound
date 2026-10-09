using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Round mechanic, split into two explicit steps so a caller (UI) can
    // reveal initiative before the attack resolves, and offer the
    // defender a Brace/Hold choice in between when the opponent attacks:
    //   1. ResolveInitiative: a contested Speed check (both sides roll)
    //      decides who attacks this round. Itself splittable into
    //      RollSpeedCheck (one side) + DetermineInitiative (pure
    //      comparison) for PvP's real two-tap roll — see EngagementSession.
    //   2. ResolveAttack: the attacker rolls Weapons and the defender
    //      rolls Shields — both contested d10+stat checks — to decide
    //      whether the hit lands.
    // Both sides roll now — this used to be asymmetric (opponent never
    // rolled), which made PvE fights either trivial or swingy depending
    // on how opponent stats were tuned. Escape/Brace are directional
    // (escapee vs. the other side; attacker vs. defender) but not
    // hardcoded to "player" — see ResolveEscapeAttempt/ResolveAttack —
    // so a PvP caller can run either one for whichever side is actually
    // acting (see EngagementSession.AttemptOpponentEscape and
    // CanOpponentDecideDefense).
    public static class CombatResolver
    {
        public const int BraceShieldBonus = 2;
        public const int BraceEnergyCost = 1;
        public const int EscapeAttemptEnergyCost = 1;

        // A single d10 + Speed check for one side — split out of
        // ResolveInitiative/ResolveEscapeAttempt below so PvP can run an
        // Escape attempt as two genuinely separate rolls, one per side's
        // own tap (see EngagementSession.BeginEscapeAttempt/
        // ResolveEscapeIntercept), rather than both rolls happening
        // silently as a side effect of whoever taps first. Initiative
        // itself no longer needs this split in PvP — see
        // EngagementSession's own class comment.
        public static (int Roll, int Total) RollSpeedCheck(Ship ship, Random rng)
        {
            var roll = rng.Next(1, 11); // d10: 1-10
            return (roll, roll + ship.GetStat(CoreStat.Speed));
        }

        // Pure comparison, no RNG — once both sides' rolls are known
        // (however they were obtained), this decides who attacks.
        public static InitiativeResult DetermineInitiative(int playerRoll, int playerTotal, int opponentRoll, int opponentTotal)
        {
            var isPlayerCriticalFailure = playerRoll == 1;
            var attacker = !isPlayerCriticalFailure && playerTotal >= opponentTotal
                ? RoundAttacker.Player
                : RoundAttacker.Opponent;

            return new InitiativeResult(attacker, playerRoll, playerTotal, opponentRoll, opponentTotal, isPlayerCriticalFailure);
        }

        // Composes the two pieces above — same RNG consumption order
        // (player rolls first, then opponent) and identical output as
        // before the split, so every existing NPC-fight call site and
        // test is unaffected. PvP uses the split pieces directly instead
        // (see EngagementSession) for a real two-tap roll.
        public static InitiativeResult ResolveInitiative(Ship player, Ship opponent, Random rng)
        {
            var (playerRoll, playerTotal) = RollSpeedCheck(player, rng);
            var (opponentRoll, opponentTotal) = RollSpeedCheck(opponent, rng);
            return DetermineInitiative(playerRoll, playerTotal, opponentRoll, opponentTotal);
        }

        // wantsBrace applies to whichever side is actually defending this
        // round (kept its original name — CombatResolverTests.cs already
        // calls this with the named argument `wantsBrace:`, and there's
        // no real clarity gain worth breaking that) — unified from two
        // near-duplicate branches (only one of which used to support
        // bracing at all, since only the player could ever brace) into
        // one generic attacker/defender flow. This is what makes brace
        // symmetric for PvP: EngagementSession.ResolveAttack's own
        // signature didn't need to change at all, since its own
        // wantsBrace already forwards straight through to this one.
        public static RoundResult ResolveAttack(Ship player, Ship opponent, RoundAttacker attacker, Random rng, bool wantsBrace = false)
        {
            var isPlayerAttacking = attacker == RoundAttacker.Player;
            var attackerShip = isPlayerAttacking ? player : opponent;
            var defenderShip = isPlayerAttacking ? opponent : player;

            var braced = wantsBrace && defenderShip.GetStat(CoreStat.Energy) > 0;
            if (braced)
                defenderShip.ApplyStatDelta(CoreStat.Energy, -BraceEnergyCost);

            var attackRoll = rng.Next(1, 11);
            var isCriticalHit = attackRoll == 10;
            var attackTotal = attackRoll + attackerShip.GetStat(CoreStat.Weapons);

            var defenseRoll = rng.Next(1, 11);
            var defenseTotal = defenseRoll + defenderShip.GetStat(CoreStat.Shields) + (braced ? BraceShieldBonus : 0);

            // A tie goes to the defender — the attacker needs to actually
            // beat the defense total, not just match it, for a non-crit
            // hit to land. isCriticalHit's own natural-10 bypass is
            // unaffected (short-circuits before this comparison matters).
            var hitLanded = isCriticalHit || attackTotal > defenseTotal;
            var damage = hitLanded ? (isCriticalHit ? 2 : 1) : 0;

            if (damage > 0)
                defenderShip.ApplyStatDelta(CoreStat.Hull, -damage);

            return new RoundResult(attacker, attackRoll, attackTotal, defenseRoll, defenseTotal, hitLanded, isCriticalHit, damage, braced);
        }

        // Pure comparison, no RNG — same split shape as
        // DetermineInitiative, letting PvP run the escapee's roll and the
        // other side's roll as two genuinely separate taps (see
        // EngagementSession.BeginEscapeAttempt/BeginOpponentEscapeAttempt/
        // ResolveEscapeIntercept) instead of both happening silently in
        // one call. Doesn't charge Energy itself (despite the attempt
        // always costing it, win or lose — see EscapeAttemptEnergyCost's
        // own comment) — callers charge it the moment the attempt
        // actually BEGINS (see EngagementSession.AttemptEscape/
        // AttemptOpponentEscape/BeginEscapeAttempt/BeginOpponentEscapeAttempt),
        // not here at resolution, so the UI reflects the cost immediately
        // on commit rather than only once the (possibly PvP, tap-delayed)
        // intercept roll finally resolves.
        public static EscapeAttemptResult DetermineEscapeOutcome(int escapeeRoll, int escapeeTotal, int otherRoll, int otherTotal)
        {
            var success = escapeeTotal >= otherTotal;
            return new EscapeAttemptResult(escapeeRoll, escapeeTotal, otherRoll, otherTotal, success);
        }

        // escapee vs. other, not hardcoded to player vs. opponent — lets
        // a PvP caller run this for whichever side is actually attempting
        // to flee (see EngagementSession.AttemptEscape/AttemptOpponentEscape).
        // Composes the two pieces above — same RNG order (escapee rolls
        // first, then other) and identical result as before the split, so
        // NPC fights and existing call sites are unaffected. PvP uses the
        // split pieces directly instead, for a real two-tap roll (see
        // EngagementSession.BeginEscapeAttempt/BeginOpponentEscapeAttempt,
        // which charge the SAME cost themselves at their own begin step,
        // for the identical reason this one charges it here: the moment
        // the attempt is made, not once it resolves).
        public static EscapeAttemptResult ResolveEscapeAttempt(Ship escapee, Ship other, Random rng)
        {
            escapee.ApplyStatDelta(CoreStat.Energy, -EscapeAttemptEnergyCost);
            var (roll, total) = RollSpeedCheck(escapee, rng);
            var (otherRoll, otherTotal) = RollSpeedCheck(other, rng);
            return DetermineEscapeOutcome(roll, total, otherRoll, otherTotal);
        }
    }
}
