namespace StarBound.Economy
{
    public enum AcceptJobFailureReason
    {
        None,
        AlreadyHasActiveJob
    }

    public readonly struct AcceptJobResult
    {
        public bool Success { get; }
        public AcceptJobFailureReason FailureReason { get; }

        private AcceptJobResult(bool success, AcceptJobFailureReason failureReason)
        {
            Success = success;
            FailureReason = failureReason;
        }

        public static AcceptJobResult Succeeded() => new(true, AcceptJobFailureReason.None);
        public static AcceptJobResult Failed(AcceptJobFailureReason reason) => new(false, reason);
    }
}
