using System;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Multiplayer;

namespace StarBound.UI
{
    // Full-screen match-over summary — see MatchHud, which shows this
    // instead of the normal HUD once Match.IsComplete. Same self-contained,
    // runtime-built convention as TurnHandoffScreen/ModeSelectionScreen,
    // and the same glass/SDF visual language as MatchHudChrome (see
    // ScreenChromeKit) — panel rim is tinted to the winner's player color,
    // same touch MatchHudChrome.SetHeader does for the in-match HUD.
    public class WinScreen : MonoBehaviour
    {
        // Never shown concurrently with TurnHandoffScreen (200) or the
        // pre-match screens (300) — order kept monotonic with the flow.
        private const int SortingOrder = 250;
        private const float PanelWidth = 360f;

        // Same values as MatchHud's PlayerOneColor/PlayerTwoColor —
        // duplicated locally (those are private to MatchHud, and this
        // screen has no other dependency on it) so a player's color
        // identity stays consistent across the in-match HUD and this
        // final summary.
        private static readonly Color PlayerOneColor = new(0.2f, 0.9f, 0.9f);
        private static readonly Color PlayerTwoColor = new(0.95f, 0.3f, 0.7f);

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Text headerText;
        private Text statsText;
        private Button newMatchButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void Show(Match match, Action onNewMatch)
        {
            headerText.text = $"{match.Winner.DisplayName} wins the match!";
            statsText.text = DescribeMatch(match);

            var winnerColor = match.Winner == match.PlayerOne ? PlayerOneColor : PlayerTwoColor;
            panelMaterial.SetColor("_RimColor", winnerColor);
            headerText.color = winnerColor;

            newMatchButton.onClick.RemoveAllListeners();
            newMatchButton.onClick.AddListener(() => onNewMatch());

            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private static string DescribeMatch(Match match)
        {
            return
                $"Turns taken: {match.TurnNumber}\n\n" +
                DescribePlayer(match.PlayerOne) + "\n" +
                DescribePlayer(match.PlayerTwo);
        }

        private static string DescribePlayer(Core.Player player) =>
            $"{player.DisplayName} — Easy: {player.EasyEngagementWins}  " +
            $"Medium: {player.MediumEngagementWins}  Hard: {player.HardEngagementWins}";

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "WinScreenCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor);
            panelRect = rect;
            panelMaterial = material;

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 26, bold: true);
            statsText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 16);

            var (newMatch, _, _) = ScreenChromeKit.CreateButton(panel.transform, "New Match", ScreenChromeKit.ConfirmColor,
                interactable: true, GlassPanelMaterials.Style.Button, width: 0f, height: 52f, fontSize: 20, stretchWidth: true);
            newMatchButton = newMatch;
        }
    }
}
