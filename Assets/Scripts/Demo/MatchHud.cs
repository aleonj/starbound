using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Movement;
using StarBound.Multiplayer;
using StarBound.Shop;
using Random = System.Random;

namespace StarBound.Demo
{
    // IMGUI debug HUD driving a Match end to end: roll dice, pick a die and
    // click a highlighted hex to move, fight or flee an engagement, shop
    // while docked, and pass turns until someone wins. Functional demo
    // harness, not the polished [UI] backlog screens.
    public class MatchHud : MonoBehaviour
    {
        private static readonly Rect HudRect = new(10, 10, 360, 620);
        private static readonly Color PlayerOneColor = new(0.2f, 0.9f, 0.9f);
        private static readonly Color PlayerTwoColor = new(0.95f, 0.3f, 0.7f);

        private Match match;
        private MapView mapView;
        private Transform markersParent;
        private float hexRadius;
        private Random rng;

        private GameObject playerOneMarker;
        private GameObject playerTwoMarker;

        private RolledDie selectedDie;
        private bool showShop;
        private IReadOnlyList<ItemDefinition> shopOffer = Array.Empty<ItemDefinition>();
        private bool showJobBoard;
        private IReadOnlyList<JobDefinition> jobOffer = Array.Empty<JobDefinition>();
        private string lastMessage;
        private readonly List<string> combatLog = new();
        private GUIStyle headerStyle;

        public void Initialize(Match match, MapView mapView, Transform markersParent, float hexRadius)
        {
            this.match = match;
            this.mapView = mapView;
            this.markersParent = markersParent;
            this.hexRadius = hexRadius;
            rng = new Random();

            CreateShipMarkers();
            RefreshView();
        }

        private void Update()
        {
            if (match == null || selectedDie == null || !match.CanMove)
                return;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            var mouseScreenPosition = mouse.position.ReadValue();
            var mouseGuiPosition = new Vector2(mouseScreenPosition.x, Screen.height - mouseScreenPosition.y);
            if (HudRect.Contains(mouseGuiPosition))
                return; // click landed on the HUD, not the map

            var camera = Camera.main;
            if (camera == null)
                return;

            var distanceFromCamera = Mathf.Abs(camera.transform.position.z);
            var worldPoint = camera.ScreenToWorldPoint(new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, distanceFromCamera));
            var targetCoordinate = HexLayout.WorldToAxial(worldPoint, hexRadius);

            var result = match.Move(selectedDie, targetCoordinate, rng);
            if (result.Success)
            {
                lastMessage = null;
                selectedDie = null;
                RefreshView();
            }
            else
            {
                lastMessage = $"Can't move there ({result.FailureReason}).";
            }
        }

        private void OnGUI()
        {
            headerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };

            GUILayout.BeginArea(HudRect, GUI.skin.box);

            if (match.IsComplete)
                DrawWinnerPanel();
            else if (match.IsInEngagement)
                DrawEngagementPanel();
            else
                DrawMainPanel();

