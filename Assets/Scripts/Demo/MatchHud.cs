using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using StarBound.Combat;
using StarBound.Core;
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
                GUILayout.Label("Dice — pick one, then click a highlighted hex on the map:");
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
                GUILayout.Label("Movement already used this turn.");
                foreach (var die in match.CurrentHand.Dice)
                    GUILayout.Label($"  {die.Terrain}{(die.IsSpent ? " (spent)" : "")}");
            }

            GUILayout.Space(10);

            var canShopHere = IsCurrentPlayerOnPlanet() && match.CanShop;
            GUI.enabled = canShopHere;
            if (GUILayout.Button(showShop ? "Close Shop" : "Open Shop"))
            {
                showShop = !showShop;
                if (showShop)
                    shopOffer = ShopOfferGenerator.GenerateOffer(rng);
            }
            GUI.enabled = true;

            if (!match.CanShop)
                GUILayout.Label("Market closed — already had an engagement this turn.");

            if (showShop && canShopHere)
                DrawShopPanel();
            else
                showShop = false;

            GUILayout.Space(10);

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

            if (match.MustMoveAfterEscape)
                GUILayout.Label("You must move to an adjacent hex after escaping before ending your turn.");

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
                    var result = ShopService.TryPurchase(match.CurrentPlayer.Ship, item);
                    lastMessage = result.Success ? $"Bought {item.Name}." : $"Purchase failed: {result.FailureReason}";
                }
            }

            GUILayout.Space(5);
            if (GUILayout.Button($"Repair Hull +1 — {RepairService.CostPerPoint}"))
            {
                var result = RepairService.TryRepairOnePoint(match.CurrentPlayer.Ship, CoreStat.Hull);
                lastMessage = result.Success ? "Hull repaired." : $"Repair failed: {result.FailureReason}";
            }
            if (GUILayout.Button($"Repair Energy +1 — {RepairService.CostPerPoint}"))
            {
                var result = RepairService.TryRepairOnePoint(match.CurrentPlayer.Ship, CoreStat.Energy);
                lastMessage = result.Success ? "Energy repaired." : $"Repair failed: {result.FailureReason}";
            }
        }

        private void DrawEngagementPanel()
        {
            var session = match.ActiveEngagement;

            GUILayout.Label($"Engagement ({session.Definition.Tier})", headerStyle);

            var opponentView = new OpponentStatView(session.Opponent);
            GUILayout.Label(
                $"Opponent — Hull {opponentView.Hull}  Weapons {opponentView.Weapons}  " +
                $"Shields {opponentView.Shields}  Speed {opponentView.Speed}");

            GUILayout.Space(5);
            DrawShipStats(session.PlayerShip);

            if (session.Outcome == EngagementOutcome.InProgress)
            {
                GUILayout.Space(10);

                GUI.enabled = session.CanAttemptEscape;
                if (GUILayout.Button("Attempt Escape"))
                {
                    var result = session.AttemptEscape(rng);
                    combatLog.Add(result.Success
                        ? $"Escape: rolled {result.Roll} = {result.Total} vs {result.OpponentSpeed} — escaped!"
                        : $"Escape: rolled {result.Roll} = {result.Total} vs {result.OpponentSpeed} — failed, lost 1 Energy.");
                }
                GUI.enabled = true;

                if (GUILayout.Button("Resolve Round"))
                {
                    var result = session.ResolveRound(rng);
                    combatLog.Add(DescribeRound(result));
                }
            }
            else
            {
                GUILayout.Space(10);
                GUILayout.Label($"Outcome: {session.Outcome}", headerStyle);
                if (GUILayout.Button("Continue"))
                {
                    match.ResolveActiveEngagement();
                    combatLog.Clear();
                    lastMessage = null;
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

        private static string DescribeRound(RoundResult result)
        {
            var speedLine =
                $"Speed: rolled {result.SpeedRoll} = {result.SpeedTotal} vs {result.OpponentSpeed} — " +
                (result.Attacker == RoundAttacker.Player ? "you attack." : "opponent attacks.");

            var attackLine = result.Attacker == RoundAttacker.Player
                ? $"Weapons: rolled {result.AttackRoll} = {result.AttackTotal} vs Shields {result.DefenderShields} — " +
                  (result.HitLanded ? "opponent lost 1 Hull." : "missed.")
                : $"Weapons: {result.AttackTotal} vs Shields {result.DefenderShields} — " +
                  (result.HitLanded ? "you lost 1 Hull." : "missed.");

            return $"{speedLine} {attackLine}";
        }

        private bool IsCurrentPlayerOnPlanet() =>
            match.Map.TryGetHex(match.CurrentPlayer.Position, out var hex) && hex.Terrain == TerrainType.PlanetOrStarport;

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
            IReadOnlyCollection<HexCoordinate> highlighted =
                selectedDie != null ? ComputeLegalTargets(selectedDie) : Array.Empty<HexCoordinate>();
            mapView.Render(match.Map, hexRadius, highlighted);
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
