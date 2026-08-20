using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Full-screen, dedicated job-board UI — replaces MatchHud's old IMGUI
    // DrawJobBoardPanel (browsing/accepting offers only — tracking an
    // ACTIVE job with Mine/Deliver stays a persistent MatchHudChrome
    // strip instead, see MatchHudChrome.SetActiveJob, so a player never
    // has to open a screen just to tap Deliver). Same glass/SDF treatment
    // and the same Browse-list/Detail-card "select -> show info -> accept"
    // workflow ShopScreen established — structurally a near-copy of it,
    // simplified: one list instead of three sections, no icon on the card
    // (a job has no CoreStat to show), Accept/Skip instead of Buy/Skip.
    //
    // Created fresh per match by MatchHud, not once in DemoBootstrap —
    // every value shown is per-match state.
    //
    // Refresh is called explicitly at state-change points by MatchHud —
    // NOT from a per-frame Update (see ShopScreen's own comment on why).
    // Selecting a row/Skip/Back switches Browse<->Detail entirely locally
    // (see ShopScreen's own comment on why that still needs to call
    // Render() itself rather than silently doing nothing) — but selection
    // here ALSO needs to reach the map (to highlight only the selected
    // offer's destination, the story's core ask), so Select/Deselect also
    // fire onSelectionChanged for MatchHud to act on.
    public class JobBoardScreen : MonoBehaviour
    {
        // Shop already claimed 170 — Job Board and Shop are never
        // simultaneously reachable (chrome, which hosts both toggle
        // buttons, is hidden while either is open), so the exact ordering
        // between them doesn't matter, but this keeps the sequence
        // monotonic with the app's other mutually-exclusive full-screen
        // states.
        private const int SortingOrder = 175;
        private const float PanelWidth = 380f;
        private const float RowHeight = 44f;
        private const float PanelBottomMargin = 10f;
        // Fixed rather than content-fitted — see the BuildUI comment on
        // why. Sized for Detail mode's worst case (a Bounty job's longer
        // description paragraph + Accept/Skip) with some headroom; Browse
        // mode's own (shorter, and itself capped at
        // JobOfferGenerator.OfferSize rows) content centers within it.
        // Calibrated down from an initial 480, then 440 — both still left
        // a visibly empty gap below the content in Play Mode screenshots
        // (Mining/Transport's single-line description, the shortest
        // case) even after the real childForceExpandHeight bug was fixed
        // (see leaveLayout/actionRowLayout's own comments). 380 leaves
        // enough headroom for a Bounty job's longer two-sentence
        // description (the untested worst case) without the excess the
        // shorter cases were visibly showing.
        private const float FixedPanelHeight = 380f;

        private static readonly Color AcceptColor = new(0.25f, 0.65f, 0.35f);
        private static readonly Color SkipColor = new(0.4f, 0.4f, 0.45f);

        private GameObject background;
        private RectTransform canvasRect;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text headerText;
        private Text statusText;

        private Transform browseContent;
        private Transform offersRow;
        private Transform leaveRow;
        private Button leaveButton;

        private Transform detailSection;
        private RectTransform detailCardRect;
        private Material detailCardMaterial;
        private Text detailNameText;
        private Text detailDescriptionText;
        private Text detailRewardText;
        private Transform detailActionRow;

        private JobDefinition selectedJob;

        // See ShopScreen's own comment on this exact pattern — Refresh is
        // the only place that rebuilds the visible UI, so purely-local
        // selection changes cache the last data and call Render()
        // themselves rather than waiting for MatchHud to supply it again.
        private RefreshData lastData;

        private class RefreshData
        {
            public string LocationName;
            public Color PlayerColor;
            public IReadOnlyList<JobDefinition> Offers;
            public string StatusMessage;
            public Func<HexCoordinate, string> DescribeHex;
            public Action<JobDefinition> OnAccept;
            public Action<JobDefinition> OnSelectionChanged;
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
        // MatchHudChrome.DiceBarReservedFraction, fed to
        // MapCameraController.SetBottomReservedFraction by MatchHud so a
        // job's destination (which OnJobBoardSelectionChanged pans the
        // camera to) lands in the visible map area above the panel
        // instead of being centered right behind it.
        public float ReservedFraction =>
            canvasRect.rect.height > 0f
                ? (panelRect.rect.height + PanelBottomMargin) / canvasRect.rect.height
                : 0f;

        public void Refresh(
            string locationName, Color playerColor,
            IReadOnlyList<JobDefinition> offers, string statusMessage,
            Func<HexCoordinate, string> describeHex,
            Action<JobDefinition> onAccept, Action<JobDefinition> onSelectionChanged, Action onClose)
        {
            lastData = new RefreshData
            {
                LocationName = locationName, PlayerColor = playerColor,
                Offers = offers, StatusMessage = statusMessage, DescribeHex = describeHex,
                OnAccept = onAccept, OnSelectionChanged = onSelectionChanged, OnClose = onClose
            };

            // A fresh Refresh always comes with authoritative data — the
            // previously-selected offer may have just been accepted (or
            // the offer list re-rolled) by the very callback that
            // triggered this call, so re-validate it's still there rather
            // than trusting stale local state.
            if (selectedJob != null)
            {
                var stillPresent = false;
                foreach (var offer in offers)
                {
                    if (offer != selectedJob)
                        continue;
                    stillPresent = true;
                    break;
                }
                if (!stillPresent)
                    selectedJob = null;
            }

            Render();
        }

        private void Render()
        {
            var data = lastData;

            headerText.text = $"Job Board — {data.LocationName}";
            headerText.color = data.PlayerColor;
            panelMaterial.SetColor("_RimColor", data.PlayerColor);
            detailCardMaterial.SetColor("_RimColor", data.PlayerColor);
            accentBar.color = data.PlayerColor;

            statusText.gameObject.SetActive(!string.IsNullOrEmpty(data.StatusMessage));
            statusText.text = data.StatusMessage ?? string.Empty;

            leaveButton.onClick.RemoveAllListeners();
            leaveButton.onClick.AddListener(() => data.OnClose?.Invoke());

            RebuildOffersRow(data.Offers, data.DescribeHex);
            RebuildDetail(data.DescribeHex, data.OnAccept);

            var showDetail = selectedJob != null;
            browseContent.gameObject.SetActive(!showDetail);
            detailSection.gameObject.SetActive(showDetail);
        }

        // Selects an offer and switches to Detail mode — purely local
        // Browse<->Detail state, but (unlike setting the field alone)
        // actually re-renders to show it, and tells MatchHud so it can
        // highlight this one offer's destination on the map.
        private void Select(JobDefinition job)
        {
            selectedJob = job;
            Render();
            lastData.OnSelectionChanged?.Invoke(job);
        }

        // Returns to Browse mode — same "must actually re-render, and
        // tell MatchHud" reasoning as Select above (clears the waypoint).
        private void Deselect()
        {
            selectedJob = null;
            Render();
            lastData.OnSelectionChanged?.Invoke(null);
        }

        private void RebuildOffersRow(IReadOnlyList<JobDefinition> offers, Func<HexCoordinate, string> describeHex)
        {
            for (var i = offersRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(offersRow.GetChild(i).gameObject);

            if (offers.Count == 0)
            {
                var empty = ScreenChromeKit.CreateText(offersRow, "No jobs available.", fontSize: 13);
                empty.color = new Color(1f, 1f, 1f, 0.6f);
                return;
            }

            foreach (var job in offers)
            {
                var captured = job;
                var (button, text, _) = ScreenChromeKit.CreateButton(offersRow, DescribeOfferRow(job, describeHex), ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: RowHeight, fontSize: 13, stretchWidth: true);
                text.alignment = TextAnchor.MiddleLeft;
                var textRect = text.GetComponent<RectTransform>();
                textRect.offsetMin = new Vector2(14f, textRect.offsetMin.y);
                textRect.offsetMax = new Vector2(-14f, textRect.offsetMax.y);
                button.onClick.AddListener(() => Select(captured));
            }
        }

        private static string DescribeOfferRow(JobDefinition job, Func<HexCoordinate, string> describeHex) => job.Type switch
        {
            JobType.BountyHunting => $"Bounty ({job.BountyTier}) at {describeHex(job.Destination)} — ${job.Reward}",
            JobType.Mining => $"Mining to {describeHex(job.Destination)} — ${job.Reward}",
            _ => $"{job.Type} to {describeHex(job.Destination)} — ${job.Reward}"
        };

        // Skip below calls Deselect() itself — a purely local Browse<->
        // Detail change with no external callback (see the class comment
        // on why that needs to trigger its own render). Accept does NOT:
        // it's about to invoke onAccept, which runs all the way through
        // MatchHud and back into the public Refresh() with fresh data
        // (offer list, active job) — calling Deselect() first would just
        // be a wasted extra rebuild with stale data.
        private void RebuildDetail(Func<HexCoordinate, string> describeHex, Action<JobDefinition> onAccept)
        {
            for (var i = detailActionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(detailActionRow.GetChild(i).gameObject);

            if (selectedJob == null)
                return;

            var job = selectedJob;
            detailNameText.text = DescribeJobHeadline(job);
            detailDescriptionText.text = DescribeJobDetail(job, describeHex);
            detailRewardText.text = $"${job.Reward}";

            var (acceptButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, $"Accept — ${job.Reward}", AcceptColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 16, stretchWidth: true);
            acceptButton.onClick.AddListener(() =>
            {
                selectedJob = null;
                onAccept?.Invoke(job);
            });

            var (skipButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, "Skip", SkipColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
            skipButton.onClick.AddListener(Deselect);
        }

        private static string DescribeJobHeadline(JobDefinition job) => job.Type switch
        {
            JobType.BountyHunting => $"Bounty Hunt ({job.BountyTier})",
            JobType.Mining => "Mining Run",
            JobType.Transport => "Transport Job",
            _ => job.Type.ToString()
        };

        private static string DescribeJobDetail(JobDefinition job, Func<HexCoordinate, string> describeHex)
        {
            var destination = describeHex(job.Destination);
            return job.Type switch
            {
                JobType.BountyHunting =>
                    $"Defeat the pirate marked at {destination}. The fight starts automatically the moment you move onto that hex. " +
                    $"Escaping voids the job and costs a ${job.Reward / 10} penalty.",
                JobType.Mining =>
                    $"Mine an Asteroids field, then deliver the minerals to {destination}.",
                JobType.Transport =>
                    $"Drop off the passenger at {destination}.",
                _ => string.Empty
            };
        }

        private void BuildUI()
        {
            (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "JobBoardCanvas", SortingOrder);

            // NOT ScreenChromeKit.CreateOpaqueBackground — every other
            // full-screen screen wants that (nothing behind them matters
            // while they're up), but this one specifically exists to show
            // the player WHERE a job is (see RefreshView's waypoint
            // computation, driven by onSelectionChanged below) — an
            // opaque backdrop would hide the very map that waypoint gets
            // drawn on. A translucent dim (same alpha PopupDialog already
            // uses) keeps the map visible underneath while still reading
            // as an active modal layer, and — since raycastTarget doesn't
            // care about alpha — still blocks taps from reaching the map
            // while this is open.
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
            // center anchor — this screen specifically needs the map
            // visible ABOVE it (see ReservedFraction/MatchHud's
            // SetBottomReservedFraction wiring): OnJobBoardSelectionChanged
            // pans the camera to a selected job's destination, and a
            // centered panel would land squarely on top of whatever just
            // got centered, defeating the whole point.
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, PanelBottomMargin);

            // A FIXED height instead of CreateGlassPanel's own default
            // content-fit (ContentSizeFitter.PreferredSize) — Browse and
            // Detail content aren't the same height (a job's description
            // paragraph varies with its offers list too), and since
            // ReservedFraction reads this same rect, letting it resize
            // per-mode would resize the camera's reserved area — and so
            // the visible map itself — every time a job is selected or
            // deselected. Sized generously for Detail mode's worst case
            // (a Bounty job's longer description + two buttons), with
            // childAlignment: MiddleCenter (CreateGlassPanel's own
            // default) centering whichever mode's shorter content within
            // the fixed space instead of the panel shrinking to fit it.
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            panelRect.sizeDelta = new Vector2(PanelWidth, FixedPanelHeight);

            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 4f;
            accentBar = accentBarObject.GetComponent<Image>();

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 22, bold: true);

            statusText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 13);
            statusText.color = new Color(0.75f, 0.9f, 1f);
            statusText.gameObject.SetActive(false);

            CreateDivider(panel.transform);

            // Offers row + Leave, grouped under one wrapper so Render()
            // can toggle Browse mode on/off with a single SetActive — see
            // ShopScreen's own BrowseContent for why (dividers between
            // sections are siblings, not children, of those sections).
            var browseContentObject = new GameObject("BrowseContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            browseContentObject.transform.SetParent(panel.transform, false);
            var browseContentLayout = browseContentObject.GetComponent<VerticalLayoutGroup>();
            browseContentLayout.spacing = 12f;
            browseContentLayout.childForceExpandWidth = true;
            browseContentLayout.childForceExpandHeight = false;
            browseContentLayout.childControlWidth = true;
            browseContentLayout.childControlHeight = true;
            browseContent = browseContentObject.transform;

            var offersRowObject = new GameObject("OffersRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            offersRowObject.transform.SetParent(browseContent, false);
            var offersRowLayout = offersRowObject.GetComponent<VerticalLayoutGroup>();
            offersRowLayout.spacing = 6f;
            offersRowLayout.childAlignment = TextAnchor.UpperCenter;
            offersRowLayout.childForceExpandWidth = true;
            offersRowLayout.childForceExpandHeight = false;
            offersRowLayout.childControlWidth = true;
            offersRowLayout.childControlHeight = true;
            offersRow = offersRowObject.transform;

            CreateDivider(browseContent);

            var leaveRowObject = new GameObject("LeaveRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            leaveRowObject.transform.SetParent(browseContent, false);
            var leaveLayout = leaveRowObject.GetComponent<VerticalLayoutGroup>();
            leaveLayout.childForceExpandWidth = true;
            // Explicit false — Unity's own default for a fresh
            // VerticalLayoutGroup is true, which would stretch the Leave
            // button to fill 100% of this fixed-height panel's leftover
            // space instead of sitting at its own intended 48pt height.
            leaveLayout.childForceExpandHeight = false;
            leaveLayout.childControlWidth = true;
            leaveLayout.childControlHeight = true;
            leaveRow = leaveRowObject.transform;

            var (leave, _, _) = ScreenChromeKit.CreateButton(leaveRow, "Leave Job Board", ScreenChromeKit.DisabledColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 48f, fontSize: 15, stretchWidth: true);
            leaveButton = leave;

            detailSection = CreateDetailSection(panel.transform, out detailCardRect, out detailCardMaterial,
                out detailNameText, out detailDescriptionText, out detailRewardText, out detailActionRow);
        }

        // No icon here — unlike Shop's items, a job has no CoreStat glyph
        // that fits (IconGlyphMaterials has nothing for bounty/mining/
        // transport, and forcing an ill-fitting one, e.g. for
        // BountyHunting, isn't worth it) — the card is text/location
        // driven instead, sized proportionately. Same "not
        // ScreenChromeKit.CreateGlassPanel" reasoning as ShopScreen's own
        // detail card: nesting a ContentSizeFitter inside the outer
        // panel's own is unproven, so this sizes via the outer panel's
        // childControlHeight instead, same as every other section does.
        private static Transform CreateDetailSection(
            Transform parent, out RectTransform cardRect, out Material cardMaterial,
            out Text nameText, out Text descriptionText, out Text rewardText, out Transform actionRow)
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

            nameText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 20, bold: true);
            descriptionText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 14);
            rewardText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 18, bold: true);

            var actionRowObject = new GameObject("DetailActionRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            actionRowObject.transform.SetParent(cardObject.transform, false);
            var actionRowLayout = actionRowObject.GetComponent<VerticalLayoutGroup>();
            actionRowLayout.spacing = 8f;
            actionRowLayout.childForceExpandWidth = true;
            // Explicit false — same reasoning as LeaveRow's own fix above:
            // Unity's default of true would stretch Accept/Skip to fill
            // the fixed-height panel's leftover space.
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
