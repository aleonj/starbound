namespace StarBound.Shop
{
    public enum SellFailureReason
    {
        None,
        ItemNotHeld
    }

    public readonly struct SellResult
    {
        public bool Success { get; }
        public SellFailureReason FailureReason { get; }
        public int Refund { get; }

        private SellResult(bool success, SellFailureReason failureReason, int refund)
        {
            Success = success;
            FailureReason = failureReason;
            Refund = refund;
        }

        public static SellResult Succeeded(int refund) => new(true, SellFailureReason.None, refund);
        public static SellResult Failed(SellFailureReason reason) => new(false, reason, refund: 0);
    }
}
