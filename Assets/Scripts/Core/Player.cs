using System;
using System.Collections.Generic;

namespace StarBound.Core
{
    public class Player
    {
        public const int HardWinsToVictory = 3;

        public string Id { get; }
        public string DisplayName { get; }
        public Ship Ship { get; }
        public HexCoordinate Position { get; set; }

        public int EasyEngagementWins { get; private set; }
        public int MediumEngagementWins { get; private set; }
        public int HardEngagementWins { get; private set; }

        public bool HasWonMatch => HardEngagementWins >= HardWinsToVictory;

        // One job at a time — a separate slot from Ship's item cargo,
        // tracking whichever mining/transport/bounty contract the player
        // has accepted from a job board.
        public JobDefinition ActiveJob { get; private set; }

        // Mining jobs are two steps, not just a delivery: the ore has to
        // actually be mined at an Asteroids field before it can be
        // delivered. Tracked here rather than on JobDefinition itself,
        // since JobDefinition is an immutable spec and this is progress
        // toward completing it.
        public bool HasMinedCargo { get; private set; }

        // Per-player discovery: an engagement marker only shows on this
        // player's view of the map once they've personally landed on it.
        // The opponent must discover the same hex independently.
        private readonly HashSet<HexCoordinate> discoveredEngagementHexes = new();
        public IReadOnlyCollection<HexCoordinate> DiscoveredEngagementHexes => discoveredEngagementHexes;
        public void DiscoverHex(HexCoordinate coordinate) => discoveredEngagementHexes.Add(coordinate);

        // Set by IntegrityPenaltyService when this player's ship is
        // destroyed. Can't just be surfaced immediately like other
        // engagement messages — a PvP loss can deplete the OTHER player's
        // Hull, who isn't necessarily the one looking at the device right
        // now — so it waits here until this player's own turn actually
        // starts (see MatchHud.OnHandoffConfirmed).
        public string PendingTurnStartNotice { get; private set; }

        public void SetPendingTurnStartNotice(string message) => PendingTurnStartNotice = message;

        public string ConsumePendingTurnStartNotice()
        {
            var notice = PendingTurnStartNotice;
            PendingTurnStartNotice = null;
            return notice;
        }

        // A separate one-shot slot from PendingTurnStartNotice above, not
        // a reuse of it — a tier unlock is match-global (see
        // Match.MaxUnlockedTier/CompleteGoal) and can land on a turn that
        // ALSO sets this player's ordinary notice (e.g. a PvP win as one
        // action, a goal-completing engagement as the other, within the
        // same turn's two-action budget — see Match.ActionsPerTurn).
        // Sharing one slot would let the second overwrite the first
        // before either was ever shown.
        public string PendingTierUnlockNotice { get; private set; }

        public void SetPendingTierUnlockNotice(string message) => PendingTierUnlockNotice = message;

        public string ConsumePendingTierUnlockNotice()
        {
            var notice = PendingTierUnlockNotice;
            PendingTierUnlockNotice = null;
            return notice;
        }

        // A third, independent one-shot slot — same reasoning as
        // PendingTierUnlockNotice's own comment, a new MatchVariable
        // event is ALSO match-global (see Match.HandleProgressionOnEngagementWin)
        // and can coincide with either of the other two within one
        // turn's two-action budget.
        public string PendingVariableEventNotice { get; private set; }

        public void SetPendingVariableEventNotice(string message) => PendingVariableEventNotice = message;

        public string ConsumePendingVariableEventNotice()
        {
            var notice = PendingVariableEventNotice;
            PendingVariableEventNotice = null;
            return notice;
        }

        // A fourth, independent one-shot slot — same reasoning as
        // PendingVariableEventNotice above. A race goal firing
        // (Match.HandleProgressionOnEngagementWin) was previously never
        // announced at all, only shown as a passive status label (the
        // same gap MatchVariable had before it got PendingVariableEventNotice).
        public string PendingGoalNotice { get; private set; }

        public void SetPendingGoalNotice(string message) => PendingGoalNotice = message;

        public string ConsumePendingGoalNotice()
        {
            var notice = PendingGoalNotice;
            PendingGoalNotice = null;
            return notice;
        }

        // Also set by IntegrityPenaltyService alongside the notice above,
        // whenever a destroyed ship's Position changes — consumed (and
        // cleared) by the map view so it can skip the normal glide-
        // across-the-map travel animation for this specific position
        // change and play a vanish/materialize effect at the new hex
        // instead. A destroyed ship didn't fly there; a fresh one
        // appeared — gliding there reads as "they survived," which is
        // backwards.
        public bool JustTeleported { get; private set; }

        public void MarkTeleported() => JustTeleported = true;

        public bool ConsumeTeleported()
        {
            var value = JustTeleported;
            JustTeleported = false;
            return value;
        }

        public Player(string id, string displayName, Ship ship)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Player id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Player display name is required.", nameof(displayName));

            Id = id;
            DisplayName = displayName;
            Ship = ship ?? throw new ArgumentNullException(nameof(ship));
        }

        public void RecordEngagementWin(EngagementTier tier)
        {
            switch (tier)
            {
                case EngagementTier.Easy:
                    EasyEngagementWins++;
                    break;
                case EngagementTier.Medium:
                    MediumEngagementWins++;
                    break;
                case EngagementTier.Hard:
                    HardEngagementWins++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), "Cannot record a win for EngagementTier.None.");
            }
        }

        public void AcceptJob(JobDefinition job)
        {
            if (ActiveJob != null)
                throw new InvalidOperationException("Already have an active job.");

            ActiveJob = job ?? throw new ArgumentNullException(nameof(job));
            HasMinedCargo = false;
        }

        public void ClearActiveJob()
        {
            ActiveJob = null;
            HasMinedCargo = false;
        }

        public void MarkCargoMined()
        {
            if (ActiveJob is not { Type: JobType.Mining })
                throw new InvalidOperationException("No active Mining job to mine cargo for.");

            HasMinedCargo = true;
        }
    }
}
