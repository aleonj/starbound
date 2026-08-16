using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Map;

namespace StarBound.UI
{
    // Second screen in the pre-match flow (see ModeSelectionScreen) —
    // picks map size before a Pass-and-Play match starts. No difficulty
    // picker: out of scope for now, DemoBootstrap keeps a fixed internal
    // difficulty. Same self-contained, runtime-built Canvas convention as
    // the other UI screens.
    public class MatchSetupScreen : MonoBehaviour
    {
        private const int SortingOrder = 300;
        private const MapSize DefaultMapSize = MapSize.Small;

        private static readonly Color SelectedColor = new(0.2f, 0.7f, 0.3f);
        private static readonly Color UnselectedColor = new(0.25f, 0.25f, 0.3f);
        private static readonly Color StartColor = new(0.2f, 0.5f, 0.8f);

        private static readonly (MapSize Size, string Label)[] MapSizeOptions =
        {
            (MapSize.Small, "Small (Recommended)"),
            (MapSize.Medium, "Medium"),
            (MapSize.Large, "Large"),
        };

        private GameObject background;
        private readonly Dictionary<MapSize, Button> mapSizeButtons = new();
        private MapSize selectedMapSize;
        private Action<MapSize> onStart;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        public void Show(Action<MapSize> onStart)
        {
            this.onStart = onStart;
            SelectMapSize(DefaultMapSize);
            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private void SelectMapSize(MapSize size)
        {
            selectedMapSize = size;
            foreach (var (optionSize, button) in mapSizeButtons)
                button.GetComponent<Image>().color = optionSize == size ? SelectedColor : UnselectedColor;
        }

        private void BuildUI()
        {
            var canvasObject = new GameObject("MatchSetupCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

            var panel = new GameObject("SetupPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(background.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 400f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 24f;
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateText(panel.transform, "Match Setup", fontSize: 40, height: 80f);
            CreateText(panel.transform, "Map Size", fontSize: 24, height: 40f);

            var row = new GameObject("MapSizeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = 90f;

            foreach (var (size, label) in MapSizeOptions)
            {
                var captured = size;
                var button = CreateButton(row.transform, label, UnselectedColor, width: 220f);
                button.onClick.AddListener(() => SelectMapSize(captured));
                mapSizeButtons[size] = button;
            }

            var startButton = CreateButton(panel.transform, "Start Match", StartColor, width: 260f);
            startButton.onClick.AddListener(() => onStart?.Invoke(selectedMapSize));
        }

        private static Text CreateText(Transform parent, string content, int fontSize, float height)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 760f;
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

        private static Button CreateButton(Transform parent, string label, Color color, float width)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = color;

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
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
            text.fontSize = 22;

            return buttonObject.GetComponent<Button>();
        }
    }
}
