using System;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Multiplayer;

namespace StarBound.UI
{
    // Full-screen match-over summary — see MatchHud, which shows this
    // instead of the normal HUD once Match.IsComplete. Same self-contained,
    // runtime-built convention as TurnHandoffScreen/ModeSelectionScreen.
    public class WinScreen : MonoBehaviour
    {
        // Never shown concurrently with TurnHandoffScreen (200) or the
        // pre-match screens (300) — order kept monotonic with the flow.
        private const int SortingOrder = 250;

        private GameObject background;
        private Text headerText;
        private Text statsText;
        private Button newMatchButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        public void Show(Match match, Action onNewMatch)
        {
            headerText.text = $"{match.Winner.DisplayName} wins the match!";
            statsText.text = DescribeMatch(match);

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
            var canvasObject = new GameObject("WinScreenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

            var panel = new GameObject("WinPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(background.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 460f);

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

            headerText = CreateText(panel.transform, fontSize: 40, height: 100f);
            statsText = CreateText(panel.transform, fontSize: 22, height: 160f);

            newMatchButton = CreateButton(panel.transform, "New Match");
        }

        private static Text CreateText(Transform parent, int fontSize, float height)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 760f;
            layoutElement.preferredHeight = height;

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 260f;
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

            return buttonObject.GetComponent<Button>();
        }
    }
}
