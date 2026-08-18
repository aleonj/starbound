using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Movement;
using StarBound.Multiplayer;
using StarBound.Shop;
using StarBound.UI;
using Random = System.Random;

namespace StarBound.Demo
{
    // IMGUI debug HUD driving a Match end to end: roll dice, pick a die and
    // click a highlighted hex to move, fight or flee an engagement, shop
    // while docked, and pass turns until someone wins. Functional demo
    // harness, not the polished [UI] backlog screens.
    public class MatchHud : MonoBehaviour
    {
        private static readonly Color PlayerOneColor = new(0.2f, 0.9f, 0.9f);
        private static readonly Color PlayerTwoColor = new(0.95f, 0.3f, 0.7f);
        // Deliberately distinct from the yellow-legal/cyan-pending/
        // purple-waypoint highlight language (see HighlightMaterials) —
        // this isn't a movement affordance, so it shouldn't read as one.
        private static readonly Color LocateFlashColor = new(1f, 0.92f, 0.55f);
        private const float LocateFlashDuration = 1f;

        private Match match;
        private MapView mapView;
        private Transform markersParent;
        private float hexRadius;
        private Random rng;
        private MapConfirmationUI confirmationUI;
        private TurnHandoffScreen handoffScreen;
        private WinScreen winScreen;
        private MapCameraController cameraController;
        private PopupDialog popupDialog;
        private Action onNewMatch;
        private bool awaitingHandoff;
        private bool winScreenShown;

        private GameObject playerOneMarker;
        private GameObject playerTwoMarker;

        private RolledDie selectedDie;
        private HexCoordinate? pendingTarget;
        private bool showShop;
        private IReadOnlyList<ItemDefinition> shopOffer = Array.Empty<ItemDefinition>();
        private bool showJobBoard;
        private IReadOnlyList<JobDefinition> jobOffer = Array.Empty<JobDefinition>();
        private bool jobOfferRolledThisTurn;
        private string lastMessage;
        private readonly List<string> combatLog = new();
        private GUIStyle headerStyle;
        private bool legacySkinScaled;
        private MatchHudChrome chrome;

        public void Initialize(Match match, MapView mapView, Transform markersParent, float hexRadius, MapConfirmationUI confirmationUI, TurnHandoffScreen handoffScreen, WinScreen winScreen, MapCameraController cameraController, PopupDialog popupDialog, Action onNewMatch)
        {
            this.match = match;
            this.mapView = mapView;
            this.markersParent = markersParent;
            this.hexRadius = hexRadius;
            this.confirmationUI = confirmationUI;
            this.handoffScreen = handoffScreen;
            this.winScreen = winScreen;
            this.cameraController = cameraController;
            this.popupDialog = popupDialog;
            this.onNewMatch = onNewMatch;
            rng = new Random();

            cameraController.SetInputBlocker(IsPointerOverUi);
            cameraController.Tapped += OnMapTapped;

            // Created fresh here rather than once in DemoBootstrap like
            // the other UGUI screens — every value it shows is per-match
            // state, so it has no reason to survive across matches.
            chrome = gameObject.AddComponent<MatchHudChrome>();
            chrome.Initialize(OnAttackClicked, OnEndTurnClicked, OnShopToggleClicked, OnJobBoardToggleClicked, OnLocatePlayerClicked);

            CreateShipMarkers();
            RefreshView();
            // Even the very first turn goes through hand-off — one code
            // path instead of special-casing match start.
            ShowHandoffForCurrentPlayer();
        }

        // Tapped is a strong reference held by the camera controller,
        // which outlives this MatchHud (a new match destroys and
        // recreates the HUD object without reloading the scene) — without
        // unsubscribing, a destroyed MatchHud would stay a dangling
        // subscriber.
        private void OnDestroy()
        {
            if (cameraController != null)
                cameraController.Tapped -= OnMapTapped;
        }

        // Hides the whole board (see OnGUI's awaitingHandoff guard) behind
        // a full-screen "pass the device" confirmation for whichever
        // player's turn is starting — see TurnHandoffScreen.
        private void ShowHandoffForCurrentPlayer()
        {
            awaitingHandoff = true;
            handoffScreen.Show(match.CurrentPlayer, OnHandoffConfirmed);
        }

