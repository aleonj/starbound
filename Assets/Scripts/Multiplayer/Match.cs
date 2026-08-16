using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Movement;
using StarBound.Shop;

namespace StarBound.Multiplayer
{
    // Ties movement, engagement triggering, and turn-passing together for
    // a single 1v1 match. Mode-agnostic: usable directly for pass-and-play,
    // and by a future networked session driving the same state.
    public class Match
    {
        // A turn is exactly two actions, drawn freely from Move,
        // Shop/Deliver/Repair (bundled as one "market visit"), Job Board,
        // Mine, Attack, Wormhole Travel, or Trade — in any order. Move and
        // the market bundle are "session" actions: chaining several dice,
        // or making several purchases, in one uninterrupted run is still
        // just one action — but the moment a DIFFERENT action happens, that
        // session closes for the rest of the turn, even if it means giving
        // up an action you'd already paid for (e.g. move, then shop —
        // movement doesn't reopen afterward). Job Board is deliberately
        // NOT part of the market session — buying something and accepting
        // a job are two separate actions, not one bundled visit. Mine,
        // Attack, and Trade are each a simple one-shot spend that also
        // closes out whatever session was open. Using a consumable item is
        // deliberately NOT in this budget at all — see CanUseItem.
        public const int ActionsPerTurn = 2;

        private enum ActionSession { None, Move, Market }

        // Successful (non-PvP) engagement wins accumulate toward the next
        // progression event, which introduces a new ActiveVariable + ActiveGoal
        // and — once that goal is completed by either player — raises
        // MaxUnlockedTier. See MatchProgressionService and HandleArrival/
        // ResolveActiveEngagement for where each piece is actually wired in.
        private const int EngagementsPerProgressionEvent = 2;

        private DiceHand currentHand;
        private EngagementSession activeEngagement;
        private int actionsUsed;
        private ActionSession openSession = ActionSession.None;
        private int successfulEngagementsSinceLastEvent;

        public GameMap Map { get; }
        public Player PlayerOne { get; }
        public Player PlayerTwo { get; }
        public Player CurrentPlayer { get; private set; }
        public Player Winner { get; private set; }

        // Starts at 1, incremented on every EndTurn (either player's) —
        // used by PlanetShopService to gate a purchased shop slot's
        // refill to "not the same turn it was bought," regardless of
        // which planet or which player is asking.
        public int TurnNumber { get; private set; } = 1;

        // Progression state — see the EngagementsPerProgressionEvent doc
        // comment above. MaxUnlockedTier starts at Easy regardless of the
        // match's overall Difficulty (a separate, map-generation-only
        // concept): a Medium/Hard marker can already exist on the map from
        // generation, but stays inert (see HandleArrival) until unlocked.
        public EngagementTier MaxUnlockedTier { get; private set; } = EngagementTier.Easy;
        public MatchVariable ActiveVariable { get; private set; } = MatchVariable.None;
        public MatchGoal ActiveGoal { get; private set; }

        // Directly advances the phase, skipping the usual "win an event's
        // goal" path — same category of direct state setup as
        // Player.RecordEngagementWin: a legitimate way to seed a match at
        // a later phase (tooling, or a test that wants a Medium/Hard
        // marker to trigger without first replaying the whole approach to
        // it) without needing to fake a goal completion. Never moves the
        // ceiling backward.
        public void UnlockTier(EngagementTier tier)
        {
            if (tier > MaxUnlockedTier)
                MaxUnlockedTier = tier;
        }

        public Match(GameMap map, Player playerOne, Player playerTwo)
        {
            Map = map;
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            CurrentPlayer = playerOne;
        }

        public DiceHand CurrentHand => currentHand;
        public EngagementSession ActiveEngagement => activeEngagement;
        public int ActionsRemaining => ActionsPerTurn - actionsUsed;

        // True from the moment an engagement starts until it's explicitly
        // resolved via ResolveActiveEngagement — deliberately NOT tied to
        // the session's Outcome still being InProgress, so that a decided
        // but unacknowledged outcome (win/loss/escape) still blocks other
        // actions until the caller finalizes it.
        public bool IsInEngagement => activeEngagement != null;
        public bool IsComplete => Winner != null;

        // Free to keep spending dice while the Move session is still open
        // (nothing else has happened since); otherwise needs a fresh
        // action, and once a DIFFERENT session has opened (e.g. shopping),
        // Move can't resume even with actions left over (see
        // ConsumeActionForSession).
        public bool CanMove =>
            !IsInEngagement && !IsComplete && currentHand != null &&
            (openSession == ActionSession.Move || actionsUsed < ActionsPerTurn);

