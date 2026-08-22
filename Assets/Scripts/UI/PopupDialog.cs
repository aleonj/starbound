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

        private static readonly Color ConfirmColor = new(0.2f, 0.7f, 0.3f);
        private static readonly Color CancelColor = new(0.7f, 0.25f, 0.25f);
        // Neither affirmative nor negative — reads as "acknowledge" for a
        // plain alert's single OK button.
        private static readonly Color NeutralColor = new(0.35f, 0.45f, 0.55f);

        private GameObject background;
        private Text titleText;
        private Text messageText;
        private Transform buttonRow;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

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
                var uiButton = CreateButton(buttonRow, button.Label, button.Color);
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
            // Was a hand-rolled ConstantPixelSize canvas sized in raw
            // pixels (panel 500x300, fontSize 30/22, buttons 180x70) —
            // never actually exercised until Forfeit became its first
            // real caller, which is when the mismatch surfaced: on a
            // point-scale device the dialog rendered a fraction of its
            // intended size, the exact "1/3 the intended size on a real
            // device" bug MatchHudChrome's own header comment already
            // documents for this exact ConstantPixelSize-vs-point-scale
            // mistake. Now built on ScreenChromeKit's canvas (same
            // ScaleWithScreenSize/400x866 reference every other screen
            // uses) with every size rescaled to match — this file
            // predates ScreenChromeKit's extraction, which is why it
            // still hand-rolls its own CreateText/CreateButton below
            // rather than reusing the kit's, but the canvas/scale mode
            // itself has to match or every screen drifts out of scale
            // with each other again.
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "PopupDialogCanvas", SortingOrder);

            // Full-screen, translucent — dims whatever's behind without
            // fully hiding it (unlike TurnHandoffScreen's opaque
            // background, which exists specifically to hide everything).
            // Also blocks clicks from reaching the map/HUD underneath
            // while a dialog is up, since it has an Image (raycast target
            // by default) covering the whole screen.
            background = new GameObject("DimBackground", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasRect, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(background.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(320f, panelRect.sizeDelta.y);
            panel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 1f);
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            titleText = CreateText(panel.transform, fontSize: 22, height: 30f);
            messageText = CreateText(panel.transform, fontSize: 16, height: 60f);

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

        private static Text CreateText(Transform parent, int fontSize, float height)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 280f;
            layoutElement.preferredHeight = height;

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static Button CreateButton(Transform parent, string label, Color color)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(130f, 48f);
            buttonObject.GetComponent<Image>().color = color;

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 130f;
            layoutElement.preferredHeight = 48f;

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
            text.fontSize = 16;

            return buttonObject.GetComponent<Button>();
        }
    }
}
