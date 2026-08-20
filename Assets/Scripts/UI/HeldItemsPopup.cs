using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Read-only "what am I carrying" popup — the one piece of the Ship
    // status screen story ShopScreen doesn't already cover. All five core
    // stats and money are always visible in MatchHudChrome's persistent
    // strip; ShopScreen's own Cargo section shows held item names too, but
    // only while on a planet with the shop open (see CanShop) — so there
    // was previously no way to see what you're carrying mid-turn anywhere
    // else. Unlike Shop/Job Board, there's nothing to act on here (that
    // already lives in Shop's Cargo section), so this is a simple overlay
    // popup in the same family as PopupDialog, not a full "select -> show
    // info -> confirm" screen.
    public class HeldItemsPopup : MonoBehaviour
    {
        // Between PopupDialog's 150 and ShopScreen's 170 — reachable from
        // chrome (50), never simultaneously open with Shop/Job Board/
        // Engagement since chrome itself hides while those are up.
        private const int SortingOrder = 155;
        private const float PanelWidth = 280f;

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Text headerText;
        private Transform itemsRow;
        private Button closeButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void SetVisible(bool visible) => background.SetActive(visible);

        private void Hide() => SetVisible(false);

        public void Show(IReadOnlyList<ItemDefinition> heldItems, int cargoCapacity, Action onClose)
        {
            headerText.text = $"Held Items ({heldItems.Count}/{cargoCapacity})";

            for (var i = itemsRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(itemsRow.GetChild(i).gameObject);

            if (heldItems.Count == 0)
            {
                var empty = ScreenChromeKit.CreateText(itemsRow, "Cargo hold is empty.", fontSize: 13);
                empty.color = new Color(1f, 1f, 1f, 0.6f);
            }
            else
            {
                foreach (var item in heldItems)
                    ScreenChromeKit.CreateText(itemsRow, $"{item.Name} — ${item.Price}", fontSize: 14, alignment: TextAnchor.MiddleLeft);
            }

            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => onClose?.Invoke());

            SetVisible(true);
        }

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "HeldItemsPopupCanvas", SortingOrder);

            // Translucent, not ScreenChromeKit.CreateOpaqueBackground —
            // this is a popup layered over chrome (like PopupDialog), not
            // a full screen replacing it, so chrome stays visible behind
            // it. Still blocks taps from reaching chrome/map underneath
            // (raycasting doesn't care about alpha), same as PopupDialog's
            // own dim background.
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

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 18, bold: true);

            var itemsRowObject = new GameObject("ItemsRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            itemsRowObject.transform.SetParent(panel.transform, false);
            var itemsRowLayout = itemsRowObject.GetComponent<VerticalLayoutGroup>();
            itemsRowLayout.spacing = 6f;
            itemsRowLayout.childAlignment = TextAnchor.UpperLeft;
            itemsRowLayout.childForceExpandWidth = true;
            itemsRowLayout.childForceExpandHeight = false;
            itemsRowLayout.childControlWidth = true;
            itemsRowLayout.childControlHeight = true;
            itemsRow = itemsRowObject.transform;

            var (button, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Close", ScreenChromeKit.DisabledColor,
                true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 15, stretchWidth: true);
            closeButton = button;
        }
    }
}
