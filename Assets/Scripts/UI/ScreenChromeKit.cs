using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // Shared runtime-UI-building pieces for the pre-match/flow screens
    // (ModeSelectionScreen, MatchSetupScreen, TurnHandoffScreen, WinScreen)
    // — extracted because all 4 duplicated near-identical
    // CreateText/CreateButton/canvas-setup boilerplate, and all 4 needed the
    // same fix: ConstantPixelSize -> ScaleWithScreenSize plus the glass/SDF
    // visual language MatchHudChrome already established for the in-match
    // HUD (see GlassPanel.shader/GlassPanelMaterials, IconGlyph.shader/
    // IconGlyphMaterials). This kit only replaces the low-level builders —
    // each screen keeps its own BuildUI() layout, which genuinely differs
    // per screen.
    public static class ScreenChromeKit
    {
        // Same reference resolution/scale-mode as MatchHudChrome, and for
        // the same reason (see that class's header comment): logical
        // points, not physical pixels, with matchWidthOrHeight = 0 (pure
        // width-match) relying on the project's portrait lock.
        public static readonly Vector2 ReferenceResolution = new(400f, 866f);
        private const float ScalerMatchWidthOrHeight = 0f;

        public static readonly Color AccentColor = new(0.35f, 0.55f, 0.85f);
        public static readonly Color ConfirmColor = new(0.2f, 0.7f, 0.3f);
        public static readonly Color DisabledColor = new(0.3f, 0.3f, 0.3f);
        public static readonly Color BackgroundColor = new(0.05f, 0.05f, 0.08f, 1f);

        public static (Canvas Canvas, RectTransform CanvasRect) CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var canvasObject = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = ScalerMatchWidthOrHeight;

            return (canvas, (RectTransform)canvasObject.transform);
        }

        public static GameObject CreateOpaqueBackground(Transform canvasTransform)
        {
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasTransform, false);

            var rect = background.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = BackgroundColor;

            return background;
        }

        // Centered, content-sized (ContentSizeFitter) glass panel — every
        // one of these 5 screens is a centered dialog, unlike
        // MatchHudChrome's corner-docked panels. Caller keeps the returned
        // Material and calls SyncPanelSize from its own LateUpdate, same
        // reason MatchHudChrome does: rounded-corner math needs the
        // panel's actual current size, which content-fitting changes.
        public static (GameObject Panel, RectTransform Rect, Material Material) CreateGlassPanel(
            Transform parent, float width, Color rimColor, float spacing = 20f, RectOffset padding = null)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);

            var material = GlassPanelMaterials.Create(GlassPanelMaterials.Style.Panel, rimColor);
            panel.GetComponent<Image>().material = material;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(28, 28, 28, 28);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return (panel, rect, material);
        }

        public static void SyncPanelSize(Material material, RectTransform rect)
        {
            if (material != null)
                material.SetVector("_Size", rect.rect.size);
        }

        public static Text CreateText(Transform parent, string content, int fontSize, bool bold = false, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        // stretchWidth: pass true when the button is a direct child of a
        // layout group that already stretches children to fill its width
        // (e.g. a single-column panel) — see MatchHudChrome.CreateButton
        // for the same parameter and reasoning.
        public static (Button Button, Text Label, Material Material) CreateButton(
            Transform parent, string label, Color color, bool interactable,
            GlassPanelMaterials.Style style, float width, float height, int fontSize, bool stretchWidth = false)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = height;
            if (!stretchWidth)
                layoutElement.preferredWidth = width;

            var image = buttonObject.GetComponent<Image>();
            image.color = color;
            var material = GlassPanelMaterials.Create(style, color);
            image.material = material;

            var button = buttonObject.GetComponent<Button>();
            button.interactable = interactable;

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
            text.fontSize = fontSize;

            return (button, text, material);
        }

        // A standard UGUI Slider dressed in the same glass/SDF visual
        // language as everything else here — background track + fill +
        // handle, reusing AccentColor/DisabledColor the same way
        // CreateButton reuses ConfirmColor/DisabledColor. Caller owns
        // wiring slider.onValueChanged and any value-label text, same
        // division of responsibility as CreateButton leaving onClick to
        // its caller.
        public static (Slider Slider, RectTransform Rect) CreateSlider(Transform parent, float width, float height, float initialValue)
        {
            var sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(LayoutElement));
            sliderObject.transform.SetParent(parent, false);
            var sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.sizeDelta = new Vector2(width, height);
            var sliderLayoutElement = sliderObject.GetComponent<LayoutElement>();
            sliderLayoutElement.preferredWidth = width;
            sliderLayoutElement.preferredHeight = height;

            var trackObject = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackObject.transform.SetParent(sliderObject.transform, false);
            var trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.35f);
            trackRect.anchorMax = new Vector2(1f, 0.65f);
            trackRect.offsetMin = Vector2.zero;
            trackRect.offsetMax = Vector2.zero;
            trackObject.GetComponent<Image>().color = DisabledColor;

            var fillAreaObject = new GameObject("FillArea", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderObject.transform, false);
            var fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(fillAreaObject.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillObject.GetComponent<Image>().color = AccentColor;

            var handleAreaObject = new GameObject("HandleArea", typeof(RectTransform));
            handleAreaObject.transform.SetParent(sliderObject.transform, false);
            var handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(handleAreaObject.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(height, height);
            handleObject.GetComponent<Image>().color = Color.white;

            var slider = sliderObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleObject.GetComponent<Image>();
            slider.SetValueWithoutNotify(initialValue);

            return (slider, sliderRect);
        }

        // Label-above-icon+value column — same pattern as
        // MatchHudChrome.CreateStatPair, generalized for reuse by
        // TurnHandoffScreen. Only reuses glyphs already proven legible in
        // the shipped in-match HUD (Hull/Energy/Weapons/Shields/Speed/
        // Money) — no new icon design here, see this story's plan.
        public static Text CreateIconStat(Transform parent, IconGlyphMaterials.Glyph glyph, string label, int valueFontSize, float iconSize = 22f)
        {
            var columnObject = new GameObject(glyph + "Stat", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            columnObject.transform.SetParent(parent, false);
            var columnLayout = columnObject.GetComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 1f;
            columnLayout.childAlignment = TextAnchor.UpperCenter;
            columnLayout.childForceExpandWidth = false;
            columnLayout.childForceExpandHeight = false;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;

            var labelText = CreateText(columnObject.transform, label, fontSize: 10);
            labelText.color = new Color(1f, 1f, 1f, 0.65f);

            var pairObject = new GameObject(glyph + "Pair", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            pairObject.transform.SetParent(columnObject.transform, false);
            var pairLayout = pairObject.GetComponent<HorizontalLayoutGroup>();
            pairLayout.spacing = 4f;
            pairLayout.childAlignment = TextAnchor.MiddleCenter;
            pairLayout.childForceExpandWidth = false;
            pairLayout.childForceExpandHeight = false;
            pairLayout.childControlWidth = true;
            pairLayout.childControlHeight = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(pairObject.transform, false);
            var iconLayoutElement = iconObject.GetComponent<LayoutElement>();
            iconLayoutElement.preferredWidth = iconSize;
            iconLayoutElement.preferredHeight = iconSize;
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.material = IconGlyphMaterials.Get(glyph);
            iconImage.raycastTarget = false;

            return CreateText(pairObject.transform, string.Empty, fontSize: valueFontSize, bold: true);
        }
    }
}
