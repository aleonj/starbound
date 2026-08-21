using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // The second half of Wormhole travel's UI — the destination picker
    // that appears once a player has actually moved onto a Wormhole hex
    // (see MatchHud.ConfirmPendingMove/RefreshWormholeDestinationsScreen).
    // Reaching that hex in the first place is just ordinary dice-based
    // movement (MatchHud grants a guaranteed Wormhole-terrain die each
    // roll to anyone holding the device — see OnRollDiceClicked — which
    // flows through the same selectedDie/ComputeLegalTargets/Move pipeline
    // as any other die), so this screen only ever needs to show once
    // that's already happened.
    //
    // Same glass/SDF treatment and Browse-list/Detail-card "select -> show
    // info -> confirm" workflow as ShopScreen/JobBoardScreen, but built on
    // JobBoardScreen's specific shape rather than ShopScreen's: a
    // translucent, bottom-docked panel with the map visible above it, and
    // an onSelectionChanged callback so MatchHud can pan/highlight the
    // currently-selected destination as a map waypoint — the story's own
    // explicit ask ("docked on the bottom so they can see the map as it
    // shows each selected destination").
    //
    // Created fresh per match by MatchHud, not once in DemoBootstrap —
    // every value shown is per-match state.
    public class WormholeScreen : MonoBehaviour
    {
        // Free slot between TradeNegotiationScreen's 176 and
        // EngagementScreen's 180 — Wormhole is never simultaneously
        // reachable with either (chrome, which hosts the dice tray this
        // screen follows on from, is hidden while any of these full-screen
        // states are open), so the exact ordering doesn't matter, but this
        // keeps the sequence monotonic with the app's other mutually-
        // exclusive full-screen states.
        private const int SortingOrder = 177;
        private const float PanelWidth = 380f;
        private const float RowHeight = 44f;
        private const float PanelBottomMargin = 10f;
        // Fixed rather than content-fitted — see JobBoardScreen's own
        // comment on why: Browse and Detail content aren't the same
        // height, and since ReservedFraction reads this same rect, letting
        // it resize per-mode would resize the camera's reserved area (and
        // so the visible map itself) every time a destination is selected
        // or deselected.
        private const float FixedPanelHeight = 320f;

        private static readonly Color BackColor = new(0.4f, 0.4f, 0.45f);

        private GameObject background;
        private RectTransform canvasRect;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text headerText;

        private Transform browseContent;
        private Transform destinationsRow;
        private Transform leaveRow;
        private Button leaveButton;

        private Transform detailSection;
        private RectTransform detailCardRect;
        private Material detailCardMaterial;
        private Text detailNameText;
        private Transform detailActionRow;

        private HexCoordinate? selectedDestination;

        // See ShopScreen/JobBoardScreen's own comment on this exact
        // pattern — Refresh is the only place that rebuilds the visible UI
        // from fresh external data, so purely-local selection changes
        // cache the last data and call Render() themselves rather than
        // waiting for MatchHud to supply it again.
        private RefreshData lastData;

        private class RefreshData
        {
            public string LocationName;
            public Color PlayerColor;
            public IReadOnlyList<HexCoordinate> Destinations;
            public Func<HexCoordinate, string> DescribeDestination;
            public Action<HexCoordinate> OnConfirmTravel;
            public Action<HexCoordinate?> OnSelectionChanged;
            public Action OnClose;
        }

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate()
        {
            ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);
            ScreenChromeKit.SyncPanelSize(detailCardMaterial, detailCardRect);
        }

        public void SetVisible(bool visible) => background.SetActive(visible);

        private void Hide() => SetVisible(false);

        // Fraction (0-1) of screen height this screen's own bottom-docked
        // panel currently occupies — same purpose and shape as
        // JobBoardScreen's own ReservedFraction, fed to
        // MapCameraController.SetBottomReservedFraction by MatchHud so a
        // selected destination (which OnWormholeDestinationSelectionChanged
        // pans the camera to) lands in the visible map area above the
        // panel instead of being centered right behind it.
        public float ReservedFraction =>
            canvasRect.rect.height > 0f
                ? (panelRect.rect.height + PanelBottomMargin) / canvasRect.rect.height
                : 0f;

        public void Refresh(
            string locationName, Color playerColor,
            IReadOnlyList<HexCoordinate> destinations, Func<HexCoordinate, string> describeDestination,
            Action<HexCoordinate> onConfirmTravel, Action<HexCoordinate?> onSelectionChanged, Action onClose)
        {
            lastData = new RefreshData
            {
                LocationName = locationName, PlayerColor = playerColor,
                Destinations = destinations, DescribeDestination = describeDestination,
                OnConfirmTravel = onConfirmTravel, OnSelectionChanged = onSelectionChanged, OnClose = onClose
            };

            // A fresh Refresh always comes with authoritative data — see
            // ShopScreen/JobBoardScreen's own comment on why this
            // re-validates rather than trusting stale local state.
            if (selectedDestination.HasValue && !destinations.Contains(selectedDestination.Value))
                selectedDestination = null;

            Render();
        }

        private void Render()
        {
            var data = lastData;

            headerText.text = $"Wormhole — {data.LocationName}";
            headerText.color = data.PlayerColor;
            panelMaterial.SetColor("_RimColor", data.PlayerColor);
            detailCardMaterial.SetColor("_RimColor", data.PlayerColor);
            accentBar.color = data.PlayerColor;

            leaveButton.onClick.RemoveAllListeners();
            leaveButton.onClick.AddListener(() => data.OnClose?.Invoke());

            RebuildDestinationsRow(data.Destinations, data.DescribeDestination);
            RebuildDetail(data.DescribeDestination, data.OnConfirmTravel);

            var showDetail = selectedDestination.HasValue;
            browseContent.gameObject.SetActive(!showDetail);
            detailSection.gameObject.SetActive(showDetail);
        }

        // Selects a destination and switches to Detail mode — purely
        // local Browse<->Detail state, but (unlike setting the field
        // alone) actually re-renders to show it, and tells MatchHud so it
        // can highlight this destination on the map.
        private void Select(HexCoordinate destination)
        {
            selectedDestination = destination;
            Render();
            lastData.OnSelectionChanged?.Invoke(destination);
        }

        // Returns to Browse mode — same "must actually re-render, and
        // tell MatchHud" reasoning as Select above (clears the waypoint).
        private void Deselect()
        {
            selectedDestination = null;
            Render();
            lastData.OnSelectionChanged?.Invoke(null);
        }

        private void RebuildDestinationsRow(IReadOnlyList<HexCoordinate> destinations, Func<HexCoordinate, string> describeDestination)
        {
            for (var i = destinationsRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(destinationsRow.GetChild(i).gameObject);

            if (destinations.Count == 0)
            {
                var empty = ScreenChromeKit.CreateText(destinationsRow, "No other wormhole exists on this map yet.", fontSize: 13);
                empty.color = new Color(1f, 1f, 1f, 0.6f);
                return;
            }

            foreach (var destination in destinations)
            {
                var captured = destination;
                var (button, text, _) = ScreenChromeKit.CreateButton(destinationsRow, describeDestination(destination), ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: RowHeight, fontSize: 13, stretchWidth: true);
                text.alignment = TextAnchor.MiddleLeft;
                var textRect = text.GetComponent<RectTransform>();
                textRect.offsetMin = new Vector2(14f, textRect.offsetMin.y);
                textRect.offsetMax = new Vector2(-14f, textRect.offsetMax.y);
                button.onClick.AddListener(() => Select(captured));
            }
        }

        // Back below calls Deselect() itself — a purely local Browse<->
        // Detail change with no external callback (see the class comment
        // on why that needs to trigger its own render). Travel does NOT:
        // it's about to invoke onConfirmTravel, which runs all the way
        // through MatchHud and closes this screen entirely — calling
        // Deselect() first would just be a wasted extra rebuild.
        private void RebuildDetail(Func<HexCoordinate, string> describeDestination, Action<HexCoordinate> onConfirmTravel)
        {
            for (var i = detailActionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(detailActionRow.GetChild(i).gameObject);

            if (!selectedDestination.HasValue)
                return;

            var destination = selectedDestination.Value;
            detailNameText.text = $"Travel to {describeDestination(destination)}";

            var (travelButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, "Travel", ScreenChromeKit.ConfirmColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 16, stretchWidth: true);
            travelButton.onClick.AddListener(() =>
            {
                selectedDestination = null;
                onConfirmTravel?.Invoke(destination);
            });

            var (backButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, "Back", BackColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
            backButton.onClick.AddListener(Deselect);
        }

        private void BuildUI()
        {
            (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "WormholeCanvas", SortingOrder);

            // NOT ScreenChromeKit.CreateOpaqueBackground — same reasoning
            // as JobBoardScreen's own background: this screen exists
            // specifically to show the player WHERE a destination is (see
            // RefreshView's waypoint computation, driven by
            // onSelectionChanged below), so an opaque backdrop would hide
            // the very map that waypoint gets drawn on. A translucent dim
            // keeps the map visible underneath while still reading as an
            // active modal layer, and still blocks taps from reaching the
            // map while this is open.
            background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasRect, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor, spacing: 12f);
            panelRect = rect;
            panelMaterial = material;

            // Bottom-anchored instead of CreateGlassPanel's own default
            // center anchor — same reasoning as JobBoardScreen's own panel:
            // this screen needs the map visible ABOVE it, and a centered
            // panel would land squarely on top of whatever a selected
            // destination just got panned to, defeating the whole point.
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, PanelBottomMargin);

            // A FIXED height instead of content-fit — see FixedPanelHeight's
            // own comment on why (ReservedFraction reads this same rect).
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            panelRect.sizeDelta = new Vector2(PanelWidth, FixedPanelHeight);

            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 4f;
            accentBar = accentBarObject.GetComponent<Image>();

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 22, bold: true);

            CreateDivider(panel.transform);

            // Destinations row + Stay Here, grouped under one wrapper so
            // Render() can toggle Browse mode on/off with a single
            // SetActive — see ShopScreen/JobBoardScreen's own
            // BrowseContent for why (dividers between sections are
            // siblings, not children, of those sections).
            var browseContentObject = new GameObject("BrowseContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            browseContentObject.transform.SetParent(panel.transform, false);
            var browseContentLayout = browseContentObject.GetComponent<VerticalLayoutGroup>();
            browseContentLayout.spacing = 12f;
            browseContentLayout.childForceExpandWidth = true;
            browseContentLayout.childForceExpandHeight = false;
            browseContentLayout.childControlWidth = true;
            browseContentLayout.childControlHeight = true;
            browseContent = browseContentObject.transform;

            var destinationsRowObject = new GameObject("DestinationsRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            destinationsRowObject.transform.SetParent(browseContent, false);
            var destinationsRowLayout = destinationsRowObject.GetComponent<VerticalLayoutGroup>();
            destinationsRowLayout.spacing = 6f;
            destinationsRowLayout.childAlignment = TextAnchor.UpperCenter;
            destinationsRowLayout.childForceExpandWidth = true;
            destinationsRowLayout.childForceExpandHeight = false;
            destinationsRowLayout.childControlWidth = true;
            destinationsRowLayout.childControlHeight = true;
            destinationsRow = destinationsRowObject.transform;

            CreateDivider(browseContent);

            var leaveRowObject = new GameObject("LeaveRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            leaveRowObject.transform.SetParent(browseContent, false);
            var leaveLayout = leaveRowObject.GetComponent<VerticalLayoutGroup>();
            leaveLayout.childForceExpandWidth = true;
            // Explicit false — Unity's own default for a fresh
            // VerticalLayoutGroup is true, which would stretch Stay Here
            // to fill this fixed-height panel's leftover space instead of
            // sitting at its own intended 48pt height (see
            // JobBoardScreen's own comment on this exact, previously-real
            // bug).
            leaveLayout.childForceExpandHeight = false;
            leaveLayout.childControlWidth = true;
            leaveLayout.childControlHeight = true;
            leaveRow = leaveRowObject.transform;

            // "Stay Here" rather than JobBoard/Shop's "Leave X" — declining
            // to warp further isn't leaving a place, it's just continuing
            // the turn from the wormhole hex the player already reached.
            var (leave, _, _) = ScreenChromeKit.CreateButton(leaveRow, "Stay Here", ScreenChromeKit.DisabledColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 48f, fontSize: 15, stretchWidth: true);
            leaveButton = leave;

            detailSection = CreateDetailSection(panel.transform, out detailCardRect, out detailCardMaterial,
                out detailNameText, out detailActionRow);
        }

        // No icon here — a wormhole destination has no CoreStat glyph the
        // way a shop item does (same reasoning JobBoardScreen's own detail
        // card already uses for jobs). Same "not
        // ScreenChromeKit.CreateGlassPanel" reasoning as Shop/Job Board's
        // detail cards too: nesting a ContentSizeFitter inside the outer
        // panel's own is unproven, so this sizes via the outer panel's
        // childControlHeight instead, same as every other section does.
        private static Transform CreateDetailSection(
            Transform parent, out RectTransform cardRect, out Material cardMaterial,
            out Text nameText, out Transform actionRow)
        {
            var cardObject = new GameObject("DetailCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            cardObject.transform.SetParent(parent, false);
            cardMaterial = GlassPanelMaterials.Create(GlassPanelMaterials.Style.Panel, ScreenChromeKit.AccentColor);
            cardObject.GetComponent<Image>().material = cardMaterial;
            var cardLayout = cardObject.GetComponent<VerticalLayoutGroup>();
            cardLayout.spacing = 12f;
            cardLayout.padding = new RectOffset(20, 20, 20, 20);
            cardLayout.childAlignment = TextAnchor.MiddleCenter;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardRect = (RectTransform)cardObject.transform;

            nameText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 18, bold: true);

            var actionRowObject = new GameObject("DetailActionRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            actionRowObject.transform.SetParent(cardObject.transform, false);
            var actionRowLayout = actionRowObject.GetComponent<VerticalLayoutGroup>();
            actionRowLayout.spacing = 8f;
            actionRowLayout.childForceExpandWidth = true;
            // Explicit false — same reasoning as LeaveRow's own fix above.
            actionRowLayout.childForceExpandHeight = false;
            actionRowLayout.childControlWidth = true;
            actionRowLayout.childControlHeight = true;
            actionRow = actionRowObject.transform;

            return cardObject.transform;
        }

        private static void CreateDivider(Transform parent)
        {
            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            dividerObject.transform.SetParent(parent, false);
            dividerObject.GetComponent<LayoutElement>().preferredHeight = 1f;
            dividerObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        }
    }
}
