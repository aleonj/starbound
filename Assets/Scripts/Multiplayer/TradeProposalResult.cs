namespace StarBound.Multiplayer
{
    // Same shape as Shop's PurchaseResult/SellResult — expected,
    // explainable failures (an item no longer held, insufficient funds,
    // no cargo room) surface here rather than as raw exceptions, since a
    // proposal's terms can be built well before they're actually
    // committed (Propose -> hand-off -> Accept), and anything could in
    // principle have changed in between.
    public enum TradeProposalFailureReason
    {
        None,
        ItemNotHeld,
        InsufficientFunds,
        CargoFull
    }

    public readonly struct TradeProposalResult
    {
        public bool Success { get; }
        public TradeProposalFailureReason FailureReason { get; }

        private TradeProposalResult(bool success, TradeProposalFailureReason failureReason)
        {
            Success = success;
            FailureReason = failureReason;
        }

        public static TradeProposalResult Succeeded() => new(true, TradeProposalFailureReason.None);
        public static TradeProposalResult Failed(TradeProposalFailureReason reason) => new(false, reason);
    }
}
