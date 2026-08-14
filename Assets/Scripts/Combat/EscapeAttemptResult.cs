namespace StarBound.Combat
{
    public readonly struct EscapeAttemptResult
    {
        public int Roll { get; }
        public int Total { get; }
        public int OpponentSpeed { get; }
        public bool Success { get; }

        public EscapeAttemptResult(int roll, int total, int opponentSpeed, bool success)
        {
            Roll = roll;
            Total = total;
            OpponentSpeed = opponentSpeed;
            Success = success;
        }
    }
}
