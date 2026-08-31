using System.Collections;
using System.Reflection;
using NUnit.Framework;
using StarBound.Map;
using UnityEngine;
using UnityEngine.TestTools;

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

        // No public accessor for maxOrthoSize (it's only consumed by the
        // private, gesture-driven ApplyZoomDelta — see this file's own
        // header comment on why gesture behavior isn't covered here), so
        // Initialize_ZoomOutCeilingStillScalesWithMapSize below reads it
        // directly via reflection, same technique as OrthoCameraField.
        private static readonly FieldInfo MaxOrthoSizeField =
            typeof(MapCameraController).GetField("maxOrthoSize", BindingFlags.NonPublic | BindingFlags.Instance);

        // PanTo used to be instant; it's now an eased coroutine (see
        // MapCameraController's own comment on why) — DrivePanToRoutine
        // below invokes this directly and steps it manually rather than
        // calling the public PanTo and waiting for Unity to service its
        // StartCoroutine. That was tried first (twice — once waiting a
        // fixed duration, once waiting a generous fixed frame count) and
        // both failed for the same underlying reason, confirmed by a real
        // failure that even a full 5 real seconds of yield-return-null
        // polling never let a StartCoroutine'd MonoBehaviour coroutine
        // progress past its first yield in this EditMode test context —
        // it just isn't serviced here, no matter how long the test waits.
        // The test's OWN [UnityTest] enumerator ticks fine (proven by
        // that same 5-second wait actually elapsing) — it's specifically
        // StartCoroutine's separate scheduling that doesn't run, so
        // driving the routine's IEnumerator by hand inside the test's own
        // loop sidesteps that entirely instead of depending on it.
        private static readonly MethodInfo PanToRoutineMethod =
            typeof(MapCameraController).GetMethod("PanToRoutine", BindingFlags.NonPublic | BindingFlags.Instance);

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

        // Drives PanToRoutine to completion inside the test's own proven-
        // reliable [UnityTest] pump loop, one MoveNext per real editor
        // tick — see PanToRoutineMethod's own comment for why this
        // bypasses the public PanTo/StartCoroutine path entirely instead
        // of waiting on it. targetZ mirrors PanTo's own trivial
        // z-preservation (new Vector3(x, y, orthoCamera's current z)) —
        // recomputed here rather than going through PanTo itself, same
        // "known math, not a black box" precedent ExpectedPanBounds below
        // already establishes for the clamp formula.
        private IEnumerator DrivePanToRoutine(Vector3 targetXY)
        {
            var target = new Vector3(targetXY.x, targetXY.y, camera.transform.position.z);
            var routine = (IEnumerator)PanToRoutineMethod.Invoke(controller, new object[] { target });
            while (routine.MoveNext())
                yield return null;
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

        // Regression test for a real bug: three side-by-side screenshots
        // at Small/Medium/Large map sizes (mapRadius 4/6/8 — see
        // MapSize.cs) showed a visibly different starting hex scale on
        // each, because Initialize used to start the camera at
        // maxOrthoSize (the whole-map-fit ceiling), which scales with
        // mapRadius by design. The starting zoom is now a fixed value
        // (see MapCameraController's own DefaultStartingOrthoSizePerHex)
        // regardless of map size.
        [TestCase(4)]
        [TestCase(6)]
        [TestCase(8)]
        public void Initialize_StartingOrthographicSizeIsTheSameRegardlessOfMapSize(int mapRadius)
        {
            controller.Initialize(mapRadius, hexRadius: 1f);

            const float defaultStartingOrthoSizePerHex = 7f;
            Assert.AreEqual(1f * defaultStartingOrthoSizePerHex, camera.orthographicSize, 0.001f);
        }

        // The starting zoom is fixed (see above), but the zoom-OUT
        // ceiling still needs to scale with mapRadius — otherwise a
        // Large map could never be zoomed out far enough to see in one
        // view.
        [Test]
        public void Initialize_ZoomOutCeilingStillScalesWithMapSize()
        {
            controller.Initialize(mapRadius: 6, hexRadius: 1f);

            // Mirrors MapCameraController's own private CameraPadding
            // constant (1.8) — the "whole map visible" formula.
            const float cameraPadding = 1.8f;
            var maxOrthoSize = (float)MaxOrthoSizeField.GetValue(controller);
            Assert.AreEqual(6f * 1f * cameraPadding, maxOrthoSize, 0.001f);
        }

        [UnityTest]
        public IEnumerator PanTo_MovesCameraExactlyToTargetWhenWellWithinMapBounds()
        {
            controller.Initialize(mapRadius: 8, hexRadius: 1f);
            camera.orthographicSize = 1f; // zoomed in, so there's real room to pan

            yield return DrivePanToRoutine(new Vector3(2f, 3f));

            Assert.AreEqual(2f, camera.transform.position.x, 0.001f);
            Assert.AreEqual(3f, camera.transform.position.y, 0.001f);
        }

        // Left as a synchronous [Test] rather than converted like its
        // siblings below — the glide's start and target z are always the
        // same value (see PanTo), so z is invariant across every frame of
        // the tween, not just the final one. Asserting immediately after
        // the call starts (before the coroutine's first yield) already
        // observes the correct, unchanging value.
        [Test]
        public void PanTo_PreservesCameraZDepthRegardlessOfTargetZ()
        {
            controller.Initialize(mapRadius: 6, hexRadius: 1f);
            var originalZ = camera.transform.position.z;

            controller.PanTo(new Vector3(1f, 1f, 999f));

            Assert.AreEqual(originalZ, camera.transform.position.z, 0.001f);
        }

        [UnityTest]
        public IEnumerator PanTo_ClampsToTheMapsHalfExtentsWhenTargetIsFarOutsideIt()
        {
            const int mapRadius = 5;
            const float hexRadius = 1f;
            controller.Initialize(mapRadius, hexRadius);
            camera.orthographicSize = 1f; // zoomed in, so clamping actually kicks in

            yield return DrivePanToRoutine(new Vector3(10000f, 10000f));

            var (expectedMaxX, expectedMaxY) = ExpectedPanBounds(mapRadius, hexRadius);
            Assert.AreEqual(expectedMaxX, camera.transform.position.x, 0.001f);
            Assert.AreEqual(expectedMaxY, camera.transform.position.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator PanTo_ClampsSymmetricallyInTheNegativeDirection()
        {
            const int mapRadius = 5;
            const float hexRadius = 1f;
            controller.Initialize(mapRadius, hexRadius);
            camera.orthographicSize = 1f;

            yield return DrivePanToRoutine(new Vector3(-10000f, -10000f));

            var (expectedMaxX, expectedMaxY) = ExpectedPanBounds(mapRadius, hexRadius);
            Assert.AreEqual(-expectedMaxX, camera.transform.position.x, 0.001f);
            Assert.AreEqual(-expectedMaxY, camera.transform.position.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator PanTo_ClampsToTheDefaultZoomedOutViewWithNoExplicitZoomApplied()
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

            yield return DrivePanToRoutine(new Vector3(50f, 50f));

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
