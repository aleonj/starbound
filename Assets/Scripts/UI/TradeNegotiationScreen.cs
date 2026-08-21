using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Full-screen trade negotiation UI — replaces the old instant-trade
    // TradeScreen entirely. Two modes on the same screen: Build (pick
    // items from both sides plus money, then Send) and Review (read-only
    // display of someone else's offer, with Accept/Reject/Counter). Both
    // the negotiation's Initiator (proposing, or reviewing a counter) and
    // its Opponent (reviewing the original offer, or building a counter)
    // use the SAME screen — MatchHud drives which mode and whose
    // perspective via Refresh, following the device hand-off (see
    // MatchHud's HandOffTradeDeviceTo).
    //
    // Created fresh per match by MatchHud, not once in DemoBootstrap —
    // every value shown is per-match state. Refresh is called explicitly
    // at state-change points, same discipline every other flow screen in
    // this project uses.
    public class TradeNegotiationScreen : MonoBehaviour
    {
        // Reuses the now-deleted TradeScreen's old slot — between
        // JobBoardScreen's 175 and EngagementScreen's 180. Negotiation and
        // engagement are never simultaneously reachable, so the exact
        // ordering doesn't matter, but this keeps the sequence monotonic.
        private const int SortingOrder = 176;
        private const float PanelWidth = 380f;
        private const float RowHeight = 40f;
        private const int MoneyStep = 10;

        private static readonly Color SelectedColor = new(0.2f, 0.5f, 0.8f);
        private static readonly Color IdleColor = new(0.22f, 0.22f, 0.26f);
        private static readonly Color CardActionColor = new(0.25f, 0.65f, 0.35f);
        private static readonly Color RejectColor = new(0.7f, 0.25f, 0.25f);
        private static readonly Color SkipColor = new(0.4f, 0.4f, 0.45f);

        private enum Mode { Build, Review }
        private Mode mode;

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text headerText;
        private Text statusText;

        // --- Build mode ---
        private Transform buildContent;
        private Text buildOwnGiveHeader;
        private Transform buildOwnGiveRow;
        private Text buildOwnMoneyValue;
        private Text buildWantHeader;
        private Transform buildWantRow;
        private Text buildWantMoneyValue;
        // Rebuilt fresh each RenderBuild() call, not mutated in place —
        // Send's color/interactable state depends on hasContent, and a
        // GlassPanel button's rim glow is baked into its material at
        // CreateButton time, so mutating Image.color afterward alone
        // would desync the tint from the rim (same reasoning ShopScreen's
        // own Buy/Repair buttons already follow — they're recreated each
        // Render() too, never persistently recolored).
        private Transform buildActionRow;

        // --- Review mode ---
        private Transform reviewContent;
        private Text reviewTheyGiveHeader;
        private Transform reviewTheyGiveRow;
        private Text reviewTheyGiveMoney;
        private Text reviewTheyWantHeader;
        private Transform reviewTheyWantRow;
        private Text reviewTheyWantMoney;
        private Button acceptButton;
        private Button rejectButton;
        private Button counterButton;

        // Local, purely-UI build state — mutated directly by row/stepper
        // taps and re-rendered locally, same "purely local changes must
        // call Render() themselves" discipline every other screen this
        // session uses (see ShopScreen's own comment on why).
        private readonly HashSet<ItemDefinition> buildOwnGiveSelected = new();
        private readonly HashSet<ItemDefinition> buildWantSelected = new();
        private int buildOwnMoney;
        private int buildWantMoney;

        private BuildData buildData;
        private ReviewData reviewData;

        private class BuildData
        {
            public string HeaderLabel;
            public string OwnPlayerName, OtherPlayerName;
            public IReadOnlyList<ItemDefinition> OwnItems, OtherItems;
            public int OwnMoney, OtherMoney;
            public Action<IReadOnlyList<ItemDefinition>, IReadOnlyList<ItemDefinition>, int, int> OnSend;
            public Action OnCancel;
        }

        private class ReviewData
        {
            public string HeaderLabel;
            public IReadOnlyList<ItemDefinition> TheyGive, TheyWant;
            public int TheyGiveMoney, TheyWantMoney;
            public bool CanCounter;
            public Action OnAccept, OnReject, OnCounter;
        }

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void SetVisible(bool visible) => background.SetActive(visible);

        private void Hide() => SetVisible(false);

        public void SetStatusMessage(string message)
        {
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            statusText.text = message ?? string.Empty;
        }

        // preselected*/preselectedMoney let a counter start pre-filled
        // from the terms it's countering, rather than blank — countering
        // reads as "adjust their offer," not "start over."
        public void ShowBuild(
            string headerLabel, string ownPlayerName, string otherPlayerName,
            IReadOnlyList<ItemDefinition> ownItems, IReadOnlyList<ItemDefinition> otherItems,
            int ownMoney, int otherMoney,
            IReadOnlyList<ItemDefinition> preselectedOwnGive, IReadOnlyList<ItemDefinition> preselectedWant,
            int preselectedOwnMoney, int preselectedWantMoney,
            Action<IReadOnlyList<ItemDefinition>, IReadOnlyList<ItemDefinition>, int, int> onSend, Action onCancel)
        {
            // A fresh entry into Build mode is never a continuation of
            // whatever failure the PREVIOUS mode was showing (e.g.
            // cancelling out of a failed Send and landing back in Review —
            // see ShowReview's matching reset) — SetStatusMessage is only
            // ever meant to annotate the CURRENT screen a caller is
            // already looking at, not survive a mode switch.
            SetStatusMessage(null);
            mode = Mode.Build;
            buildData = new BuildData
            {
                HeaderLabel = headerLabel, OwnPlayerName = ownPlayerName, OtherPlayerName = otherPlayerName,
                OwnItems = ownItems, OtherItems = otherItems, OwnMoney = ownMoney, OtherMoney = otherMoney,
                OnSend = onSend, OnCancel = onCancel
            };

            buildOwnGiveSelected.Clear();
            foreach (var item in preselectedOwnGive)
                buildOwnGiveSelected.Add(item);
            buildWantSelected.Clear();
            foreach (var item in preselectedWant)
                buildWantSelected.Add(item);
            buildOwnMoney = Mathf.Clamp(preselectedOwnMoney, 0, ownMoney);
            buildWantMoney = Mathf.Clamp(preselectedWantMoney, 0, otherMoney);

            Render();
        }

        public void ShowReview(
            string headerLabel,
            IReadOnlyList<ItemDefinition> theyGive, IReadOnlyList<ItemDefinition> theyWant,
            int theyGiveMoney, int theyWantMoney, bool canCounter,
            Action onAccept, Action onReject, Action onCounter)
        {
            // See ShowBuild's matching reset for why.
            SetStatusMessage(null);
            mode = Mode.Review;
            reviewData = new ReviewData
            {
                HeaderLabel = headerLabel,
                TheyGive = theyGive, TheyWant = theyWant,
                TheyGiveMoney = theyGiveMoney, TheyWantMoney = theyWantMoney, CanCounter = canCounter,
                OnAccept = onAccept, OnReject = onReject, OnCounter = onCounter
            };

            Render();
        }

        private void Render()
        {
            buildContent.gameObject.SetActive(mode == Mode.Build);
            reviewContent.gameObject.SetActive(mode == Mode.Review);

            if (mode == Mode.Build)
                RenderBuild();
            else
                RenderReview();
        }

        private void RenderBuild()
        {
            var data = buildData;
            headerText.text = data.HeaderLabel;

            RebuildToggleRow(buildOwnGiveRow, data.OwnItems, buildOwnGiveSelected, () => RenderBuild());
            buildOwnGiveHeader.text = $"You give ({data.OwnPlayerName})";
            buildOwnMoneyValue.text = $"${buildOwnMoney}";

            RebuildToggleRow(buildWantRow, data.OtherItems, buildWantSelected, () => RenderBuild());
            buildWantHeader.text = $"You get ({data.OtherPlayerName})";
            buildWantMoneyValue.text = $"${buildWantMoney}";

            for (var i = buildActionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(buildActionRow.GetChild(i).gameObject);

            // An empty proposal (nothing selected on either side, no
            // money either way) isn't a meaningful offer.
            var hasContent = buildOwnGiveSelected.Count > 0 || buildWantSelected.Count > 0 || buildOwnMoney > 0 || buildWantMoney > 0;
            var (sendButton, _, _) = ScreenChromeKit.CreateButton(buildActionRow, "Send", hasContent ? CardActionColor : ScreenChromeKit.DisabledColor,
                hasContent, GlassPanelMaterials.Style.Button, width: 0f, height: 48f, fontSize: 16, stretchWidth: true);
            sendButton.onClick.AddListener(() =>
                data.OnSend?.Invoke(buildOwnGiveSelected.ToList(), buildWantSelected.ToList(), buildOwnMoney, buildWantMoney));

            var (cancelButton, _, _) = ScreenChromeKit.CreateButton(buildActionRow, "Cancel", SkipColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
            cancelButton.onClick.AddListener(() => data.OnCancel?.Invoke());
        }

        private void RebuildToggleRow(Transform parent, IReadOnlyList<ItemDefinition> items, HashSet<ItemDefinition> selected, Action onChanged)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediate(parent.GetChild(i).gameObject);

            if (items.Count == 0)
            {
                var label = ScreenChromeKit.CreateText(parent, "Nothing here.", fontSize: 13);
                label.color = new Color(1f, 1f, 1f, 0.6f);
                return;
            }

            foreach (var item in items)
            {
                var captured = item;
                var isSelected = selected.Contains(item);
                var (button, text, _) = ScreenChromeKit.CreateButton(parent, item.Name,
                    isSelected ? SelectedColor : IdleColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: RowHeight, fontSize: 13, stretchWidth: true);
                text.alignment = TextAnchor.MiddleLeft;
                var textRect = text.GetComponent<RectTransform>();
                textRect.offsetMin = new Vector2(14f, textRect.offsetMin.y);
                textRect.offsetMax = new Vector2(-14f, textRect.offsetMax.y);

                button.onClick.AddListener(() =>
                {
                    if (!selected.Remove(captured))
                        selected.Add(captured);
                    onChanged();
                });
            }
        }

        private void RenderReview()
        {
            var data = reviewData;
            headerText.text = data.HeaderLabel;

            RebuildReadOnlyRow(reviewTheyGiveRow, data.TheyGive);
            reviewTheyGiveMoney.gameObject.SetActive(data.TheyGiveMoney > 0);
            reviewTheyGiveMoney.text = $"+ ${data.TheyGiveMoney}";

            RebuildReadOnlyRow(reviewTheyWantRow, data.TheyWant);
            reviewTheyWantMoney.gameObject.SetActive(data.TheyWantMoney > 0);
            reviewTheyWantMoney.text = $"+ ${data.TheyWantMoney}";

            counterButton.gameObject.SetActive(data.CanCounter);

            acceptButton.onClick.RemoveAllListeners();
            acceptButton.onClick.AddListener(() => data.OnAccept?.Invoke());
            rejectButton.onClick.RemoveAllListeners();
            rejectButton.onClick.AddListener(() => data.OnReject?.Invoke());
            counterButton.onClick.RemoveAllListeners();
            counterButton.onClick.AddListener(() => data.OnCounter?.Invoke());
        }

        private static void RebuildReadOnlyRow(Transform parent, IReadOnlyList<ItemDefinition> items)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediate(parent.GetChild(i).gameObject);

            if (items.Count == 0)
            {
                var label = ScreenChromeKit.CreateText(parent, "Nothing.", fontSize: 13);
                label.color = new Color(1f, 1f, 1f, 0.6f);
                return;
            }

            foreach (var item in items)
            {
                var text = ScreenChromeKit.CreateText(parent, item.Name, fontSize: 13, alignment: TextAnchor.MiddleLeft);
                text.color = Color.white;
            }
        }

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "TradeNegotiationCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor, spacing: 12f);
            panelRect = rect;
            panelMaterial = material;

            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 4f;
            accentBar = accentBarObject.GetComponent<Image>();
            accentBar.color = ScreenChromeKit.AccentColor;

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 20, bold: true);

            statusText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 13);
            statusText.color = new Color(1f, 0.7f, 0.6f);
            statusText.gameObject.SetActive(false);

            CreateDivider(panel.transform);

            buildContent = BuildBuildContent(panel.transform);
            reviewContent = BuildReviewContent(panel.transform);
        }

        private Transform BuildBuildContent(Transform parent)
        {
            var contentObject = new GameObject("BuildContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            contentObject.transform.SetParent(parent, false);
            var contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            var content = contentObject.transform;

            buildOwnGiveHeader = CreateSectionHeader(content, "You give");
            buildOwnGiveRow = CreateItemsColumn(content);
            buildOwnMoneyValue = CreateMoneyStepper(content, () => buildOwnMoney = AdjustMoney(buildOwnMoney, -MoneyStep, buildData?.OwnMoney ?? 0), () => buildOwnMoney = AdjustMoney(buildOwnMoney, MoneyStep, buildData?.OwnMoney ?? 0), () => RenderBuild());

            CreateDivider(content);

            buildWantHeader = CreateSectionHeader(content, "You get");
            buildWantRow = CreateItemsColumn(content);
            buildWantMoneyValue = CreateMoneyStepper(content, () => buildWantMoney = AdjustMoney(buildWantMoney, -MoneyStep, buildData?.OtherMoney ?? 0), () => buildWantMoney = AdjustMoney(buildWantMoney, MoneyStep, buildData?.OtherMoney ?? 0), () => RenderBuild());

            CreateDivider(content);

            // Empty container — Send/Cancel are built fresh into this each
            // RenderBuild() call (see that method's own comment on why).
            var buttonRowObject = new GameObject("BuildButtonRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            buttonRowObject.transform.SetParent(content, false);
            var buttonRowLayout = buttonRowObject.GetComponent<VerticalLayoutGroup>();
            buttonRowLayout.spacing = 8f;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childForceExpandHeight = false;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childControlHeight = true;
            buildActionRow = buttonRowObject.transform;

            return content;
        }

        private static int AdjustMoney(int current, int delta, int max) => Mathf.Clamp(current + delta, 0, max);

        private Transform BuildReviewContent(Transform parent)
        {
            var contentObject = new GameObject("ReviewContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            contentObject.transform.SetParent(parent, false);
            var contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            var content = contentObject.transform;

            reviewTheyGiveHeader = CreateSectionHeader(content, "They give you");
            reviewTheyGiveRow = CreateItemsColumn(content);
            reviewTheyGiveMoney = ScreenChromeKit.CreateText(content, string.Empty, fontSize: 15, bold: true);
            reviewTheyGiveMoney.gameObject.SetActive(false);

            CreateDivider(content);

            reviewTheyWantHeader = CreateSectionHeader(content, "They want from you");
            reviewTheyWantRow = CreateItemsColumn(content);
            reviewTheyWantMoney = ScreenChromeKit.CreateText(content, string.Empty, fontSize: 15, bold: true);
            reviewTheyWantMoney.gameObject.SetActive(false);

            CreateDivider(content);

            var buttonRowObject = new GameObject("ReviewButtonRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            buttonRowObject.transform.SetParent(content, false);
            var buttonRowLayout = buttonRowObject.GetComponent<VerticalLayoutGroup>();
            buttonRowLayout.spacing = 8f;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childForceExpandHeight = false;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childControlHeight = true;

            var (accept, _, _) = ScreenChromeKit.CreateButton(buttonRowObject.transform, "Accept", CardActionColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 48f, fontSize: 16, stretchWidth: true);
            acceptButton = accept;

            var (counter, _, _) = ScreenChromeKit.CreateButton(buttonRowObject.transform, "Counter", ScreenChromeKit.AccentColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
            counterButton = counter;

            var (reject, _, _) = ScreenChromeKit.CreateButton(buttonRowObject.transform, "Reject", RejectColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
            rejectButton = reject;

            return content;
        }

        private static Text CreateSectionHeader(Transform parent, string title)
        {
            var header = ScreenChromeKit.CreateText(parent, title, fontSize: 14, bold: true, alignment: TextAnchor.MiddleLeft);
            header.color = new Color(1f, 1f, 1f, 0.8f);
            return header;
        }

        private static Transform CreateItemsColumn(Transform parent)
        {
            var rowObject = new GameObject("ItemsColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<VerticalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            return rowObject.transform;
        }

        // +/- money stepper — MoneyStep increments, clamped by the caller
        // (onDecrease/onIncrease already know their own max). Rebuilds the
        // whole Build content via onChanged same as an item toggle, rather
        // than mutating the value label directly, so it stays consistent
        // with the single "local change -> Render()" discipline.
        private static Text CreateMoneyStepper(Transform parent, Action onDecrease, Action onIncrease, Action onChanged)
        {
            var rowObject = new GameObject("MoneyStepper", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            var (minus, _, _) = ScreenChromeKit.CreateButton(rowObject.transform, "-", SkipColor,
                true, GlassPanelMaterials.Style.Button, width: 40f, height: 32f, fontSize: 16);
            minus.onClick.AddListener(() => { onDecrease(); onChanged(); });

            var valueText = ScreenChromeKit.CreateText(rowObject.transform, "$0", fontSize: 15, bold: true);
            valueText.GetComponent<LayoutElement>().preferredWidth = 70f;

            var (plus, _, _) = ScreenChromeKit.CreateButton(rowObject.transform, "+", SkipColor,
                true, GlassPanelMaterials.Style.Button, width: 40f, height: 32f, fontSize: 16);
            plus.onClick.AddListener(() => { onIncrease(); onChanged(); });

            return valueText;
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