        // Not gated on having rolled dice — a player who doesn't intend to
        // move at all this turn shouldn't be forced to roll movement dice
        // first just to shop. The action budget itself is what prevents a
        // "free" turn, not a dice-roll prerequisite. Covers the bundled
        // market visit (Shop, Deliver, Repair) — same session-based
        // reasoning as CanMove. Job Board is separate — see CanAcceptJob.
        public bool CanShop =>
            !IsInEngagement && !IsComplete &&
            (openSession == ActionSession.Market || actionsUsed < ActionsPerTurn);

        // A standalone one-shot action, not bundled with the Shop session —
        // buying something and accepting a job are two separate actions.
        public bool CanAcceptJob =>
            !IsInEngagement && !IsComplete && actionsUsed < ActionsPerTurn;

        public bool IsCurrentPlayerOnPlanet =>
            Map.TryGetHex(CurrentPlayer.Position, out var hex) && hex.Terrain == TerrainType.PlanetOrStarport;

        public Player OtherPlayer => CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;

        // Landing on the other player's hex is a standing opportunity, not
        // a forced trigger — the mover can attack or simply ignore it and
        // keep playing, so this stays a plain condition check rather than
        // something Move() resolves automatically.
        public bool IsOnOpponentHex => CurrentPlayer.Position == OtherPlayer.Position;

        public bool CanAttackOpponent =>
            IsOnOpponentHex && !IsInEngagement && !IsComplete && actionsUsed < ActionsPerTurn;

        // Deliberately NOT gated by !IsInEngagement or the action budget —
        // using a consumable (e.g. a repair kit) mid-fight is the whole
        // point, and it would usually be unusable there otherwise, since
        // entering the fight already spends the whole remaining budget.
        public bool CanUseItem(ItemDefinition item) =>
            !IsComplete && item.Kind == ItemKind.Consumable && CurrentPlayer.Ship.HeldItems.Contains(item);

        public bool CanTradeWithOpponent(ItemDefinition item) =>
            IsOnOpponentHex && !IsInEngagement && !IsComplete && actionsUsed < ActionsPerTurn &&
            CurrentPlayer.Ship.HeldItems.Contains(item) && OtherPlayer.Ship.CanHoldAnotherItem &&
            OtherPlayer.Ship.Money >= item.Price / 2;

        // Mining is two steps: mine at any Asteroids field first, then
        // deliver — a delivery isn't possible until the cargo's mined. Not
        // gated on having rolled dice, same reasoning as CanShop — mining
        // doesn't spend a die, so there's no need to force a roll first.
        public bool CanMineAsteroid =>
            !IsInEngagement && !IsComplete && actionsUsed < ActionsPerTurn &&
            CurrentPlayer.ActiveJob is { Type: JobType.Mining } && !CurrentPlayer.HasMinedCargo &&
            Map.TryGetHex(CurrentPlayer.Position, out var hex) && hex.Terrain == TerrainType.Asteroids;

        public bool CanDeliverJob =>
            CanShop && CurrentPlayer.ActiveJob is { } job &&
            job.Type != JobType.BountyHunting && job.Destination == CurrentPlayer.Position &&
            (job.Type != JobType.Mining || CurrentPlayer.HasMinedCargo);

        // A standing movement option for anyone holding the Wormhole
        // Device — deliberately NOT tied to standing on a wormhole hex or
        // to having rolled a matching die: making an expensive device
        // purchase depend on the rare Wormhole die face meant it could sit
        // unusable for turns at a time. Without the device it's never
        // available at all. Shares the Move session (see CanMove) since
        // this is just another way of spending your movement, not a
        // separate action category — free to chain with dice-based moves
        // in either order within one continuous session.
        public bool CanTravelWormhole =>
            !IsInEngagement && !IsComplete &&
            (openSession == ActionSession.Move || actionsUsed < ActionsPerTurn) &&
            CurrentPlayer.Ship.HeldItems.Contains(ItemPool.WormholeDevice);

        public IEnumerable<HexCoordinate> OtherWormholeDestinations =>
            Map.Hexes.Where(h => h.Terrain == TerrainType.Wormhole && h.Coordinate != CurrentPlayer.Position)
                .Select(h => h.Coordinate);

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
        // (up to all 5) while the Move session stays open — see CanMove.
        public MoveResult Move(RolledDie die, HexCoordinate to, Random rng)
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't move mid-engagement.");
            if (currentHand == null)
                throw new InvalidOperationException("Roll dice before moving.");
            if (!CanMove)
                throw new InvalidOperationException("Movement has already ended for this turn.");

