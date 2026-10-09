using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Full-screen, dedicated shop UI — replaces MatchHud's old IMGUI
    // DrawShopPanel/DrawHeldItems. Same glass/SDF treatment (via
    // ScreenChromeKit) as the other full-screen flow screens, and the
    // same "select -> show info -> confirm" workflow this story and the
    // (not yet built) Job Board redesign both call for: Browse mode lists
    // compact tappable rows only, Detail mode is the actual "card" with
    // full info and the committing action — nothing buys/sells directly
    // from a list row.
    //
    // Created fresh per match by MatchHud (like EngagementScreen), not
    // once in DemoBootstrap — every value shown is per-match state.
    //
    // Refresh is called explicitly at state-change points by MatchHud,
    // same discipline EngagementScreen already uses — NOT from a
    // per-frame Update (see EngagementScreen's own comment on why: Unity's
    // Button needs the SAME GameObject across pointer-down/pointer-up, so
    // rebuilding mid-click breaks every click).
    public class ShopScreen : MonoBehaviour
    {
        // Between PopupDialog's 150 and EngagementScreen's 180 — Shop and
        // Engagement are never simultaneously reachable, so the exact
        // ordering between them doesn't matter, but this keeps the
        // sequence monotonic with the app's other mutually-exclusive
        // full-screen states.
        private const int SortingOrder = 170;
        private const float PanelWidth = 380f;
        private const float RowHeight = 44f;

        private static readonly Color CardActionColor = new(0.25f, 0.65f, 0.35f);
        private static readonly Color SkipColor = new(0.4f, 0.4f, 0.45f);

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text headerText;
        private Text moneyValueText;
        private Text statusText;

        private Transform browseContent;
        private Transform buySection;
        private Transform buyRow;
        private Transform repairSection;
        private Transform repairRow;
        private Transform cargoSection;
        private Text cargoHeaderText;
        private Transform cargoRow;
        private Transform leaveRow;
        private Button leaveButton;

        private Transform detailSection;
        private RectTransform detailCardRect;
        private Material detailCardMaterial;
        private Text detailNameText;
        private Image detailIcon;
        private Text detailEffectText;
        private Text detailPriceText;
        private Transform detailActionRow;

        private enum SelectionSource { Buy, Cargo }
        private ItemDefinition selectedItem;
        private SelectionSource selectedSource;

        // Selecting a row, or tapping Skip/Back, switches Browse<->Detail
        // mode entirely locally — no domain data changes, so there's no
        // need to ask MatchHud for anything. But that also means nothing
        // else re-renders the screen for us: Refresh (below) is the only
        // place that actually rebuilds the visible UI, and it's only ever
        // called externally by MatchHud. Caching the last data Refresh
        // received lets purely-local selection changes call Render()
        // themselves afterward, using the same data, without waiting for
        // MatchHud to supply it again.
        private RefreshData lastData;

        private class RefreshData
        {
            public string LocationName;
            public Color PlayerColor;
            public int Money;
            public IReadOnlyList<ItemDefinition> BuyableItems;
            // Separate maxes, not one shared "MaxIntegrity" — Energy's
            // ceiling (Ship.MaxEnergyValue) is higher than Hull's (Ship.
            // DefaultStatValue) since [Combat] Energy overhaul.
            public int Hull, Energy, MaxHull, MaxEnergy, RepairCostPerPoint;
            public IReadOnlyList<ItemDefinition> HeldItems;
            public int CargoCapacity;
            public string OpponentDisplayName;
            public string StatusMessage;
            public Action<ItemDefinition> OnBuy;
            public Action OnRepairHull, OnRepairEnergy;
            public Action<ItemDefinition> OnUseItem, OnSellItem, OnTradeItem;
            public Func<ItemDefinition, bool> CanUseItem, CanTradeItem;
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

        public void Refresh(
            string locationName, Color playerColor, int money,
            IReadOnlyList<ItemDefinition> buyableItems,
            int hull, int energy, int maxHull, int maxEnergy, int repairCostPerPoint,
            IReadOnlyList<ItemDefinition> heldItems, int cargoCapacity, string opponentDisplayName,
            string statusMessage,
            Action<ItemDefinition> onBuy, Action onRepairHull, Action onRepairEnergy,
            Action<ItemDefinition> onUseItem, Action<ItemDefinition> onSellItem, Action<ItemDefinition> onTradeItem,
            Func<ItemDefinition, bool> canUseItem, Func<ItemDefinition, bool> canTradeItem,
            Action onClose)
        {
            lastData = new RefreshData
            {
                LocationName = locationName, PlayerColor = playerColor, Money = money,
                BuyableItems = buyableItems,
                Hull = hull, Energy = energy, MaxHull = maxHull, MaxEnergy = maxEnergy, RepairCostPerPoint = repairCostPerPoint,
                HeldItems = heldItems, CargoCapacity = cargoCapacity, OpponentDisplayName = opponentDisplayName,
                StatusMessage = statusMessage,
                OnBuy = onBuy, OnRepairHull = onRepairHull, OnRepairEnergy = onRepairEnergy,
                OnUseItem = onUseItem, OnSellItem = onSellItem, OnTradeItem = onTradeItem,
                CanUseItem = canUseItem, CanTradeItem = canTradeItem,
                OnClose = onClose
            };

            // A fresh Refresh always comes with authoritative, up-to-date
            // data — the previously-selected item may have just been
            // bought/sold by the very callback that triggered this call,
            // so re-validate it's still actually there rather than
            // trusting stale local state.
            if (selectedItem != null)
            {
                var source = selectedSource == SelectionSource.Buy ? buyableItems : heldItems;
                var stillPresent = false;
                foreach (var item in source)
                {
                    if (item != selectedItem)
                        continue;
                    stillPresent = true;
                    break;
                }
                if (!stillPresent)
                    selectedItem = null;
            }

            Render();
        }

        // The single place that actually rebuilds the visible UI from
        // lastData + the current selection — called by Refresh (fresh
        // external data) and again by every purely-local selection change
        // below (Browse row taps, Skip, Back), so those are never a
        // silent no-op.
        private void Render()
        {
            var data = lastData;

            headerText.text = $"Shop — {data.LocationName}";
            headerText.color = data.PlayerColor;
            panelMaterial.SetColor("_RimColor", data.PlayerColor);
            detailCardMaterial.SetColor("_RimColor", data.PlayerColor);
            accentBar.color = data.PlayerColor;
            moneyValueText.text = data.Money.ToString();

            statusText.gameObject.SetActive(!string.IsNullOrEmpty(data.StatusMessage));
            statusText.text = data.StatusMessage ?? string.Empty;

            leaveButton.onClick.RemoveAllListeners();
            leaveButton.onClick.AddListener(() => data.OnClose?.Invoke());

            var canHoldAnotherItem = data.HeldItems.Count < data.CargoCapacity;

            RebuildBuyRow(data.BuyableItems, data.OnBuy);
            RebuildRepairRow(data.Hull, data.Energy, data.MaxHull, data.MaxEnergy, data.RepairCostPerPoint, data.Money, data.OnRepairHull, data.OnRepairEnergy);
            RebuildCargoRow(data.HeldItems, data.CargoCapacity);
            RebuildDetail(data.Money, canHoldAnotherItem, data.OpponentDisplayName, data.OnBuy, data.OnUseItem, data.OnSellItem, data.OnTradeItem, data.CanUseItem, data.CanTradeItem);

            var showDetail = selectedItem != null;
            browseContent.gameObject.SetActive(!showDetail);
            detailSection.gameObject.SetActive(showDetail);
        }

        // Selects an item and switches to Detail mode — purely local, but
        // (unlike setting the field alone) actually re-renders to show it.
        private void Select(ItemDefinition item, SelectionSource source)
        {
            selectedItem = item;
            selectedSource = source;
            Render();
        }

        // Returns to Browse mode — same "must actually re-render" reasoning
        // as Select above.
        private void Deselect()
        {
            selectedItem = null;
            Render();
        }

        private void RebuildBuyRow(IReadOnlyList<ItemDefinition> buyableItems, Action<ItemDefinition> onBuy)
        {
            for (var i = buyRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(buyRow.GetChild(i).gameObject);

            if (buyableItems.Count == 0)
            {
                CreateEmptyStateLabel(buyRow, "No items in stock.");
                return;
            }

            foreach (var item in buyableItems)
            {
                var captured = item;
                var button = CreateListRow(buyRow, $"{item.Name} — ${item.Price}{KindTag(item)}");
                button.onClick.AddListener(() => Select(captured, SelectionSource.Buy));
            }
        }

        private void RebuildRepairRow(int hull, int energy, int maxHull, int maxEnergy, int costPerPoint, int money, Action onRepairHull, Action onRepairEnergy)
        {
            for (var i = repairRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(repairRow.GetChild(i).gameObject);

            CreateRepairButton(repairRow, "Repair Hull", hull, maxHull, costPerPoint, money, onRepairHull);
            CreateRepairButton(repairRow, "Repair Energy", energy, maxEnergy, costPerPoint, money, onRepairEnergy);
        }

        private static void CreateRepairButton(Transform parent, string label, int current, int max, int costPerPoint, int money, Action onClick)
        {
            var canAfford = current < max && money >= costPerPoint;
            var (button, _, _) = ScreenChromeKit.CreateButton(parent, $"{label} +1 — ${costPerPoint}",
                canAfford ? ScreenChromeKit.AccentColor : ScreenChromeKit.DisabledColor,
                canAfford, GlassPanelMaterials.Style.Button, width: 0f, height: RowHeight, fontSize: 13, stretchWidth: true);
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        private void RebuildCargoRow(IReadOnlyList<ItemDefinition> heldItems, int cargoCapacity)
        {
            cargoHeaderText.text = $"Cargo ({heldItems.Count}/{cargoCapacity})";

            for (var i = cargoRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(cargoRow.GetChild(i).gameObject);

            if (heldItems.Count == 0)
            {
                CreateEmptyStateLabel(cargoRow, "Cargo hold is empty.");
                return;
            }

            foreach (var item in heldItems)
            {
                var captured = item;
                var button = CreateListRow(cargoRow, $"{item.Name}{KindTag(item)}");
                button.onClick.AddListener(() => Select(captured, SelectionSource.Cargo));
            }
        }

        // Compact, left-aligned, full-width tappable row — Browse mode
        // only ever selects here (see the class comment); the actual
        // commit action lives on the Detail card below.
        private static Button CreateListRow(Transform parent, string label)
        {
            var (button, text, _) = ScreenChromeKit.CreateButton(parent, label, ScreenChromeKit.AccentColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: RowHeight, fontSize: 13, stretchWidth: true);
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = text.GetComponent<RectTransform>();
            textRect.offsetMin = new Vector2(14f, textRect.offsetMin.y);
            textRect.offsetMax = new Vector2(-14f, textRect.offsetMax.y);
            return button;
        }

        private static void CreateEmptyStateLabel(Transform parent, string message)
        {
            var label = ScreenChromeKit.CreateText(parent, message, fontSize: 13);
            label.color = new Color(1f, 1f, 1f, 0.6f);
        }

        // Buy/Use/Sell/Trade below set selectedItem = null directly rather
        // than calling Deselect() — they're about to invoke an external
        // callback (onBuy etc.) that runs all the way through MatchHud
        // and back into the public Refresh() with fresh data, which
        // already re-renders; calling Deselect()'s own Render() first
        // would just be a wasted extra rebuild with stale data. Skip and
        // Back have no such external callback, so they call Deselect()
        // themselves — see the class comment on why local-only state
        // changes need to trigger their own render.
        private void RebuildDetail(
            int money, bool canHoldAnotherItem, string opponentDisplayName,
            Action<ItemDefinition> onBuy, Action<ItemDefinition> onUseItem, Action<ItemDefinition> onSellItem, Action<ItemDefinition> onTradeItem,
            Func<ItemDefinition, bool> canUseItem, Func<ItemDefinition, bool> canTradeItem)
        {
            for (var i = detailActionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(detailActionRow.GetChild(i).gameObject);

            if (selectedItem == null)
                return;

            var item = selectedItem;
            detailNameText.text = item.Name;
            var glyph = GlyphForStat(item.AffectedStat);
            // Toggle the icon's own row, not just the icon — hiding only
            // the Image would leave its wrapper row's padding/spacing
            // behind as an empty gap for Unlock items (no CoreStat, e.g.
            // the Wormhole Device).
            detailIcon.transform.parent.gameObject.SetActive(glyph.HasValue);
            if (glyph.HasValue)
                detailIcon.material = IconGlyphMaterials.Get(glyph.Value);
            detailEffectText.text = DescribeEffect(item);
            detailPriceText.text = $"${item.Price}";

            if (selectedSource == SelectionSource.Buy)
            {
                var canAfford = money >= item.Price && canHoldAnotherItem;
                var (buyButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, $"Buy — ${item.Price}",
                    canAfford ? CardActionColor : ScreenChromeKit.DisabledColor,
                    canAfford, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 16, stretchWidth: true);
                buyButton.onClick.AddListener(() =>
                {
                    selectedItem = null;
                    onBuy?.Invoke(item);
                });

                var (skipButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, "Skip", SkipColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
                skipButton.onClick.AddListener(Deselect);
                return;
            }

            if (canUseItem(item))
            {
                var (useButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, "Use", CardActionColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
                useButton.onClick.AddListener(() =>
                {
                    selectedItem = null;
                    onUseItem?.Invoke(item);
                });
            }

            if (canTradeItem(item))
            {
                var (tradeButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, $"Trade to {opponentDisplayName} — ${item.Price / 2}", ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 13, stretchWidth: true);
                tradeButton.onClick.AddListener(() =>
                {
                    selectedItem = null;
                    onTradeItem?.Invoke(item);
                });
            }
            else
            {
                var (sellButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, $"Sell — ${item.Price / 2}", ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
                sellButton.onClick.AddListener(() =>
                {
                    selectedItem = null;
                    onSellItem?.Invoke(item);
                });
            }

            var (backButton, _, _) = ScreenChromeKit.CreateButton(detailActionRow, "Back", SkipColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
            backButton.onClick.AddListener(Deselect);
        }

        private static string KindTag(ItemDefinition item) => item.Kind switch
        {
            ItemKind.Consumable => " (consumable)",
            ItemKind.Unlock => " (device)",
            _ => string.Empty
        };

        private static string DescribeEffect(ItemDefinition item) => item.Kind switch
        {
            ItemKind.Permanent => $"Permanent +{item.StatDelta} {item.AffectedStat}.",
            ItemKind.Consumable => $"One-time +{item.StatDelta} {item.AffectedStat} on use.",
            ItemKind.Unlock => "Unlocks instant travel between Wormhole hexes.",
            _ => string.Empty
        };

        private static IconGlyphMaterials.Glyph? GlyphForStat(CoreStat? stat) => stat switch
        {
            CoreStat.Hull => IconGlyphMaterials.Glyph.Hull,
            CoreStat.Energy => IconGlyphMaterials.Glyph.Energy,
            CoreStat.Weapons => IconGlyphMaterials.Glyph.Weapons,
            CoreStat.Shields => IconGlyphMaterials.Glyph.Shields,
            CoreStat.Speed => IconGlyphMaterials.Glyph.Speed,
            _ => null
        };

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "ShopCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor, spacing: 12f);
            panelRect = rect;
            panelMaterial = material;

            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 4f;
            accentBar = accentBarObject.GetComponent<Image>();

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 22, bold: true);
            moneyValueText = CreateMoneyRow(panel.transform);

            statusText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 13);
            statusText.color = new Color(0.75f, 0.9f, 1f);
            statusText.gameObject.SetActive(false);

            CreateDivider(panel.transform);

            // Buy/Repair/Cargo/Leave and the dividers BETWEEN them are all
            // grouped under one wrapper so Render() can toggle Browse mode
            // on/off with a single SetActive — the dividers are siblings
            // of the sections, not children, so without this wrapper
            // hiding the sections for Detail mode would leave their
            // dividers stranded and visible with nothing between them.
            var browseContentObject = new GameObject("BrowseContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            browseContentObject.transform.SetParent(panel.transform, false);
            var browseContentLayout = browseContentObject.GetComponent<VerticalLayoutGroup>();
            browseContentLayout.spacing = 12f;
            browseContentLayout.childForceExpandWidth = true;
            browseContentLayout.childForceExpandHeight = false;
            browseContentLayout.childControlWidth = true;
            browseContentLayout.childControlHeight = true;
            browseContent = browseContentObject.transform;

            buySection = CreateSection(browseContent, "Buy", out buyRow, out _);
            CreateDivider(browseContent);
            repairSection = CreateSection(browseContent, "Repair", out repairRow, out _);
            CreateDivider(browseContent);
            cargoSection = CreateSection(browseContent, "Cargo", out cargoRow, out cargoHeaderText);
            CreateDivider(browseContent);

            leaveRow = CreateLeaveRow(browseContent, out leaveButton);

            detailSection = CreateDetailSection(panel.transform, out detailCardRect, out detailCardMaterial,
                out detailNameText, out detailIcon, out detailEffectText, out detailPriceText, out detailActionRow);
        }

        private static Text CreateMoneyRow(Transform parent)
        {
            var rowObject = new GameObject("MoneyRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(rowObject.transform, false);
            var iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = 22f;
            iconLayout.preferredHeight = 22f;
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.material = IconGlyphMaterials.Get(IconGlyphMaterials.Glyph.Money);
            iconImage.raycastTarget = false;

            return ScreenChromeKit.CreateText(rowObject.transform, string.Empty, fontSize: 20, bold: true);
        }

        private static Transform CreateSection(Transform parent, string title, out Transform row, out Text header)
        {
            var sectionObject = new GameObject(title + "Section", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            sectionObject.transform.SetParent(parent, false);
            var sectionLayout = sectionObject.GetComponent<VerticalLayoutGroup>();
            sectionLayout.spacing = 6f;
            sectionLayout.childAlignment = TextAnchor.UpperCenter;
            sectionLayout.childForceExpandWidth = true;
            sectionLayout.childForceExpandHeight = false;
            sectionLayout.childControlWidth = true;
            sectionLayout.childControlHeight = true;

            header = ScreenChromeKit.CreateText(sectionObject.transform, title, fontSize: 14, bold: true, alignment: TextAnchor.MiddleLeft);
            header.color = new Color(1f, 1f, 1f, 0.8f);

            var rowObject = new GameObject(title + "Row", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(sectionObject.transform, false);
            var rowLayout = rowObject.GetComponent<VerticalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.UpperCenter;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            row = rowObject.transform;

            return sectionObject.transform;
        }

        private static Transform CreateLeaveRow(Transform parent, out Button button)
        {
            var rowObject = new GameObject("LeaveRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var layout = rowObject.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            // Explicit false — Unity's own default for a fresh
            // VerticalLayoutGroup is true, which would stretch Leave Shop
            // to fill any leftover vertical space in the panel instead of
            // sitting at its own intended 48pt height. Harmless today
            // since this panel is content-fitted (~0 leftover space to
            // stretch into), but set explicitly for the same reason every
            // other layout group in this file already does — found as a
            // real, visible bug in JobBoardScreen's identical omission,
            // once its own panel became fixed-height instead.
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var (leaveButton, _, _) = ScreenChromeKit.CreateButton(rowObject.transform, "Leave Shop", ScreenChromeKit.DisabledColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 48f, fontSize: 15, stretchWidth: true);
            button = leaveButton;
            return rowObject.transform;
        }

        // Deliberately NOT ScreenChromeKit.CreateGlassPanel here — that
        // builds a ContentSizeFitter-driven panel meant to sit directly
        // under a canvas/background, sized by its OWN content. Nesting
        // one inside the outer panel's own ContentSizeFitter would be a
        // new, unproven pattern (no other screen nests a ContentSizeFitter
        // inside another one) — this card instead sizes the exact same
        // way buySection/repairSection/cargoSection already do: a plain
        // VerticalLayoutGroup, sized entirely by the OUTER panel's own
        // childControlHeight.
        private static Transform CreateDetailSection(
            Transform parent, out RectTransform cardRect, out Material cardMaterial,
            out Text nameText, out Image icon, out Text effectText, out Text priceText, out Transform actionRow)
        {
            var cardObject = new GameObject("DetailCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            cardObject.transform.SetParent(parent, false);
            cardMaterial = GlassPanelMaterials.Create(GlassPanelMaterials.Style.Panel, ScreenChromeKit.AccentColor);
            cardObject.GetComponent<Image>().material = cardMaterial;
            var cardLayout = cardObject.GetComponent<VerticalLayoutGroup>();
            cardLayout.spacing = 10f;
            cardLayout.padding = new RectOffset(20, 20, 20, 20);
            cardLayout.childAlignment = TextAnchor.MiddleCenter;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardRect = (RectTransform)cardObject.transform;

            nameText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 20, bold: true);

            // A plain child of cardObject would get stretched to the
            // card's full width — cardObject's own VerticalLayoutGroup has
            // childForceExpandWidth = true, which only respects a child's
            // own preferredWidth if that child isn't itself directly
            // subject to expansion. Same fix ScreenChromeKit.CreateIconStat
            // already uses for exactly this reason: a small, non-expanding
            // HorizontalLayoutGroup wrapper around the icon.
            var iconRowObject = new GameObject("IconRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            iconRowObject.transform.SetParent(cardObject.transform, false);
            var iconRowLayout = iconRowObject.GetComponent<HorizontalLayoutGroup>();
            iconRowLayout.childAlignment = TextAnchor.MiddleCenter;
            iconRowLayout.childForceExpandWidth = false;
            iconRowLayout.childForceExpandHeight = false;
            iconRowLayout.childControlWidth = true;
            iconRowLayout.childControlHeight = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(iconRowObject.transform, false);
            var iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = 40f;
            iconLayout.preferredHeight = 40f;
            icon = iconObject.GetComponent<Image>();
            icon.raycastTarget = false;

            effectText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 14);
            priceText = ScreenChromeKit.CreateText(cardObject.transform, string.Empty, fontSize: 18, bold: true);

            var actionRowObject = new GameObject("DetailActionRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            actionRowObject.transform.SetParent(cardObject.transform, false);
            var actionRowLayout = actionRowObject.GetComponent<VerticalLayoutGroup>();
            actionRowLayout.spacing = 8f;
            actionRowLayout.childForceExpandWidth = true;
            // Explicit false — same reasoning as CreateLeaveRow's own fix.
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
