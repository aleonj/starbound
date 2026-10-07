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
        // Set by Match.Move after the fact (ShipMover itself has no idea
        // about hazard terrain) — lets the caller actually tell the
        // player a hazard hit landed, instead of the damage happening
        // silently with no feedback at all.
        public bool HazardHit { get; }

        private MoveResult(bool success, MoveFailureReason failureReason, HexCoordinate newPosition, bool hazardHit)
        {
            Success = success;
            FailureReason = failureReason;
            NewPosition = newPosition;
            HazardHit = hazardHit;
        }

        public static MoveResult Succeeded(HexCoordinate newPosition, bool hazardHit = false) =>
            new(true, MoveFailureReason.None, newPosition, hazardHit);

        public static MoveResult Failed(MoveFailureReason reason) =>
            new(false, reason, default, false);
    }
}
