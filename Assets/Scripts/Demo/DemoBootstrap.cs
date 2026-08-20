using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Multiplayer;
using StarBound.UI;

namespace StarBound.Demo
{
    // Shows a mode-select then match-setup screen on scene load, then
    // starts a full playable pass-and-play match with a simple IMGUI
    // debug HUD (MatchHud) to drive it. This is a functional demo harness
    // for exercising the whole game loop end to end.
    public static class DemoBootstrap
    {
        // Difficulty has no player-facing picker yet (out of scope for the
        // Match Setup screen) — fixed here exactly as it was before that
        // screen existed.
        private const Difficulty FixedDifficulty = Difficulty.Medium;
        private const float HexRadius = 1f;
        private const int StartingCargoCapacity = 3;
        // Generous fixed size for the background quad — comfortably covers
        // the camera's view even at the largest map/most zoomed-out size
        // (see MapCameraController's zoom-out ceiling), simpler than
        // resizing it dynamically.
        private const float GalaxyBackgroundSize = 80f;

        private static TurnHandoffScreen handoffScreen;
        private static ModeSelectionScreen modeSelectionScreen;
        private static MatchSetupScreen matchSetupScreen;
        private static WinScreen winScreen;
        private static PopupDialog popupDialog;
        private static MapCameraController mapCameraController;
        private static GameObject worldRoot;
        private static GameObject hudObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            EnsureEventSystem();
            CreateGalaxyBackground();
            EnsureMapCameraController();

            var handoffScreenObject = new GameObject("TurnHandoffScreen", typeof(TurnHandoffScreen));
            handoffScreen = handoffScreenObject.GetComponent<TurnHandoffScreen>();

            var modeSelectionObject = new GameObject("ModeSelectionScreen", typeof(ModeSelectionScreen));
            modeSelectionScreen = modeSelectionObject.GetComponent<ModeSelectionScreen>();

            var matchSetupObject = new GameObject("MatchSetupScreen", typeof(MatchSetupScreen));
            matchSetupScreen = matchSetupObject.GetComponent<MatchSetupScreen>();

            var winScreenObject = new GameObject("WinScreen", typeof(WinScreen));
            winScreen = winScreenObject.GetComponent<WinScreen>();

            var popupDialogObject = new GameObject("PopupDialog", typeof(PopupDialog));
            popupDialog = popupDialogObject.GetComponent<PopupDialog>();

            ShowPreMatchFlow();
        }

        private static void ShowPreMatchFlow()
        {
            modeSelectionScreen.Show(() =>
            {
                modeSelectionScreen.Hide();
                matchSetupScreen.Show(mapSize =>
                {
                    matchSetupScreen.Hide();
                    StartMatch(mapSize);
                });
            });
        }

        private static void StartMatch(MapSize mapSize)
        {
            if (worldRoot != null)
                Object.Destroy(worldRoot);
            if (hudObject != null)
                Object.Destroy(hudObject);

            var seed = System.Environment.TickCount;

            var playerOne = new Player("p1", "Player One", new Ship(StartingCargoCapacity, EconomyConstants.StartingMoney));
            var playerTwo = new Player("p2", "Player Two", new Ship(StartingCargoCapacity, EconomyConstants.StartingMoney));
            var match = MatchFactory.CreateMatch(mapSize, FixedDifficulty, seed, playerOne, playerTwo);

            worldRoot = new GameObject("World");

            var mapViewObject = new GameObject("MapView", typeof(MapView));
            mapViewObject.transform.SetParent(worldRoot.transform, false);
            var mapView = mapViewObject.GetComponent<MapView>();

            // A sibling of the map's transform, not a child of it — MapView
            // destroys and recreates all of its own children on every
            // Render call, which would delete the ship markers too.
            var markersRoot = new GameObject("ShipMarkers");
            markersRoot.transform.SetParent(worldRoot.transform, false);

            hudObject = new GameObject("MatchHud", typeof(MatchHud));
            hudObject.GetComponent<MatchHud>().Initialize(match, mapView, markersRoot.transform, HexRadius, handoffScreen, winScreen, mapCameraController, popupDialog, OnNewMatchRequested);

            mapCameraController.Initialize(mapSize.ToRadius(), HexRadius);
        }

        private static void OnNewMatchRequested()
        {
            winScreen.Hide();
            ShowPreMatchFlow();
        }

        // The actual galaxy every hex's glass look is meant to float over
        // — see Assets/Shaders/GalaxyBackground.shader. Without this, the
        // starfield only ever showed up inside each hex's own mesh; the
        // gaps between tiles and the area outside the grid were just the
        // camera's flat clear color.
        private static void CreateGalaxyBackground()
        {
            var backgroundObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backgroundObject.name = "GalaxyBackground";
            Object.Destroy(backgroundObject.GetComponent<Collider>());
            backgroundObject.transform.localScale = new Vector3(GalaxyBackgroundSize, GalaxyBackgroundSize, 1f);
            backgroundObject.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("StarBound/GalaxyBackground"));
        }

        // UGUI (the new Confirm/Cancel map-move panel) needs an
        // EventSystem to receive input at all — none exists in this
        // procedurally-built scene otherwise. Uses the new Input System's
        // module, matching the InputSystem package already used elsewhere
        // (see MapCameraController's Mouse/Touchscreen polling).
        private static void EnsureEventSystem()
        {
            // Just need to know one exists — ordering doesn't matter here,
            // so FindAnyObjectByType (not FindFirstObjectByType) is both
            // the correct and the faster choice.
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // Pinch-zoom/pan lives on the Main Camera itself (see
        // MapCameraController) rather than as a one-shot fit — added once
        // here, re-Initialize'd per match in StartMatch instead of
        // recreated, since the camera GameObject is a static scene object
        // that persists across matches.
        private static void EnsureMapCameraController()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            mapCameraController = camera.GetComponent<MapCameraController>();
            if (mapCameraController == null)
                mapCameraController = camera.gameObject.AddComponent<MapCameraController>();
        }
    }
}