        private void OnHandoffConfirmed()
        {
            awaitingHandoff = false;
            handoffScreen.Hide();
            // Recenter on whoever's turn it now is — same PanTo the
            // locate-player button uses, which already clamps to the
            // map's pan bounds (see MapCameraController.ClampPosition),
            // so a player near the map's edge still gets centered as
            // closely as the bounds allow rather than snapping oddly.
            // Runs on every hand-off including the very first turn (see
            // Initialize's own comment on why hand-off isn't special-
            // cased for match start).
            cameraController.PanTo(HexLayout.AxialToWorld(match.CurrentPlayer.Position, hexRadius));
            RefreshView();
        }

        // Fired by MapCameraController once per completed tap (press +
        // release under its drag threshold) that didn't start over UI —
        // replaces the old direct Mouse.current polling in Update, which
        // fired on press rather than release and so had no way to tell a
        // tap apart from the start of a pan/pinch drag.
        private void OnMapTapped(Vector2 screenPosition)
        {
            if (match == null || selectedDie == null || !match.CanMove)
                return;

            var camera = Camera.main;
            if (camera == null)
                return;

            var distanceFromCamera = Mathf.Abs(camera.transform.position.z);
            var worldPoint = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, distanceFromCamera));
            var targetCoordinate = HexLayout.WorldToAxial(worldPoint, hexRadius);

            // Illegal targets aren't selectable at all — no attempted
            // move, no error message, just ignored.
            if (!ComputeLegalTargets(selectedDie).Contains(targetCoordinate))
                return;

