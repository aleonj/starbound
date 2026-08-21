using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;
using StarBound.Movement;

namespace StarBound.UI
{
    // The always-visible in-match HUD chrome — turn header, ship stats,
    // engagement wins, actions remaining, progression status, dice tray,
    // Attack/End Turn — rebuilt in UGUI from MatchHud's old IMGUI
    // DrawMainPanel. Shop/Job Board/Trade/Wormhole/Engagement stay IMGUI
    // for now (each has its own separate backlog story) and simply draw
    // on top of this, since legacy OnGUI content always renders above any
    // Canvas regardless of sortingOrder.
    //
    // Unlike the other UGUI screens (TurnHandoffScreen, WinScreen, etc.),
    // which are created once in DemoBootstrap and reused across matches,
    // this one is created fresh per match by MatchHud itself —
    // every value it shows is per-match/per-turn state, so there's no
    // reason to survive a match boundary.
    //
    // Visual language: rounded, rim-glowing "glass" panels/buttons (see
    // GlassPanel.shader/GlassPanelMaterials) echoing the map's own
    // GlassHex look, and glowing SDF stat icons (see IconGlyph.shader/
    // IconGlyphMaterials) instead of plain text numbers.
    public class MatchHudChrome : MonoBehaviour
    {
        // Below PopupDialog's 150 — nothing else currently claims
        // anything under 100, and chrome should never need to win over
        // any of the overlay/flow screens.
        private const int SortingOrder = 50;

        // Reference resolution is in LOGICAL POINTS (~iPhone-13-class
        // width), not physical pixels — every size below is authored in
        // that same point-scale. (Round 1 of this story used a
        // physical-pixel-scale reference resolution while sizing
        // everything as if it were point-scale, which rendered at
        // roughly 1/3 the intended size on a real device — see the
        // Notion story's progress notes.) matchWidthOrHeight = 0 (pure
        // width-match) requires the game to be locked to portrait (see
        // ProjectSettings' defaultScreenOrientation) — in landscape,
        // "width" becomes the long edge and this panel would balloon.
        private static readonly Vector2 ReferenceResolution = new(400f, 866f);
        private const float ScalerMatchWidthOrHeight = 0f;

        // Sized bottom-up from the hard constraint: up to 6 dice — the 5
        // rolled (MovementDiceSet.Dice is a fixed 5-element array) plus
        // the device-granted Wormhole die (see MatchHud.OnRollDiceClicked)
        // when the current player holds the Wormhole Device — must fit on
        // one row without overflowing, each still clearing the ~44pt
        // minimum touch-target floor. Was 60f sized for exactly 5; a 6th
        // die at that width overflowed DiceBarWidth's fixed panel (found
        // as a real, visible bug in Play Mode once the Wormhole die
        // shipped).
        private const float DieButtonWidth = 48f;
        // Taller than it is wide now — icon-only dice didn't reliably
        // communicate their terrain (confirmed against real screenshots,
        // twice), so every die button carries a short text abbreviation
        // under its icon too. The extra height is for that label.
        private const float DieButtonHeight = 70f;
        private const float DiceRowSpacing = 8f;

        // 5 * DieButtonWidth + 4 * DiceRowSpacing + 2 * panel padding —
        // the panel is sized to exactly fit the dice row, which is why
        // it ends up close to full screen width. "Corner-docked" is
        // about the anchor point, not narrowness.
        private const float PanelWidth = 360f;
        private const float PanelPaddingHorizontal = 16f;
        private const float PanelPaddingBottom = 14f;

        // Bottom dice bar — a second, independent panel so dice stay
        // reachable (and the top panel's own height never affects them)
        // regardless of whether the top panel is expanded or collapsed.
        // Near-full reference width, same reasoning as PanelWidth above:
        // sized to comfortably fit 5 dice at real touch-target size.
        private const float DiceBarWidth = 380f;
        private const float DiceBarMarginBottom = 20f;
        // Extra breathing room reserved above the panel itself (see
        // DiceBarReservedFraction) so the map's cropped bottom edge
        // doesn't sit flush against the panel's glowing top edge.
        private const float DiceBarTopGap = 16f;

        // Die icons fill most of the button (padding ~10% of button
        // size) — noticeably bigger than the header utility buttons
        // (collapse/locate, padding ~22%), which stay small deliberately
        // since those glyphs are simpler and the buttons themselves are
        // smaller (36pt vs 60pt).
        private const float DieIconPaddingFraction = 0.10f;
        private const float UtilityIconPaddingFraction = 0.22f;

        private const float IconSize = 24f;
        private const float PrimaryButtonHeight = 52f;

        private static readonly Color IdleDieColor = new(0.22f, 0.22f, 0.26f);
        private static readonly Color SelectedDieColor = new(0.2f, 0.5f, 0.8f);
        // Deliberately darker than IdleDieColor (was lighter before,
        // which read backwards — a spent die should recede, not stand
        // out more than one still available to pick) — combined with
        // CreateIconButton's automatic icon/label dimming for
        // non-interactable buttons, this makes spent dice clearly
        // distinct at a glance instead of nearly identical to idle ones.
        private static readonly Color SpentDieColor = new(0.14f, 0.14f, 0.16f);
        private static readonly Color ConfirmColor = new(0.2f, 0.7f, 0.3f);
        private static readonly Color AttackColor = new(0.7f, 0.25f, 0.25f);
        // Same RGB as AttackColor above (both read as "destructive/
        // negative" red) — named separately here for what it means in
        // this context: backing out of a pending move, not an attack.
        private static readonly Color CancelColor = new(0.7f, 0.25f, 0.25f);
        private static readonly Color UtilityColor = new(0.25f, 0.45f, 0.65f);
        private static readonly Color DisabledColor = new(0.3f, 0.3f, 0.3f);
        private static readonly Color DividerColor = new(1f, 1f, 1f, 0.12f);
        // Deliberately lighter than IdleDieColor — that color nearly
        // vanished against the panel's own dark fill at the collapse
        // toggle's small size with no strong rim glow to compensate.
        private static readonly Color ToggleColor = new(0.42f, 0.48f, 0.58f);
        // Matches the space background's own dark tone (see
        // GalaxyBackground/ClearSpace terrain colors) — used only for
        // diceBackdrop, which exists purely to hide the stale render
        // artifact the map camera leaves behind in the area it no longer
        // covers (see MapCameraController.SetBottomReservedFraction);
        // opaque and full-bleed, unlike every other panel here.
        private static readonly Color DiceBackdropColor = new(0.04f, 0.04f, 0.07f, 1f);

