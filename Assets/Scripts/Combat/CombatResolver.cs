using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Round mechanic: a Speed check decides who attacks this round, then
    // the attacker's Weapons vs. the defender's flat Shields decides
    // whether the hit lands. The player always rolls for their own side;
    // when the PvE opponent wins initiative, its attack is a flat
    // Weapons-vs-Shields comparison with no roll, since NPCs never roll.
    public static class CombatResolver
    {
        public static RoundResult ResolveRound(Ship player, Ship opponent, Random rng)
        {
            var speedRoll = rng.Next(1, 11); // d10: 1-10
            var speedTotal = speedRoll + player.GetStat(CoreStat.Speed);
            var opponentSpeed = opponent.GetStat(CoreStat.Speed);
            var playerWinsInitiative = speedTotal >= opponentSpeed;

            if (playerWinsInitiative)
            {
                var attackRoll = rng.Next(1, 11);
                var attackTotal = attackRoll + player.GetStat(CoreStat.Weapons);
                var defenderShields = opponent.GetStat(CoreStat.Shields);
                var hitLanded = attackTotal >= defenderShields;

                if (hitLanded)
                    opponent.ApplyStatDelta(CoreStat.Hull, -1);

                return new RoundResult(RoundAttacker.Player, speedRoll, speedTotal, opponentSpeed, attackRoll, attackTotal, defenderShields, hitLanded);
            }
            else
            {
                var attackTotal = opponent.GetStat(CoreStat.Weapons); // no roll — NPCs never roll
                var defenderShields = player.GetStat(CoreStat.Shields);
                var hitLanded = attackTotal >= defenderShields;

                if (hitLanded)
                    player.ApplyStatDelta(CoreStat.Hull, -1);

                return new RoundResult(RoundAttacker.Opponent, speedRoll, speedTotal, opponentSpeed, null, attackTotal, defenderShields, hitLanded);
            }
        }

        public static EscapeAttemptResult ResolveEscapeAttempt(Ship player, Ship opponent, Random rng)
        {
            var roll = rng.Next(1, 11);
            var total = roll + player.GetStat(CoreStat.Speed);
            var opponentSpeed = opponent.GetStat(CoreStat.Speed);
            var success = total >= opponentSpeed;

            if (!success)
                player.ApplyStatDelta(CoreStat.Energy, -1);

            return new EscapeAttemptResult(roll, total, opponentSpeed, success);
        }
    }
}
