using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // Shared, reusable confirmation/alert popup — every future screen
    // (shop, job board, trade, engagement, pause) can call ShowConfirmation
    // or ShowAlert instead of building its own dialog. Same "no prefabs,
    // build at runtime" convention as TurnHandoffScreen.
    public class PopupDialog : MonoBehaviour
    {
        // Below TurnHandoffScreen's 200 — never needs to appear during
        // turn handoff or the win screen, which are distinct app-flow
        // phases a popup wouldn't reasonably be triggered during. Above
        // PauseScreen's 190 (was 150, sitting BELOW Pause) — Forfeit's
        // confirmation is invoked while Pause is still showing underneath
        // it, and Pause has to stay up so Cancel has something to return
        // to, so the dialog must out-rank it or it renders hidden behind
        // an opaque screen that's still on top.
        private const int SortingOrder = 195;
        private const float PanelWidth = 320f;

        private static readonly Color ConfirmColor = new(0.2f, 0.7f, 0.3f);
        private static readonly Color CancelColor = new(0.7f, 0.25f, 0.25f);
        // Neither affirmative nor negative — reads as "acknowledge" for a
        // plain alert's single OK button.
        private static readonly Color NeutralColor = new(0.35f, 0.45f, 0.55f);

        private GameObject background;
        private Text titleText;
        private Text messageText;
        private Transform buttonRow;
        private RectTransform panelRect;
        private Material panelMaterial;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        // Rounded-corner glass math (see GlassPanelMaterials) needs the
        // panel's actual current size, which its own ContentSizeFitter
        // changes every time the title/message/button count changes —
        // same reason every other glass-panel screen (WinScreen,
        // SettingsScreen, etc.) syncs this from LateUpdate rather than
        // once at build time.
        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void ShowConfirmation(string title, string message, string confirmLabel, Action onConfirm, string cancelLabel = "Cancel", Action onCancel = null) =>
            Show(title, message, (confirmLabel, ConfirmColor, onConfirm), (cancelLabel, CancelColor, onCancel));

        public void ShowAlert(string title, string message, string okLabel = "OK", Action onOk = null) =>
            Show(title, message, (okLabel, NeutralColor, onOk));

        public void Hide() => background.SetActive(false);

        // Every button here hides the dialog itself before invoking its
        // callback — with many future callers expected, requiring each
        // one to remember to hide it is exactly the kind of repeated
        // boilerplate this component exists to remove.
        private void Show(string title, string message, params (string Label, Color Color, Action Callback)[] buttons)
        {
            titleText.text = title;
            messageText.text = message;

            foreach (Transform child in buttonRow)
                Destroy(child.gameObject);

            foreach (var button in buttons)
            {
                var (uiButton, _, _) = ScreenChromeKit.CreateButton(buttonRow, button.Label, button.Color,
                    interactable: true, GlassPanelMaterials.Style.Button, width: 130f, height: 48f, fontSize: 16);
                var callback = button.Callback;
                uiButton.onClick.AddListener(() =>
                {
                    Hide();
                    callback?.Invoke();
                });
            }

            background.SetActive(true);
        }

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "PopupDialogCanvas", SortingOrder);

            // Full-screen, translucent — dims whatever's behind without
            // fully hiding it (unlike ScreenChromeKit.CreateOpaqueBackground,
            // which exists specifically to hide everything, e.g. behind a
            // full pre-match flow screen). Also blocks clicks from
            // reaching the map/HUD underneath while a dialog is up, since
            // it has an Image (raycast target by default) covering the
            // whole screen.
            background = new GameObject("DimBackground", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasRect, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            // Same rounded, rim-glowing glass look every other screen
            // uses (see ScreenChromeKit.CreateGlassPanel) — this used to
            // be a flat-colored rect with plain Text/Button children,
            // which read as noticeably plainer than the rest of the UI
            // once this became a frequent, ordinary interaction (the hex
            // info popup) rather than a rare confirmation.
            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            titleText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 22, bold: true);
            messageText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 16);

            var buttonRowObject = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttonRowObject.transform.SetParent(panel.transform, false);
            buttonRowObject.GetComponent<LayoutElement>().preferredHeight = 48f;

            var rowLayout = buttonRowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            // Buttons keep their own explicit sizeDelta rather than being
            // stretched/shrunk by the layout group.
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;

            buttonRow = buttonRowObject.transform;
        }
    }
}
