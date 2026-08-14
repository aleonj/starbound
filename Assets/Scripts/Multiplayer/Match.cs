using System;
using System.Linq;
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
        private bool movementLocked;
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

        public bool CanMove =>
            !IsInEngagement && !IsComplete && currentHand != null && !movementLocked;

        // Requires dice to have been rolled this turn — otherwise a player
        // sitting on a planet at the start of their turn (e.g. their match
        // start, or having ended a prior turn docked) could shop without
        // taking any action at all. Also blocked for the rest of the turn
        // once an engagement has happened.
        public bool CanShop => !IsInEngagement && !IsComplete && !hadEngagementThisTurn && currentHand != null;

        public bool IsCurrentPlayerOnPlanet =>
            Map.TryGetHex(CurrentPlayer.Position, out var hex) && hex.Terrain == TerrainType.PlanetOrStarport;

        public Player OtherPlayer => CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;

        // Landing on the other player's hex is a standing opportunity, not
        // a forced trigger — the mover can attack or simply ignore it and
        // keep playing, so this stays a plain condition check rather than
        // something Move() resolves automatically.
        public bool IsOnOpponentHex => CurrentPlayer.Position == OtherPlayer.Position;

        public bool CanAttackOpponent => IsOnOpponentHex && !IsInEngagement && !IsComplete;

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

        // Ordinary movement can chain through multiple dice in one turn
        // (up to all 5). Movement only locks when an engagement triggers
        // (immediately, no choice) or when the player chooses to enter the
        // market (EnterMarket) after landing on a planet/starport.
        public MoveResult Move(RolledDie die, HexCoordinate to, Random rng)
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't move mid-engagement.");
            if (currentHand == null)
                throw new InvalidOperationException("Roll dice before moving.");
            if (movementLocked)
                throw new InvalidOperationException("Movement has already ended for this turn.");

            var result = ShipMover.TryMove(Map, die, CurrentPlayer.Position, to);
            if (!result.Success)
                return result;

            CurrentPlayer.Position = result.NewPosition;

            var session = EngagementTrigger.TryTrigger(CurrentPlayer, Map, rng);
            if (session != null)
            {
                activeEngagement = session;
                movementLocked = true; // engagements always end movement immediately
            }
            // else: movement stays open — the player can keep spending
            // dice, whether they're on ordinary terrain or a planet.

            return result;
        }

        // Choosing to shop ends movement for the rest of the turn.
        public void EnterMarket()
        {
            EnsureMatchInProgress();
            if (!CanShop)
                throw new InvalidOperationException("Can't enter the market right now.");

            movementLocked = true;
        }

        // Choosing to attack the other player ends movement for the rest
        // of the turn, same as any other engagement. Reuses the PvE round
        // mechanics against the opponent's real (persistent) ship rather
        // than a generated one — trading and mutual escape are still
        // deferred, tracked separately.
        public void AttackOpponent()
        {
            EnsureMatchInProgress();
            if (!CanAttackOpponent)
                throw new InvalidOperationException("There's no opponent to attack here.");

            var definition = new EngagementDefinition(
                EngagementTier.None, hullRange: (0, 0), weaponsRange: (0, 0), shieldsRange: (0, 0), speedRange: (0, 0));
            activeEngagement = new EngagementSession(definition, CurrentPlayer, OtherPlayer.Ship, isPvP: true);
            movementLocked = true;
        }

        // Call once ActiveEngagement.Outcome has left InProgress, to clear
        // the marker and apply the outcome's follow-on effects. Movement
        // already ended the instant the engagement began, so an escape
        // doesn't reopen it for the player to spend a die on — it
        // relocates them to a random adjacent hex on the spot instead.
        public void ResolveActiveEngagement(Random rng)
        {
            if (activeEngagement == null || activeEngagement.Outcome == EngagementOutcome.InProgress)
                throw new InvalidOperationException("There's no resolved engagement to finalize.");

            var wasPvP = activeEngagement.IsPvP;

            // Only defeating the opponent removes it from the map — on a
            // loss or an escape, nothing was actually resolved, so the
            // marker (and whatever's guarding it) stays for a future
            // encounter, by either player.
            if (!wasPvP && activeEngagement.Outcome == EngagementOutcome.PlayerWon)
                EngagementTrigger.ClearMarker(Map, CurrentPlayer.Position);
            hadEngagementThisTurn = true;

            if (activeEngagement.Outcome == EngagementOutcome.PlayerLost)
            {
                IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map);
            }
            else if (activeEngagement.Outcome == EngagementOutcome.PlayerEscaped)
            {
                RelocateAfterEscape(rng);
            }

            // The opponent is a real, persistent player here — a PvP loss
            // can deplete their Hull too, so they get the same zero-Hull
            // relocation/penalty the active player would.
            if (wasPvP)
                IntegrityPenaltyService.ApplyIfDepleted(OtherPlayer, Map);

            if (CurrentPlayer.HasWonMatch)
                Winner = CurrentPlayer;

            activeEngagement = null;
        }

        public bool CanEndTurn => !IsInEngagement && !IsComplete;

        public void EndTurn()
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't end turn mid-encounter.");

            currentHand = null; // unspent dice are discarded, no carryover
            movementLocked = false;
            hadEngagementThisTurn = false;
            CurrentPlayer = CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;
        }

        // Picks a random neighboring hex with no engagement marker of its
        // own — landing straight back into another ambush would defeat the
        // point of escaping. Leaves the player in place if every neighbor
        // is either off the map or itself marked (e.g. boxed in).
        private void RelocateAfterEscape(Random rng)
        {
            var candidates = Map.GetNeighborCoordinates(CurrentPlayer.Position)
                .Where(coordinate => Map.TryGetHex(coordinate, out var hex) && !hex.HasEngagement)
                .ToList();

            if (candidates.Count > 0)
                CurrentPlayer.Position = candidates[rng.Next(candidates.Count)];
        }

        private void EnsureMatchInProgress()
        {
            if (IsComplete)
                throw new InvalidOperationException("This match has already ended.");
        }
    }
}
