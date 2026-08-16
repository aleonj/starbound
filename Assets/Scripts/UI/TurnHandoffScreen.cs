using System;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Core;

namespace StarBound.UI
{
    // Full-screen "pass the device" confirmation between turns — see
    // MatchHud. Same self-contained, runtime-built convention as
    // MapConfirmationUI, but opaque and covering the whole viewport
    // instead of a small floating panel, since its entire job is to
    // fully hide whatever was on screen before it.
    public class TurnHandoffScreen : MonoBehaviour
    {
        // Above MapConfirmationUI's 100 — this must always win, since it
        // exists specifically to guarantee nothing behind it is visible.
        private const int SortingOrder = 200;

        private GameObject background;
        private Text headerText;
        private Text statsText;
        private Button beginTurnButton;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        public void Show(Player incomingPlayer, Action onReady)
        {
            headerText.text = $"Pass the device to\n{incomingPlayer.DisplayName}";
            statsText.text = DescribeAtAGlance(incomingPlayer);

            beginTurnButton.onClick.RemoveAllListeners();
            beginTurnButton.onClick.AddListener(() => onReady());

            background.SetActive(true);
        }

        public void Hide() => background.SetActive(false);

        private static string DescribeAtAGlance(Player player)
        {
            var ship = player.Ship;
            return
                $"Hull {ship.GetStat(CoreStat.Hull)}  Energy {ship.GetStat(CoreStat.Energy)}  " +
                $"Weapons {ship.GetStat(CoreStat.Weapons)}  Shields {ship.GetStat(CoreStat.Shields)}  " +
                $"Speed {ship.GetStat(CoreStat.Speed)}\n" +
                $"Money: {ship.Money}\n" +
                $"Wins — Easy: {player.EasyEngagementWins}  Medium: {player.MediumEngagementWins}  " +
                $"Hard: {player.HardEngagementWins}/{Player.HardWinsToVictory}";
        }

        private void BuildUI()
        {
            var canvasObject = new GameObject("HandoffCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // Constant pixel size — see MapConfirmationUI for why
            // ScaleWithScreenSize is avoided here.
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 1f); // fully opaque

            var panel = new GameObject("HandoffPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(background.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700f, 400f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 24f;
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            // Unlike MapConfirmationUI's buttons (which set their own
            // explicit sizeDelta), these children rely on LayoutElement's
            // preferred size — which only takes effect when the group is
            // actually allowed to control it. Leaving these false (as
            // MapConfirmationUI does) silently ignores preferredWidth/
            // Height entirely, collapsing every child to Unity's ~100x100
            // default RectTransform size — that was the actual bug.
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            headerText = CreateText(panel.transform, fontSize: 40, height: 100f);
            statsText = CreateText(panel.transform, fontSize: 24, height: 120f);
            beginTurnButton = CreateButton(panel.transform, "Begin Turn");
        }

        private static Text CreateText(Transform parent, int fontSize, float height)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 700f;
            layoutElement.preferredHeight = height;

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow; // never hard-clip if wrapped content needs a bit more room than preferredHeight

            return text;
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f);

            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 240f;
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
            text.fontSize = 30;

            return buttonObject.GetComponent<Button>();
        }
    }
}
