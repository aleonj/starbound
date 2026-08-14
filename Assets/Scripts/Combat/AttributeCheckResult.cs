using StarBound.Core;

namespace StarBound.Combat
{
    public readonly struct AttributeCheckResult
    {
        public CoreStat Attribute { get; }
        public int Roll { get; }
        public int Total { get; }
        public int OpponentValue { get; }
        public bool PlayerWonExchange { get; }

        public AttributeCheckResult(CoreStat attribute, int roll, int total, int opponentValue, bool playerWonExchange)
        {
            Attribute = attribute;
            Roll = roll;
            Total = total;
            OpponentValue = opponentValue;
            PlayerWonExchange = playerWonExchange;
        }
    }
}
