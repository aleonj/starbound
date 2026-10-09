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
        // and — once that goal is completed by either player, AND both
        // players' Weapons/Shields/Speed clear the next tier's floor (see
        // CompleteGoal/IsReadyForTier) — raises MaxUnlockedTier. See
        // MatchProgressionService and HandleArrival/ResolveActiveEngagement
        // for where each piece is actually wired in.
        private const int EngagementsPerProgressionEvent = 2;

        // Variable events (MatchVariable) fire on their OWN independent
        // win-counter now — decoupled from goal events (see
        // HandleProgressionOnEngagementWin's own comment for why: they
        // used to be bundled into one combined event, tying unrelated
        // mechanics to the same cadence, and tying event turnover to
        // goal pacing meant a variable could get "stuck" active for the
        // whole rest of the match once Hard tier unlocked and goal
        // events stopped firing). Same starting value as goals, tunable
        // independently.
        private const int EngagementsPerVariableEvent = 2;

        // [Combat] Energy overhaul — the forced floor-reset after a
        // 0-Energy turn (see EndTurn) is a deliberate fixed value, NOT
        // derived from the ordinary banking formula: at 0 usable dice
        // every rolled die is already "unused" by construction, which
        // would otherwise bank the full Ship.MaxEnergyValue in one go
        // and erase the intended one-turn cost. A smaller explicit
        // floor also avoids a soft-lock — 0 usable dice means the
        // player can never CHOOSE to under-use dice to recover through
        // banking alone.
        private const int EnergyFloorResetValue = 2;

        private DiceHand currentHand;
        private EngagementSession activeEngagement;
        private TradeNegotiation activeTradeNegotiation;
        private int actionsUsed;
        private ActionSession openSession = ActionSession.None;
        // The Move action can now be paid up front by RollDice, before any
        // actual move happens — moveSessionPaid tracks that payment
        // independent of openSession, since a later, different session
        // (e.g. shopping) overwrites openSession without un-paying it.
        // hasMovedThisTurn distinguishes "paid but not yet used" from
        // "already used": a still-unused roll survives being interrupted
        // by something else (rolling was the commitment — see RollDice),
        // but once movement has actually happened, a later, different
        // session closes it back out for the rest of the turn, same as it
        // always has (see BuyItem_AfterMoving_ClosesTheMoveSessionEvenWith-
        // NoActionsSpentOnItAgain) — see ConsumeActionForSession.
        private bool moveSessionPaid;
        private bool hasMovedThisTurn;
        private int successfulEngagementsSinceLastEvent;
        private int successfulEngagementsSinceLastVariableEvent;

        // Snapshot of each player's Energy at the moment THEIR turn
        // began — read back at the end of that same turn (see EndTurn)
        // to decide whether this was a forced 0-usable-dice turn and so
        // whether the floor-reset applies instead of ordinary banking.
        // Keyed per-player rather than a single flag since turns
        // alternate between PlayerOne/PlayerTwo.
        private readonly Dictionary<Player, int> energyAtTurnStart = new();

        // Set whenever an engagement starts mid-turn (an ambush via
        // HandleArrival, or AttackOpponent) — both forcibly ExhaustActions,
        // cutting the turn short through no choice of the player's. Read
        // (and reset) by EndTurn's own banking step: dice still unspent
        // at that point were never a genuine "I could have kept moving
        // but chose not to," so they shouldn't bank Energy the way a
        // deliberately-ended turn with dice to spare would — see
        // ApplyEnergyForEndingTurn.
        private bool engagementOccurredThisTurn;

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

            energyAtTurnStart[playerOne] = playerOne.Ship.GetStat(CoreStat.Energy);
            energyAtTurnStart[playerTwo] = playerTwo.Ship.GetStat(CoreStat.Energy);
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

        public TradeNegotiation ActiveTradeNegotiation => activeTradeNegotiation;

        // True from ProposeTrade until the negotiation is resolved via
        // AcceptTrade/RejectTrade — blocks everything else the same way
        // IsInEngagement does (see the sweep across CanMove/CanShop/etc.
        // below), since it's a modal, device-hand-off-driven exchange with
        // the opponent, not something that can happen alongside normal
        // play.
        public bool IsNegotiatingTrade => activeTradeNegotiation != null;

        // Free to move once the Move action is paid for — via RollDice, an
        // earlier Move, or a wormhole jump this turn, whichever came first
        // (see moveSessionPaid) — regardless of what else has happened
        // since; otherwise needs a fresh action. Rolling is the actual
        // commitment now (see RollDice's own comment): once you've rolled,
        // using the dice never costs anything more or gets blocked by
        // doing something else first.
        public bool CanMove =>
            !IsInEngagement && !IsComplete && !IsNegotiatingTrade && currentHand != null &&
            (moveSessionPaid || actionsUsed < ActionsPerTurn);

        // Not gated on having rolled dice — a player who doesn't intend to
        // move at all this turn shouldn't be forced to roll movement dice
        // first just to shop. The action budget itself is what prevents a
        // "free" turn, not a dice-roll prerequisite. Covers the bundled
        // market visit (Shop, Deliver, Repair) — same session-based
        // reasoning as CanMove. Job Board is separate — see CanAcceptJob.
        public bool CanShop =>
            !IsInEngagement && !IsComplete && !IsNegotiatingTrade &&
            (openSession == ActionSession.Market || actionsUsed < ActionsPerTurn);

        // A standalone one-shot action, not bundled with the Shop session —
        // buying something and accepting a job are two separate actions.
        public bool CanAcceptJob =>
            !IsInEngagement && !IsComplete && !IsNegotiatingTrade && actionsUsed < ActionsPerTurn;

        public bool IsCurrentPlayerOnPlanet =>
            Map.TryGetHex(CurrentPlayer.Position, out var hex) && hex.Terrain == TerrainType.PlanetOrStarport;

        public Player OtherPlayer => CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;

        // Landing on the other player's hex is a standing opportunity, not
        // a forced trigger — the mover can attack or simply ignore it and
        // keep playing, so this stays a plain condition check rather than
        // something Move() resolves automatically.
        public bool IsOnOpponentHex => CurrentPlayer.Position == OtherPlayer.Position;

        public bool CanAttackOpponent =>
            IsOnOpponentHex && !IsInEngagement && !IsComplete && !IsNegotiatingTrade && actionsUsed < ActionsPerTurn;

        // Deliberately NOT gated by !IsInEngagement or the action budget —
        // using a consumable (e.g. a repair kit) mid-fight is the whole
        // point, and it would usually be unusable there otherwise, since
        // entering the fight already spends the whole remaining budget.
        public bool CanUseItem(ItemDefinition item) =>
            !IsComplete && item.Kind == ItemKind.Consumable && CurrentPlayer.Ship.HeldItems.Contains(item);

        public bool CanTradeWithOpponent(ItemDefinition item) =>
            IsOnOpponentHex && !IsInEngagement && !IsComplete && !IsNegotiatingTrade && actionsUsed < ActionsPerTurn &&
            CurrentPlayer.Ship.HeldItems.Contains(item) && OtherPlayer.Ship.CanHoldAnotherItem &&
            OtherPlayer.Ship.Money >= item.Price / 2;

        // Same base gate CanAttackOpponent/CanTradeWithOpponent already
        // share — a negotiation is just a bigger version of the same
        // standing "sharing a hex" opportunity. Same shape as
        // CanTradeWithOpponent but without a specific item — the actual
        // terms are validated at ProposeTrade time instead, since a
        // proposal isn't about one fixed item.
        public bool CanProposeTrade =>
            IsOnOpponentHex && !IsInEngagement && !IsComplete && !IsNegotiatingTrade && actionsUsed < ActionsPerTurn;

        // Mining is two steps: mine at any Asteroids field first, then
        // deliver — a delivery isn't possible until the cargo's mined. Not
        // gated on having rolled dice, same reasoning as CanShop — mining
        // doesn't spend a die, so there's no need to force a roll first.
        public bool CanMineAsteroid =>
            !IsInEngagement && !IsComplete && !IsNegotiatingTrade && actionsUsed < ActionsPerTurn &&
            CurrentPlayer.ActiveJob is { Type: JobType.Mining } && !CurrentPlayer.HasMinedCargo &&
            Map.TryGetHex(CurrentPlayer.Position, out var hex) && hex.Terrain == TerrainType.Asteroids;

        public bool CanDeliverJob =>
            CanShop && CurrentPlayer.ActiveJob is { } job &&
            job.Type != JobType.BountyHunting && job.Destination == CurrentPlayer.Position &&
            (job.Type != JobType.Mining || CurrentPlayer.HasMinedCargo);

        // The second half of Wormhole travel — warping onward from a
        // Wormhole hex to any other one. Reaching that first hex no longer
        // depends on the rare rolled Wormhole die face: MatchHud grants
        // anyone holding the device a guaranteed synthetic Wormhole-terrain
        // die each roll (see MatchHud.OnRollDiceClicked), which flows
        // through the exact same Move/ShipMover.TryMove pipeline as any
        // other die (including its own WormholeDeviceRequired gate) — this
        // property only governs what happens once they've actually landed
        // there. Shares the Move session (see CanMove) since the whole
        // "move onto the wormhole, then warp elsewhere" sequence is one
        // continuous use of that turn's movement, not a separate action.
        public bool CanTravelWormhole =>
            !IsInEngagement && !IsComplete && !IsNegotiatingTrade &&
            ActiveVariable != MatchVariable.IonStorm &&
            (moveSessionPaid || actionsUsed < ActionsPerTurn) &&
            CurrentPlayer.Ship.HeldItems.Contains(ItemPool.WormholeDevice) &&
            Map.TryGetHex(CurrentPlayer.Position, out var currentHex) && currentHex.Terrain == TerrainType.Wormhole;

        public IEnumerable<HexCoordinate> OtherWormholeDestinations =>
            Map.Hexes.Where(h => h.Terrain == TerrainType.Wormhole && h.Coordinate != CurrentPlayer.Position)
                .Select(h => h.Coordinate);

        // Gated on the action budget — rolling is now what actually opens
        // (and pays for) the Move session, not the first successful move
        // (see RollDice). A 0-budget roll would just hand the player a
        // dice hand CanMove can never legally spend, so it's blocked here
        // rather than silently producing a useless roll. Also gated on
        // Energy (see [Combat] Energy overhaul) — at 0, none of the 5
        // rolled dice would be usable anyway (see ShipMover.TryMove's
        // own Energy check), so rolling is blocked outright with a
        // distinguishable reason rather than handing over a hand of
        // dice that can never legally move.
        public bool CanRollDice =>
            !IsInEngagement && !IsComplete && !IsNegotiatingTrade && currentHand == null && actionsUsed < ActionsPerTurn
            && CurrentPlayer.Ship.GetStat(CoreStat.Energy) > 0;

        public DiceHand RollDice(Random rng)
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't roll movement dice mid-engagement.");
            if (currentHand != null)
                throw new InvalidOperationException("Dice have already been rolled this turn.");
            if (actionsUsed >= ActionsPerTurn)
                throw new InvalidOperationException("No actions remaining to roll dice with.");
            if (CurrentPlayer.Ship.GetStat(CoreStat.Energy) <= 0)
                throw new InvalidOperationException("No Energy left to move with this turn.");

            currentHand = DiceRoller.Roll(rng, Map);
            // Rolling itself now spends the Move session's action —
            // previously only an actual move did (see Move's own
            // ConsumeActionForSession call below), which let a player roll
            // "for free" just to see the dice and, if they didn't like the
            // result, do something else instead without it ever costing
            // them anything. Sharing Move's session means a move made with
            // this hand afterward doesn't pay a second time (see
            // ConsumeActionForSession's same-session guard) — rolling and
            // the move it enables are still just one action together,
            // exactly as before, just charged at roll time instead of at
            // the first successful move.
            ConsumeActionForSession(ActionSession.Move);
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

            var tollPerHex = ActiveVariable switch
            {
                MatchVariable.TradeBoom => 0,
                MatchVariable.FuelShortage => TollPricing.TradelaneTollPerHex * TollPricing.FuelShortageTollMultiplier,
                _ => TollPricing.TradelaneTollPerHex
            };
            var result = ShipMover.TryMove(Map, die, CurrentPlayer, to, tollPerHex);
            if (!result.Success)
                return result;

            CurrentPlayer.Position = result.NewPosition;
            hasMovedThisTurn = true; // see moveSessionPaid's own comment
            ConsumeActionForSession(ActionSession.Move);
            var hazardHit = HandleArrival(result.NewPosition, rng);

            return MoveResult.Succeeded(result.NewPosition, hazardHit);
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
            hasMovedThisTurn = true; // see moveSessionPaid's own comment
            ConsumeActionForSession(ActionSession.Move);
            HandleArrival(destination, rng);
        }

        // Shared by Move and TravelToWormhole so arriving at a hex behaves
        // identically regardless of how the player got there: mark an
        // engagement hex as discovered by this player (see
        // Player.DiscoverHex — this is what makes it show on their view of
        // the map), then trigger combat if the hex is guarded. Returns
        // whether a hazard hit landed, so Move can report it back through
        // MoveResult — TravelToWormhole ignores it (Wormhole terrain
        // never rolls hazard damage, see ApplyHazardDamageIfUnlucky).
        private bool HandleArrival(HexCoordinate position, Random rng)
        {
            Map.TryGetHex(position, out var hex);

            // A marker above the current phase stays fully hidden, not
            // just non-triggering — discovering it here (and so revealing
            // its marker icon) with no way to actually engage it would
            // just be confusing. It becomes discoverable the moment its
            // tier is within MaxUnlockedTier, same visit or a later one.
            if (hex != null && hex.HasEngagement && hex.Engagement <= MaxUnlockedTier)
                CurrentPlayer.DiscoverHex(position);

            // Intrinsic hazard damage — applied against the arrival hex
            // itself, before any relocation a depleted Hull might cause
            // (see IntegrityPenaltyService's documented "invoke after
            // anything that can deplete Hull/Energy" contract).
            var hazardHit = ApplyHazardDamageIfUnlucky(hex, rng);

            TryCompleteTravelAndPayGoal();

            // No exemption needed here for a DefeatNamedTarget goal's own
            // marker — it's placed at the player's current MaxUnlockedTier
            // (see MatchProgressionService), so it's always already within
            // the normal gating ceiling.
            var statBoost = ActiveVariable == MatchVariable.PirateSurge ? EngagementTrigger.PirateSurgeStatBoost : 0;
            var rewardMultiplier = ActiveVariable == MatchVariable.SalvageRush ? EngagementSession.SalvageRushRewardMultiplier : 1.0;
            // Only DefeatNamedTarget, not just "coordinates happen to
            // match" — a TravelAndPay goal's target hex has no exclusion
            // against already-marked hexes, so it COULD coincidentally
            // overlap an ambient engagement, but fighting (and winning)
            // that engagement does nothing toward completing a
            // TravelAndPay goal (only arrival + having enough money
            // does, independent of combat). Tagging the fight screen
            // "Race Goal" there would wrongly imply this fight matters
            // for it. (The hex-info popup's own, separate "This is the
            // race goal's target" line has no such restriction — that's
            // a travel/arrival fact, correct for either goal type.)
            var goalTargetHex = ActiveGoal is { Type: MatchGoalType.DefeatNamedTarget } ? ActiveGoal.TargetHex : (HexCoordinate?)null;
            var session = EngagementTrigger.TryTrigger(CurrentPlayer, Map, rng, MaxUnlockedTier, statBoost, rewardMultiplier, goalTargetHex);
            if (session != null)
            {
                activeEngagement = session;
                engagementOccurredThisTurn = true;
                ExhaustActions(); // an ambush spends the whole remaining budget
            }

            return hazardHit;
        }

        // Asteroids and Mines each carry their own intrinsic damage
        // chance (see HazardChances) — Debris is deliberately not
        // included here, it isn't considered hazardous. Previously Mines
        // only ever damaged a player when the (temporary, event-driven)
        // MinefieldDamage variable happened to be active, meaning most of
        // a match it was harmless, and Asteroids never damaged a player
        // at all. Asteroids stays the less dangerous of the two — it also
        // doubles as a mining opportunity (see CanMineAsteroid), Mines
        // has no upside at all. MinefieldDamage now intensifies the base
        // Mines danger rather than being the sole source of it. Returns
        // whether damage actually landed — callers used to have no way
        // to tell the player it happened at all, so hazard damage was
        // taken completely silently.
        private bool ApplyHazardDamageIfUnlucky(Hex hex, Random rng)
        {
            if (hex == null)
                return false;

            // CalmSpace applies uniformly to both hazard terrains — checked
            // first on each branch so it can't be shadowed by the other
            // terrain-specific variables below.
            var damageChance = hex.Terrain switch
            {
                TerrainType.Asteroids => ActiveVariable switch
                {
                    MatchVariable.CalmSpace => HazardChances.CalmSpaceHazardChance,
                    MatchVariable.AsteroidStorm => HazardChances.AsteroidDamageChanceDuringStorm,
                    _ => HazardChances.AsteroidDamageChance
                },
                TerrainType.Mines => ActiveVariable switch
                {
                    MatchVariable.CalmSpace => HazardChances.CalmSpaceHazardChance,
                    MatchVariable.MinefieldDamage => HazardChances.MinefieldDamageChanceDuringAlert,
                    _ => HazardChances.MinefieldDamageChance
                },
                _ => 0.0
            };

            if (damageChance <= 0.0 || rng.NextDouble() >= damageChance)
                return false;

            CurrentPlayer.Ship.ApplyStatDelta(CoreStat.Hull, -1);
            IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map, rng, out _); // hazard damage has no "winner" to credit
            return true;
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

        // Pays the reward and clears the goal unconditionally, but the
        // ceiling itself only rises if both players are actually ready for
        // it — MaxUnlockedTier is shared match state (see its own doc
        // comment), so one player's gear can't open Hard on the other
        // player's behalf. If neither side qualifies yet, the goal is
        // still claimed and ActiveVariable/ActiveGoal reset as normal, so
        // HandleProgressionOnEngagementWin keeps firing fresh events (and
        // paying out money) every EngagementsPerProgressionEvent wins
        // regardless — the next completed goal re-checks readiness, so
        // this delays the unlock rather than blocking progress outright.
        private void CompleteGoal(Player awardee)
        {
            awardee.Ship.AddMoney(ActiveGoal.RewardMoney);

            var nextTier = NextUnlockTier();
            if (BothPlayersReadyFor(nextTier))
            {
                MaxUnlockedTier = nextTier;

                // Both players, not just awardee — the ceiling is shared
                // (see this method's own doc comment), so whoever isn't
                // holding the device right now still needs to find out.
                // A separate slot from PendingTurnStartNotice (see
                // Player.PendingTierUnlockNotice) rather than that one,
                // since this and an ordinary turn-start notice can both
                // land on the same player within one turn's two actions.
                var notice = $"{nextTier} engagements are now unlocked!";
                PlayerOne.SetPendingTierUnlockNotice(notice);
                PlayerTwo.SetPendingTierUnlockNotice(notice);
            }

            ActiveGoal = null;
        }

        private EngagementTier NextUnlockTier() => (EngagementTier)((int)MaxUnlockedTier + 1);

        // "Ready" means not mathematically overmatched by the weakest
        // possible roll at that tier — the MIN of each performance stat's
        // range, not the midpoint (RandomMatchBot's own grind-target
        // heuristic uses the midpoint because it's aiming to play
        // competitively; this gate only needs to rule out guaranteed
        // overmatch). Hull is deliberately excluded — it's a depleting
        // health pool, not a gear investment, so it doesn't belong in a
        // gear-readiness check the way Weapons/Shields/Speed do.
        private static bool IsReadyForTier(Ship ship, EngagementTier tier)
        {
            var definition = EngagementDefinitionTable.For(tier);
            return ship.GetStat(CoreStat.Weapons) >= definition.WeaponsRange.Min
                && ship.GetStat(CoreStat.Shields) >= definition.ShieldsRange.Min
                && ship.GetStat(CoreStat.Speed) >= definition.SpeedRange.Min;
        }

        private bool BothPlayersReadyFor(EngagementTier tier) =>
            IsReadyForTier(PlayerOne.Ship, tier) && IsReadyForTier(PlayerTwo.Ship, tier);

        // Called only for a real (non-PvP) engagement win — see
        // ResolveActiveEngagement, whose own call site already excludes
        // PvP, so neither counter below ever moves on a PvP win. Three
        // independent things can happen on the same win: it might be the
        // one that finally defeats the active DefeatNamedTarget goal,
        // and/or it might cross the threshold for a brand new GOAL event
        // (only once no goal is currently active and there's still a
        // tier left to unlock), and/or it might independently cross the
        // threshold for a brand new VARIABLE event. Goal and variable
        // events used to be bundled into one combined event, drawn and
        // replaced together — decoupled now (see their own counters and
        // MatchProgressionService) so a hazard/economy modifier isn't
        // tied to goal pacing, and so it keeps cycling for the rest of
        // the match instead of getting stuck once Hard tier unlocks and
        // goal events stop firing (variable firing is deliberately NOT
        // gated on MaxUnlockedTier the way goal firing is).
        private void HandleProgressionOnEngagementWin(HexCoordinate resolvedHex, Random rng)
        {
            successfulEngagementsSinceLastEvent++;
            successfulEngagementsSinceLastVariableEvent++;

            if (ActiveGoal is { Type: MatchGoalType.DefeatNamedTarget } goal && resolvedHex == goal.TargetHex)
                CompleteGoal(CurrentPlayer);

            if (ActiveGoal == null && MaxUnlockedTier < EngagementTier.Hard &&
                successfulEngagementsSinceLastEvent >= EngagementsPerProgressionEvent)
            {
                ActiveGoal = MatchProgressionService.FireGoalEvent(Map, PlayerOne.Position, PlayerTwo.Position, MaxUnlockedTier, rng);
                successfulEngagementsSinceLastEvent = 0;

                // Same gap this match-global variable event had before it
                // got PendingVariableEventNotice — a goal used to only ever
                // show up as a passive status label, never announced.
                var goalNotice = $"New Goal: {MatchGoalDescriptions.Describe(Map, ActiveGoal)}";
                PlayerOne.SetPendingGoalNotice(goalNotice);
                PlayerTwo.SetPendingGoalNotice(goalNotice);
            }

            if (successfulEngagementsSinceLastVariableEvent >= EngagementsPerVariableEvent)
            {
                ActiveVariable = MatchProgressionService.PickVariable(rng, ActiveVariable);
                successfulEngagementsSinceLastVariableEvent = 0;

                // Both players, not just whoever just won — this is
                // shared match state, same reasoning (and same separate-
                // slot rationale) as the tier-unlock notice above.
                var notice = $"New Event: {MatchVariableDescriptions.Describe(ActiveVariable)}";
                PlayerOne.SetPendingVariableEventNotice(notice);
                PlayerTwo.SetPendingVariableEventNotice(notice);
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
            engagementOccurredThisTurn = true;
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
        // selling it to the market, just to a player instead. A flat,
        // always-available, single-item offer with no opponent agency —
        // this is specifically ShopScreen's own per-item "Trade" action on
        // a held Cargo item's detail card (see OnShopTradeItemClicked),
        // NOT the standalone Trade screen, which is a real multi-item/
        // money negotiation instead (see ProposeTrade below). A simple
        // one-shot action — a second trade the same turn spends the other
        // action slot too.
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

        // Opens a negotiation — CurrentPlayer becomes the negotiation's
        // fixed Initiator (see TradeNegotiation) for its whole lifetime,
        // even though control (via MatchHud's own device hand-off, mirror
        // of the mid-fight PvP one) passes to the Opponent to respond and
        // possibly back again. Costs the one action up front, regardless
        // of how the negotiation is ultimately resolved (accepted,
        // rejected, or countered-then-resolved) — same "the commitment is
        // the trigger, not the outcome" reasoning RollDice already
        // established this session.
        public TradeProposalResult ProposeTrade(
            IReadOnlyList<ItemDefinition> initiatorGives, IReadOnlyList<ItemDefinition> opponentGives,
            int initiatorMoney, int opponentMoney)
        {
            EnsureMatchInProgress();
            if (!CanProposeTrade)
                throw new InvalidOperationException("Can't propose a trade right now.");

            var validation = ValidateProposalTerms(CurrentPlayer, OtherPlayer, initiatorGives, opponentGives, initiatorMoney, opponentMoney);
            if (!validation.Success)
                return validation;

            activeTradeNegotiation = new TradeNegotiation(CurrentPlayer, OtherPlayer, initiatorGives, opponentGives, initiatorMoney, opponentMoney);
            ConsumeAction();
            return TradeProposalResult.Succeeded();
        }

        // The Opponent's one allowed reply to the Initiator's terms —
        // replaces them wholesale rather than layering on top (see
        // TradeNegotiation.ApplyCounter). No action cost: the Initiator
        // already paid at ProposeTrade time, and countering isn't the
        // Opponent's own match turn to begin with.
        public TradeProposalResult CounterTrade(
            IReadOnlyList<ItemDefinition> initiatorGives, IReadOnlyList<ItemDefinition> opponentGives,
            int initiatorMoney, int opponentMoney)
        {
            EnsureMatchInProgress();
            if (activeTradeNegotiation == null)
                throw new InvalidOperationException("There's no trade proposal to counter.");
            if (activeTradeNegotiation.HasBeenCountered)
                throw new InvalidOperationException("Only one counter-offer is allowed.");

            var validation = ValidateProposalTerms(
                activeTradeNegotiation.Initiator, activeTradeNegotiation.Opponent,
                initiatorGives, opponentGives, initiatorMoney, opponentMoney);
            if (!validation.Success)
                return validation;

            activeTradeNegotiation.ApplyCounter(initiatorGives, opponentGives, initiatorMoney, opponentMoney);
            return TradeProposalResult.Succeeded();
        }

        // Commits the negotiation's CURRENT terms (the original proposal,
        // or the one counter if there was one) atomically. Re-validates
        // first even though nothing else can legally change either ship's
        // held items/money while a negotiation is active (it's modal —
        // see IsNegotiatingTrade) — cheap insurance against exactly that
        // assumption ever quietly becoming false later.
        public TradeProposalResult AcceptTrade()
        {
            EnsureMatchInProgress();
            if (activeTradeNegotiation == null)
                throw new InvalidOperationException("There's no trade proposal to accept.");

            var negotiation = activeTradeNegotiation;
            var validation = ValidateProposalTerms(
                negotiation.Initiator, negotiation.Opponent,
                negotiation.InitiatorGives, negotiation.OpponentGives, negotiation.InitiatorMoney, negotiation.OpponentMoney);
            if (!validation.Success)
                return validation;

            foreach (var item in negotiation.InitiatorGives)
            {
                negotiation.Initiator.Ship.TryRemoveItem(item);
                negotiation.Opponent.Ship.TryAddItem(item);
            }
            foreach (var item in negotiation.OpponentGives)
            {
                negotiation.Opponent.Ship.TryRemoveItem(item);
                negotiation.Initiator.Ship.TryAddItem(item);
            }
            if (negotiation.InitiatorMoney > 0)
            {
                negotiation.Initiator.Ship.TrySpendMoney(negotiation.InitiatorMoney);
                negotiation.Opponent.Ship.AddMoney(negotiation.InitiatorMoney);
            }
            if (negotiation.OpponentMoney > 0)
            {
                negotiation.Opponent.Ship.TrySpendMoney(negotiation.OpponentMoney);
                negotiation.Initiator.Ship.AddMoney(negotiation.OpponentMoney);
            }

            activeTradeNegotiation = null;
            return TradeProposalResult.Succeeded();
        }

        // Ends the negotiation with no transfers — the Initiator's action
        // spent at ProposeTrade time isn't refunded (same "the commitment
        // is the trigger" reasoning as ProposeTrade's own comment).
        public void RejectTrade()
        {
            EnsureMatchInProgress();
            if (activeTradeNegotiation == null)
                throw new InvalidOperationException("There's no trade proposal to reject.");

            activeTradeNegotiation = null;
        }

        // Shared by ProposeTrade/CounterTrade/AcceptTrade — checked
        // against whichever pair of ships the terms are actually relative
        // to (always Initiator/Opponent, never CurrentPlayer/OtherPlayer
        // directly, since the Opponent is the one proposing when
        // countering). Cargo capacity is checked properly (net items
        // gained vs. lost), not via Ship.CanHoldAnotherItem's single-item
        // shortcut, since a proposal can move several items each way at
        // once.
        private static TradeProposalResult ValidateProposalTerms(
            Player initiator, Player opponent,
            IReadOnlyList<ItemDefinition> initiatorGives, IReadOnlyList<ItemDefinition> opponentGives,
            int initiatorMoney, int opponentMoney)
        {
            foreach (var item in initiatorGives)
                if (!initiator.Ship.HeldItems.Contains(item))
                    return TradeProposalResult.Failed(TradeProposalFailureReason.ItemNotHeld);
            foreach (var item in opponentGives)
                if (!opponent.Ship.HeldItems.Contains(item))
                    return TradeProposalResult.Failed(TradeProposalFailureReason.ItemNotHeld);

            if (initiator.Ship.Money < initiatorMoney || opponent.Ship.Money < opponentMoney)
                return TradeProposalResult.Failed(TradeProposalFailureReason.InsufficientFunds);

            var initiatorItemCountAfter = initiator.Ship.HeldItems.Count - initiatorGives.Count + opponentGives.Count;
            var opponentItemCountAfter = opponent.Ship.HeldItems.Count - opponentGives.Count + initiatorGives.Count;
            if (initiatorItemCountAfter > initiator.Ship.CargoCapacity || opponentItemCountAfter > opponent.Ship.CargoCapacity)
                return TradeProposalResult.Failed(TradeProposalFailureReason.CargoFull);

            return TradeProposalResult.Succeeded();
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

            var price = ActiveVariable == MatchVariable.MarketCrash
                ? (int)(item.Price * ItemPricing.MarketCrashDiscountMultiplier)
                : item.Price;
            var result = ShopService.TryPurchase(CurrentPlayer.Ship, item, price);
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

            var costPerPoint = ActiveVariable == MatchVariable.RepairDiscount
                ? (int)(RepairService.CostPerPoint * RepairService.RepairDiscountMultiplier)
                : RepairService.CostPerPoint;
            var result = RepairService.TryRepairOnePoint(CurrentPlayer.Ship, stat, costPerPoint);
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
                IntegrityPenaltyService.ApplyIfDepleted(CurrentPlayer, Map, rng, out var moneyLost);
                // PvP only — in PvE there's no real opponent to hand the
                // loser's money to (what, if anything, should happen to
                // it there is still an open question).
                if (wasPvP && moneyLost > 0)
                {
                    OtherPlayer.Ship.AddMoney(moneyLost);
                    // Same "tell them what happened" treatment as the
                    // loser's own destruction notice — this overwrites
                    // whatever OtherPlayer's notice slot already held,
                    // but that's fine here: this engagement is the only
                    // thing that could have just set one for them, and
                    // it didn't (only the LOSER gets one from
                    // ApplyIfDepleted). Deferred to OtherPlayer's own
                    // next turn handoff, same as their own destruction
                    // notice would be — they aren't the one holding the
                    // device right now.
                    OtherPlayer.SetPendingTurnStartNotice(
                        $"You destroyed {CurrentPlayer.DisplayName}'s ship and claimed {moneyLost} money from the wreckage!");
                }
            }
            else if (activeEngagement.Outcome == EngagementOutcome.PlayerEscaped)
            {
                RelocateAfterEscape(CurrentPlayer, rng);
            }
            else if (activeEngagement.Outcome == EngagementOutcome.OpponentEscaped)
            {
                // PvP-only (see EngagementOutcome) — the OTHER player fled,
                // so they're the one who gets relocated, same treatment
                // CurrentPlayer gets above for their own escape.
                RelocateAfterEscape(OtherPlayer, rng);
            }

            // Skipped for PvP: a bounty job targets a specific marked NPC
            // hex, never the other player, even if they happen to share it.
            if (!wasPvP)
                JobService.ResolveBountyOutcome(CurrentPlayer, resolvedHex, activeEngagement.Outcome);

            // The opponent is a real, persistent player here — a PvP loss
            // can deplete their Hull too, so they get the same zero-Hull
            // relocation/penalty the active player would. If it actually
            // did (i.e. CurrentPlayer just won), their money goes to
            // CurrentPlayer rather than just vanishing — same reasoning
            // as the symmetric PlayerLost branch above.
            if (wasPvP)
            {
                IntegrityPenaltyService.ApplyIfDepleted(OtherPlayer, Map, rng, out var otherMoneyLost);
                if (otherMoneyLost > 0)
                {
                    CurrentPlayer.Ship.AddMoney(otherMoneyLost);
                    // CurrentPlayer IS the one holding the device right
                    // now — OnEngagementContinueClicked already shows
                    // whatever's in their notice slot immediately, same
                    // as it would for their own destruction notice.
                    CurrentPlayer.SetPendingTurnStartNotice(
                        $"You destroyed {OtherPlayer.DisplayName}'s ship and claimed {otherMoneyLost} money from the wreckage!");
                }
            }

            if (CurrentPlayer.HasWonMatch)
                Winner = CurrentPlayer;

            activeEngagement = null;
        }

        public bool CanEndTurn => !IsInEngagement && !IsComplete && !IsNegotiatingTrade;

        public void EndTurn()
        {
            EnsureMatchInProgress();
            if (IsInEngagement)
                throw new InvalidOperationException("Can't end turn mid-encounter.");
            if (IsNegotiatingTrade)
                throw new InvalidOperationException("Can't end turn mid-negotiation.");

            ApplyEnergyForEndingTurn();
            engagementOccurredThisTurn = false;

            currentHand = null; // unspent dice are discarded, no carryover
            actionsUsed = 0;
            openSession = ActionSession.None;
            moveSessionPaid = false;
            hasMovedThisTurn = false;
            CurrentPlayer = CurrentPlayer == PlayerOne ? PlayerTwo : PlayerOne;
            TurnNumber++;

            // Snapshot the NEW CurrentPlayer's Energy right as their turn
            // begins — read back by their own future EndTurn call above.
            energyAtTurnStart[CurrentPlayer] = CurrentPlayer.Ship.GetStat(CoreStat.Energy);
        }

        // [Combat] Energy overhaul — exactly one of two mutually
        // exclusive outcomes for the player whose turn is ending:
        //   - This turn started at 0 Energy (see energyAtTurnStart) —
        //     a forced floor-reset up to EnergyFloorResetValue, not the
        //     ordinary banking formula below (see that constant's own
        //     comment for why banking alone can't be used here).
        //   - Otherwise, ordinary banking: 1 Energy per rolled die that
        //     went unspent this turn, capped at Ship.MaxEnergyValue.
        //     Never rolling at all banks nothing — "fewer than the
        //     AVAILABLE dice" implies dice were actually made available
        //     by rolling; otherwise skipping the roll entirely would be
        //     a strictly better way to "bank" than actually engaging
        //     with the dice system, which would make rolling pointless.
        //     An engagement starting mid-turn ALSO banks nothing, even
        //     with dice left unspent — ExhaustActions cut the turn
        //     short through no choice of the player's (an ambush, or
        //     choosing to Attack instead of continuing to move), so
        //     those leftover dice were never a genuine "could have kept
        //     moving but chose not to." Without this, a 1-Energy escape
        //     attempt (or several) would routinely get refunded in full
        //     by whatever dice the ambush happened to leave unspent,
        //     making Energy cost nothing at exactly the moment — combat
        //     — it's supposed to matter most. See engagementOccurredThisTurn.
        private void ApplyEnergyForEndingTurn()
        {
            var ship = CurrentPlayer.Ship;
            var startedTurnAtZeroEnergy = energyAtTurnStart.TryGetValue(CurrentPlayer, out var startingEnergy) && startingEnergy <= 0;

            if (startedTurnAtZeroEnergy)
            {
                var current = ship.GetStat(CoreStat.Energy);
                if (current < EnergyFloorResetValue)
                    ship.ApplyStatDelta(CoreStat.Energy, EnergyFloorResetValue - current);
                return;
            }

            if (engagementOccurredThisTurn || currentHand == null)
                return;

            // Only dice within the Energy cap count as "left unused" —
            // a die whose own index is beyond the player's Energy was
            // never usable in the first place (see ShipMover.TryMove's
            // matching real gate), so it was never a choice to hold
            // back and shouldn't inflate the bank. Energy can't have
            // changed mid-turn here (nothing outside combat touches it,
            // and any combat this turn already bailed out above via
            // engagementOccurredThisTurn), so the cap was constant for
            // the whole turn — today's GetStat(Energy) is exactly it.
            var currentEnergy = ship.GetStat(CoreStat.Energy);
            var unspentCount = currentHand.UnspentDice.Count(d => d.DieIndex < currentEnergy);
            var bankable = Math.Min(unspentCount, Ship.MaxEnergyValue - currentEnergy);
            if (bankable > 0)
                ship.ApplyStatDelta(CoreStat.Energy, bankable);
        }

        // No engagement/negotiation guard needed — unlike EndTurn, the
        // Pause menu this is reached from is itself unreachable whenever
        // either is active (chrome hides then, same as every other
        // in-match screen), so those states can't coincide with a call
        // here.
        public void Forfeit()
        {
            EnsureMatchInProgress();
            Winner = OtherPlayer;
        }

        // Picks a random neighboring hex with no engagement marker of its
        // own — landing straight back into another ambush would defeat the
        // point of escaping. Leaves the player in place if every neighbor
        // is either off the map or itself marked (e.g. boxed in). Takes
        // whichever Player actually escaped — CurrentPlayer for their own
        // PlayerEscaped, OtherPlayer for a PvP OpponentEscaped.
        // User-requested: an escape shouldn't be able to land on Tradelane
        // or Wormhole terrain (those exist for passing through, not for
        // being a destination — same reasoning as goal targets/job
        // destinations/engagement markers) OR on any hex that would show a
        // beacon marker (see HexMarkerOverride) — a real engagement, the
        // race goal's target, or this player's own active job destination.
        private void RelocateAfterEscape(Player player, Random rng)
        {
            var candidates = Map.GetNeighborCoordinates(player.Position)
                .Where(coordinate => IsEligibleEscapeDestination(coordinate, player))
                .ToList();

            if (candidates.Count > 0)
                player.Position = candidates[rng.Next(candidates.Count)];
        }

        // Mirrors exactly what HexMarkerOverride would show a beacon for
        // (plus the Tradelane/Wormhole terrain rule) — a fleeing ship
        // shouldn't land by chance on any hex that reads as "somewhere you
        // deliberately navigate to." Checks THIS player's own ActiveJob,
        // not CurrentPlayer's — RelocateAfterEscape is also called for
        // OtherPlayer on a PvP OpponentEscaped outcome, and it's their own
        // job (if any) that matters for them, not whoever's turn it is.
        private bool IsEligibleEscapeDestination(HexCoordinate coordinate, Player player)
        {
            if (!Map.TryGetHex(coordinate, out var hex))
                return false;
            if (hex.HasEngagement)
                return false;
            if (hex.Terrain == TerrainType.Tradelane || hex.Terrain == TerrainType.Wormhole)
                return false;
            if (ActiveGoal != null && ActiveGoal.TargetHex == coordinate)
                return false;

            if (player.ActiveJob is { } job)
            {
                if (job.Type == JobType.Mining && !player.HasMinedCargo)
                {
                    if (hex.Terrain == TerrainType.Asteroids)
                        return false;
                }
                else if (job.Destination == coordinate)
                {
                    return false;
                }
            }

            return true;
        }

        // For the simple one-shot actions (Mine, Trade, Job Board) — always
        // spends a fresh action and closes out any open Move/Market
        // session, since doing something else ends it. Also closes out an
        // already-*used* (not just paid) Move privilege — see
        // ConsumeActionForSession's own comment on the distinction.
        private void ConsumeAction()
        {
            if (actionsUsed < ActionsPerTurn)
                actionsUsed++;
            openSession = ActionSession.None;
            if (hasMovedThisTurn)
                moveSessionPaid = false;
        }

        // For the two "session" categories (Move, Market) — free to
        // continue if this same session is already open; otherwise spends
        // a fresh action and opens it, implicitly closing whatever
        // session (if any) was open before. See CanMove/CanShop.
        //
        // Move is the one exception to "implicitly closes the previous
        // session" — but only while it's still an unspent voucher. Once
        // paid (by RollDice, an actual Move, or a wormhole jump —
        // whichever happens first this turn) and NOT yet actually used,
        // switching to a different session doesn't un-pay it: rolling was
        // the commitment (see RollDice), so a shop visit before you've
        // moved at all shouldn't cost you the move you already paid for.
        // But once you've actually moved and THEN switch sessions, the
        // Move privilege closes for the rest of the turn exactly as it
        // always has — see BuyItem_AfterMoving_ClosesTheMoveSessionEvenWith-
        // NoActionsSpentOnItAgain, and hasMovedThisTurn's own comment.
        private void ConsumeActionForSession(ActionSession session)
        {
            if (session == ActionSession.Move && moveSessionPaid)
                return;
            if (openSession == session)
                return;

            if (session != ActionSession.Move && hasMovedThisTurn)
                moveSessionPaid = false;

            if (actionsUsed < ActionsPerTurn)
                actionsUsed++;
            openSession = session;
            if (session == ActionSession.Move)
                moveSessionPaid = true;
        }

        // For engagements (see AttackOpponent/HandleArrival) — spends
        // whatever's left of the budget outright and closes any open
        // session, regardless of how much had already been spent. Also
        // closes out an unspent Move voucher — engaging in combat is a
        // bigger interruption than an ordinary market visit, so a roll
        // that hasn't been used yet doesn't survive it either.
        private void ExhaustActions()
        {
            actionsUsed = ActionsPerTurn;
            openSession = ActionSession.None;
            moveSessionPaid = false;
        }

        private void EnsureMatchInProgress()
        {
            if (IsComplete)
                throw new InvalidOperationException("This match has already ended.");
        }
    }
}
