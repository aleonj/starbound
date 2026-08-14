namespace StarBound.Shop
{
    public enum PurchaseFailureReason
    {
        None,
        InsufficientFunds,
        CargoFull
    }

    public readonly struct PurchaseResult
    {
        public bool Success { get; }
        public PurchaseFailureReason FailureReason { get; }

        private PurchaseResult(bool success, PurchaseFailureReason failureReason)
        {
            Success = success;
            FailureReason = failureReason;
        }

        public static PurchaseResult Succeeded() => new(true, PurchaseFailureReason.None);
        public static PurchaseResult Failed(PurchaseFailureReason reason) => new(false, reason);
    }
}
