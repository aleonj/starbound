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
    // the other UI screens, and the same glass/SDF visual language as
    // MatchHudChrome (see ScreenChromeKit).
    public class MatchSetupScreen : MonoBehaviour
    {
        private const int SortingOrder = 300;
        private const float PanelWidth = 360f;
        private const MapSize DefaultMapSize = MapSize.Small;

        // Same values as MatchHudChrome's SelectedDieColor/IdleDieColor —
        // duplicated locally rather than shared, since this screen has no
        // other dependency on MatchHudChrome, but the colors themselves
        // are kept identical so "selected" reads the same everywhere in
        // the app.
        private static readonly Color SelectedColor = new(0.2f, 0.5f, 0.8f);
        private static readonly Color UnselectedColor = new(0.22f, 0.22f, 0.26f);

        private static readonly (MapSize Size, string Label)[] MapSizeOptions =
        {
            (MapSize.Small, "Small"),
            (MapSize.Medium, "Medium"),
            (MapSize.Large, "Large"),
        };

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private readonly Dictionary<MapSize, (Button Button, Material Material)> mapSizeButtons = new();
        private MapSize selectedMapSize;
        private Action<MapSize> onStart;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

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
            foreach (var (optionSize, entry) in mapSizeButtons)
            {
                var color = optionSize == size ? SelectedColor : UnselectedColor;
                entry.Button.GetComponent<Image>().color = color;
                entry.Material.SetColor("_RimColor", color);
            }
        }

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "MatchSetupCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            ScreenChromeKit.CreateText(panel.transform, "Match Setup", fontSize: 28, bold: true);
            ScreenChromeKit.CreateText(panel.transform, "Map Size", fontSize: 16);

            var row = new GameObject("MapSizeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(panel.transform, false);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = 52f;

            foreach (var (size, label) in MapSizeOptions)
            {
                var captured = size;
                var (button, _, buttonMaterial) = ScreenChromeKit.CreateButton(row.transform, label, UnselectedColor,
                    interactable: true, GlassPanelMaterials.Style.Button, width: 96f, height: 52f, fontSize: 15);
                button.onClick.AddListener(() => SelectMapSize(captured));
                mapSizeButtons[size] = (button, buttonMaterial);
            }

            var (startButton, _, _) = ScreenChromeKit.CreateButton(panel.transform, "Start Match", ScreenChromeKit.ConfirmColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            startButton.onClick.AddListener(() => onStart?.Invoke(selectedMapSize));
        }
    }
}