            GUILayout.EndArea();
        }

        private void DrawMainPanel()
        {
            GUILayout.Label($"Turn: {match.CurrentPlayer.DisplayName}", headerStyle);
            DrawShipStats(match.CurrentPlayer.Ship);
            GUILayout.Label(
                $"Wins — Easy: {match.CurrentPlayer.EasyEngagementWins}  " +
                $"Medium: {match.CurrentPlayer.MediumEngagementWins}  " +
                $"Hard: {match.CurrentPlayer.HardEngagementWins}/{Player.HardWinsToVictory}");
            GUILayout.Label($"Actions remaining: {match.ActionsRemaining}/{Match.ActionsPerTurn}");

            GUILayout.Space(10);

            if (match.CurrentHand == null)
            {
                if (GUILayout.Button("Roll Dice"))
                {
                    match.RollDice(rng);
                    lastMessage = null;
                    RefreshView();
                }
            }
            else if (match.CanMove)
            {
                GUILayout.Label(match.IsCurrentPlayerOnPlanet
                    ? "Docked — pick a die to keep traveling, or browse the market (browsing is free):"
                    : "Dice — pick one, then click a highlighted hex on the map:");
                foreach (var die in match.CurrentHand.Dice)
                {
                    if (die.IsSpent)
                    {
                        GUILayout.Label($"  {die.Terrain} (spent)");
                        continue;
                    }

                    var label = die == selectedDie ? $"> {die.Terrain} <" : die.Terrain.ToString();
                    if (GUILayout.Button(label))
                    {
                        selectedDie = die == selectedDie ? null : die;
                        RefreshView();
                    }
                }
            }
            else
            {
                GUILayout.Label("No actions left to move with this turn.");
                foreach (var die in match.CurrentHand.Dice)
                    GUILayout.Label($"  {die.Terrain}{(die.IsSpent ? " (spent)" : "")}");
            }

            if (match.CanTravelWormhole)
                DrawWormholeTravelPanel();

            GUILayout.Space(10);

            var canShopHere = match.IsCurrentPlayerOnPlanet && match.CanShop;
            GUI.enabled = canShopHere;
            if (GUILayout.Button(showShop ? "Close Shop" : "Open Shop"))
            {
                showShop = !showShop;
                if (showShop)
                    shopOffer = ShopOfferGenerator.GenerateOffer(rng);
            }
            GUI.enabled = true;

            if (match.IsCurrentPlayerOnPlanet && !match.CanShop)
                GUILayout.Label("Market closed — no actions left this turn.");

            if (showShop && canShopHere)
                DrawShopPanel();
            else
                showShop = false;

            GUILayout.Space(10);

            var canVisitJobBoardHere = match.IsCurrentPlayerOnPlanet && match.CanAcceptJob;
            GUI.enabled = canVisitJobBoardHere;
            if (GUILayout.Button(showJobBoard ? "Close Job Board" : "Open Job Board"))
            {
                showJobBoard = !showJobBoard;
                if (showJobBoard)
                    jobOffer = JobOfferGenerator.GenerateOffer(rng, match.CurrentPlayer.Position, match.Map);
            }
            GUI.enabled = true;

            if (match.IsCurrentPlayerOnPlanet && !match.CanAcceptJob)
                GUILayout.Label("Job board closed — no actions left this turn.");

            if (showJobBoard && canVisitJobBoardHere)
                DrawJobBoardPanel();
            else
                showJobBoard = false;

            if (match.CurrentPlayer.ActiveJob is { } activeJob)
                DrawActiveJobStatus(activeJob);

            GUILayout.Space(10);

            if (match.CanAttackOpponent)
            {
                GUILayout.Label($"{match.OtherPlayer.DisplayName} is here.");
                if (GUILayout.Button($"Attack {match.OtherPlayer.DisplayName}"))
                {
                    match.AttackOpponent();
                    lastMessage = null;
                    combatLog.Clear();
                }
                DrawTradePanel();
                GUILayout.Space(10);
            }

            GUI.enabled = match.CanEndTurn;
            if (GUILayout.Button("End Turn"))
            {
                match.EndTurn();
                selectedDie = null;
                showShop = false;
                lastMessage = null;
                combatLog.Clear();
                RefreshView();
            }
            GUI.enabled = true;

            if (lastMessage != null)
                GUILayout.Label(lastMessage);
        }

        private void DrawShipStats(Ship ship)
        {
            GUILayout.Label(
                $"Hull {ship.GetStat(CoreStat.Hull)}  Energy {ship.GetStat(CoreStat.Energy)}  " +
                $"Weapons {ship.GetStat(CoreStat.Weapons)}  Shields {ship.GetStat(CoreStat.Shields)}  " +
                $"Speed {ship.GetStat(CoreStat.Speed)}");
            GUILayout.Label($"Money: {ship.Money}   Cargo: {ship.HeldItems.Count}/{ship.CargoCapacity}");
        }

        private void DrawShopPanel()
        {
            GUILayout.Space(5);
            GUILayout.Label("-- Shop --", headerStyle);

            foreach (var item in shopOffer)
            {
                if (GUILayout.Button($"Buy {item.Name} — {item.Price}"))
                {
                    var result = match.BuyItem(item);
                    lastMessage = result.Success ? $"Bought {item.Name}." : $"Purchase failed: {result.FailureReason}";
                }
            }

            GUILayout.Space(5);
            // Always shown (not part of the randomized offer above) — a
            // mechanic-unlocking purchase shouldn't be gated by luck.
            if (!match.CurrentPlayer.Ship.HeldItems.Contains(ItemPool.WormholeDevice))
            {
                if (GUILayout.Button($"Buy {ItemPool.WormholeDevice.Name} — {ItemPool.WormholeDevice.Price}"))
                {
                    var result = match.BuyItem(ItemPool.WormholeDevice);
                    lastMessage = result.Success ? $"Bought {ItemPool.WormholeDevice.Name}." : $"Purchase failed: {result.FailureReason}";
                }
            }

            GUILayout.Space(5);
            if (GUILayout.Button($"Repair Hull +1 — {RepairService.CostPerPoint}"))
            {
                var result = match.RepairStat(CoreStat.Hull);
                lastMessage = result.Success ? "Hull repaired." : $"Repair failed: {result.FailureReason}";
            }
            if (GUILayout.Button($"Repair Energy +1 — {RepairService.CostPerPoint}"))
            {
                var result = match.RepairStat(CoreStat.Energy);
                lastMessage = result.Success ? "Energy repaired." : $"Repair failed: {result.FailureReason}";
            }

            GUILayout.Space(5);
            GUILayout.Label("-- Cargo --");
            DrawHeldItems();
        }

        // Shared between the shop panel and the mid-engagement panel — a
        // Consumable can be used from either place (see Match.CanUseItem).
        private void DrawHeldItems()
        {
            foreach (var item in match.CurrentPlayer.Ship.HeldItems.ToList())
            {
                var kindLabel = item.Kind switch
                {
                    ItemKind.Consumable => " (consumable)",
                    ItemKind.Unlock => " (device)",
                    _ => ""
                };
                GUILayout.Label($"{item.Name}{kindLabel}");

                if (item.Kind == ItemKind.Consumable && match.CanUseItem(item))
                {
                    if (GUILayout.Button($"  Use {item.Name}"))
                    {
                        match.UseItem(item);
                        lastMessage = $"Used {item.Name}.";
                    }
                }

                if (match.CanTradeWithOpponent(item))
                {
                    if (GUILayout.Button($"  Trade to {match.OtherPlayer.DisplayName} — ${item.Price / 2}"))
                    {
                        match.TradeItemToOpponent(item);
                        lastMessage = $"Traded {item.Name} to {match.OtherPlayer.DisplayName}.";
                    }
                }
                else if (GUILayout.Button($"  Sell {item.Name} — ${item.Price / 2}"))
                {
                    var result = match.SellItem(item);
                    lastMessage = result.Success ? $"Sold {item.Name} for ${result.Refund}." : $"Sell failed: {result.FailureReason}";
                }
            }
        }

        // Mid-fight use only — no sell/trade here, those stay market/opponent-side actions.
        private void DrawConsumablesMidEngagement()
        {
            var usable = match.CurrentPlayer.Ship.HeldItems
                .Where(item => item.Kind == ItemKind.Consumable && match.CanUseItem(item))
                .ToList();

            if (usable.Count == 0)
                return;

            GUILayout.Space(5);
            foreach (var item in usable)
            {
                if (GUILayout.Button($"Use {item.Name}"))
                {
                    match.UseItem(item);
                    combatLog.Add($"Used {item.Name}.");
                }
            }
        }

        // Standing trade offer while sharing a hex with the opponent —
        // deliberately independent of the Shop/planet gating (CanTradeWithOpponent
        // doesn't require being on a planet, so this can't live inside
        // DrawShopPanel/DrawHeldItems or it'd be unreachable off-planet).
        private void DrawTradePanel()
        {
            var heldItems = match.CurrentPlayer.Ship.HeldItems.ToList();

            GUILayout.Space(5);
            GUILayout.Label("-- Trade --");

            if (heldItems.Count == 0)
            {
                // Trade only offers what the mover is carrying — it's not a
                // request-from-opponent flow — so this is expected, not an
                // error, whenever the mover hasn't bought anything yet.
                GUILayout.Label($"You have nothing to trade {match.OtherPlayer.DisplayName}. Buy something from a shop first.");
                return;
            }

            foreach (var item in heldItems)
            {
                if (match.CanTradeWithOpponent(item))
                {
                    if (GUILayout.Button($"Trade {item.Name} to {match.OtherPlayer.DisplayName} — ${item.Price / 2}"))
                    {
                        match.TradeItemToOpponent(item);
                        lastMessage = $"Traded {item.Name} to {match.OtherPlayer.DisplayName}.";
                    }
                }
                else
                {
                    var reason = !match.OtherPlayer.Ship.CanHoldAnotherItem
                        ? $"{match.OtherPlayer.DisplayName}'s cargo is full"
                        : $"{match.OtherPlayer.DisplayName} can't afford ${item.Price / 2}";
                    GUILayout.Label($"{item.Name} — can't trade: {reason}");
                }
            }
        }

        // Standing movement option for anyone holding the Wormhole Device
        // (see Match.CanTravelWormhole) — available any time during the
        // movement phase, not just while parked on a wormhole hex. Lists
        // every wormhole on the map as a travel target.
        private void DrawWormholeTravelPanel()
        {
            GUILayout.Space(5);
            GUILayout.Label("-- Wormhole --", headerStyle);

            var destinations = match.OtherWormholeDestinations.ToList();
            if (destinations.Count == 0)
            {
                GUILayout.Label("No other wormhole exists on this map yet.");
                return;
            }

            foreach (var destination in destinations)
            {
                if (GUILayout.Button($"Travel to ({destination.Q}, {destination.R})"))
                {
                    match.TravelToWormhole(destination, rng);
                    lastMessage = null;
                    combatLog.Clear();
                    RefreshView();
                }
            }
        }

        private void DrawJobBoardPanel()
        {
            GUILayout.Space(5);
            GUILayout.Label("-- Job Board --", headerStyle);

            if (match.CurrentPlayer.ActiveJob != null)
            {
                GUILayout.Label("Already have an active job — deliver or complete it first.");
                return;
            }

            foreach (var job in jobOffer)
            {
                var description = job.Type switch
                {
                    JobType.BountyHunting => $"Bounty ({job.BountyTier}) at {job.Destination} — ${job.Reward}",
                    JobType.Mining => $"Mining: mine an Asteroids field, then deliver to {job.Destination} — ${job.Reward}",
                    _ => $"{job.Type} to {job.Destination} — ${job.Reward}"
                };

                if (GUILayout.Button($"Accept: {description}"))
                {
                    var result = match.AcceptJob(job);
                    lastMessage = result.Success ? $"Accepted job: {description}" : $"Accept failed: {result.FailureReason}";
                    if (result.Success)
                        RefreshView();
                }
            }
        }

        private void DrawActiveJobStatus(JobDefinition job)
        {
            GUILayout.Space(5);
            GUILayout.Label("-- Active Job --", headerStyle);

            if (job.Type == JobType.BountyHunting)
            {
                GUILayout.Label($"Bounty ({job.BountyTier}): defeat the pirate marked at {job.Destination} (highlighted on the map) — reward ${job.Reward}.");
                GUILayout.Label("Just move onto that hex — the fight starts automatically, same as any encounter.");
                GUILayout.Label($"Escaping that fight voids the job and costs a ${job.Reward / 10} penalty.");
                return;
            }

            if (job.Type == JobType.Mining && !match.CurrentPlayer.HasMinedCargo)
            {
                GUILayout.Label($"Mining: find an Asteroids field (highlighted on the map) and mine there first, then deliver to {job.Destination} — reward ${job.Reward}.");

                if (match.CanMineAsteroid)
                {
                    if (GUILayout.Button("Mine"))
                    {
                        match.MineAsteroid();
                        lastMessage = "Mined — now deliver the cargo.";
                        RefreshView();
                    }
                }
                else
                {
                    GUILayout.Label("Not on an Asteroids field yet.");
                }

                return;
            }

            var verb = job.Type == JobType.Mining ? "Deliver the minerals" : "Drop off the passenger";
            GUILayout.Label($"{job.Type}: {verb} at {job.Destination} (highlighted on the map) — reward ${job.Reward}.");

            if (match.CanDeliverJob)
            {
                if (GUILayout.Button($"Deliver — ${job.Reward}"))
                {
                    match.DeliverJob();
                    lastMessage = $"Delivered — earned ${job.Reward}.";
                    RefreshView();
                }
            }
            else if (match.CurrentPlayer.Position == job.Destination)
            {
                GUILayout.Label("You're here — Deliver will appear once you've rolled dice this turn.");
            }
            else
            {
                GUILayout.Label("Travel there, then Deliver will appear.");
            }
        }

        private void DrawEngagementPanel()
        {
            var session = match.ActiveEngagement;

            var header = session.IsPvP ? $"PvP Engagement — vs {match.OtherPlayer.DisplayName}" : $"Engagement ({session.Definition.Tier})";
            GUILayout.Label(header, headerStyle);

            var opponentLabel = session.IsPvP ? match.OtherPlayer.DisplayName : "Opponent";
            var opponentView = new OpponentStatView(session.Opponent);
            GUILayout.Label(
                $"{opponentLabel} — Hull {opponentView.Hull}  Weapons {opponentView.Weapons}  " +
                $"Shields {opponentView.Shields}  Speed {opponentView.Speed}");

            GUILayout.Space(5);
            DrawShipStats(session.PlayerShip);

            if (session.Outcome == EngagementOutcome.InProgress)
            {
                DrawConsumablesMidEngagement();

                GUILayout.Space(10);

                if (!session.IsAwaitingAttackResolution)
                {
                    GUI.enabled = session.CanAttemptEscape;
                    if (GUILayout.Button("Attempt Escape"))
                    {
                        var result = session.AttemptEscape(rng);
                        combatLog.Add(DescribeEscape(result));
                    }
                    GUI.enabled = true;

                    if (session.Outcome == EngagementOutcome.InProgress && GUILayout.Button("Roll Initiative"))
                    {
                        var initiative = session.ResolveInitiative(rng);
                        combatLog.Add(DescribeInitiative(initiative));
                    }
                }
                else if (session.PendingAttacker == RoundAttacker.Opponent)
                {
                    GUILayout.Label("Opponent has the initiative — brace for impact?");

                    var canBrace = session.PlayerShip.GetStat(CoreStat.Energy) > 0;
                    GUI.enabled = canBrace;
                    if (GUILayout.Button($"Brace (-{CombatResolver.BraceEnergyCost} Energy, +{CombatResolver.BraceShieldBonus} Shields)"))
                    {
                        var result = session.ResolveAttack(rng, wantsBrace: true);
                        combatLog.Add(DescribeAttack(result));
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("Hold"))
                    {
                        var result = session.ResolveAttack(rng, wantsBrace: false);
                        combatLog.Add(DescribeAttack(result));
                    }
                }
                else
                {
                    GUILayout.Label("You have the initiative!");
                    if (GUILayout.Button("Attack"))
                    {
                        var result = session.ResolveAttack(rng);
                        combatLog.Add(DescribeAttack(result));
                    }
                }
            }
            else
            {
                GUILayout.Space(10);
                GUILayout.Label($"Outcome: {session.Outcome}", headerStyle);
                if (GUILayout.Button("Continue"))
                {
                    var wasEscape = session.Outcome == EngagementOutcome.PlayerEscaped;
                    var previousPosition = match.CurrentPlayer.Position;

                    match.ResolveActiveEngagement(rng);
                    combatLog.Clear();

                    lastMessage = wasEscape
                        ? (match.CurrentPlayer.Position != previousPosition
                            ? $"Escaped — carried to {match.CurrentPlayer.Position}."
                            : "Escaped — but nowhere safe nearby to be carried to.")
                        : null;

                    RefreshView();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("Log:");
            foreach (var line in combatLog)
                GUILayout.Label(line);
        }

        private void DrawWinnerPanel()
        {
            GUILayout.Label($"{match.Winner.DisplayName} wins the match!", headerStyle);
        }

        private static string DescribeEscape(EscapeAttemptResult result) =>
            result.Success
                ? $"Escape: you rolled {result.Roll} = {result.Total}, opponent rolled {result.OpponentRoll} = {result.OpponentTotal} — escaped!"
                : $"Escape: you rolled {result.Roll} = {result.Total}, opponent rolled {result.OpponentRoll} = {result.OpponentTotal} — failed, lost 1 Energy.";

        private static string DescribeInitiative(InitiativeResult result)
        {
            var fumble = result.WasPlayerCriticalFailure ? " (natural 1 — critical failure!)" : "";
            return $"Speed: you rolled {result.PlayerSpeedRoll} = {result.PlayerSpeedTotal}, " +
                $"opponent rolled {result.OpponentSpeedRoll} = {result.OpponentSpeedTotal}{fumble} — " +
                (result.Attacker == RoundAttacker.Player ? "you attack." : "opponent attacks.");
        }

        private static string DescribeAttack(RoundResult result)
        {
            if (result.Attacker == RoundAttacker.Player)
            {
                var crit = result.WasCriticalHit ? " CRITICAL HIT!" : "";
                return $"Weapons: you rolled {result.AttackRoll} = {result.AttackTotal}, opponent's Shields rolled {result.DefenseRoll} = {result.DefenseTotal} — " +
                    (result.HitLanded ? $"opponent lost {result.Damage} Hull.{crit}" : "missed.");
            }

            var opponentCrit = result.WasCriticalHit ? " CRITICAL HIT!" : "";
            var braceNote = result.DefenderBraced ? " (braced)" : "";
            return $"Weapons: opponent rolled {result.AttackRoll} = {result.AttackTotal}, your Shields rolled {result.DefenseRoll} = {result.DefenseTotal}{braceNote} — " +
                (result.HitLanded ? $"you lost {result.Damage} Hull.{opponentCrit}" : "missed.");
        }

        private void CreateShipMarkers()
        {
            playerOneMarker = new GameObject("Player One Ship", typeof(ShipMarkerView));
            playerOneMarker.transform.SetParent(markersParent, false);
            playerOneMarker.GetComponent<ShipMarkerView>().Initialize(hexRadius * 0.3f, PlayerOneColor);

            playerTwoMarker = new GameObject("Player Two Ship", typeof(ShipMarkerView));
            playerTwoMarker.transform.SetParent(markersParent, false);
            playerTwoMarker.GetComponent<ShipMarkerView>().Initialize(hexRadius * 0.3f, PlayerTwoColor);
        }

        private void UpdateShipMarkers()
        {
            var offset = hexRadius * 0.3f;
            var p1Center = HexLayout.AxialToWorld(match.PlayerOne.Position, hexRadius);
            var p2Center = HexLayout.AxialToWorld(match.PlayerTwo.Position, hexRadius);

            playerOneMarker.transform.localPosition = p1Center + new Vector3(-offset, offset * 0.6f, -0.02f);
            playerTwoMarker.transform.localPosition = p2Center + new Vector3(offset, -offset * 0.6f, -0.02f);
        }

        private void RefreshView()
        {
            var highlighted = selectedDie != null ? ComputeLegalTargets(selectedDie) : new List<HexCoordinate>();

            // Always show where the active job wants you to go next —
            // otherwise there's no way to tell. An unmined Mining job
            // wants any Asteroids field (there's no single destination
            // for that step); everything else has one fixed hex.
            if (match.CurrentPlayer.ActiveJob is { } job)
            {
                if (job.Type == JobType.Mining && !match.CurrentPlayer.HasMinedCargo)
                {
                    highlighted.AddRange(match.Map.Hexes
                        .Where(hex => hex.Terrain == TerrainType.Asteroids)
                        .Select(hex => hex.Coordinate));
                }
                else
                {
                    highlighted.Add(job.Destination);
                }
            }

            mapView.Render(match.Map, hexRadius, highlighted, match.CurrentPlayer.DiscoveredEngagementHexes);
            UpdateShipMarkers();
        }

        private List<HexCoordinate> ComputeLegalTargets(RolledDie die)
        {
            var results = new List<HexCoordinate>();
            foreach (var neighbor in match.Map.GetNeighborCoordinates(match.CurrentPlayer.Position))
            {
                if (match.Map.TryGetHex(neighbor, out var hex) &&
                    (hex.Terrain == die.Terrain || hex.Terrain == TerrainType.PlanetOrStarport))
                {
                    results.Add(neighbor);
                }
            }

            return results;
        }
    }
}
