namespace StarBound.Combat
{
    public enum RoundAttacker
    {
        Player,
        Opponent
    }

    public readonly struct RoundResult
    {
        public RoundAttacker Attacker { get; }
        public int SpeedRoll { get; }
        public int SpeedTotal { get; }
        public int OpponentSpeed { get; }

        // Null when the opponent is the attacker — NPCs never roll, so
        // their attack is a flat Weapons-vs-Shields comparison.
        public int? AttackRoll { get; }
        public int AttackTotal { get; }
        public int DefenderShields { get; }
        public bool HitLanded { get; }

        public RoundResult(
            RoundAttacker attacker, int speedRoll, int speedTotal, int opponentSpeed,
            int? attackRoll, int attackTotal, int defenderShields, bool hitLanded)
        {
            Attacker = attacker;
            SpeedRoll = speedRoll;
            SpeedTotal = speedTotal;
            OpponentSpeed = opponentSpeed;
            AttackRoll = attackRoll;
            AttackTotal = attackTotal;
            DefenderShields = defenderShields;
            HitLanded = hitLanded;
        }
    }
}
