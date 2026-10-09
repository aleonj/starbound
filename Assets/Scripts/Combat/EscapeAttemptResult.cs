namespace StarBound.Combat
{
    public readonly struct EscapeAttemptResult
    {
        public int Roll { get; }
        public int Total { get; }

        // The opponent now rolls too — a contested d10+Speed check, rather
        // than the player's roll vs. a flat Speed stat.
        public int OpponentRoll { get; }
        public int OpponentTotal { get; }
        public bool Success { get; }

        // A natural 10 on the escapee's own roll — bypasses the contested
        // total entirely, mirroring RoundResult.WasCriticalHit's same
        // bypass for attack rolls. Guarantees a 10% floor chance to
        // escape regardless of how far behind on Speed the escapee is.
        public bool WasNaturalTen { get; }

        public EscapeAttemptResult(int roll, int total, int opponentRoll, int opponentTotal, bool success, bool wasNaturalTen = false)
        {
            Roll = roll;
            Total = total;
            OpponentRoll = opponentRoll;
            OpponentTotal = opponentTotal;
            Success = success;
            WasNaturalTen = wasNaturalTen;
        }
    }
}
