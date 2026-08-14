using System;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Map;
using StarBound.Movement;

namespace StarBound.Multiplayer
{
    // Ties movement, engagement triggering, and turn-passing together for
    // a single 1v1 match. Mode-agnostic: usable directly for pass-and-play,
    // and by a future networked session driving the same state.
    public class Match
    {
        private DiceHand currentHand;
        private EngagementSession activeEngagement;
        private bool hasMovedThisTurn;
        private bool mustMoveAfterEscape;
        private bool hadEngagementThisTurn;

        public GameMap Map { get; }
        public Player PlayerOne { get; }
        public Player PlayerTwo { get; }
        public Player CurrentPlayer { get; private set; }
        public Player Winner { get; private set; }

        public Match(GameMap map, Player playerOne, Player playerTwo)
        {
            Map = map;
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            CurrentPlayer = playerOne;
        }

        public DiceHand CurrentHand => currentHand;
        public EngagementSession ActiveEngagement => activeEngagement;

        // True from the moment an engagement starts until it's explicitly
        // resolved via ResolveActiveEngagement — deliberately NOT tied to
        // the session's Outcome still being InProgress, so that a decided
        // but unacknowledged outcome (win/loss/escape) still blocks other
        // actions until the caller finalizes it.
        public bool IsInEngagement => activeEngagement != null;
        public bool IsComplete => Winner != null;
        public bool MustMoveAfterEscape => mustMoveAfterEscape;

        public bool CanMove =>
            !IsInEngagement && !IsComplete && currentHand != null && (!hasMovedThisTurn || mustMoveAfterEscape);

        // Blocked for the rest of the turn once an engagement has happened,
        // even if a later forced move (e.g. after escaping) lands on a
        // planet/starport.
        public bool CanShop => !IsInEngagement && !IsComplete && !hadEngagementThisTurn;

        public DiceHand RollDice(Random rng)
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't roll movement dice mid-engagement.");
            if (currentHand != null)
                throw new InvalidOperationException("Dice have already been rolled this turn.");

            currentHand = DiceRoller.Roll(rng);
            return currentHand;
        }

        // Only one voluntary movement is allowed per turn — the exception
        // is the mandatory move after a successful escape, which is still
        // permitted even though the turn's movement was already used.
        public MoveResult Move(RolledDie die, HexCoordinate to, Random rng)
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't move mid-engagement.");
            if (currentHand == null)
                throw new InvalidOperationException("Roll dice before moving.");
            if (hasMovedThisTurn && !mustMoveAfterEscape)
                throw new InvalidOperationException("Only one movement is allowed per turn.");

            var result = ShipMover.TryMove(Map, die, CurrentPlayer.Position, to);
            if (!result.Success)
                return result;

            CurrentPlayer.Position = result.NewPosition;
            hasMovedThisTurn = true;
            mustMoveAfterEscape = false;

            var session = EngagementTrigger.TryTrigger(CurrentPlayer, Map, rng);
            if (session != null)
                activeEngagement = session;

            return result;
        }

        // Call once ActiveEngagement.Outcome has left InProgress, to clear
        // the marker and apply the outcome's follow-on effects.
        public void ResolveActiveEngagement()
        {
            if (activeEngagement == null || activeEngagement.Outcome == EngagementOutcome.InProgress)
                throw new InvalidOperationException("There's no resolved engagement to finalize.");

            EngagementTrigger.ClearMarker(Map, CurrentPlayer.Position);
            hadEngagementThisTurn = true;

            if (activeEngagement.Outcome == EngagementOutcome.PlayerLost)
            {
                IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map);
            }
            else if (activeEngagement.Outcome == EngagementOutcome.PlayerEscaped)
            {
                // Waive the requirement if there's no unspent die that can
                // actually reach a legal adjacent hex — otherwise the
                // player could be stuck unable to end their turn.
                mustMoveAfterEscape = HasAnyLegalMove();
            }

            if (CurrentPlayer.HasWonMatch)
                Winner = CurrentPlayer;

            activeEngagement = null;
        }

        public bool CanEndTurn => !IsInEngagement && !IsComplete && !mustMoveAfterEscape;

        public void EndTurn()
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't end turn mid-encounter.");
            if (mustMoveAfterEscape)
                throw new InvalidOperationException("Must move to an adjacent hex after escaping before ending turn.");

            currentHand = null; // unspent dice are discarded, no carryover
            hasMovedThisTurn = false;
            hadEngagementThisTurn = false;
            CurrentPlayer = CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;
        }

        private bool HasAnyLegalMove()
        {
            if (currentHand == null)
                return false;

            foreach (var die in currentHand.UnspentDice)
            {
                foreach (var neighbor in Map.GetNeighborCoordinates(CurrentPlayer.Position))
                {
                    if (Map.TryGetHex(neighbor, out var hex) &&
                        (hex.Terrain == die.Terrain || hex.Terrain == TerrainType.PlanetOrStarport))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void EnsureMatchInProgress()
        {
            if (IsComplete)
                throw new InvalidOperationException("This match has already ended.");
        }
    }
}
