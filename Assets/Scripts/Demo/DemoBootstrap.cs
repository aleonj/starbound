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
    // Auto-starts a full playable pass-and-play match on scene load, with
    // a simple IMGUI debug HUD (MatchHud) to drive it. This is a functional
    // demo harness for exercising the whole game loop end to end — not the
    // polished screens the [UI] backlog stories will eventually build.
    public static class DemoBootstrap
    {
        private const MapSize DefaultMapSize = MapSize.Small;
        private const Difficulty DefaultDifficulty = Difficulty.Medium;
        private const float HexRadius = 1f;
        private const int StartingCargoCapacity = 3;
        private const float CameraPadding = 1.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            var seed = System.Environment.TickCount;

            var playerOne = new Player("p1", "Player One", new Ship(StartingCargoCapacity, EconomyConstants.StartingMoney));
            var playerTwo = new Player("p2", "Player Two", new Ship(StartingCargoCapacity, EconomyConstants.StartingMoney));
            var match = MatchFactory.CreateMatch(DefaultMapSize, DefaultDifficulty, seed, playerOne, playerTwo);

            var worldRoot = new GameObject("World");

            var mapViewObject = new GameObject("MapView", typeof(MapView));
            mapViewObject.transform.SetParent(worldRoot.transform, false);
            var mapView = mapViewObject.GetComponent<MapView>();

            // A sibling of the map's transform, not a child of it — MapView
            // destroys and recreates all of its own children on every
            // Render call, which would delete the ship markers too.
            var markersRoot = new GameObject("ShipMarkers");
            markersRoot.transform.SetParent(worldRoot.transform, false);

            EnsureEventSystem();
            var confirmationUIObject = new GameObject("MapConfirmationUI", typeof(MapConfirmationUI));
            var confirmationUI = confirmationUIObject.GetComponent<MapConfirmationUI>();

            var hudObject = new GameObject("MatchHud", typeof(MatchHud));
            hudObject.GetComponent<MatchHud>().Initialize(match, mapView, markersRoot.transform, HexRadius, confirmationUI);

            FitCameraToMap(DefaultMapSize.ToRadius(), HexRadius);
        }

        // UGUI (the new Confirm/Cancel map-move panel) needs an
        // EventSystem to receive input at all — none exists in this
        // procedurally-built scene otherwise. Uses the new Input System's
        // module, matching the InputSystem package already used elsewhere
        // (see MatchHud's Mouse.current usage).
        private static void EnsureEventSystem()
        {
            // Just need to know one exists — ordering doesn't matter here,
            // so FindAnyObjectByType (not FindFirstObjectByType) is both
            // the correct and the faster choice.
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static void FitCameraToMap(int radius, float hexRadius)
        {
            var camera = Camera.main;
            if (camera == null || !camera.orthographic)
                return;

            camera.orthographicSize = Mathf.Max(radius * hexRadius * CameraPadding, 2f);
            var position = camera.transform.position;
            camera.transform.position = new Vector3(0f, 0f, position.z);
        }
    }
}
