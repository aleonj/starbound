using System.Reflection;
using NUnit.Framework;
using StarBound.Map;
using UnityEngine;

namespace StarBound.Tests
{
    // The first EditMode test in this project needing a real Unity
    // object rather than a plain C# class — MapCameraController is
    // [RequireComponent(typeof(Camera))] and reads Camera.orthographicSize/
    // aspect/rect directly. Gesture-driven behavior (pan/pinch/tap) isn't
    // covered here — it depends on live Touchscreen/Mouse device state
    // that isn't practical to simulate in EditMode — this covers the
    // pure public-API surface instead: Initialize's zoom/position reset,
    // and PanTo/SetBottomReservedFraction's clamping math, the exact
    // pieces that have driven real bugs this project already hit (an
    // under-clamped pan approximation, and a since-fixed viewport-
    // fraction miscalculation).
    //
    // MapCameraController's private orthoCamera field (set in Awake via
    // GetComponent<Camera>()) is assigned directly via reflection in
    // SetUp below rather than relying on Awake() actually firing before
    // each test body runs — confirmed by two real failures (once with
    // plain [SetUp], once with a [UnitySetUp] + yield return null meant
    // to flush a deferred Awake) that Awake's timing in this specific
    // EditMode test context isn't something to depend on. This sidesteps
    // that ambiguity entirely rather than chasing a third theory about it.
    public class MapCameraControllerTests
    {
        private static readonly FieldInfo OrthoCameraField =
            typeof(MapCameraController).GetField("orthoCamera", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject cameraObject;
        private Camera camera;
        private MapCameraController controller;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("TestCamera", typeof(Camera), typeof(MapCameraController));
            camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            // Fixed rather than whatever the Game View happens to be —
            // camera.aspect otherwise depends on the current render
            // target size, which would make the pan-clamp math below
            // flaky across different editor/CI environments.
            camera.aspect = 1f;
            controller = cameraObject.GetComponent<MapCameraController>();

            OrthoCameraField.SetValue(controller, camera);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void Initialize_ResetsPositionToOriginButPreservesZDepth()
        {
            camera.transform.position = new Vector3(50f, -30f, -10f);

            controller.Initialize(mapRadius: 6, hexRadius: 1f);

            Assert.AreEqual(0f, camera.transform.position.x, 0.001f);
            Assert.AreEqual(0f, camera.transform.position.y, 0.001f);
            Assert.AreEqual(-10f, camera.transform.position.z, 0.001f);
        }

        [Test]
        public void Initialize_SetsOrthographicSizeToFitTheWholeMap()
        {
            controller.Initialize(mapRadius: 6, hexRadius: 1f);

            // Mirrors MapCameraController's own private CameraPadding
            // constant (1.8) — the same "whole map visible" formula the
            // old one-shot FitCameraToMap used before this component
            // replaced it (see Initialize's own comment).
            const float cameraPadding = 1.8f;
            Assert.AreEqual(6f * 1f * cameraPadding, camera.orthographicSize, 0.001f);
        }

        [Test]
        public void PanTo_MovesCameraExactlyToTargetWhenWellWithinMapBounds()
        {
            controller.Initialize(mapRadius: 8, hexRadius: 1f);
            camera.orthographicSize = 1f; // zoomed in, so there's real room to pan

            controller.PanTo(new Vector3(2f, 3f, 0f));

            Assert.AreEqual(2f, camera.transform.position.x, 0.001f);
            Assert.AreEqual(3f, camera.transform.position.y, 0.001f);
        }

        [Test]
        public void PanTo_PreservesCameraZDepthRegardlessOfTargetZ()
        {
            controller.Initialize(mapRadius: 6, hexRadius: 1f);
            var originalZ = camera.transform.position.z;

            controller.PanTo(new Vector3(1f, 1f, 999f));

            Assert.AreEqual(originalZ, camera.transform.position.z, 0.001f);
        }

        [Test]
        public void PanTo_ClampsToTheMapsHalfExtentsWhenTargetIsFarOutsideIt()
        {
            const int mapRadius = 5;
            const float hexRadius = 1f;
            controller.Initialize(mapRadius, hexRadius);
            camera.orthographicSize = 1f; // zoomed in, so clamping actually kicks in

            controller.PanTo(new Vector3(10000f, 10000f, 0f));

            var (expectedMaxX, expectedMaxY) = ExpectedPanBounds(mapRadius, hexRadius);
            Assert.AreEqual(expectedMaxX, camera.transform.position.x, 0.001f);
            Assert.AreEqual(expectedMaxY, camera.transform.position.y, 0.001f);
        }

        [Test]
        public void PanTo_ClampsSymmetricallyInTheNegativeDirection()
        {
            const int mapRadius = 5;
            const float hexRadius = 1f;
            controller.Initialize(mapRadius, hexRadius);
            camera.orthographicSize = 1f;

            controller.PanTo(new Vector3(-10000f, -10000f, 0f));

            var (expectedMaxX, expectedMaxY) = ExpectedPanBounds(mapRadius, hexRadius);
            Assert.AreEqual(-expectedMaxX, camera.transform.position.x, 0.001f);
            Assert.AreEqual(-expectedMaxY, camera.transform.position.y, 0.001f);
        }

        [Test]
        public void PanTo_ClampsToTheDefaultZoomedOutViewWithNoExplicitZoomApplied()
        {
            // Confirms the same clamp formula holds at Initialize's own
            // default zoom, not just a manually-zoomed-in orthographicSize
            // like the other clamp tests above use. Deliberately doesn't
            // assume the result is (0,0) — CameraPadding (1.8) was tuned
            // close to the map's 1.5x X-extent factor, which does clamp
            // X to exactly 0 pan room at this test's 1:1 aspect, but the
            // map's sqrt(3)~=1.73x Y-extent factor isn't quite covered by
            // that same padding at a square aspect, leaving a small
            // residual in Y (~0.66 for this radius) — confirmed by an
            // earlier version of this test wrongly hardcoding (0,0) and
            // failing on the real, correct Y value.
            const int mapRadius = 5;
            const float hexRadius = 1f;
            controller.Initialize(mapRadius, hexRadius);

            controller.PanTo(new Vector3(50f, 50f, 0f));

            var (expectedMaxX, expectedMaxY) = ExpectedPanBounds(mapRadius, hexRadius);
            Assert.AreEqual(expectedMaxX, camera.transform.position.x, 0.001f);
            Assert.AreEqual(expectedMaxY, camera.transform.position.y, 0.001f);
        }

        // Mirrors MapCameraController's own private mapHalfExtentX/Y
        // (see Initialize) and ClampPosition's maxPanX/Y formulas, which
        // aren't exposed directly — recomputed here from the same known
        // inputs (mapRadius/hexRadius/the fixed test aspect/orthographicSize)
        // rather than asserting against a black box.
        private (float MaxPanX, float MaxPanY) ExpectedPanBounds(int mapRadius, float hexRadius)
        {
            var mapHalfExtentX = hexRadius * 1.5f * mapRadius + hexRadius;
            var mapHalfExtentY = hexRadius * Mathf.Sqrt(3f) * mapRadius + hexRadius;
            var viewHalfWidth = camera.orthographicSize * camera.aspect;
            var viewHalfHeight = camera.orthographicSize;
            return (Mathf.Max(0f, mapHalfExtentX - viewHalfWidth), Mathf.Max(0f, mapHalfExtentY - viewHalfHeight));
        }

        [TestCase(0f, 0f, 1f)]
        [TestCase(0.2f, 0.2f, 0.8f)]
        [TestCase(1f, 1f, 0f)]
        public void SetBottomReservedFraction_SetsCameraViewportRect(float fraction, float expectedY, float expectedHeight)
        {
            controller.SetBottomReservedFraction(fraction);

            Assert.AreEqual(0f, camera.rect.x, 0.001f);
            Assert.AreEqual(expectedY, camera.rect.y, 0.001f);
            Assert.AreEqual(1f, camera.rect.width, 0.001f);
            Assert.AreEqual(expectedHeight, camera.rect.height, 0.001f);
        }

        [TestCase(-0.5f, 0f, 1f)]
        [TestCase(1.5f, 1f, 0f)]
        public void SetBottomReservedFraction_ClampsOutOfRangeInput(float fraction, float expectedY, float expectedHeight)
        {
            controller.SetBottomReservedFraction(fraction);

            Assert.AreEqual(expectedY, camera.rect.y, 0.001f);
            Assert.AreEqual(expectedHeight, camera.rect.height, 0.001f);
        }
    }
}