        private CanvasScaler scaler;
        private RectTransform canvasRect;
        private GameObject panel;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text turnLabelText;
        private Text headerText;
        private Button locateButton;
        private GameObject collapsibleContent;
        private Button collapseToggleButton;
        private Image collapseToggleIcon;
        private bool isCollapsed;

        private RectTransform diceBackdropRect;
        private GameObject dicePanel;
        private RectTransform dicePanelRect;
        private Material dicePanelMaterial;

        private Text hullValueText;
        private Text energyValueText;
        private Text weaponsValueText;
        private Text shieldsValueText;
        private Text speedValueText;
        private Text moneyValueText;
        private Text cargoValueText;

        private Text winsText;
        private Text actionsText;
        private Text phaseText;
        private Text eventText;
        private Text goalText;
        private Text diceInstructionText;
        private Transform diceButtonRow;
        private Button shopButton;
        private Text shopLabel;
        private Material shopMaterial;
        private Button jobBoardButton;
        private Text jobBoardLabel;
        private Material jobBoardMaterial;
        private Button heldItemsButton;
        private GameObject activeJobDivider;
        private Text activeJobStatusText;
        private Button activeJobActionButton;
        private Text activeJobActionLabel;
        private Material activeJobActionMaterial;
        private GameObject attackDivider;
        private Button attackButton;
        private Text attackLabel;
        private Button tradeButton;
        private Text tradeLabel;
        private Button endTurnButton;
        private Material endTurnMaterial;
        private Text messageText;

        private void Awake() => BuildUI();

        // Keeps the panel's GlassPanel material in sync with its actual
        // point-size, which changes at runtime as the ContentSizeFitter
        // grows/shrinks the panel's height (Event/Goal/Message rows
        // toggle visibility) — without this, rounded corners would
        // desync from the panel's real bounds. LateUpdate gives layout a
        // frame to settle first; a one-frame lag on a slow-changing HUD
        // is imperceptible.
        private void LateUpdate()
        {
            if (panelMaterial != null)
                panelMaterial.SetVector("_Size", panelRect.rect.size);
            // Dice bar's height can also shift slightly frame to frame —
            // its instruction text changes between "Roll the dice..."/
            // "Pick a die..."/"No actions left..." at the same width,
            // which can wrap differently and change the panel's content-
            // fitted height.
            if (dicePanelMaterial != null)
                dicePanelMaterial.SetVector("_Size", dicePanelRect.rect.size);

            // Keeps the opaque backdrop's height matched to exactly how
            // much of the screen the camera currently has reserved for
            // it (see DiceBarReservedFraction / SetBottomReservedFraction)
            // — has to stay in lockstep every frame since the reserved
            // amount itself changes with the dice bar's content.
            if (diceBackdropRect != null)
            {
                var backdropHeight = dicePanelRect.rect.height + DiceBarMarginBottom + DiceBarTopGap;
                diceBackdropRect.sizeDelta = new Vector2(diceBackdropRect.sizeDelta.x, backdropHeight);
            }
        }

        // Wires the handlers that never change for this chrome instance's
        // lifetime (Attack/End Turn) — kept separate from the per-frame
        // Set*() calls below so refreshing values every frame doesn't
        // also re-allocate a fresh closure and re-register a listener
        // every frame.
        public void Initialize(Action onAttack, Action onEndTurn, Action onToggleShop, Action onToggleJobBoard, Action onLocatePlayer, Action onActiveJobAction, Action onToggleHeldItems, Action onToggleTrade)
        {
            attackButton.onClick.AddListener(() => onAttack?.Invoke());
            endTurnButton.onClick.AddListener(() => onEndTurn?.Invoke());
            shopButton.onClick.AddListener(() => onToggleShop?.Invoke());
            jobBoardButton.onClick.AddListener(() => onToggleJobBoard?.Invoke());
            locateButton.onClick.AddListener(() => onLocatePlayer?.Invoke());
            activeJobActionButton.onClick.AddListener(() => onActiveJobAction?.Invoke());
            heldItemsButton.onClick.AddListener(() => onToggleHeldItems?.Invoke());
            tradeButton.onClick.AddListener(() => onToggleTrade?.Invoke());
        }

        public void SetVisible(bool visible)
        {
            panel.SetActive(visible);
            dicePanel.SetActive(visible);
        }

