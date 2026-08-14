namespace StarBound.Shop
{
    public enum RepairFailureReason
    {
        None,
        AlreadyAtMax,
        InsufficientFunds
    }

    public readonly struct RepairResult
    {
        public bool Success { get; }
        public RepairFailureReason FailureReason { get; }

        private RepairResult(bool success, RepairFailureReason failureReason)
        {
            Success = success;
            FailureReason = failureReason;
        }

        public static RepairResult Succeeded() => new(true, RepairFailureReason.None);
        public static RepairResult Failed(RepairFailureReason reason) => new(false, reason);
    }
}
