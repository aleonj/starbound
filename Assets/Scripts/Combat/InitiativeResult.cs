namespace StarBound.Combat
{
    public readonly struct InitiativeResult
    {
        public RoundAttacker Attacker { get; }
        public int PlayerSpeedRoll { get; }
        public int PlayerSpeedTotal { get; }
        public int OpponentSpeedRoll { get; }
        public int OpponentSpeedTotal { get; }

        // A natural 1 on the player's Speed roll always hands the
        // opponent initiative, regardless of either total.
        public bool WasPlayerCriticalFailure { get; }

        public InitiativeResult(
            RoundAttacker attacker, int playerSpeedRoll, int playerSpeedTotal,
            int opponentSpeedRoll, int opponentSpeedTotal, bool wasPlayerCriticalFailure)
        {
            Attacker = attacker;
            PlayerSpeedRoll = playerSpeedRoll;
            PlayerSpeedTotal = playerSpeedTotal;
            OpponentSpeedRoll = opponentSpeedRoll;
            OpponentSpeedTotal = opponentSpeedTotal;
            WasPlayerCriticalFailure = wasPlayerCriticalFailure;
        }
    }
}