            var result = ShipMover.TryMove(Map, die, CurrentPlayer, to, waiveTradelaneToll: ActiveVariable == MatchVariable.TradeBoom);
            if (!result.Success)
                return result;

            CurrentPlayer.Position = result.NewPosition;
            ConsumeActionForSession(ActionSession.Move);
            HandleArrival(result.NewPosition, rng);

            return result;
        }

        // Warps the player directly to any other wormhole hex on the map
        // (see CanTravelWormhole) — no adjacency, no current-position
        // requirement. Part of the Move session, so it's free to mix with
        // dice-based moves, in whichever order, within one turn.
        public void TravelToWormhole(HexCoordinate destination, Random rng)
        {
            EnsureMatchInProgress();
            if (!CanTravelWormhole)
                throw new InvalidOperationException("Can't travel by wormhole right now.");
            if (!Map.TryGetHex(destination, out var destinationHex) ||
                destinationHex.Terrain != TerrainType.Wormhole || destination == CurrentPlayer.Position)
                throw new InvalidOperationException("That's not a valid wormhole destination.");

            CurrentPlayer.Position = destination;
            ConsumeActionForSession(ActionSession.Move);
            HandleArrival(destination, rng);
        }

        // Shared by Move and TravelToWormhole so arriving at a hex behaves
        // identically regardless of how the player got there: mark an
        // engagement hex as discovered by this player (see
        // Player.DiscoverHex — this is what makes it show on their view of
        // the map), then trigger combat if the hex is guarded.
        private void HandleArrival(HexCoordinate position, Random rng)
        {
            Map.TryGetHex(position, out var hex);

            // A marker above the current phase stays fully hidden, not
            // just non-triggering — discovering it here (and so revealing
            // its marker icon) with no way to actually engage it would
            // just be confusing. It becomes discoverable the moment its
            // tier is within MaxUnlockedTier, same visit or a later one.
            if (hex != null && hex.HasEngagement && hex.Engagement <= MaxUnlockedTier)
                CurrentPlayer.DiscoverHex(position);

            // Minefield Damage variable — applied against the arrival hex
            // itself, before any relocation a depleted Hull might cause
            // (see IntegrityPenaltyService's documented "invoke after
            // anything that can deplete Hull/Energy" contract).
            if (hex != null && hex.Terrain == TerrainType.Mines && ActiveVariable == MatchVariable.MinefieldDamage)
            {
                CurrentPlayer.Ship.ApplyStatDelta(CoreStat.Hull, -1);
                IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map);
            }

            TryCompleteTravelAndPayGoal();