        // Fraction (0-1) of screen height the dice bar currently occupies
        // at the bottom (panel height + its own bottom margin + a little
        // breathing room above it) — MatchHud feeds this to
        // MapCameraController.SetBottomReservedFraction so the map
        // camera itself renders into a shorter viewport instead of the
        // dice bar floating on top of a full-screen map, which could
        // otherwise hide the very hex a player is trying to reach.
        //
        // Deliberately a pure ratio computed entirely within canvas
        // space (dicePanelRect's height against the Canvas's OWN
        // RectTransform height), not converted through device pixels —
        // an earlier version multiplied by scaler.scaleFactor and
        // divided by Screen.height on the camera side, which silently
        // produced a fraction ~40x too small under Device Simulator: its
        // Camera.pixelRect (the camera's actual render target) doesn't
        // match what Screen.height reports at all (confirmed via a
        // runtime log — pixelRect.height was ~1004px against a reported
        // Screen.height of 2532). canvasRect.rect.height is in the same
        // canvas-space units as dicePanelRect regardless of that
        // mismatch, so this ratio is correct no matter what the actual
        // render target resolution turns out to be.
        public float DiceBarReservedFraction =>
            canvasRect.rect.height > 0f
                ? (dicePanelRect.rect.height + DiceBarMarginBottom + DiceBarTopGap) / canvasRect.rect.height
                : 0f;

        // A player-controlled UI preference — the always-expanded panel
        // covers enough of the map that a player who just wants to look
        // around needs a way to shrink it down to the header strip. Stays
        // collapsed across turns until the player taps it open again. Also
        // triggered automatically once the player rolls dice (see Collapse
        // below) — that's the moment the map itself becomes what the
        // player needs to see most (where the rolled dice can actually
        // move them), so the panel gets out of the way on its own instead
        // of waiting for a manual tap.
        private void SetCollapsed(bool collapsed)
        {
            isCollapsed = collapsed;
            collapsibleContent.SetActive(!collapsed);
            // Chevron points toward what tapping will do — down (reveal
            // what's below) while collapsed, up (tuck it away) while
            // expanded — same glowing SDF icon language as the stat row.
            collapseToggleIcon.material = IconGlyphMaterials.Get(
                collapsed ? IconGlyphMaterials.Glyph.ChevronDown : IconGlyphMaterials.Glyph.ChevronUp);
        }

        // MatchHud calls this right after a successful dice roll — see
        // SetCollapsed's own comment for why. A no-op (still collapses
        // cleanly) if the panel was already collapsed.
        public void Collapse() => SetCollapsed(true);

        // "Turn:" itself is a static label (see BuildUI's nameColumn) —
        // this only ever sets the player's name on the line below it.
        public void SetHeader(string playerName, Color color)
        {
            headerText.text = playerName;
            headerText.color = color;
            accentBar.color = color;
            panelMaterial.SetColor("_RimColor", color);
            dicePanelMaterial.SetColor("_RimColor", color);
        }

        public void SetStats(Ship ship)
        {
            hullValueText.text = ship.GetStat(CoreStat.Hull).ToString();
            energyValueText.text = ship.GetStat(CoreStat.Energy).ToString();
            weaponsValueText.text = ship.GetStat(CoreStat.Weapons).ToString();
            shieldsValueText.text = ship.GetStat(CoreStat.Shields).ToString();
            speedValueText.text = ship.GetStat(CoreStat.Speed).ToString();
            moneyValueText.text = $"${ship.Money}";
            cargoValueText.text = $"{ship.HeldItems.Count}/{ship.CargoCapacity}";
        }

        public void SetWins(string text) => winsText.text = text;

        public void SetActionsRemaining(string text) => actionsText.text = text;

        public void SetProgression(string phase, string eventLine, string goalLine)
        {
            phaseText.text = phase;

            eventText.gameObject.SetActive(!string.IsNullOrEmpty(eventLine));
            eventText.text = eventLine ?? string.Empty;

            goalText.gameObject.SetActive(!string.IsNullOrEmpty(goalLine));
            goalText.text = goalLine ?? string.Empty;
        }

        public void SetAttack(bool visible, string label)
        {
            attackDivider.SetActive(visible);
            attackButton.gameObject.SetActive(visible);
            if (visible)
                attackLabel.text = label;
        }

        // Sits directly under Attack, sharing its divider — trading is the
        // other thing sharing a hex with the opponent lets you do, so
        // rather than a persistent "Open Trade" utility button always
        // competing with Shop/Job Board for space, it only appears at all
        // in the same moment Attack does (same visible/hidden shape as
        // SetAttack, not the always-visible-but-sometimes-disabled shape
        // Shop/Job Board's toggles use).
        public void SetTrade(bool visible, string label)
        {
            tradeButton.gameObject.SetActive(visible);
            if (visible)
                tradeLabel.text = label;
        }

        // Tracking an ACTIVE job (status + Mine/Deliver) is deliberately
        // NOT part of the full-screen JobBoardScreen (which only handles
        // browsing/accepting offers) — it stays here as a persistent,
        // always-visible strip, exactly like Attack above, so tapping
        // Deliver never requires opening a screen first. actionVisible is
        // false for a Bounty job (the fight triggers automatically on
        // arrival, there's nothing to tap); actionInteractable grays the
        // button out (not just disables it — see JobBoardScreen's own
        // Buy-button fix for why interactable alone isn't enough to read
        // as disabled) when the player isn't in the right place/phase yet
        // (e.g. "not on an Asteroids field").
        public void SetActiveJob(bool visible, string statusText, string actionLabel, bool actionVisible, bool actionInteractable)
        {
            activeJobDivider.SetActive(visible);
            activeJobStatusText.gameObject.SetActive(visible);
            if (visible)
                activeJobStatusText.text = statusText;

            activeJobActionButton.gameObject.SetActive(visible && actionVisible);
            if (!actionVisible)
                return;

            activeJobActionLabel.text = actionLabel;
            activeJobActionButton.interactable = actionInteractable;
            var color = actionInteractable ? UtilityColor : DisabledColor;
            activeJobActionButton.GetComponent<Image>().color = color;
            activeJobActionMaterial.SetColor("_RimColor", color);
        }

