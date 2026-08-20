using System;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Full-screen "pass the device" confirmation between turns — see
    // MatchHud. Opaque and covering the whole viewport, since its entire
    // job is to fully hide whatever was on screen before it. Same
    // glass/SDF visual language as MatchHudChrome (see ScreenChromeKit)
    // — the stat row reuses the exact icons already shipped and legible
    // in the in-match HUD, not a new blind icon design.
    public class TurnHandoffScreen : MonoBehaviour
    {
        // Above PopupDialog's 150 — this must always win, since it
        // exists specifically to guarantee nothing behind it is visible.
        private const int SortingOrder = 200;
        private const float PanelWidth = 340f;

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Text headerText;
        private Text hullValueText;
        private Text energyValueText;
        private Text weaponsValueText;
        private Text shieldsValueText;
        private Text speedValueText;
        private Text moneyValueText;
        private Text winsText;
        private Button beginTurnButton;
        private Text beginTurnLabel;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        // readyLabel: lets a mid-fight reuse (see EngagementSession's
        // symmetric PvP opponent-defense flow in MatchHud) say something
        // other than "Begin Turn" — this screen's own "physically hand
        // the device over" moment is exactly what that flow also needs,
        // no reason to build a second one.
        public void Show(Player incomingPlayer, Action onReady, string readyLabel = "Begin Turn")
        {
            headerText.text = $"Pass the device to\n{incomingPlayer.DisplayName}";
            beginTurnLabel.text = readyLabel;

            var ship = incomingPlayer.Ship;
            hullValueText.text = ship.GetStat(CoreStat.Hull).ToString();
            energyValueText.text = ship.GetStat(CoreStat.Energy).ToString();
            weaponsValueText.text = ship.GetStat(CoreStat.Weapons).ToString();
            shieldsValueText.text = ship.GetStat(CoreStat.Shields).ToString();
            speedValueText.text = ship.GetStat(CoreStat.Speed).ToString();
            moneyValueText.text = $"${ship.Money}";

            winsText.text =
                $"Wins — Easy: {incomingPlayer.EasyEngagementWins}  Medium: {incomingPlayer.MediumEngagementWins}  " +
                $"Hard: {incomingPlayer.HardEngagementWins}/{Player.HardWinsToVictory}";

            beginTurnButton.onClick.RemoveAllListeners();
            beginTurnButton.onClick.AddListener(() => onReady());

            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "HandoffCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 26, bold: true);

            var statsRow = CreateStatRow(panel.transform);
            hullValueText = ScreenChromeKit.CreateIconStat(statsRow, IconGlyphMaterials.Glyph.Hull, "Hull", valueFontSize: 16);
            energyValueText = ScreenChromeKit.CreateIconStat(statsRow, IconGlyphMaterials.Glyph.Energy, "Nrg", valueFontSize: 16);
            weaponsValueText = ScreenChromeKit.CreateIconStat(statsRow, IconGlyphMaterials.Glyph.Weapons, "Wpn", valueFontSize: 16);
            shieldsValueText = ScreenChromeKit.CreateIconStat(statsRow, IconGlyphMaterials.Glyph.Shields, "Shld", valueFontSize: 16);
            speedValueText = ScreenChromeKit.CreateIconStat(statsRow, IconGlyphMaterials.Glyph.Speed, "Spd", valueFontSize: 16);

            // Money gets its own row rather than crowding onto the
            // 5-stat row above — 6 icon+label columns didn't fit
            // comfortably at this panel's width.
            var moneyRow = CreateStatRow(panel.transform);
            moneyValueText = ScreenChromeKit.CreateIconStat(moneyRow, IconGlyphMaterials.Glyph.Money, "Gold", valueFontSize: 16);

            // No established icon for a win tally — stays plain text
            // rather than repeating last story's blind-icon mistake.
            winsText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 15);

            var (begin, beginLabel, _) = ScreenChromeKit.CreateButton(panel.transform, "Begin Turn", ScreenChromeKit.ConfirmColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            beginTurnButton = begin;
            beginTurnLabel = beginLabel;
        }

        private static Transform CreateStatRow(Transform parent)
        {
            var rowObject = new GameObject("StatRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            return rowObject.transform;
        }
    }
}