            // No exemption needed here for a DefeatNamedTarget goal's own
            // marker — it's placed at the player's current MaxUnlockedTier
            // (see MatchProgressionService), so it's always already within
            // the normal gating ceiling.
            var session = EngagementTrigger.TryTrigger(CurrentPlayer, Map, rng, MaxUnlockedTier);
            if (session != null)
            {
                activeEngagement = session;
                ExhaustActions(); // an ambush spends the whole remaining budget
            }
        }

        // First player to be standing on the goal's hex with enough money
        // on hand claims it immediately on arrival — no separate "deliver"
        // action, unlike jobs.
        private void TryCompleteTravelAndPayGoal()
        {
            if (ActiveGoal is not { Type: MatchGoalType.TravelAndPay } goal)
                return;
            if (CurrentPlayer.Position != goal.TargetHex || CurrentPlayer.Ship.Money < goal.MoneyRequired)
                return;

            CurrentPlayer.Ship.TrySpendMoney(goal.MoneyRequired);
            CompleteGoal(CurrentPlayer);
        }

        // Pays the reward, advances the progression phase, and clears the
        // goal — ActiveVariable deliberately stays as-is, since it persists
        // until the *next* event replaces it, not until the goal is claimed.
        private void CompleteGoal(Player awardee)
        {
            awardee.Ship.AddMoney(ActiveGoal.RewardMoney);
            MaxUnlockedTier = NextUnlockTier();
            ActiveGoal = null;
        }

        private EngagementTier NextUnlockTier() => (EngagementTier)((int)MaxUnlockedTier + 1);

        // Called only for a real (non-PvP) engagement win — see
        // ResolveActiveEngagement. Two independent things can happen on
        // the same win: it might be the one that finally defeats the
        // active DefeatNamedTarget goal, and/or it might be the one that
        // crosses the threshold for a brand new event to fire (only once
        // no goal is currently active and there's still a tier left to
        // unlock).
        private void HandleProgressionOnEngagementWin(HexCoordinate resolvedHex, Random rng)
        {
            successfulEngagementsSinceLastEvent++;

            if (ActiveGoal is { Type: MatchGoalType.DefeatNamedTarget } goal && resolvedHex == goal.TargetHex)
                CompleteGoal(CurrentPlayer);

            if (ActiveGoal == null && MaxUnlockedTier < EngagementTier.Hard &&
                successfulEngagementsSinceLastEvent >= EngagementsPerProgressionEvent)
            {
                var (variable, newGoal) = MatchProgressionService.FireEvent(
                    Map, PlayerOne.Position, PlayerTwo.Position, MaxUnlockedTier, rng);
                ActiveVariable = variable;
                ActiveGoal = newGoal;
                successfulEngagementsSinceLastEvent = 0;
            }
        }

        // Choosing to attack the other player spends the whole remaining
        // budget, same as any other engagement (see HandleArrival) —
        // combat blocks everything else anyway via IsInEngagement, and
        // once it resolves the turn is effectively over. Reuses the PvE
        // round mechanics against the opponent's real (persistent) ship
        // rather than a generated one — mutual escape (the opponent
        // getting to act too) is still deferred, tracked separately. Item
        // trading (TradeItemToOpponent) is a separate standing action
        // while sharing a hex, not tied to choosing Attack.
        public void AttackOpponent()
        {
            EnsureMatchInProgress();
            if (!CanAttackOpponent)
                throw new InvalidOperationException("There's no opponent to attack here.");

            var definition = new EngagementDefinition(
                EngagementTier.None, hullRange: (0, 0), weaponsRange: (0, 0), shieldsRange: (0, 0), speedRange: (0, 0));
            activeEngagement = new EngagementSession(definition, CurrentPlayer, OtherPlayer.Ship, isPvP: true);
            ExhaustActions();
        }

        // Using a consumable applies its effect immediately and destroys
        // it — permitted mid-engagement and never costs an action (see
        // CanUseItem).
        public void UseItem(ItemDefinition item)
        {
            EnsureMatchInProgress();
            if (!CanUseItem(item))
                throw new InvalidOperationException("Can't use that item right now.");

            CurrentPlayer.Ship.UseConsumableItem(item);
        }

        // The receiving player pays half the item's price — same rate as
        // selling it to the market, just to a player instead. No
        // negotiation UI; this is a flat, always-available offer. A
        // simple one-shot action — a second trade the same turn spends
        // the other action slot too.
        public void TradeItemToOpponent(ItemDefinition item)
        {
            EnsureMatchInProgress();
            if (!CanTradeWithOpponent(item))
                throw new InvalidOperationException("Can't trade that item right now.");

            var price = item.Price / 2;
            OtherPlayer.Ship.TrySpendMoney(price);
            CurrentPlayer.Ship.TryRemoveItem(item);
            OtherPlayer.Ship.TryAddItem(item);
            CurrentPlayer.Ship.AddMoney(price);
            ConsumeAction();
        }

        // Mining the cargo is a simple one-shot action — it doesn't pay
        // out by itself, it just unlocks delivery at the job's
        // destination.
        public void MineAsteroid()
        {
            EnsureMatchInProgress();
            if (!CanMineAsteroid)
                throw new InvalidOperationException("There's no cargo to mine here.");

            CurrentPlayer.MarkCargoMined();
            ConsumeAction();
        }

        // Buying/selling/repairing/delivering a job are all part of the
        // same bundled "market visit" action (see CanShop) — the first
        // successful one pays for the turn's market action, the rest are
        // free for the rest of the turn. Each wrapper below only pays on
        // an actual success, so browsing or a failed attempt (insufficient
        // funds, cargo full, etc.) costs nothing.
        public PurchaseResult BuyItem(ItemDefinition item)
        {
            EnsureMatchInProgress();
            if (!CanShop)
                throw new InvalidOperationException("Can't shop right now.");

            var result = ShopService.TryPurchase(CurrentPlayer.Ship, item);
            if (result.Success)
            {
                ConsumeActionForSession(ActionSession.Market);

                // The Wormhole Device is a standing purchase outside the
                // random pool/offer (see ItemPool), so it never touches a
                // planet's persistent shelf.
                if (item != ItemPool.WormholeDevice &&
                    Map.TryGetHex(CurrentPlayer.Position, out var hex) &&
                    hex.Terrain == TerrainType.PlanetOrStarport)
                {
                    PlanetShopService.RecordPurchase(hex, item, TurnNumber);
                }
            }
            return result;
        }

        // Single source of truth for a planet's shop shelf — see
        // PlanetShopService. The HUD calls this instead of rolling its
        // own offer, so purchases and refill timing stay consistent
        // regardless of who's asking or how many times the panel is
        // opened and closed.
        public IReadOnlyList<ItemDefinition> GetShopOffer(Random rng)
        {
            if (!Map.TryGetHex(CurrentPlayer.Position, out var hex) || hex.Terrain != TerrainType.PlanetOrStarport)
                throw new InvalidOperationException("Not standing on a planet.");

            return PlanetShopService.GetOffer(hex, TurnNumber, rng);
        }

        public SellResult SellItem(ItemDefinition item)
        {
            EnsureMatchInProgress();
            if (!CanShop)
                throw new InvalidOperationException("Can't shop right now.");

            var result = ShopService.TrySell(CurrentPlayer.Ship, item);
            if (result.Success)
                ConsumeActionForSession(ActionSession.Market);
            return result;
        }

        public RepairResult RepairStat(CoreStat stat)
        {
            EnsureMatchInProgress();
            if (!CanShop)
                throw new InvalidOperationException("Can't shop right now.");

            var result = RepairService.TryRepairOnePoint(CurrentPlayer.Ship, stat);
            if (result.Success)
                ConsumeActionForSession(ActionSession.Market);
            return result;
        }

        // A standalone one-shot action — see CanAcceptJob.
        public AcceptJobResult AcceptJob(JobDefinition job)
        {
            EnsureMatchInProgress();
            if (!CanAcceptJob)
                throw new InvalidOperationException("Can't visit the job board right now.");

            var result = JobService.TryAcceptJob(CurrentPlayer, job);
            if (result.Success)
                ConsumeAction();
            return result;
        }

        // Delivering a mining/transport job pays out — part of the
        // bundled market action, same as buying/selling/accepting a job.
        public void DeliverJob()
        {
            EnsureMatchInProgress();
            if (!CanDeliverJob)
                throw new InvalidOperationException("There's no job to deliver here.");

            CurrentPlayer.Ship.AddMoney(CurrentPlayer.ActiveJob.Reward);
            CurrentPlayer.ClearActiveJob();
            ConsumeActionForSession(ActionSession.Market);
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
            var resolvedHex = CurrentPlayer.Position; // captured before RelocateAfterEscape can move the player

            // Only defeating the opponent removes it from the map — on a
            // loss or an escape, nothing was actually resolved, so the
            // marker (and whatever's guarding it) stays for a future
            // encounter, by either player.
            if (!wasPvP && activeEngagement.Outcome == EngagementOutcome.PlayerWon)
            {
                EngagementTrigger.ClearMarker(Map, CurrentPlayer.Position);
                HandleProgressionOnEngagementWin(resolvedHex, rng);
            }

            if (activeEngagement.Outcome == EngagementOutcome.PlayerLost)
            {
                IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map);
            }
            else if (activeEngagement.Outcome == EngagementOutcome.PlayerEscaped)
            {
                RelocateAfterEscape(rng);
            }

            // Skipped for PvP: a bounty job targets a specific marked NPC
            // hex, never the other player, even if they happen to share it.
            if (!wasPvP)
                JobService.ResolveBountyOutcome(CurrentPlayer, resolvedHex, activeEngagement.Outcome);

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
            actionsUsed = 0;
            openSession = ActionSession.None;
            CurrentPlayer = CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;
            TurnNumber++;
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

        // For the simple one-shot actions (Mine, Trade, Job Board) — always
        // spends a fresh action and closes out any open Move/Market
        // session, since doing something else ends it.
        private void ConsumeAction()
        {
            if (actionsUsed < ActionsPerTurn)
                actionsUsed++;
            openSession = ActionSession.None;
        }

        // For the two "session" categories (Move, Market) — free to
        // continue if this same session is already open; otherwise spends
        // a fresh action and opens it, implicitly closing whatever
        // session (if any) was open before. See CanMove/CanShop.
        private void ConsumeActionForSession(ActionSession session)
        {
            if (openSession == session)
                return;

            if (actionsUsed < ActionsPerTurn)
                actionsUsed++;
            openSession = session;
        }

        // For engagements (see AttackOpponent/HandleArrival) — spends
        // whatever's left of the budget outright and closes any open
        // session, regardless of how much had already been spent.
        private void ExhaustActions()
        {
            actionsUsed = ActionsPerTurn;
            openSession = ActionSession.None;
        }

        private void EnsureMatchInProgress()
        {
            if (IsComplete)
                throw new InvalidOperationException("This match has already ended.");
        }
    }
}
