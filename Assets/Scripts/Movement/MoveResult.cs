using StarBound.Core;

namespace StarBound.Movement
{
    public enum MoveFailureReason
    {
        None,
        DieAlreadySpent,
        TargetNotAdjacent,
        TargetTerrainMismatch,
        WormholeDeviceRequired,
        InsufficientFundsForToll
    }

    public readonly struct MoveResult
    {
        public bool Success { get; }
        public MoveFailureReason FailureReason { get; }
        public HexCoordinate NewPosition { get; }

        private MoveResult(bool success, MoveFailureReason failureReason, HexCoordinate newPosition)
        {
            Success = success;
            FailureReason = failureReason;
            NewPosition = newPosition;
        }

        public static MoveResult Succeeded(HexCoordinate newPosition) =>
            new(true, MoveFailureReason.None, newPosition);

        public static MoveResult Failed(MoveFailureReason reason) =>
            new(false, reason, default);
    }
}
