using System;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Reachable from both the pre-match mode-selection screen and the
    // in-match Pause menu — a single shared instance created once in
    // DemoBootstrap (like TurnHandoffScreen/WinScreen/PopupDialog), not
    // per-match, since nothing it shows is match-scoped. Structurally
    // modeled on ModeSelectionScreen (opaque background, single centered
    // glass panel) — no Browse/Detail modes needed for a flat settings
    // list. Push-style Show(onClose)/Hide() like ModeSelectionScreen/
    // TurnHandoffScreen/WinScreen, not the showX/Update() pull pattern
    // MatchHud's domain-gated screens use — nothing here depends on match
    // state, so there's nothing to gate on every frame.
    //
    // Only Master Volume exists — no audio/haptics/other settings-worthy
    // systems exist in the codebase yet (see GameSettings). Add more rows
    // here as those systems land.
    public class SettingsScreen : MonoBehaviour
    {
        // Above every other screen's order (max existing is
        // ModeSelectionScreen/MatchSetupScreen at 300) — Settings must
        // draw on top regardless of which very different context (pre-
        // match menu at 300, in-match Pause at 190) opened it.
        private const int SortingOrder = 310;
        private const float PanelWidth = 340f;
        private const float SliderWidth = 240f;
        private const float SliderHeight = 28f;

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Slider volumeSlider;
        private Text volumeValueText;
        private Button backButton;
        private Action onClose;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void Show(Action onClose)
        {
            this.onClose = onClose;

            volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
            RefreshVolumeLabel(GameSettings.MasterVolume);

            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private void OnVolumeChanged(float value)
        {
            GameSettings.MasterVolume = value;
            RefreshVolumeLabel(value);
        }

        private void RefreshVolumeLabel(float value) => volumeValueText.text = $"{Mathf.RoundToInt(value * 100f)}%";

        private void OnBackClicked()
        {
            Hide();
            onClose?.Invoke();
        }

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "SettingsCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            ScreenChromeKit.CreateText(panel.transform, "Settings", fontSize: 28, bold: true);

            ScreenChromeKit.CreateText(panel.transform, "Master Volume", fontSize: 16, alignment: TextAnchor.MiddleLeft);

            var volumeRow = new GameObject("VolumeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            volumeRow.transform.SetParent(panel.transform, false);
            var volumeRowLayout = volumeRow.GetComponent<HorizontalLayoutGroup>();
            volumeRowLayout.spacing = 10f;
            volumeRowLayout.childAlignment = TextAnchor.MiddleLeft;
            volumeRowLayout.childForceExpandWidth = false;
            volumeRowLayout.childForceExpandHeight = false;
            volumeRowLayout.childControlWidth = true;
            volumeRowLayout.childControlHeight = true;

            var (slider, _) = ScreenChromeKit.CreateSlider(volumeRow.transform, SliderWidth, SliderHeight, GameSettings.MasterVolume);
            volumeSlider = slider;
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

            volumeValueText = ScreenChromeKit.CreateText(volumeRow.transform, string.Empty, fontSize: 16, bold: true);

            var (back, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Back", ScreenChromeKit.AccentColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 48f, fontSize: 18, stretchWidth: true);
            backButton = back;
            backButton.onClick.AddListener(OnBackClicked);
        }
    }
}
