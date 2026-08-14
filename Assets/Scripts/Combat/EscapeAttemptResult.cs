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

        public EscapeAttemptResult(int roll, int total, int opponentRoll, int opponentTotal, bool success)
        {
            Roll = roll;
            Total = total;
            OpponentRoll = opponentRoll;
            OpponentTotal = opponentTotal;
            Success = success;
        }
    }
}
