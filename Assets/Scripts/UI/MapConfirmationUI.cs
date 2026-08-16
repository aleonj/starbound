using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // Self-contained Confirm/Cancel affordance for the map-click move
    // interaction (see MatchHud) — builds its own Canvas at runtime, same
    // "no prefabs" convention as HexTileView/ShipMarkerView. This is the
    // project's first real UGUI usage, so it also stands up the shared
    // Canvas/CanvasScaler/GraphicRaycaster groundwork the rest of the
    // [UI] backlog stories will reuse.
    public class MapConfirmationUI : MonoBehaviour
    {
        private GameObject panel;
        private Button confirmButton;
        private Button cancelButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        // Wires fresh handlers each call (rather than requiring callers to
        // manage subscriptions) and shows the panel — see MatchHud's
        // ConfirmPendingMove/CancelPendingMove.
        public void Show(Action onConfirm, Action onCancel)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(() => onConfirm());
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() => onCancel());
            panel.SetActive(true);
        }

        public void Hide() => panel.SetActive(false);

        private void BuildUI()
        {
            var canvasObject = new GameObject("ConfirmationCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // always on top — nothing else in this scene uses a Canvas yet

            // Constant pixel size, not screen-relative scaling — simplest
            // and safest for now (no scale-factor computation that can go
            // wrong depending on the Editor Game View's aspect/resolution
            // at the moment this runs). Proper multi-resolution scaling is
            // a [UI] Visual beautification pass concern, not this story's.
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            panel = new GameObject("ConfirmationPanel", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            panel.transform.SetParent(canvasObject.transform, false);

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 80f);
            panelRect.sizeDelta = new Vector2(360f, 100f);

            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            // Explicit, not relying on the LayoutGroup's own default —
            // buttons keep the size CreateButton gives them.
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            confirmButton = CreateButton(panel.transform, "Confirm", new Color(0.2f, 0.7f, 0.3f));
            cancelButton = CreateButton(panel.transform, "Cancel", new Color(0.7f, 0.25f, 0.25f));
        }

        private static Button CreateButton(Transform parent, string label, Color color)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 80f);
            buttonObject.GetComponent<Image>().color = color;

            // Belt-and-braces alongside the parent HorizontalLayoutGroup's
            // childControlWidth/Height = false — an explicit preferred
            // size the layout group can't ignore or zero out.
            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 160f;
            layoutElement.preferredHeight = 80f;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.text = label;
            // Unity's built-in fallback font — no font asset needed, and
            // avoids pulling in TextMeshPro as a new dependency just for
            // two button labels. "LegacyRuntime.ttf" is the current name
            // for this (Unity replaced "Arial.ttf" a few versions back).
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 28;

            return buttonObject.GetComponent<Button>();
        }
    }
}
