using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // Resume / Settings / Forfeit Match — reached from the Pause icon in
    // MatchHudChrome's header row. Created fresh per match by MatchHud
    // (like ShopScreen/JobBoardScreen), not shared across matches like
    // SettingsScreen, since Forfeit needs a live match reference for the
    // whole thing to mean anything. Structurally modeled on
    // ModeSelectionScreen (opaque background, single centered glass
    // panel, stacked buttons) — nothing here is dynamic per-open (no
    // Shop/JobBoard-style offer data to refresh), so callbacks are wired
    // once via Initialize rather than re-wired on every show.
    public class PauseScreen : MonoBehaviour
    {
        // Between EngagementScreen's 180 and TurnHandoffScreen's 200 —
        // always above ordinary in-match screens, but hand-off/win
        // screens still take priority on paper (MatchHud's visibility
        // gating already prevents them from coinciding in practice).
        private const int SortingOrder = 190;
        private const float PanelWidth = 320f;

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Button resumeButton;
        private Button settingsButton;
        private Button forfeitButton;

        private void Awake()
        {
            BuildUI();
            SetVisible(false);
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void Initialize(Action onResume, Action onSettings, Action onForfeit)
        {
            resumeButton.onClick.AddListener(() => onResume?.Invoke());
            settingsButton.onClick.AddListener(() => onSettings?.Invoke());
            forfeitButton.onClick.AddListener(() => onForfeit?.Invoke());
        }

        public void SetVisible(bool visible) => background.SetActive(visible);

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "PauseCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            ScreenChromeKit.CreateText(panel.transform, "Paused", fontSize: 28, bold: true);

            var (resume, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Resume", ScreenChromeKit.ConfirmColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            resumeButton = resume;

            var (settings, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Settings", ScreenChromeKit.AccentColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            settingsButton = settings;

            var (forfeit, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Forfeit Match", new Color(0.7f, 0.25f, 0.25f),
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            forfeitButton = forfeit;
        }
    }
}
