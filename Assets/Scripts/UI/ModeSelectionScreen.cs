using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarBound.UI
{
    // First screen a player sees on launch — choose Pass-and-Play or WiFi
    // LAN before configuring a match. Same self-contained, runtime-built
    // convention as TurnHandoffScreen, and the same glass/SDF visual
    // language as MatchHudChrome (see ScreenChromeKit).
    // WiFi LAN has no networking behind it yet (no host/join/discovery),
    // so its button is shown but disabled — remove the disabled state once
    // that lands.
    public class ModeSelectionScreen : MonoBehaviour
    {
        // Above TurnHandoffScreen's 200 — this is always the very first
        // thing shown, before any match/world objects exist.
        private const int SortingOrder = 300;
        private const float PanelWidth = 340f;

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Button passAndPlayButton;
        private Button wifiLanButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void Show(Action onPassAndPlaySelected)
        {
            passAndPlayButton.onClick.RemoveAllListeners();
            passAndPlayButton.onClick.AddListener(() => onPassAndPlaySelected());

            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "ModeSelectionCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            ScreenChromeKit.CreateText(panel.transform, "Choose Match Mode", fontSize: 28, bold: true);

            var (passAndPlay, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Pass and Play", ScreenChromeKit.ConfirmColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            passAndPlayButton = passAndPlay;

            var (wifiLan, _, _) = ScreenChromeKit.CreateButton(panel.transform, "WiFi LAN — Coming Soon", ScreenChromeKit.DisabledColor,
                interactable: false, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 16, stretchWidth: true);
            wifiLanButton = wifiLan;
        }
    }
}
