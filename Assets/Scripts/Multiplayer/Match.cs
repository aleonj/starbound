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
        private bool mustMoveAfterEscape;

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
        public bool IsInEngagement => activeEngagement != null && activeEngagement.Outcome == EngagementOutcome.InProgress;
        public bool IsComplete => Winner != null;
        public bool MustMoveAfterEscape => mustMoveAfterEscape;

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

        public MoveResult Move(RolledDie die, HexCoordinate to, Random rng)
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't move mid-engagement.");
            if (currentHand == null)
                throw new InvalidOperationException("Roll dice before moving.");

            var result = ShipMover.TryMove(Map, die, CurrentPlayer.Position, to);
            if (!result.Success)
                return result;

            CurrentPlayer.Position = result.NewPosition;
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

            if (activeEngagement.Outcome == EngagementOutcome.PlayerLost)
            {
                IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map);
            }
            else if (activeEngagement.Outcome == EngagementOutcome.PlayerEscaped)
            {
                // If no dice remain to move with, waive the requirement —
                // the source rules don't address this edge case.
                mustMoveAfterEscape = currentHand != null && currentHand.HasUnspentDice;
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
            CurrentPlayer = CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;
        }

        private void EnsureMatchInProgress()
        {
            if (IsComplete)
                throw new InvalidOperationException("This match has already ended.");
        }
    }
}
