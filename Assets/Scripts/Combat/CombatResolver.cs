using System;
using StarBound.Core;

namespace StarBound.Combat
{
    // Pure dice-resolution rules: d10 + player's stat vs. the opponent's
    // flat stat (no roll on the opponent's side), for both the attribute
    // check and the escape check.
    public static class CombatResolver
    {
        public static AttributeCheckResult ResolveAttributeCheck(Ship player, Ship opponent, CoreStat attribute, Random rng)
        {
            var roll = rng.Next(1, 11); // d10: 1-10
            var total = roll + player.GetStat(attribute);
            var opponentValue = opponent.GetStat(attribute);
            var playerWonExchange = total >= opponentValue;

            if (playerWonExchange)
                opponent.ApplyStatDelta(CoreStat.Hull, -1);
            else
                player.ApplyStatDelta(CoreStat.Hull, -1);

            return new AttributeCheckResult(attribute, roll, total, opponentValue, playerWonExchange);
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