            // Tapping any other legal hex while one's already pending just
            // switches the pending target — no need to Cancel first.
            pendingTarget = targetCoordinate;
            lastMessage = null;
            RefreshView();
            confirmationUI.Show(ConfirmPendingMove, CancelPendingMove);
        }

        // Legacy overlay Rect for whatever's still IMGUI (Shop/Job
        // Board/Trade/Wormhole/Engagement) — docked in the corner
        // opposite the new UGUI chrome (which docks top-left) so the two
        // can never collide regardless of chrome's actual (dynamically
        // sized) height, and computed from Screen.width/height instead of
        // a hardcoded box so at least this much scales with screen size
        // too. Everything drawn in here gets its own dedicated UGUI story
        // soon (Shop/Job Board/Trade/Wormhole/Engagement are all already
        // tracked separately), so this is a deliberately temporary shape.
        private static Rect ComputeLegacyOverlayRect() =>
            new(Screen.width - 480f, 10f, 460f, Screen.height - 20f);

        // Used by MapCameraController to reject a gesture that starts
        // over UI, so dragging from the HUD panel never pans the map
        // underneath it. EventSystem now covers the UGUI chrome for
        // free; the manual Rect check below only matters for the
        // still-IMGUI legacy overlay, which EventSystem doesn't know
        // about at all since IMGUI isn't part of its raycasting pipeline
        // — and only while something's actually drawn there, so an
        // inactive overlay doesn't block map taps over empty screen space.
        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return true;

            if (!IsLegacyOverlayActive())
                return false;

            var guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            return ComputeLegacyOverlayRect().Contains(guiPosition);
        }

        // True whenever the still-IMGUI legacy overlay (Shop/Job Board
        // panel content, Trade, Wormhole, Engagement) is actually
        // drawing something — used both to gate input-blocking above and
        // to decide whether OnGUI's BeginArea should paint a background
        // at all (see OnGUI — GUI.skin.box has its own default dark
        // translucent fill that would otherwise render as an unexplained
        // shadow over an empty Rect the rest of the time).
        private bool IsLegacyOverlayActive() =>
            showShop || showJobBoard || match.CanAttackOpponent || match.CanTravelWormhole || match.IsInEngagement;

        private void ConfirmPendingMove()
        {
            if (pendingTarget == null)
                return;

            var result = match.Move(selectedDie, pendingTarget.Value, rng);
            pendingTarget = null;
            confirmationUI.Hide();

            if (result.Success)
            {
                lastMessage = null;
                selectedDie = null;
            }
            else
            {
                // Shouldn't normally happen since the target came from
                // ComputeLegalTargets, but a Tradelane toll the player can
                // no longer afford (say, after an intervening purchase)
                // can still fail here — keep the die selected so they can
                // pick a different target instead of losing their turn.
                lastMessage = $"Can't move there ({result.FailureReason}).";
            }

            RefreshView();
        }

        private void CancelPendingMove()
        {
            pendingTarget = null;
            confirmationUI.Hide();
            RefreshView();
        }

        // Chrome's dice-tray/Roll/Attack/End Turn callbacks — extracted
        // out of the old inline IMGUI button blocks so both the UGUI
        // chrome and (were it ever needed) the old IMGUI code could call
        // the same logic.
        private void OnRollDiceClicked()
        {
            match.RollDice(rng);
            lastMessage = null;
            RefreshView();
        }

        private void OnDieClicked(RolledDie die)
        {
            selectedDie = die == selectedDie ? null : die;
            pendingTarget = null;
            confirmationUI.Hide();
            RefreshView();
        }

        private void OnAttackClicked()
        {
            match.AttackOpponent();
            lastMessage = null;
            combatLog.Clear();
        }

        private void OnShopToggleClicked()
        {
            showShop = !showShop;
            // The planet's shelf persists in Match/Hex now (see
            // PlanetShopService) — fetching it fresh on every open just
            // reads whatever's currently there, it doesn't reroll it.
            if (showShop)
                shopOffer = match.GetShopOffer(rng);
        }

        private void OnJobBoardToggleClicked()
        {
            showJobBoard = !showJobBoard;
            // Roll once per turn, not once per open — same reasoning as
            // the Shop offer above.
            if (showJobBoard && !jobOfferRolledThisTurn)
            {
                jobOffer = JobOfferGenerator.GenerateOffer(rng, match.CurrentPlayer.Position, match.Map, match.MaxUnlockedTier);
                jobOfferRolledThisTurn = true;
            }

            // Show/hide each offer's destination as a waypoint the
            // moment the board opens/closes, not just on the next
            // unrelated state change.
            RefreshView();
        }

        private void OnLocatePlayerClicked()
        {
            var worldPosition = HexLayout.AxialToWorld(match.CurrentPlayer.Position, hexRadius);
            cameraController.PanTo(worldPosition);
            mapView.FlashHex(match.CurrentPlayer.Position, LocateFlashColor, LocateFlashDuration);
        }

        private void OnEndTurnClicked()
        {
            match.EndTurn();
            selectedDie = null;
            pendingTarget = null;
            confirmationUI.Hide();
            showShop = false;
            showJobBoard = false;
            jobOfferRolledThisTurn = false;
            lastMessage = null;
            combatLog.Clear();
            RefreshView();
            ShowHandoffForCurrentPlayer();
        }

        // Text-only chrome sync, every frame — mirrors IMGUI's own
        // "just re-read live state every frame" cost model, since UGUI
        // has no equivalent for free. Cheap: string formatting only, no
        // GameObject allocation (the dice tray's more expensive
        // Destroy/Instantiate rebuild stays off this path — see
        // RefreshView/MatchHudChrome.RefreshDiceTray). This exists
        // separately from the old input-polling Update() removed earlier
        // in favor of event-driven map taps — unrelated purpose, just the
        // same method name.
        private void Update()
        {
            if (chrome == null || match == null)
                return;

            var chromeVisible = !awaitingHandoff && !match.IsComplete && !match.IsInEngagement;
            chrome.SetVisible(chromeVisible);
            // The map camera's own viewport shrinks to leave room for the
            // dice bar (see MapCameraController.SetBottomReservedFraction)
            // rather than the bar floating on top of a full-screen map —
            // reset to full-screen whenever chrome (and the bar with it)
            // isn't showing, so hand-off/engagement/the win screen don't
            // leave the map needlessly cropped underneath them.
            cameraController.SetBottomReservedFraction(chromeVisible ? chrome.DiceBarReservedFraction : 0f);
            if (!chromeVisible)
                return;

            var player = match.CurrentPlayer;
            var playerColor = player == match.PlayerOne ? PlayerOneColor : PlayerTwoColor;
            chrome.SetHeader($"Turn: {player.DisplayName}", playerColor);

            chrome.SetStats(player.Ship);

            chrome.SetWins(
                $"Wins — Easy: {player.EasyEngagementWins}  " +
                $"Medium: {player.MediumEngagementWins}  " +
                $"Hard: {player.HardEngagementWins}/{Player.HardWinsToVictory}");

            chrome.SetActionsRemaining($"Actions remaining: {match.ActionsRemaining}/{Match.ActionsPerTurn}");

            chrome.SetProgression(
                $"Phase: {match.MaxUnlockedTier} unlocked",
                match.ActiveVariable != MatchVariable.None ? $"Event: {DescribeVariable(match.ActiveVariable)}" : null,
                match.ActiveGoal is { } goal ? $"Race goal: {DescribeGoal(goal)}" : null);

            chrome.SetAttack(match.CanAttackOpponent, $"Attack {match.OtherPlayer.DisplayName}");
            chrome.SetEndTurn(match.CanEndTurn);

            var canShopHere = match.IsCurrentPlayerOnPlanet && match.CanShop;
            chrome.SetShop(canShopHere, showShop ? "Close Shop" : "Open Shop");

            var canVisitJobBoardHere = match.IsCurrentPlayerOnPlanet && match.CanAcceptJob;
            chrome.SetJobBoard(canVisitJobBoardHere, showJobBoard ? "Close Job Board" : "Open Job Board");

            chrome.SetMessage(lastMessage);
        }

        private void OnGUI()
        {
            // Nothing from the HUD renders at all while awaiting hand-off
            // confirmation — guarantees the previous player's board can't
            // leak through underneath the full-screen TurnHandoffScreen,
            // and sidesteps any IMGUI/UGUI draw-order ambiguity entirely.
            if (awaitingHandoff)
                return;

            // Same reasoning as the awaitingHandoff guard above — nothing
            // from the IMGUI HUD renders once the match is over, so the
            // full-screen WinScreen (UGUI) can't have it drawn on top.
            if (match.IsComplete)
            {
                if (!winScreenShown)
                {
                    winScreenShown = true;
                    winScreen.Show(match, onNewMatch);
                }
                return;
            }

            // IMGUI has no CanvasScaler equivalent — GUI.skin's default
            // font size is tuned for desktop pixel density and doesn't
            // scale with the device's actual pixel ratio at all, so it
            // renders illegibly tiny on a phone (the same physical-vs-
            // logical scale problem the UGUI chrome had, just never
            // fixed here since this whole region is scheduled to be
            // replaced by dedicated UGUI screens soon — Shop/Job Board/
            // Trade/Wormhole/Engagement, Order #9-13). This is a
            // deliberately rough interim bump, not a real fix.
            if (!legacySkinScaled)
            {
                GUI.skin.label.fontSize = 34;
                GUI.skin.button.fontSize = 34;
                GUI.skin.button.padding = new RectOffset(20, 20, 14, 14);
                legacySkinScaled = true;
            }
            headerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 40 };

            // GUI.skin.box paints its own dark translucent background for
            // the whole Rect regardless of whether DrawMainPanel actually
            // puts anything inside it — only worth that background when
            // something's really being shown there.
            var backgroundStyle = IsLegacyOverlayActive() ? GUI.skin.box : GUIStyle.none;
            GUILayout.BeginArea(ComputeLegacyOverlayRect(), backgroundStyle);

            if (match.IsInEngagement)
                DrawEngagementPanel();
            else
                DrawMainPanel();

            GUILayout.EndArea();
        }

        // Shop/Job Board's toggle BUTTONS now live in the UGUI chrome
        // (see MatchHudChrome.SetShop/SetJobBoard, wired to
        // OnShopToggleClicked/OnJobBoardToggleClicked) — this only draws
        // their actual panel CONTENT, and only while showShop/showJobBoard
        // is true, so the legacy overlay Rect is empty (and out of the
        // way) the rest of the time instead of permanently competing with
        // chrome for the same on-screen space.
        private void DrawMainPanel()
        {
            if (match.CanTravelWormhole)
                DrawWormholeTravelPanel();

            GUILayout.Space(10);

            if (showShop && match.IsCurrentPlayerOnPlanet && match.CanShop)
                DrawShopPanel();
            else
                showShop = false;

            GUILayout.Space(10);

            if (showJobBoard && match.IsCurrentPlayerOnPlanet && match.CanAcceptJob)
                DrawJobBoardPanel();
            else
                showJobBoard = false;

            if (match.CurrentPlayer.ActiveJob is { } activeJob)
                DrawActiveJobStatus(activeJob);

            GUILayout.Space(10);

            if (match.CanAttackOpponent)
            {
                GUILayout.Label($"{match.OtherPlayer.DisplayName} is here.");
                DrawTradePanel();
                GUILayout.Space(10);
            }
        }

        // Still used by DescribeVariable/DescribeGoal's callers — see
        // Update(), which builds the same progression text for the UGUI
        // chrome now instead of this method's old IMGUI labels.
        private static string DescribeVariable(MatchVariable variable) => variable switch
        {
            MatchVariable.MinefieldDamage => "Minefield Damage — entering a Mines hex costs 1 Hull.",
            MatchVariable.TradeBoom => "Trade Boom — Tradelane tolls are waived.",
            _ => variable.ToString()
        };

        private static string DescribeGoal(MatchGoal goal) => goal.Type switch
        {
            MatchGoalType.TravelAndPay =>
                $"Travel & Pay — reach ({goal.TargetHex.Q}, {goal.TargetHex.R}) with at least ${goal.MoneyRequired} (reward ${goal.RewardMoney}).",
            MatchGoalType.DefeatNamedTarget =>
                $"Defeat the marked target at ({goal.TargetHex.Q}, {goal.TargetHex.R}) (reward ${goal.RewardMoney}).",
            _ => goal.Type.ToString()
        };

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
            GUILayout.Label($"-- Shop: {HexDisplayName(match.CurrentPlayer.Position)} --", headerStyle);

            // Snapshot before iterating — same reasoning as DrawHeldItems:
            // buying can mutate the underlying offer mid-loop.
            foreach (var item in shopOffer.ToList())
            {
                if (GUILayout.Button($"Buy {item.Name} — {item.Price}"))
                {
                    popupDialog.ShowConfirmation("Confirm Purchase", $"Buy {item.Name} for ${item.Price}?", "Buy", () =>
                    {
                        var result = match.BuyItem(item);
                        lastMessage = result.Success ? $"Bought {item.Name}." : $"Purchase failed: {result.FailureReason}";
                    });
                }
            }

            GUILayout.Space(5);
            // Always shown (not part of the randomized offer above) — a
            // mechanic-unlocking purchase shouldn't be gated by luck.
            if (!match.CurrentPlayer.Ship.HeldItems.Contains(ItemPool.WormholeDevice))
            {
                if (GUILayout.Button($"Buy {ItemPool.WormholeDevice.Name} — {ItemPool.WormholeDevice.Price}"))
                {
                    popupDialog.ShowConfirmation("Confirm Purchase", $"Buy {ItemPool.WormholeDevice.Name} for ${ItemPool.WormholeDevice.Price}?", "Buy", () =>
                    {
                        var result = match.BuyItem(ItemPool.WormholeDevice);
                        lastMessage = result.Success ? $"Bought {ItemPool.WormholeDevice.Name}." : $"Purchase failed: {result.FailureReason}";
                    });
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
            GUILayout.Label($"-- Job Board: {HexDisplayName(match.CurrentPlayer.Position)} --", headerStyle);

            if (match.CurrentPlayer.ActiveJob != null)
            {
                GUILayout.Label("Already have an active job — deliver or complete it first.");
                return;
            }

            foreach (var job in jobOffer)
            {
                var description = job.Type switch
                {
                    JobType.BountyHunting => $"Bounty ({job.BountyTier}) at {HexDisplayName(job.Destination)} — ${job.Reward}",
                    JobType.Mining => $"Mining: mine an Asteroids field, then deliver to {HexDisplayName(job.Destination)} — ${job.Reward}",
                    _ => $"{job.Type} to {HexDisplayName(job.Destination)} — ${job.Reward}"
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
                GUILayout.Label($"Bounty ({job.BountyTier}): defeat the pirate marked at {HexDisplayName(job.Destination)} (highlighted on the map) — reward ${job.Reward}.");
                GUILayout.Label("Just move onto that hex — the fight starts automatically, same as any encounter.");
                GUILayout.Label($"Escaping that fight voids the job and costs a ${job.Reward / 10} penalty.");
                return;
            }

            if (job.Type == JobType.Mining && !match.CurrentPlayer.HasMinedCargo)
            {
                GUILayout.Label($"Mining: find an Asteroids field (highlighted on the map) and mine there first, then deliver to {HexDisplayName(job.Destination)} — reward ${job.Reward}.");

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
            GUILayout.Label($"{job.Type}: {verb} at {HexDisplayName(job.Destination)} (highlighted on the map) — reward ${job.Reward}.");

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
                            ? $"Escaped — carried to {HexDisplayName(match.CurrentPlayer.Position)}."
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
            playerOneMarker.GetComponent<ShipMarkerView>().Initialize(hexRadius * 0.3f, PlayerOneColor, hullStyle: 0);

            playerTwoMarker = new GameObject("Player Two Ship", typeof(ShipMarkerView));
            playerTwoMarker.transform.SetParent(markersParent, false);
            playerTwoMarker.GetComponent<ShipMarkerView>().Initialize(hexRadius * 0.3f, PlayerTwoColor, hullStyle: 1);
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
            var waypoints = new List<HexCoordinate>();

            // Always show where the active job wants you to go next —
            // otherwise there's no way to tell. An unmined Mining job
            // wants any Asteroids field (there's no single destination
            // for that step); everything else has one fixed hex.
            if (match.CurrentPlayer.ActiveJob is { } job)
            {
                if (job.Type == JobType.Mining && !match.CurrentPlayer.HasMinedCargo)
                {
                    waypoints.AddRange(match.Map.Hexes
                        .Where(hex => hex.Terrain == TerrainType.Asteroids)
                        .Select(hex => hex.Coordinate));
                }
                else
                {
                    waypoints.Add(job.Destination);
                }
            }

            // Job Board offers aren't accepted yet, but the player should
            // still be able to see where each one would send them before
            // committing.
            if (showJobBoard && match.CurrentPlayer.ActiveJob == null)
                waypoints.AddRange(jobOffer.Select(offer => offer.Destination));

            if (match.ActiveGoal is { } goal)
                highlighted.Add(goal.TargetHex);

            mapView.Render(match.Map, hexRadius, highlighted, match.CurrentPlayer.DiscoveredEngagementHexes, pendingTarget, waypoints);
            UpdateShipMarkers();

            // The one relatively expensive chrome refresh (Destroy/
            // Instantiate on the die button row) — deliberately not on
            // the per-frame Update() path, so it only runs at RefreshView's
            // existing "state changed, resync" call sites (roll, die
            // select/deselect, move confirm/cancel, end turn, mine,
            // wormhole travel, post-engagement continue, etc.).
            chrome?.RefreshDiceTray(
                match.CurrentHand?.Dice ?? Array.Empty<RolledDie>(),
                selectedDie,
                match.CurrentHand != null,
                match.CanMove,
                OnRollDiceClicked,
                OnDieClicked);
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

        // Falls back to the raw coordinate when the hex has no Name —
        // correct, not just defensive, for Bounty Hunting jobs, whose
        // Destination is the marked engagement hex and often isn't a
        // planet at all.
        private string HexDisplayName(HexCoordinate coordinate) =>
            match.Map.TryGetHex(coordinate, out var hex) && !string.IsNullOrEmpty(hex.Name)
                ? hex.Name
                : coordinate.ToString();
    }
}
