using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Round mechanic, split into two explicit steps so a caller (UI) can
    // reveal initiative before the attack resolves, and offer the
    // defender a Brace/Hold choice in between when the opponent attacks:
    //   1. ResolveInitiative: a contested Speed check (both sides roll)
    //      decides who attacks this round.
    //   2. ResolveAttack: the attacker rolls Weapons and the defender
    //      rolls Shields — both contested d10+stat checks — to decide
    //      whether the hit lands.
    // Both sides roll now — this used to be asymmetric (opponent never
    // rolled), which made PvE fights either trivial or swingy depending
    // on how opponent stats were tuned. Escape is still player-initiated
    // only (opponents never attempt their own escape), but resolving one
    // is also a contested d10+Speed roll on both sides now, not the
    // player's roll against a flat Speed stat.
    public static class CombatResolver
    {
        public const int BraceShieldBonus = 2;
        public const int BraceEnergyCost = 1;

        public static InitiativeResult ResolveInitiative(Ship player, Ship opponent, Random rng)
        {
            var playerRoll = rng.Next(1, 11); // d10: 1-10
            var playerTotal = playerRoll + player.GetStat(CoreStat.Speed);
            var opponentRoll = rng.Next(1, 11);
            var opponentTotal = opponentRoll + opponent.GetStat(CoreStat.Speed);

            var isPlayerCriticalFailure = playerRoll == 1;
            var attacker = !isPlayerCriticalFailure && playerTotal >= opponentTotal
                ? RoundAttacker.Player
                : RoundAttacker.Opponent;

            return new InitiativeResult(attacker, playerRoll, playerTotal, opponentRoll, opponentTotal, isPlayerCriticalFailure);
        }

        // wantsBrace is ignored when the player is the attacker — bracing
        // only makes sense when defending against the opponent's attack.
        public static RoundResult ResolveAttack(Ship player, Ship opponent, RoundAttacker attacker, Random rng, bool wantsBrace = false)
        {
            if (attacker == RoundAttacker.Player)
            {
                var attackRoll = rng.Next(1, 11);
                var isCriticalHit = attackRoll == 10;
                var attackTotal = attackRoll + player.GetStat(CoreStat.Weapons);

                var defenseRoll = rng.Next(1, 11);
                var defenseTotal = defenseRoll + opponent.GetStat(CoreStat.Shields);

                var hitLanded = isCriticalHit || attackTotal >= defenseTotal;
                var damage = hitLanded ? (isCriticalHit ? 2 : 1) : 0;

                if (damage > 0)
                    opponent.ApplyStatDelta(CoreStat.Hull, -damage);

                return new RoundResult(RoundAttacker.Player, attackRoll, attackTotal, defenseRoll, defenseTotal, hitLanded, isCriticalHit, damage, defenderBraced: false);
            }
            else
            {
                var braced = wantsBrace && player.GetStat(CoreStat.Energy) > 0;
                if (braced)
                    player.ApplyStatDelta(CoreStat.Energy, -BraceEnergyCost);

                var attackRoll = rng.Next(1, 11);
                var isCriticalHit = attackRoll == 10;
                var attackTotal = attackRoll + opponent.GetStat(CoreStat.Weapons);

                var defenseRoll = rng.Next(1, 11);
                var defenseTotal = defenseRoll + player.GetStat(CoreStat.Shields) + (braced ? BraceShieldBonus : 0);

                var hitLanded = isCriticalHit || attackTotal >= defenseTotal;
                var damage = hitLanded ? (isCriticalHit ? 2 : 1) : 0;

                if (damage > 0)
                    player.ApplyStatDelta(CoreStat.Hull, -damage);

                return new RoundResult(RoundAttacker.Opponent, attackRoll, attackTotal, defenseRoll, defenseTotal, hitLanded, isCriticalHit, damage, braced);
            }
        }

        public static EscapeAttemptResult ResolveEscapeAttempt(Ship player, Ship opponent, Random rng)
        {
            var roll = rng.Next(1, 11);
            var total = roll + player.GetStat(CoreStat.Speed);
            var opponentRoll = rng.Next(1, 11);
            var opponentTotal = opponentRoll + opponent.GetStat(CoreStat.Speed);
            var success = total >= opponentTotal;

            if (!success)
                player.ApplyStatDelta(CoreStat.Energy, -1);

            return new EscapeAttemptResult(roll, total, opponentRoll, opponentTotal, success);
        }
    }
}
