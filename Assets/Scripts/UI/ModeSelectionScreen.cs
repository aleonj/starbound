using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // First screen a player sees on launch — choose Pass-and-Play or WiFi
    // LAN before configuring a match. Same self-contained, runtime-built
    // convention as MapConfirmationUI/TurnHandoffScreen. WiFi LAN has no
    // networking behind it yet (no host/join/discovery), so its button is
    // shown but disabled — remove the disabled state once that lands.
    public class ModeSelectionScreen : MonoBehaviour
    {
        // Above TurnHandoffScreen's 200 — this is always the very first
        // thing shown, before any match/world objects exist.
        private const int SortingOrder = 300;

        private static readonly Color EnabledColor = new(0.2f, 0.5f, 0.8f);
        private static readonly Color DisabledColor = new(0.3f, 0.3f, 0.3f);

        private GameObject background;
        private Button passAndPlayButton;
        private Button wifiLanButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        public void Show(Action onPassAndPlaySelected)
        {
            passAndPlayButton.onClick.RemoveAllListeners();
            passAndPlayButton.onClick.AddListener(() => onPassAndPlaySelected());

            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private void BuildUI()
        {
            var canvasObject = new GameObject("ModeSelectionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 1f);

            var panel = new GameObject("ModePanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(background.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700f, 420f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 24f;
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            // Must be true or LayoutElement preferred-size hints below are
            // silently ignored — see TurnHandoffScreen for the bug this caused.
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateText(panel.transform, "Choose Match Mode", fontSize: 40, height: 100f);

            passAndPlayButton = CreateButton(panel.transform, "Pass and Play", EnabledColor, interactable: true);
            wifiLanButton = CreateButton(panel.transform, "WiFi LAN — Coming Soon", DisabledColor, interactable: false);
        }

        private static Text CreateText(Transform parent, string content, int fontSize, float height)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 700f;
            layoutElement.preferredHeight = height;

            var text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static Button CreateButton(Transform parent, string label, Color color, bool interactable)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = color;

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 500f;
            layoutElement.preferredHeight = 90f;

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

            var button = buttonObject.GetComponent<Button>();
            button.interactable = interactable;
            return button;
        }
    }
}
