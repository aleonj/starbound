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
        private TurnHandoffScreen handoffScreen;
        private WinScreen winScreen;
        private MapCameraController cameraController;
        private PopupDialog popupDialog;
        private Action onNewMatch;
        private bool awaitingHandoff;
        private bool winScreenShown;
        private bool wasEngagementVisible;
        // Mid-fight "pass the device" moments for symmetric PvP —
        // deliberately separate from awaitingHandoff, which is the
        // turn-boundary flow (camera recenter, OnHandoffConfirmed
        // semantics) this isn't. deviceHolder tracks which side of the
        // active engagement is actually holding the phone right now (only
        // meaningful for PvP — NPC fights never move it off Player), so
        // HandOffDeviceTo can tell whether a hand-off screen is actually
        // needed or whether the right person already has it.
        private bool awaitingEngagementHandoff;
        private RoundAttacker deviceHolder;
        // Identifies a genuinely NEW engagement, as opposed to merely
        // returning from a mid-fight hand-off screen — both produce the
        // same "engagementVisible flips from false to true" edge that
        // wasEngagementVisible alone can't tell apart. Conflating the two
        // was a real bug: it reset deviceHolder to Player and wiped
        // lastRoundBanner after EVERY hand-off, discarding exactly the
        // state HandOffDeviceTo's own callback had just set correctly.
        private EngagementSession trackedEngagementSession;

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
        // The full sentence behind the round banner below (e.g. "Weapons:
        // Player One rolled 5 = 8, Player Two's Shields rolled 10 = 13 —
        // missed."), shown as its own line under the banner on
        // EngagementScreen — replaces a scrolling combat log, which had
        // the same problem the banner itself was built to fix: small text
        // at the bottom, easy to miss entirely. Also doubles as the
        // "Used X." feedback line when a consumable is used mid-fight,
        // which isn't a roll but shares the same single-line slot.
        private string lastEventDetail;
        // The most recent round's outcome, shown as a prominent banner on
        // EngagementScreen.
        private (string Text, Color Color, bool IsCrit) lastRoundBanner = (string.Empty, Color.white, false);
        private static readonly Color BannerMissColor = new(0.6f, 0.6f, 0.65f);
        private static readonly Color BannerHitColor = new(0.85f, 0.35f, 0.25f);
        private static readonly Color BannerCritColor = new(1f, 0.82f, 0.2f);
        private static readonly Color BannerEscapeColor = new(0.3f, 0.75f, 0.85f);
        private static readonly Color BannerInitiativeColor = new(0.6f, 0.65f, 0.95f);
        private GUIStyle headerStyle;
        private bool legacySkinScaled;
        private MatchHudChrome chrome;
        private EngagementScreen engagementScreen;
        private ShopScreen shopScreen;
        private bool wasShopVisible;
        // The shop's own local feedback line ("Bought X.", "Sold Y for
        // $Z.") — separate from lastMessage/chrome.SetMessage since
        // chrome is hidden behind this full-screen overlay while it's
        // open, same reasoning EngagementScreen's own round banner has
        // for not relying on chrome either.
        private string shopStatusMessage;
        private JobBoardScreen jobBoardScreen;
        private bool wasJobBoardVisible;
        private string jobBoardStatusMessage;
        // Drives which single offer's destination shows as a map waypoint
        // (see RefreshView) — replaces the old "show every offer at once"
        // behavior the story specifically called out as ambiguous. Purely
        // a UI-selection mirror of JobBoardScreen's own internal
        // selection, kept here because RefreshView (which computes
        // waypoints) lives in MatchHud, not the screen.
        private JobDefinition selectedJobOffer;

        public void Initialize(Match match, MapView mapView, Transform markersParent, float hexRadius, TurnHandoffScreen handoffScreen, WinScreen winScreen, MapCameraController cameraController, PopupDialog popupDialog, Action onNewMatch)
        {
            this.match = match;
            this.mapView = mapView;
            this.markersParent = markersParent;
            this.hexRadius = hexRadius;
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
            chrome.Initialize(OnAttackClicked, OnEndTurnClicked, OnShopToggleClicked, OnJobBoardToggleClicked, OnLocatePlayerClicked, OnActiveJobActionClicked);

            // Same "created fresh per match" reasoning as chrome above —
            // every value it shows is per-engagement state.
            engagementScreen = gameObject.AddComponent<EngagementScreen>();
            shopScreen = gameObject.AddComponent<ShopScreen>();
            jobBoardScreen = gameObject.AddComponent<JobBoardScreen>();

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
            // RefreshView re-derives and passes the pending-move state
            // into chrome's dice bar every time, same as any other state
            // change (die select, roll, etc.) — no separate Show() call
            // needed.
            pendingTarget = targetCoordinate;
            lastMessage = null;
            RefreshView();
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
            match.CanAttackOpponent || match.CanTravelWormhole;

        private void ConfirmPendingMove()
        {
            if (pendingTarget == null)
                return;

            var result = match.Move(selectedDie, pendingTarget.Value, rng);
            pendingTarget = null;

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
            RefreshView();
        }

        private void OnAttackClicked()
        {
            match.AttackOpponent();
            lastMessage = null;
        }

        // Engagement round-by-round handlers — each mutates domain state,
        // sets lastEventDetail, then advances (which refreshes the
        // screen, possibly via a device hand-off first — see
        // AdvanceAfterRoundAction/AdvanceAfterInitiative). Deliberately
        // NOT a per-frame refresh from Update(): an earlier version
        // rebuilt EngagementScreen's action buttons via DestroyImmediate
        // every single frame while visible, which broke click detection
        // entirely — Unity's Button needs the SAME GameObject to receive
        // both the pointer-down and pointer-up, and recreating it
        // mid-click meant no click ever completed. Same "rebuild only on
        // an actual state change" discipline the dice bar already uses.
        private void OnAttemptEscapeClicked()
        {
            var session = match.ActiveEngagement;

            // NPC fights keep the original single-call roll — no second
            // real player to hand the "did they get away" half of the
            // check to. PvP splits it exactly like Initiative/Attack: the
            // escapee's own tap only rolls their own half; the OTHER
            // side's own tap (via ContinueEscapeIntercept) is what
            // actually decides whether the escape succeeds.
            if (!session.IsPvP)
            {
                var result = session.AttemptEscape(rng);
                var labels = GetCombatLogLabels(session);
                lastEventDetail = DescribeEscape(result, labels.PlayerLabel, labels.OpponentLabel);
                lastRoundBanner = DescribeEscapeBanner(result, labels.PlayerLabel);
                AdvanceAfterRoundAction();
                return;
            }

            session.BeginEscapeAttempt(rng);
            HandOffDeviceTo(RoundAttacker.Opponent, "Try to Stop Them!", ContinueEscapeIntercept);
        }

        // Shared by both escape directions (the player's own pre-round
        // attempt above, and the opponent's post-initiative one in
        // OnOpponentAttemptEscapeClicked below) — whoever the device was
        // just handed to is the side being asked "did you catch them?",
        // and their own tap is what actually rolls that half of the
        // contested check (see EngagementSession.ResolveEscapeIntercept).
        private void ContinueEscapeIntercept()
        {
            var session = match.ActiveEngagement;
            var escapee = session.PendingEscapee!.Value;
            var labels = GetCombatLogLabels(session);
            var escapeeLabel = escapee == RoundAttacker.Player ? labels.PlayerLabel : labels.OpponentLabel;
            var otherLabel = escapee == RoundAttacker.Player ? labels.OpponentLabel : labels.PlayerLabel;

            var result = session.ResolveEscapeIntercept(rng);
            lastEventDetail = DescribeEscape(result, escapeeLabel, otherLabel);
            lastRoundBanner = DescribeEscapeBanner(result, escapeeLabel);
            AdvanceAfterRoundAction();
        }

        private void OnRollInitiativeClicked()
        {
            var session = match.ActiveEngagement;

            // NPC fights keep the original single-call roll — there's no
            // second real player to hand the device to. PvP always splits
            // it into two genuine per-player rolls (see EngagementSession)
            // so the opponent's own Speed check is their own tap, not a
            // side effect of the current player's.
            if (!session.IsPvP)
            {
                var initiative = session.ResolveInitiative(rng);
                var npcLabels = GetCombatLogLabels(session);
                lastEventDetail = DescribeInitiative(initiative, npcLabels.PlayerLabel, npcLabels.OpponentLabel);
                lastRoundBanner = DescribeInitiativeBanner(initiative, npcLabels.PlayerLabel, npcLabels.OpponentLabel);
                RefreshEngagementScreen();
                return;
            }

            session.RollPlayerInitiative(rng);
            HandOffDeviceTo(RoundAttacker.Opponent, "Roll Initiative", ContinueOpponentInitiativeRoll);
        }

        private void ContinueOpponentInitiativeRoll()
        {
            var session = match.ActiveEngagement;
            var initiative = session.RollOpponentInitiativeAndDetermineAttacker(rng);
            var labels = GetCombatLogLabels(session);
            lastEventDetail = DescribeInitiative(initiative, labels.PlayerLabel, labels.OpponentLabel);
            // Who won was previously only visible by reading the log line
            // — easy to miss exactly like a round's hit/miss outcome was.
            // Shown on whichever screen comes next (the defender's Brace/
            // Hold choice, or the attacker's own hand-off), same banner
            // slot the round-outcome banner already uses.
            lastRoundBanner = DescribeInitiativeBanner(initiative, labels.PlayerLabel, labels.OpponentLabel);
            AdvanceAfterInitiative();
        }

        // Whoever just lost initiative (the new defender) is the one with
        // a real decision to make next. If that's the opponent, they
        // already hold the device (they just rolled on the hand-off
        // screen) — HandOffDeviceTo short-circuits with no extra screen,
        // and they flow straight into their Escape/Brace/Hold choice,
        // which reads as a natural "you rolled low, now what?" beat.
        private void AdvanceAfterInitiative()
        {
            var defender = match.ActiveEngagement.PendingAttacker == RoundAttacker.Player
                ? RoundAttacker.Opponent
                : RoundAttacker.Player;
            HandOffDeviceTo(defender, "Your Move", RefreshEngagementScreen);
        }

        private void OnBraceClicked() => DeclareOrResolveDefense(wantsBrace: true);

        private void OnHoldClicked() => DeclareOrResolveDefense(wantsBrace: false);

        // The player's own turn to attack (as opposed to defending
        // against the opponent's, which offers a Brace/Hold choice
        // first) never braces. PvP never reaches this handler at all —
        // see CanOpponentDecideDefense/AdvanceAfterInitiative above.
        private void OnEngagementAttackClicked() => ResolveEngagementAttack(wantsBrace: false);

        // Shared by all four Brace/Hold buttons (current player defending
        // and opponent defending alike). NPC fights resolve immediately
        // in one call — today's exact behavior, no hand-off, since
        // there's no second real player to hand a tap to. PvP instead
        // declares the choice, then hands the device to whichever side
        // actually won initiative — their own "Attack!" tap on the
        // hand-off screen is what triggers the roll directly (same
        // pattern as the Initiative hand-off's own "Roll Initiative" tap
        // — one tap, one action, not a hand-off into a second screen that
        // asks for the same thing again), rather than the roll happening
        // as a side effect of the defender's choice (see EngagementSession.
        // DeclareDefense/ExecuteAttack).
        private void DeclareOrResolveDefense(bool wantsBrace)
        {
            var session = match.ActiveEngagement;
            if (!session.IsPvP)
            {
                ResolveEngagementAttack(wantsBrace);
                return;
            }

            session.DeclareDefense(wantsBrace);
            HandOffDeviceTo(session.PendingAttacker!.Value, "Attack!", OnExecuteAttackClicked);
        }

        private void ResolveEngagementAttack(bool wantsBrace) =>
            LogAttackResultAndAdvance(match.ActiveEngagement.ResolveAttack(rng, wantsBrace));

        // PvP-only — the attacker's own hand-off "Attack!" tap (see
        // DeclareOrResolveDefense's HandOffDeviceTo call) is what actually
        // runs the roll, after the defender has already declared their
        // Brace/Hold choice. Not a button on EngagementScreen itself —
        // there's no separate on-screen confirmation step, same as
        // Roll Initiative's own hand-off tap doesn't get one either.
        private void OnExecuteAttackClicked() =>
            LogAttackResultAndAdvance(match.ActiveEngagement.ExecuteAttack(rng));

        private void LogAttackResultAndAdvance(RoundResult result)
        {
            var labels = GetCombatLogLabels(match.ActiveEngagement);
            lastEventDetail = DescribeAttack(result, labels.PlayerLabel, labels.PlayerPossessive, labels.OpponentLabel, labels.OpponentPossessive);
            lastRoundBanner = DescribeRoundBanner(result, labels.PlayerLabel, labels.OpponentLabel);
            AdvanceAfterRoundAction();
        }

        private void OnUseItemDuringEngagement(ItemDefinition item)
        {
            match.UseItem(item);
            lastEventDetail = $"Used {item.Name}.";
            RefreshEngagementScreen();
        }

        // The general "who currently holds the phone" hand-off — used at
        // every point control needs to move to the other side, not just
        // one specific case. Reuses TurnHandoffScreen (already built for
        // exactly "hand the device to X, they tap ready") with a
        // mid-fight-appropriate button label each time, instead of a
        // second full-screen component for the same physical gesture.
        // Short-circuits with no visible screen when the right person
        // already has it (e.g. the opponent, right after rolling their
        // own Initiative).
        private void HandOffDeviceTo(RoundAttacker holder, string readyLabel, Action onReady)
        {
            if (deviceHolder == holder)
            {
                onReady();
                return;
            }

            var incomingPlayer = holder == RoundAttacker.Player ? match.CurrentPlayer : match.OtherPlayer;
            awaitingEngagementHandoff = true;
            handoffScreen.Show(incomingPlayer, () =>
            {
                awaitingEngagementHandoff = false;
                handoffScreen.Hide();
                deviceHolder = holder;
                onReady();
            }, readyLabel);
        }

        // Always PvP — CanOpponentDecideDefense (which gates this button)
        // already requires it, so there's no NPC branch to consider here.
        private void OnOpponentAttemptEscapeClicked()
        {
            match.ActiveEngagement.BeginOpponentEscapeAttempt(rng);
            HandOffDeviceTo(RoundAttacker.Player, "Try to Stop Them!", ContinueEscapeIntercept);
        }

        private void OnOpponentBraceClicked() => DeclareOrResolveDefense(wantsBrace: true);

        private void OnOpponentHoldClicked() => DeclareOrResolveDefense(wantsBrace: false);

        // Shared by every handler that resolves a round's action
        // (Escape, Attack, Opponent-Escape). If the fight just ended,
        // just refresh — dismissing results with Continue isn't a
        // privileged action, so whoever currently holds the device can
        // tap it with no hand-off. Otherwise, the next round's pre-round
        // Escape/Roll-Initiative choice is always the initiating
        // player's call, so hand back to them if the opponent still has it.
        private void AdvanceAfterRoundAction()
        {
            if (match.ActiveEngagement.Outcome != EngagementOutcome.InProgress)
            {
                RefreshEngagementScreen();
                return;
            }

            HandOffDeviceTo(RoundAttacker.Player, "Your Move", RefreshEngagementScreen);
        }

        // The one engagement handler that still calls RefreshView() —
        // ending the engagement genuinely needs to resync the map/chrome,
        // unlike every round-by-round action above.
        private void OnEngagementContinueClicked()
        {
            var session = match.ActiveEngagement;
            var outcome = session.Outcome;
            var previousPosition = match.CurrentPlayer.Position;

            match.ResolveActiveEngagement(rng);

            lastMessage = outcome switch
            {
                EngagementOutcome.PlayerEscaped => match.CurrentPlayer.Position != previousPosition
                    ? $"Escaped — carried to {HexDisplayName(match.CurrentPlayer.Position)}."
                    : "Escaped — but nowhere safe nearby to be carried to.",
                // PvP-only — the other player fled, not the current one,
                // so there's nothing to say about the current player's
                // own position; just note what happened to them instead.
                EngagementOutcome.OpponentEscaped => $"{match.OtherPlayer.DisplayName} escaped.",
                _ => null
            };

            RefreshView();
        }

        private void RefreshEngagementScreen()
        {
            var session = match.ActiveEngagement;

            // PvP gets each side tinted to that player's own established
            // color (same identity language chrome/WinScreen already
            // use) instead of the flat neutral accent every NPC fight
            // uses — a cheap way to make a PvP bout read as a genuine
            // "versus" rather than the same panel with a different name.
            var opponentColor = session.IsPvP
                ? (match.OtherPlayer == match.PlayerOne ? PlayerOneColor : PlayerTwoColor)
                : ScreenChromeKit.AccentColor;
            var playerColor = session.IsPvP
                ? (match.CurrentPlayer == match.PlayerOne ? PlayerOneColor : PlayerTwoColor)
                : ScreenChromeKit.AccentColor;

            // The panel's big, hard-to-miss visual signals (rim glow, top
            // accent bar, header) previously always used playerColor,
            // regardless of who was actually being asked to act — the
            // instruction text alone changed color, which wasn't obvious
            // enough. This instead follows deviceHolder, so the whole
            // panel's identity color visibly flips to whoever currently
            // has the phone, not just a line of text.
            var activeAccentColor = session.IsPvP
                ? (deviceHolder == RoundAttacker.Player ? playerColor : opponentColor)
                : ScreenChromeKit.AccentColor;

            // These are the player's own items — while the opponent holds
            // the device for their own decision, showing "Use X" buttons
            // for someone else's items doesn't make sense.
            var usableConsumables = deviceHolder == RoundAttacker.Player
                ? match.CurrentPlayer.Ship.HeldItems.Where(item => item.Kind == ItemKind.Consumable && match.CanUseItem(item)).ToList()
                : new List<ItemDefinition>();

            engagementScreen.Refresh(
                session,
                match.OtherPlayer.DisplayName,
                opponentColor,
                playerColor,
                activeAccentColor,
                lastEventDetail,
                usableConsumables,
                lastRoundBanner.Text,
                lastRoundBanner.Color,
                lastRoundBanner.IsCrit,
                OnAttemptEscapeClicked,
                OnRollInitiativeClicked,
                OnBraceClicked,
                OnHoldClicked,
                OnEngagementAttackClicked,
                OnEngagementContinueClicked,
                OnUseItemDuringEngagement,
                OnOpponentAttemptEscapeClicked,
                OnOpponentBraceClicked,
                OnOpponentHoldClicked);
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

        // Shop's own action handlers — each mutates domain state, sets
        // shopStatusMessage, then refreshes. Deliberately NOT gated behind
        // a popup confirmation (unlike the old IMGUI panel's Buy button) —
        // ShopScreen's own Detail card already IS the confirmation step
        // (see the story's "select -> show info -> confirm" workflow),
        // so a second native dialog on top would be redundant.
        private void OnShopBuyClicked(ItemDefinition item)
        {
            var result = match.BuyItem(item);
            if (result.Success)
                // The offer snapshot doesn't self-update — PlanetShopService's
                // RecordPurchase mutates the hex's own list, not this copy
                // (see GetShopOffer's own comment: it always returns a
                // fresh snapshot). Re-fetching is what makes the bought
                // item actually disappear from the Buy list.
                shopOffer = match.GetShopOffer(rng);
            shopStatusMessage = result.Success ? $"Bought {item.Name}." : $"Purchase failed — {Describe(result.FailureReason)}.";
            RefreshShopScreen();
        }

        private void OnShopRepairHullClicked()
        {
            var result = match.RepairStat(CoreStat.Hull);
            shopStatusMessage = result.Success ? "Hull repaired." : $"Repair failed — {Describe(result.FailureReason)}.";
            RefreshShopScreen();
        }

        private void OnShopRepairEnergyClicked()
        {
            var result = match.RepairStat(CoreStat.Energy);
            shopStatusMessage = result.Success ? "Energy repaired." : $"Repair failed — {Describe(result.FailureReason)}.";
            RefreshShopScreen();
        }

        private void OnShopUseItemClicked(ItemDefinition item)
        {
            match.UseItem(item);
            shopStatusMessage = $"Used {item.Name}.";
            RefreshShopScreen();
        }

        private void OnShopSellItemClicked(ItemDefinition item)
        {
            var result = match.SellItem(item);
            shopStatusMessage = result.Success ? $"Sold {item.Name} for ${result.Refund}." : $"Sell failed — {Describe(result.FailureReason)}.";
            RefreshShopScreen();
        }

        private void OnShopTradeItemClicked(ItemDefinition item)
        {
            match.TradeItemToOpponent(item);
            shopStatusMessage = $"Traded {item.Name} to {match.OtherPlayer.DisplayName}.";
            RefreshShopScreen();
        }

        private void OnShopCloseClicked() => showShop = false;

        private void RefreshShopScreen()
        {
            var ship = match.CurrentPlayer.Ship;
            var buyableItems = ship.HeldItems.Contains(ItemPool.WormholeDevice)
                ? shopOffer
                : shopOffer.Append(ItemPool.WormholeDevice).ToList();
            var playerColor = match.CurrentPlayer == match.PlayerOne ? PlayerOneColor : PlayerTwoColor;

            shopScreen.Refresh(
                HexDisplayName(match.CurrentPlayer.Position), playerColor, ship.Money,
                buyableItems,
                ship.GetStat(CoreStat.Hull), ship.GetStat(CoreStat.Energy), Ship.DefaultStatValue, RepairService.CostPerPoint,
                ship.HeldItems, ship.CargoCapacity, match.OtherPlayer.DisplayName,
                shopStatusMessage,
                OnShopBuyClicked, OnShopRepairHullClicked, OnShopRepairEnergyClicked,
                OnShopUseItemClicked, OnShopSellItemClicked, OnShopTradeItemClicked,
                match.CanUseItem, match.CanTradeWithOpponent,
                OnShopCloseClicked);
        }

        private static string Describe(PurchaseFailureReason reason) => reason switch
        {
            PurchaseFailureReason.InsufficientFunds => "not enough money",
            PurchaseFailureReason.CargoFull => "cargo hold is full",
            _ => reason.ToString()
        };

        private static string Describe(SellFailureReason reason) => reason switch
        {
            SellFailureReason.ItemNotHeld => "item no longer held",
            _ => reason.ToString()
        };

        private static string Describe(RepairFailureReason reason) => reason switch
        {
            RepairFailureReason.AlreadyAtMax => "already at max",
            RepairFailureReason.InsufficientFunds => "not enough money",
            _ => reason.ToString()
        };

        private static string Describe(AcceptJobFailureReason reason) => reason switch
        {
            AcceptJobFailureReason.AlreadyHasActiveJob => "you already have an active job",
            _ => reason.ToString()
        };

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

            // No RefreshView() here — Update()'s own visibility block
            // handles populating JobBoardScreen on the rising edge
            // (nothing's selected yet, so there's no waypoint to show),
            // and clears any lingering waypoint on the falling edge. This
            // handler's own "close" path is effectively unreachable in
            // practice anyway: chrome (which hosts this button) is hidden
            // the entire time showJobBoard is true.
        }

        private void OnJobBoardAcceptClicked(JobDefinition job)
        {
            var result = match.AcceptJob(job);
            if (result.Success)
            {
                // Nothing left to browse for once a job's active — close
                // outright rather than staying open on an now-unusable
                // list (mirrors DrawJobBoardPanel's own old early-return
                // for "already have an active job").
                showJobBoard = false;
                selectedJobOffer = null;
                RefreshView();
            }
            else
            {
                jobBoardStatusMessage = $"Accept failed — {Describe(result.FailureReason)}.";
                RefreshJobBoardScreen();
            }
        }

        // Mirrors ContinueEscapeIntercept/OnShopBuyClicked's own "fires
        // a domain call, then re-renders" shape, but for the map instead
        // of the screen itself — this is what makes only the currently
        // selected offer's destination show as a waypoint (the story's
        // core ask), instead of the old behavior of showing every
        // offered job's destination simultaneously with no way to tell
        // which was which. Marking it as a waypoint alone isn't enough if
        // it's off-screen, though — also pans/flashes the camera there,
        // reusing OnLocatePlayerClicked's exact mechanic, so selecting a
        // job actually reveals where it is rather than just marking a
        // point the player has to go hunting for.
        private void OnJobBoardSelectionChanged(JobDefinition job)
        {
            selectedJobOffer = job;
            RefreshView();

            if (job == null)
                return;

            var worldPosition = HexLayout.AxialToWorld(job.Destination, hexRadius);
            cameraController.PanTo(worldPosition);
            mapView.FlashHex(job.Destination, LocateFlashColor, LocateFlashDuration);
        }

        private void OnJobBoardCloseClicked() => showJobBoard = false;

        private void RefreshJobBoardScreen()
        {
            var playerColor = match.CurrentPlayer == match.PlayerOne ? PlayerOneColor : PlayerTwoColor;

            jobBoardScreen.Refresh(
                HexDisplayName(match.CurrentPlayer.Position), playerColor,
                jobOffer, jobBoardStatusMessage, HexDisplayName,
                OnJobBoardAcceptClicked, OnJobBoardSelectionChanged, OnJobBoardCloseClicked);
        }

        // Mirrors DrawActiveJobStatus's own three branches, now as plain
        // strings for MatchHudChrome.SetActiveJob instead of GUILayout
        // calls. ActionVisible is false only for BountyHunting — that
        // fight triggers automatically on arrival, there's nothing to tap.
        // Also folds in ActionInteractable so the Mine/Deliver hints below
        // (why the button's grayed out right now) can share this method's
        // job-phase branching instead of duplicating it in the caller.
        private (string StatusText, string ActionLabel, bool ActionVisible, bool ActionInteractable) DescribeActiveJobStatus(JobDefinition job)
        {
            if (job.Type == JobType.BountyHunting)
            {
                return ($"Bounty ({job.BountyTier}): defeat the pirate marked at {HexDisplayName(job.Destination)} " +
                    $"(highlighted on the map) — reward ${job.Reward}. Escaping voids the job and costs a ${job.Reward / 10} penalty.",
                    string.Empty, false, false);
            }

            if (job.Type == JobType.Mining && !match.CurrentPlayer.HasMinedCargo)
            {
                var miningHint = match.CanMineAsteroid ? string.Empty : " Not on an Asteroids field yet.";
                return ($"Mining: find an Asteroids field (highlighted on the map) and mine there first, then deliver to " +
                    $"{HexDisplayName(job.Destination)} — reward ${job.Reward}.{miningHint}", "Mine", true, match.CanMineAsteroid);
            }

            var verb = job.Type == JobType.Mining ? "Deliver the minerals" : "Drop off the passenger";
            var deliverHint = match.CanDeliverJob ? string.Empty
                : match.CurrentPlayer.Position == job.Destination
                    ? " You're here — Deliver will appear once you've rolled dice this turn."
                    : " Travel there, then Deliver will appear.";
            return ($"{job.Type}: {verb} at {HexDisplayName(job.Destination)} (highlighted on the map) — reward ${job.Reward}.{deliverHint}",
                $"Deliver — ${job.Reward}", true, match.CanDeliverJob);
        }

        private void OnActiveJobActionClicked()
        {
            if (match.CanMineAsteroid)
            {
                match.MineAsteroid();
                lastMessage = "Mined — now deliver the cargo.";
            }
            else if (match.CanDeliverJob)
            {
                var reward = match.CurrentPlayer.ActiveJob.Reward;
                match.DeliverJob();
                lastMessage = $"Delivered — earned ${reward}.";
            }

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
            showShop = false;
            showJobBoard = false;
            jobOfferRolledThisTurn = false;
            lastMessage = null;
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

            var chromeVisible = !awaitingHandoff && !match.IsComplete && !match.IsInEngagement && !showShop && !showJobBoard;
            chrome.SetVisible(chromeVisible);

            // Same "explicit state-change call sites" discipline chrome's
            // dice tray already uses — refreshed once on the frame the
            // engagement actually starts (nothing else currently signals
            // that moment directly), and otherwise only from the click
            // handlers below as the fight progresses. Hidden during any
            // mid-fight hand-off too — handoffScreen (a higher
            // sortingOrder) already covers it, but this also stops it
            // refreshing/rebuilding buttons underneath that hand-off.
            var engagementVisible = !awaitingHandoff && !match.IsComplete && match.IsInEngagement && !awaitingEngagementHandoff;
            engagementScreen.SetVisible(engagementVisible);

            // A new engagement always starts with whoever's own match turn
            // it is holding the device (they're the one who just moved
            // onto a hex or tapped Attack) — deviceHolder tracks this so
            // HandOffDeviceTo knows when a hand-off screen is actually
            // needed versus when the right person already has it (see
            // HandOffDeviceTo/AdvanceAfter*). Keyed off the session
            // INSTANCE, not the visibility edge below — a mid-fight
            // hand-off also flips engagementVisible false-then-true, and
            // resetting on that edge instead would stomp the deviceHolder/
            // lastRoundBanner state the hand-off's own callback just set.
            var activeSession = match.IsInEngagement ? match.ActiveEngagement : null;
            if (activeSession != trackedEngagementSession)
            {
                trackedEngagementSession = activeSession;
                if (activeSession != null)
                {
                    deviceHolder = RoundAttacker.Player;
                    lastRoundBanner = (string.Empty, Color.white, false);
                    lastEventDetail = null;
                }
            }

            if (engagementVisible && !wasEngagementVisible)
                RefreshEngagementScreen();
            wasEngagementVisible = engagementVisible;

            // Shop is a UI-only toggle (not a domain state like
            // IsInEngagement), so it self-corrects to false the moment the
            // conditions that allowed opening it stop holding (ran out of
            // actions, moved off the planet, match ended) — mirrors
            // DrawMainPanel's old "else showShop = false" fallback for the
            // legacy panel this replaces.
            if (showShop && !(match.IsCurrentPlayerOnPlanet && match.CanShop))
                showShop = false;

            var shopVisible = !awaitingHandoff && !match.IsComplete && showShop;
            shopScreen.SetVisible(shopVisible);
            if (shopVisible && !wasShopVisible)
            {
                shopStatusMessage = null;
                RefreshShopScreen();
            }
            wasShopVisible = shopVisible;

            // Same self-correcting, UI-only-toggle reasoning as Shop
            // above — plus ActiveJob == null, since accepting a second
            // job always fails once one is active, so there's nothing
            // useful to browse for (mirrors DrawJobBoardPanel's own old
            // early-return for this exact case).
            if (showJobBoard && !(match.IsCurrentPlayerOnPlanet && match.CanAcceptJob && match.CurrentPlayer.ActiveJob == null))
                showJobBoard = false;

            var jobBoardVisible = !awaitingHandoff && !match.IsComplete && showJobBoard;
            jobBoardScreen.SetVisible(jobBoardVisible);
            if (jobBoardVisible && !wasJobBoardVisible)
            {
                selectedJobOffer = null;
                jobBoardStatusMessage = null;
                RefreshJobBoardScreen();
            }
            // The board can also close with an offer still selected (e.g.
            // the self-correction above firing mid-Detail-card) — clear
            // the waypoint that selection was driving rather than leaving
            // it stuck on the map with nothing open to have caused it.
            else if (!jobBoardVisible && wasJobBoardVisible && selectedJobOffer != null)
            {
                selectedJobOffer = null;
                RefreshView();
            }
            wasJobBoardVisible = jobBoardVisible;

            // The map camera's own viewport shrinks to leave room for
            // whichever bottom-docked UI is actually showing — the dice
            // bar under chrome, or JobBoardScreen's own bottom-anchored
            // panel (see its ReservedFraction/class comment: unlike every
            // other full-screen modal, this one specifically needs the
            // map to stay visible above it, since selecting a job pans
            // the camera to its destination — see OnJobBoardSelectionChanged
            // — and that would land right behind a centered panel
            // otherwise) — rather than either floating on top of a
            // full-screen map. Resets to full-screen whenever neither is
            // showing, so hand-off/engagement/the win screen don't leave
            // the map needlessly cropped underneath them.
            var reservedFraction = chromeVisible ? chrome.DiceBarReservedFraction
                : jobBoardVisible ? jobBoardScreen.ReservedFraction
                : 0f;
            cameraController.SetBottomReservedFraction(reservedFraction);

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

            // Gated on ActiveJob == null too — nothing useful to browse
            // for once a job's already active (see the matching
            // self-correction in the visibility block above).
            var canVisitJobBoardHere = match.IsCurrentPlayerOnPlanet && match.CanAcceptJob && match.CurrentPlayer.ActiveJob == null;
            chrome.SetJobBoard(canVisitJobBoardHere, showJobBoard ? "Close Job Board" : "Open Job Board");

            if (match.CurrentPlayer.ActiveJob is { } activeJob)
            {
                var (statusText, actionLabel, actionVisible, actionInteractable) = DescribeActiveJobStatus(activeJob);
                chrome.SetActiveJob(true, statusText, actionLabel, actionVisible, actionInteractable);
            }
            else
            {
                chrome.SetActiveJob(false, string.Empty, string.Empty, false, false);
            }

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

            // Same reasoning again — nothing from the legacy IMGUI HUD
            // renders during an engagement either, now that it has its
            // own full-screen UGUI EngagementScreen. Not just cosmetic:
            // legacy OnGUI content always draws on top of every UGUI
            // Canvas regardless of sortingOrder (see MatchHudChrome's own
            // header comment), so without this guard EngagementScreen
            // would be invisibly stuck underneath any leftover legacy
            // content — e.g. the Wormhole travel panel left open right
            // before the fight triggered.
            if (match.IsInEngagement)
                return;

            // Shop and Job Board each have their own full-screen UGUI
            // screen now too (see RefreshShopScreen/RefreshJobBoardScreen)
            // — same reasoning as the engagement guard above, otherwise
            // either would be invisibly stuck underneath any leftover
            // legacy content.
            if (showShop)
                return;
            if (showJobBoard)
                return;

            // IMGUI has no CanvasScaler equivalent — GUI.skin's default
            // font size is tuned for desktop pixel density and doesn't
            // scale with the device's actual pixel ratio at all, so it
            // renders illegibly tiny on a phone (the same physical-vs-
            // logical scale problem the UGUI chrome had, just never
            // fixed here since this whole region is scheduled to be
            // replaced by dedicated UGUI screens soon — Trade/Wormhole,
            // Order #11-12). This is a deliberately rough interim bump,
            // not a real fix.
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
            DrawMainPanel();
            GUILayout.EndArea();
        }

        // Shop and Job Board's own content now live entirely in
        // ShopScreen/JobBoardScreen (see RefreshShopScreen/
        // RefreshJobBoardScreen and OnGUI's own showShop/showJobBoard
        // guards above) — active job tracking moved to a persistent
        // MatchHudChrome strip (see SetActiveJob). Only Wormhole/Trade
        // are still legacy IMGUI here, each with its own pending redesign
        // story.
        private void DrawMainPanel()
        {
            if (match.CanTravelWormhole)
                DrawWormholeTravelPanel();

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

        // Standing trade offer while sharing a hex with the opponent —
        // deliberately independent of the Shop/planet gating
        // (CanTradeWithOpponent doesn't require being on a planet, so
        // this can't live inside ShopScreen or it'd be unreachable
        // off-planet). Note: this is a different Trade than the per-item
        // Trade action on a Cargo item's ShopScreen detail card — that
        // one only offers items already IN the shop's own Cargo list.
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
                    RefreshView();
                }
            }
        }

        // "you"/"opponent" reads fine for an NPC fight (only one real,
        // controllable party) but is actively misleading in PvP the
        // moment the device gets physically handed to the OTHER player
        // mid-fight — "you attack" means something different depending
        // on who's currently holding it. PvP always uses both players'
        // real names instead, computed once per log line by
        // GetCombatLogLabels below, so the log reads correctly regardless
        // of who's looking at the screen. NPC fights get the exact same
        // text as before ("you"/"opponent"/"your"/"opponent's" passed in
        // as literal label strings), so nothing changes there.
        private (string PlayerLabel, string PlayerPossessive, string OpponentLabel, string OpponentPossessive) GetCombatLogLabels(EngagementSession session)
        {
            if (!session.IsPvP)
                return ("you", "your", "opponent", "opponent's");

            var playerName = session.Player.DisplayName;
            var opponentName = match.OtherPlayer.DisplayName;
            return (playerName, $"{playerName}'s", opponentName, $"{opponentName}'s");
        }

        // "you" is second person (needs the bare verb — "you win"), but
        // every other label this session ever produces ("opponent", or a
        // real PvP DisplayName) is third-person singular and needs the
        // "-s" form ("opponent wins", "Player One wins") — a template
        // written for one reads as broken English for the other ("you
        // wins!"). Every Describe* sentence below that conjugates a verb
        // for whichever label is its subject picks the right form through
        // this instead of hardcoding one.
        private static string Conjugate(string label, string baseForm, string thirdPersonForm) =>
            label == "you" ? baseForm : thirdPersonForm;

        // selfLabel/otherLabel are whichever side is actually attempting
        // the escape — OnAttemptEscapeClicked's NPC path and
        // ContinueEscapeIntercept both figure out which side that is
        // (PlayerLabel or OpponentLabel) before calling this, since
        // EscapeAttemptResult.Roll/Total are always the escapee's own
        // roll regardless of which side that is.
        private static string DescribeEscape(EscapeAttemptResult result, string selfLabel, string otherLabel) =>
            result.Success
                ? $"Escape: {selfLabel} rolled {result.Roll} = {result.Total}, {otherLabel} rolled {result.OpponentRoll} = {result.OpponentTotal} — escaped!"
                : $"Escape: {selfLabel} rolled {result.Roll} = {result.Total}, {otherLabel} rolled {result.OpponentRoll} = {result.OpponentTotal} — failed, lost 1 Energy.";

        private static string DescribeInitiative(InitiativeResult result, string playerLabel, string opponentLabel)
        {
            // WasPlayerCriticalFailure is always about the PLAYER's own
            // roll (see CombatResolver.ResolveInitiative) — it was
            // previously tacked onto the opponent's roll clause below,
            // reading as if the opponent had fumbled when it was really
            // the player's natural 1.
            var fumble = result.WasPlayerCriticalFailure ? " (natural 1 — critical failure!)" : "";
            return $"Speed: {playerLabel} rolled {result.PlayerSpeedRoll} = {result.PlayerSpeedTotal}{fumble}, " +
                $"{opponentLabel} rolled {result.OpponentSpeedRoll} = {result.OpponentSpeedTotal} — " +
                (result.Attacker == RoundAttacker.Player
                    ? $"{playerLabel} {Conjugate(playerLabel, "attack", "attacks")}."
                    : $"{opponentLabel} {Conjugate(opponentLabel, "attack", "attacks")}.");
        }

        private static string DescribeAttack(RoundResult result, string playerLabel, string playerPossessive, string opponentLabel, string opponentPossessive)
        {
            if (result.Attacker == RoundAttacker.Player)
            {
                var crit = result.WasCriticalHit ? " CRITICAL HIT!" : "";
                // DefenderBraced used to be impossible on this branch (the
                // opponent could never brace) so it was never mentioned —
                // now that PvP opponents can brace when defending against
                // the player's own attack, the log needs to say so, same
                // as the other branch below already does.
                var braceNote = result.DefenderBraced ? " (braced)" : "";
                return $"Weapons: {playerLabel} rolled {result.AttackRoll} = {result.AttackTotal}, {opponentPossessive} Shields rolled {result.DefenseRoll} = {result.DefenseTotal}{braceNote} — " +
                    (result.HitLanded ? $"{opponentLabel} lost {result.Damage} Hull.{crit}" : "missed.");
            }

            var opponentCrit = result.WasCriticalHit ? " CRITICAL HIT!" : "";
            var opponentBraceNote = result.DefenderBraced ? " (braced)" : "";
            return $"Weapons: {opponentLabel} rolled {result.AttackRoll} = {result.AttackTotal}, {playerPossessive} Shields rolled {result.DefenseRoll} = {result.DefenseTotal}{opponentBraceNote} — " +
                (result.HitLanded ? $"{playerLabel} lost {result.Damage} Hull.{opponentCrit}" : "missed.");
        }

        // Short, punchy versions of the log lines above, for the
        // prominent EngagementScreen banner — the roll-by-roll numbers
        // behind it show separately, on their own line (see
        // DescribeInitiative/DescribeAttack/DescribeEscape and
        // EngagementScreen's own rollDetailText), but shouldn't be the
        // only place a round's actual outcome is visible.
        private static (string Text, Color Color, bool IsCrit) DescribeRoundBanner(RoundResult result, string playerLabel, string opponentLabel)
        {
            var attackerLabel = result.Attacker == RoundAttacker.Player ? playerLabel : opponentLabel;
            var defenderLabel = result.Attacker == RoundAttacker.Player ? opponentLabel : playerLabel;
            var takes = Conjugate(defenderLabel, "take", "takes");

            if (!result.HitLanded)
                return ($"{attackerLabel} missed!", BannerMissColor, false);
            if (result.WasCriticalHit)
                return ($"CRITICAL HIT! {defenderLabel} {takes} {result.Damage} damage!", BannerCritColor, true);
            return ($"{defenderLabel} {takes} {result.Damage} damage.", BannerHitColor, false);
        }

        private static (string Text, Color Color, bool IsCrit) DescribeInitiativeBanner(InitiativeResult result, string playerLabel, string opponentLabel) =>
            result.Attacker == RoundAttacker.Player
                ? ($"{playerLabel} {Conjugate(playerLabel, "win", "wins")} initiative!", BannerInitiativeColor, false)
                : ($"{opponentLabel} {Conjugate(opponentLabel, "win", "wins")} initiative!", BannerInitiativeColor, false);

        private static (string Text, Color Color, bool IsCrit) DescribeEscapeBanner(EscapeAttemptResult result, string selfLabel) =>
            result.Success
                ? ($"{selfLabel} escaped!", BannerEscapeColor, false)
                : ($"{selfLabel}'s escape failed!", BannerMissColor, false);

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

            // Only the currently SELECTED offer's destination, not every
            // offer's at once — showing all of them simultaneously gave
            // no way to tell which waypoint belonged to which offer (the
            // story's own explicit complaint). Set by
            // OnJobBoardSelectionChanged as JobBoardScreen's own Detail
            // card selection changes.
            if (selectedJobOffer != null)
                waypoints.Add(selectedJobOffer.Destination);

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
                pendingTarget.HasValue ? "Confirm this move?" : null,
                OnRollDiceClicked,
                OnDieClicked,
                ConfirmPendingMove,
                CancelPendingMove);
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