        // Shop and Job Board's own content (browsing/buying, browsing/
        // accepting) each have their own dedicated full-screen UGUI
        // screen now (see RefreshShopScreen/RefreshJobBoardScreen) — these
        // two methods are just their always-visible toggle buttons, kept
        // in chrome so they don't compete with the map for the same
        // on-screen space at all times.
        public void SetShop(bool interactable, string label)
        {
            var color = interactable ? UtilityColor : DisabledColor;
            shopButton.interactable = interactable;
            shopButton.GetComponent<Image>().color = color;
            shopMaterial.SetColor("_RimColor", color);
            shopLabel.text = label;
        }

        public void SetJobBoard(bool interactable, string label)
        {
            var color = interactable ? UtilityColor : DisabledColor;
            jobBoardButton.interactable = interactable;
            jobBoardButton.GetComponent<Image>().color = color;
            jobBoardMaterial.SetColor("_RimColor", color);
            jobBoardLabel.text = label;
        }

        public void SetEndTurn(bool canEndTurn)
        {
            var color = canEndTurn ? ConfirmColor : DisabledColor;
            endTurnButton.interactable = canEndTurn;
            endTurnButton.GetComponent<Image>().color = color;
            endTurnMaterial.SetColor("_RimColor", color);
        }

        public void SetMessage(string message)
        {
            messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            messageText.text = message ?? string.Empty;
        }

