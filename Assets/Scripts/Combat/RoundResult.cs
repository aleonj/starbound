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
        public int AttackRoll { get; }
        public int AttackTotal { get; }

        // The defender now rolls too: a d10 + Shields (+ Brace bonus when
        // the player braced against an opponent attack) contested against
        // the attacker's total, rather than a flat Shields threshold.
        public int DefenseRoll { get; }
        public int DefenseTotal { get; }
        public bool HitLanded { get; }

        // A natural 10 on the attacker's roll — bypasses Shields entirely
        // and deals bonus damage. Applies to either side now that both roll.
        public bool WasCriticalHit { get; }
        public int Damage { get; }

        // True when the player chose to Brace against an opponent attack
        // (and had enough Energy to actually do so). Opponents have no
        // equivalent — their Shields are never boosted when defending.
        public bool DefenderBraced { get; }

        public RoundResult(
            RoundAttacker attacker, int attackRoll, int attackTotal, int defenseRoll, int defenseTotal,
            bool hitLanded, bool wasCriticalHit, int damage, bool defenderBraced)
        {
            Attacker = attacker;
            AttackRoll = attackRoll;
            AttackTotal = attackTotal;
            DefenseRoll = defenseRoll;
            DefenseTotal = defenseTotal;
            HitLanded = hitLanded;
            WasCriticalHit = wasCriticalHit;
            Damage = damage;
            DefenderBraced = defenderBraced;
        }
    }
}
