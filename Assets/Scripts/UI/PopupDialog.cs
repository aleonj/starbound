using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // Shared, reusable confirmation/alert popup — every future screen
    // (shop, job board, trade, engagement, pause) can call ShowConfirmation
    // or ShowAlert instead of building its own dialog. Same "no prefabs,
    // build at runtime" convention as MapConfirmationUI/TurnHandoffScreen.
    public class PopupDialog : MonoBehaviour
    {
        // Between MapConfirmationUI (100) and TurnHandoffScreen (200) —
        // needs to win over in-match panels the same tier MapConfirmationUI
        // claims, but never needs to appear during turn handoff or the win
        // screen, which are distinct app-flow phases a popup wouldn't
        // reasonably be triggered during.
        private const int SortingOrder = 150;

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

        // Unlike MapConfirmationUI (whose one caller hides it manually as
        // part of ConfirmPendingMove/CancelPendingMove), every button here
        // hides the dialog itself before invoking its callback — with many
        // future callers expected, requiring each one to remember to hide
        // it is exactly the kind of repeated boilerplate this component
        // exists to remove.
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
            var canvasObject = new GameObject("PopupDialogCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // Full-screen, translucent — dims whatever's behind without
            // fully hiding it (unlike TurnHandoffScreen's opaque
            // background, which exists specifically to hide everything).
            // Also blocks clicks from reaching the map/HUD underneath
            // while a dialog is up, since it has an Image (raycast target
            // by default) covering the whole screen.
            background = new GameObject("DimBackground", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(background.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(500f, 300f);
            panel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 1f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            titleText = CreateText(panel.transform, fontSize: 30, height: 60f);
            messageText = CreateText(panel.transform, fontSize: 22, height: 100f);

            var buttonRowObject = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttonRowObject.transform.SetParent(panel.transform, false);
            buttonRowObject.GetComponent<LayoutElement>().preferredHeight = 80f;

            var rowLayout = buttonRowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 20f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            // Buttons keep their own explicit sizeDelta, same reasoning as
            // MapConfirmationUI's button row.
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;

            buttonRow = buttonRowObject.transform;
        }

        private static Text CreateText(Transform parent, int fontSize, float height)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 460f;
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
            buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 70f);
            buttonObject.GetComponent<Image>().color = color;

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 180f;
            layoutElement.preferredHeight = 70f;

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
            text.fontSize = 26;

            return buttonObject.GetComponent<Button>();
        }
    }
}