        // The one relatively expensive piece (Destroy/Instantiate) — kept
        // off the per-frame path, called instead from MatchHud's existing
        // RefreshView() "state changed, resync" hook whenever the hand,
        // selection, move eligibility, or pending move actually changes.
        public void RefreshDiceTray(
            IReadOnlyList<RolledDie> dice, RolledDie selectedDie, bool hasHand, bool canRoll, bool canMove,
            string pendingMoveLabel, Action onRoll, Action<RolledDie> onDieClicked,
            Action onConfirmMove, Action onCancelMove)
        {
            // DestroyImmediate, not Destroy — Destroy defers actual
            // removal to end of frame, so the about-to-die old buttons
            // stayed structurally present (and rendered) alongside the
            // brand-new ones this method appends below for one whole
            // frame, doubling the row's child count under its
            // HorizontalLayoutGroup and producing a visible reflow
            // "flash" on every rebuild (roll, select, deselect, confirm/
            // cancel — anything that calls this). Safe here since this
            // only ever runs from ordinary runtime click handlers, never
            // from the physics loop or OnValidate. Indexed backward
            // rather than foreach, since DestroyImmediate mutates
            // diceButtonRow's live child list as it goes.
            for (var i = diceButtonRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(diceButtonRow.GetChild(i).gameObject);

            // A pending move (a die picked, then a legal hex tapped) takes
            // over the exact same row dice normally occupy — the natural
            // extension of "you just picked a die from this bar," rather
            // than a separate floating panel appearing on top of it.
            // Takes priority over every other state below.
            if (!string.IsNullOrEmpty(pendingMoveLabel))
            {
                diceInstructionText.text = pendingMoveLabel;

                var (confirmButton, _) = CreateButton(diceButtonRow, "Confirm", ConfirmColor, true,
                    GlassPanelMaterials.Style.DieButton, width: 160f, height: DieButtonHeight, fontSize: 16);
                confirmButton.onClick.AddListener(() => onConfirmMove?.Invoke());

                var (cancelButton, _) = CreateButton(diceButtonRow, "Cancel", CancelColor, true,
                    GlassPanelMaterials.Style.DieButton, width: 160f, height: DieButtonHeight, fontSize: 16);
                cancelButton.onClick.AddListener(() => onCancelMove?.Invoke());
                return;
            }

            if (!hasHand)
            {
                // Rolling now spends an action too (see Match.RollDice) —
                // with none left, offer nothing to tap rather than a
                // button that would just throw when pressed.
                if (!canRoll)
                {
                    diceInstructionText.text = "No actions left to roll dice with.";
                    return;
                }

                diceInstructionText.text = "Roll the dice to move.";
                var (rollButton, _) = CreateButton(diceButtonRow, "Roll Dice", ConfirmColor, true,
                    GlassPanelMaterials.Style.DieButton, width: 140f, height: DieButtonHeight, fontSize: 15);
                rollButton.onClick.AddListener(() => onRoll?.Invoke());
                return;
            }

            if (!canMove)
            {
                diceInstructionText.text = "No actions left to move with this turn.";
                foreach (var die in dice)
                    CreateDieButton(die, SpentDieColor, interactable: false, onDieClicked: null);
                return;
            }

            diceInstructionText.text = "Pick a die, then tap a highlighted hex on the map.";
            foreach (var die in dice)
            {
                if (die.IsSpent)
                {
                    CreateDieButton(die, SpentDieColor, interactable: false, onDieClicked: null);
                    continue;
                }

                var color = die == selectedDie ? SelectedDieColor : IdleDieColor;
                CreateDieButton(die, color, true, onDieClicked);
            }
        }

        private void CreateDieButton(RolledDie die, Color color, bool interactable, Action<RolledDie> onDieClicked)
        {
            var (dieButton, _) = CreateIconButton(diceButtonRow, TerrainGlyph(die.Terrain), color,
                GlassPanelMaterials.Style.DieButton, DieButtonWidth, interactable, DieIconPaddingFraction,
                TerrainAbbreviation(die.Terrain), DieButtonHeight);
            if (onDieClicked == null)
                return;

            var capturedDie = die;
            dieButton.onClick.AddListener(() => onDieClicked.Invoke(capturedDie));
        }

        // Every terrain a die can actually show — Planet never appears on
        // a die (see MovementDiceSet); Wormhole normally wouldn't either
        // (too rare a face to design for), but MatchHud now grants a
        // guaranteed synthetic Wormhole-terrain die whenever the current
        // player holds the device (see OnRollDiceClicked), so it needs its
        // own glyph like every other terrain here.
        private static IconGlyphMaterials.Glyph TerrainGlyph(TerrainType terrain) => terrain switch
        {
            TerrainType.ClearSpace => IconGlyphMaterials.Glyph.ClearSpace,
            TerrainType.Tradelane => IconGlyphMaterials.Glyph.Tradelane,
            TerrainType.Asteroids => IconGlyphMaterials.Glyph.Asteroids,
            TerrainType.Debris => IconGlyphMaterials.Glyph.Debris,
            TerrainType.Mines => IconGlyphMaterials.Glyph.Mines,
            TerrainType.Wormhole => IconGlyphMaterials.Glyph.Wormhole,
            _ => IconGlyphMaterials.Glyph.ClearSpace
        };

        // Icons alone didn't reliably communicate terrain across two
        // separate redesign passes — every die button now also carries
        // one of these under its icon. Abbreviated purely to fit a 60pt-
        // wide button; the two longest terrain names (ClearSpace,
        // Tradelane) don't fit at full length.
        private static string TerrainAbbreviation(TerrainType terrain) => terrain switch
        {
            TerrainType.ClearSpace => "Clear",
            TerrainType.Tradelane => "Trade",
            TerrainType.Asteroids => "Ast.",
            TerrainType.Wormhole => "W.Hole",
            _ => terrain.ToString()
        };

        private void BuildUI()
        {
            var canvasObject = new GameObject("MatchHudChromeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = ScalerMatchWidthOrHeight;
            canvasRect = (RectTransform)canvasObject.transform;

            panel = new GameObject("ChromePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(canvasObject.transform, false);

            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f);
            panelRect.sizeDelta = new Vector2(PanelWidth, panelRect.sizeDelta.y);

            // Base fill is the shader's own dark glass tint, not the
            // Image's vertex color — unlike buttons, nothing swaps this
            // panel's Image.color at runtime, so the tint lives directly
            // in the GlassPanel material (see GlassPanelMaterials).
            var panelImage = panel.GetComponent<Image>();
            panelMaterial = GlassPanelMaterials.Create(GlassPanelMaterials.Style.Panel, Color.white);
            panelImage.material = panelMaterial;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset((int)PanelPaddingHorizontal, (int)PanelPaddingHorizontal, 0, (int)PanelPaddingBottom);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // A colored strip across the top, tinted to the current
            // player's color in SetHeader — gives the panel an identity
            // "banner" instead of opening straight into plain text. Still
            // inset by the layout group's left/right padding like every
            // other child, but the top RectOffset above is 0 specifically
            // so this bar sits flush with the panel's top edge rather than
            // floating below a gap.
            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 5f;
            accentBar = accentBarObject.GetComponent<Image>();

            // Header + End Turn + collapse toggle share one row so both
            // stay reachable even when everything else is hidden — this is
            // ALL of the collapsed panel's body content, not just a title
            // bar, which is why it's kept deliberately compact (see
            // nameColumn below).
            var headerRow = new GameObject("HeaderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            headerRow.transform.SetParent(panel.transform, false);
            var headerRowLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
            headerRowLayout.spacing = 8f;
            headerRowLayout.childAlignment = TextAnchor.MiddleLeft;
            headerRowLayout.childForceExpandWidth = false;
            headerRowLayout.childForceExpandHeight = false;
            headerRowLayout.childControlWidth = true;
            headerRowLayout.childControlHeight = true;

            // "Turn:" / player name stacked on two short lines instead of
            // one long "Turn: Player One" line — frees up enough width in
            // the row for End Turn to sit right next to it, rather than
            // needing its own full-width row below (which used to be the
            // ONLY thing shown once the panel was collapsed, but still
            // needed a divider + its own row height on top of the header).
            var nameColumn = new GameObject("NameColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            nameColumn.transform.SetParent(headerRow.transform, false);
            var nameColumnLayout = nameColumn.GetComponent<VerticalLayoutGroup>();
            nameColumnLayout.spacing = 0f;
            nameColumnLayout.childAlignment = TextAnchor.UpperLeft;
            nameColumnLayout.childForceExpandWidth = true;
            nameColumnLayout.childForceExpandHeight = false;
            nameColumnLayout.childControlWidth = true;
            nameColumnLayout.childControlHeight = true;
            // Takes all the row's leftover width so End Turn/locate/toggle
            // stay pinned at their own small fixed sizes next to it.
            nameColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            turnLabelText = CreateText(nameColumn.transform, fontSize: 11, bold: false);
            turnLabelText.text = "Turn:";
            turnLabelText.color = new Color(1f, 1f, 1f, 0.6f);

            headerText = CreateText(nameColumn.transform, fontSize: 16, bold: true);

            var (endTurn, endTurnMat) = CreateButton(headerRow.transform, "End Turn", ConfirmColor, true,
                GlassPanelMaterials.Style.Button, width: 88f, height: 40f, fontSize: 14);
            endTurnButton = endTurn;
            endTurnMaterial = endTurnMat;

            // Header row, not nameColumn — stays reachable even while the
            // HUD is collapsed, which is exactly when a player who's
            // panned away is most likely to want it.
            var (locate, _) = CreateIconButton(headerRow.transform, IconGlyphMaterials.Glyph.LocatePin, ToggleColor,
                GlassPanelMaterials.Style.DieButton, size: 36f);
            locateButton = locate;

            var (toggle, toggleIcon) = CreateIconButton(headerRow.transform, IconGlyphMaterials.Glyph.ChevronUp, ToggleColor,
                GlassPanelMaterials.Style.DieButton, size: 36f);
            collapseToggleButton = toggle;
            collapseToggleIcon = toggleIcon;
            collapseToggleButton.onClick.AddListener(() => SetCollapsed(!isCollapsed));

            // Everything below the header — toggled as one unit by
            // SetCollapsed rather than juggling each row's visibility
            // individually.
            collapsibleContent = new GameObject("CollapsibleContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            collapsibleContent.transform.SetParent(panel.transform, false);
            var contentLayout = collapsibleContent.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            var content = collapsibleContent.transform;

            CreateDivider(content);
            var statsRow = CreateRow(content, spacing: 14f);
            hullValueText = CreateStatPair(statsRow, IconGlyphMaterials.Glyph.Hull, "Hull");
            energyValueText = CreateStatPair(statsRow, IconGlyphMaterials.Glyph.Energy, "Nrg");
            weaponsValueText = CreateStatPair(statsRow, IconGlyphMaterials.Glyph.Weapons, "Wpn");
            shieldsValueText = CreateStatPair(statsRow, IconGlyphMaterials.Glyph.Shields, "Shld");
            speedValueText = CreateStatPair(statsRow, IconGlyphMaterials.Glyph.Speed, "Spd");

            var resourcesRow = CreateRow(content, spacing: 20f);
            moneyValueText = CreateStatPair(resourcesRow, IconGlyphMaterials.Glyph.Money, "Gold");
            cargoValueText = CreateStatPair(resourcesRow, IconGlyphMaterials.Glyph.Cargo, "Cargo");

            winsText = CreateText(content, fontSize: 16, bold: false);
            actionsText = CreateText(content, fontSize: 16, bold: false);

            CreateDivider(content);
            phaseText = CreateText(content, fontSize: 15, bold: false);
            eventText = CreateText(content, fontSize: 15, bold: false);
            goalText = CreateText(content, fontSize: 15, bold: false);

            CreateDivider(content);
            var utilityRow = new GameObject("UtilityRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            utilityRow.transform.SetParent(content, false);
            var utilityRowLayout = utilityRow.GetComponent<HorizontalLayoutGroup>();
            utilityRowLayout.spacing = 10f;
            utilityRowLayout.childAlignment = TextAnchor.MiddleLeft;
            utilityRowLayout.childForceExpandWidth = true;
            utilityRowLayout.childForceExpandHeight = false;
            utilityRowLayout.childControlWidth = true;
            utilityRowLayout.childControlHeight = true;

            var (shop, shopMat) = CreateButton(utilityRow.transform, "Open Shop", UtilityColor, true,
                GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 15, stretchWidth: true);
            shopButton = shop;
            shopLabel = shopButton.GetComponentInChildren<Text>();
            shopMaterial = shopMat;

            var (jobBoard, jobBoardMat) = CreateButton(utilityRow.transform, "Open Job Board", UtilityColor, true,
                GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 15, stretchWidth: true);
            jobBoardButton = jobBoard;
            jobBoardLabel = jobBoardButton.GetComponentInChildren<Text>();
            jobBoardMaterial = jobBoardMat;

            // Always reachable — unlike Shop/Job Board, viewing held items
            // has no planet/turn-phase gate, so this never needs a
            // SetX(interactable, label) call from MatchHud's per-frame
            // refresh; it's wired once here and left alone.
            var (cargo, _) = CreateButton(utilityRow.transform, "View Cargo", UtilityColor, true,
                GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 15, stretchWidth: true);
            heldItemsButton = cargo;

            // Divider hidden along with the section it introduces — a
            // bare divider with nothing shown after it (both rows below
            // default to hidden) would otherwise render as a stray line
            // with no content on either side of it. See SetActiveJob.
            activeJobDivider = CreateDivider(content);
            activeJobDivider.SetActive(false);
            // Active job status/Mine/Deliver — see SetActiveJob's own
            // comment for why this lives here rather than inside
            // JobBoardScreen. Hidden by default; both toggled on/off
            // together (status text only, no action, for a Bounty job
            // mid-fight-trigger) rather than as separate always-visible
            // rows, so there's no empty gap when there's no active job.
            activeJobStatusText = CreateText(content, fontSize: 14, bold: false);
            activeJobStatusText.gameObject.SetActive(false);

            var (activeJobAction, activeJobActionMat) = CreateButton(content, string.Empty, UtilityColor, true,
                GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 15, stretchWidth: true);
            activeJobActionButton = activeJobAction;
            activeJobActionLabel = activeJobActionButton.GetComponentInChildren<Text>();
            activeJobActionMaterial = activeJobActionMat;
            activeJobActionButton.gameObject.SetActive(false);

            // Same "hide the divider along with its section" reasoning as
            // activeJobDivider above — see SetAttack.
            attackDivider = CreateDivider(content);
            attackDivider.SetActive(false);
            var (attack, _) = CreateButton(content, "Attack", AttackColor, true,
                GlassPanelMaterials.Style.Button, width: 0f, height: PrimaryButtonHeight, fontSize: 18, stretchWidth: true);
            attackButton = attack;
            attackLabel = attackButton.GetComponentInChildren<Text>();
            attackButton.gameObject.SetActive(false);

            // No own divider — see SetTrade's own comment on why this
            // sits directly under Attack instead.
            var (trade, _) = CreateButton(content, "Trade", UtilityColor, true,
                GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 15, stretchWidth: true);
            tradeButton = trade;
            tradeLabel = tradeButton.GetComponentInChildren<Text>();
            tradeButton.gameObject.SetActive(false);

            messageText = CreateText(content, fontSize: 15, bold: false);
            messageText.gameObject.SetActive(false);

            BuildDiceBar(canvasObject.transform);

            SetVisible(false);
        }

        // A second, independent panel (not part of the top ChromePanel or
        // its collapsibleContent) docked at the bottom of the screen —
        // dice need to stay reachable and the map needs to stay visible
        // around them regardless of whether the top panel is expanded or
        // collapsed, which wasn't possible while the dice tray lived
        // inside the top panel's own collapsible content.
        private void BuildDiceBar(Transform canvasTransform)
        {
            // Opaque, full-width, created BEFORE dicePanel (so it's an
            // earlier sibling and renders behind it) — exists purely to
            // hide the map camera's own stale render in the strip it no
            // longer covers (see MapCameraController.SetBottomReservedFraction).
            // Without this, shrinking the camera's viewport leaves
            // whatever was drawn there in the last frame before the
            // shrink permanently on screen, since nothing clears pixels
            // outside a camera's current rect.
            var backdrop = new GameObject("DiceBarBackdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(canvasTransform, false);
            diceBackdropRect = backdrop.GetComponent<RectTransform>();
            diceBackdropRect.anchorMin = new Vector2(0f, 0f);
            diceBackdropRect.anchorMax = new Vector2(1f, 0f);
            diceBackdropRect.pivot = new Vector2(0.5f, 0f);
            diceBackdropRect.anchoredPosition = Vector2.zero;
            diceBackdropRect.sizeDelta = new Vector2(0f, 0f);
            backdrop.GetComponent<Image>().color = DiceBackdropColor;

            dicePanel = new GameObject("DiceBarPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            dicePanel.transform.SetParent(canvasTransform, false);

            dicePanelRect = dicePanel.GetComponent<RectTransform>();
            dicePanelRect.anchorMin = new Vector2(0.5f, 0f);
            dicePanelRect.anchorMax = new Vector2(0.5f, 0f);
            dicePanelRect.pivot = new Vector2(0.5f, 0f);
            dicePanelRect.anchoredPosition = new Vector2(0f, DiceBarMarginBottom);
            dicePanelRect.sizeDelta = new Vector2(DiceBarWidth, dicePanelRect.sizeDelta.y);

            var dicePanelImage = dicePanel.GetComponent<Image>();
            dicePanelMaterial = GlassPanelMaterials.Create(GlassPanelMaterials.Style.Panel, Color.white);
            dicePanelImage.material = dicePanelMaterial;
            dicePanel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var diceBarLayout = dicePanel.GetComponent<VerticalLayoutGroup>();
            diceBarLayout.spacing = 8f;
            diceBarLayout.padding = new RectOffset(16, 16, 14, 14);
            diceBarLayout.childAlignment = TextAnchor.MiddleCenter;
            diceBarLayout.childForceExpandWidth = true;
            diceBarLayout.childForceExpandHeight = false;
            diceBarLayout.childControlWidth = true;
            diceBarLayout.childControlHeight = true;

            diceInstructionText = CreateText(dicePanel.transform, fontSize: 15, bold: false);
            diceInstructionText.alignment = TextAnchor.MiddleCenter;

            var diceButtonRowObject = new GameObject("DiceButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            diceButtonRowObject.transform.SetParent(dicePanel.transform, false);
            diceButtonRowObject.GetComponent<LayoutElement>().preferredHeight = DieButtonHeight;
            var diceRowLayout = diceButtonRowObject.GetComponent<HorizontalLayoutGroup>();
            diceRowLayout.spacing = DiceRowSpacing;
            diceRowLayout.childAlignment = TextAnchor.MiddleCenter;
            diceRowLayout.childForceExpandWidth = false;
            diceRowLayout.childForceExpandHeight = false;
            diceRowLayout.childControlWidth = false;
            diceRowLayout.childControlHeight = false;
            diceButtonRow = diceButtonRowObject.transform;
        }

        // A left-aligned, content-sized (not stretched) horizontal row —
        // used to hold a handful of icon+value stat pairs.
        private static Transform CreateRow(Transform parent, float spacing)
        {
            var rowObject = new GameObject("StatRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = spacing;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            return rowObject.transform;
        }

        // A short label above an icon + numeric-value row, auto-sized to
        // content (the icon's fixed LayoutElement size, the label/value
        // text's own natural width) rather than stretched — see
        // CreateRow. The label exists because icon-only stats didn't
        // reliably communicate their meaning (confirmed against real
        // screenshots — Hull/Energy/Speed specifically read as unclear,
        // and Shields was easy to mistake for Hull before that).
        private static Text CreateStatPair(Transform parent, IconGlyphMaterials.Glyph glyph, string label)
        {
            var columnObject = new GameObject(glyph + "Stat", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            columnObject.transform.SetParent(parent, false);
            var columnLayout = columnObject.GetComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 1f;
            columnLayout.childAlignment = TextAnchor.UpperCenter;
            columnLayout.childForceExpandWidth = false;
            columnLayout.childForceExpandHeight = false;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;

            var labelText = CreateText(columnObject.transform, fontSize: 10, bold: false);
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            // Muted relative to the value below it — secondary, not the
            // main thing the eye should land on.
            labelText.color = new Color(1f, 1f, 1f, 0.65f);

            var pairObject = new GameObject(glyph + "Pair", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            pairObject.transform.SetParent(columnObject.transform, false);
            var pairLayout = pairObject.GetComponent<HorizontalLayoutGroup>();
            pairLayout.spacing = 4f;
            pairLayout.childAlignment = TextAnchor.MiddleCenter;
            pairLayout.childForceExpandWidth = false;
            pairLayout.childForceExpandHeight = false;
            pairLayout.childControlWidth = true;
            pairLayout.childControlHeight = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(pairObject.transform, false);
            var iconLayoutElement = iconObject.GetComponent<LayoutElement>();
            iconLayoutElement.preferredWidth = IconSize;
            iconLayoutElement.preferredHeight = IconSize;
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.material = IconGlyphMaterials.Get(glyph);
            iconImage.raycastTarget = false;

            return CreateText(pairObject.transform, fontSize: 17, bold: true);
        }

        private static Text CreateText(Transform parent, int fontSize, bool bold)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        // width/stretchWidth: die/roll buttons and End Turn (in headerRow,
        // alongside the name/locate/toggle) pass an explicit width, since
        // none of those rows stretch children to fill available width.
        // Attack passes stretchWidth instead — it's a direct child of the
        // panel's own layout group, which DOES stretch children to fill
        // the panel's content width (see BuildUI) — width is then just an
        // inert initial RectTransform.sizeDelta.x fallback.
        private static (Button Button, Material Material) CreateButton(Transform parent, string label, Color color, bool interactable,
            GlassPanelMaterials.Style glassStyle, float width, float height, int fontSize, bool stretchWidth = false)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = height;
            if (!stretchWidth)
                layoutElement.preferredWidth = width;

            var image = buttonObject.GetComponent<Image>();
            image.color = color;
            var material = GlassPanelMaterials.Create(glassStyle, color);
            image.material = material;

            var button = buttonObject.GetComponent<Button>();
            button.interactable = interactable;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = fontSize;

            return (button, material);
        }

        // A small glass button with an icon, optionally with a short
        // text label under it — used icon-only for the collapse toggle
        // and locate-pin (square, no label — those read clearly enough
        // on their own), and icon+label for every die button (icon-only
        // dice didn't reliably communicate their terrain across two
        // separate redesign passes). Kept separate from CreateButton
        // rather than adding an icon branch to it, since most other
        // buttons in this file still want text only, no icon.
        private static (Button Button, Image Icon) CreateIconButton(Transform parent, IconGlyphMaterials.Glyph glyph, Color color,
            GlassPanelMaterials.Style glassStyle, float size, bool interactable = true, float paddingFraction = UtilityIconPaddingFraction,
            string label = null, float height = -1f)
        {
            var buttonHeight = height > 0f ? height : size;
            var buttonObject = new GameObject(glyph + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(size, buttonHeight);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = size;
            layoutElement.preferredHeight = buttonHeight;

            var backgroundImage = buttonObject.GetComponent<Image>();
            backgroundImage.color = color;
            backgroundImage.material = GlassPanelMaterials.Create(glassStyle, color);

            var button = buttonObject.GetComponent<Button>();
            button.interactable = interactable;

            var hasLabel = !string.IsNullOrEmpty(label);
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(buttonObject.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            if (hasLabel)
            {
                // Icon occupies the top ~60% of the button, the label
                // strip along the bottom ~35% (with a small gap between).
                iconRect.anchorMin = new Vector2(0.16f, 0.38f);
                iconRect.anchorMax = new Vector2(0.84f, 0.95f);
                iconRect.offsetMin = Vector2.zero;
                iconRect.offsetMax = Vector2.zero;
            }
            else
            {
                var iconPadding = size * paddingFraction;
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(iconPadding, iconPadding);
                iconRect.offsetMax = new Vector2(-iconPadding, -iconPadding);
            }

            // Button.interactable has no visual effect on its own here —
            // this is a runtime-built Button with no targetGraphic wired
            // up (Selectable's auto-assign-on-Reset only runs in the
            // Editor, not via AddComponent at runtime), so its built-in
            // ColorTint transition never fires. The background square
            // already gets an explicit different color from the caller
            // (e.g. SpentDieColor vs IdleDieColor), but the icon glyph
            // itself — the most visually prominent part of the button —
            // used a shared, fixed-brightness material with no per-
            // instance dimming at all, so a spent die's icon glowed
            // exactly as brightly as an available one. Fixed by tinting
            // the icon (and label) via their own Image/Text color, which
            // IconGlyph.shader explicitly multiplies into its output
            // (same vertex-color trick GlassPanel.shader uses) — this
            // dims both without needing a second material per state.
            var contentColor = interactable ? Color.white : new Color(1f, 1f, 1f, 0.35f);

            var iconImage = iconObject.GetComponent<Image>();
            iconImage.material = IconGlyphMaterials.Get(glyph);
            iconImage.color = contentColor;
            iconImage.raycastTarget = false;

            if (hasLabel)
            {
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(buttonObject.transform, false);
                var labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.03f);
                labelRect.anchorMax = new Vector2(1f, 0.34f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var labelText = labelObject.GetComponent<Text>();
                labelText.text = label;
                labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.color = contentColor;
                labelText.fontSize = 12;
                labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
                labelText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            return (button, iconImage);
        }

        // Thin separator between logical groups (identity/turn, ship &
        // progress, dice tray, primary actions) — cheap way to give a
        // text-heavy panel real visual structure. Stays a plain flat
        // Image (not GlassPanel-shaded) — a 1pt line has no meaningful
        // rounded-corner/rim story at that thinness.
        private static GameObject CreateDivider(Transform parent)
        {
            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            dividerObject.transform.SetParent(parent, false);
            dividerObject.GetComponent<LayoutElement>().preferredHeight = 1f;
            dividerObject.GetComponent<Image>().color = DividerColor;
            return dividerObject;
        }
    }
}
