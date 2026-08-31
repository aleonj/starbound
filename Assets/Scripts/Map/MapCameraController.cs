using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarBound.Map
{
    // Continuous pinch-zoom/pan for the map camera, replacing the old
    // one-shot DemoBootstrap.FitCameraToMap. Lives on the same Camera
    // GameObject it drives. Touch (mobile) and mouse (Editor/testing) are
    // mutually exclusive per frame — real devices never have both, and in
    // the Editor without Device Simulator touch input there's simply no
    // active touch, so it falls straight through to mouse.
    [RequireComponent(typeof(Camera))]
    public class MapCameraController : MonoBehaviour
    {
        private const float CameraPadding = 1.8f;
        // Roughly how many hex-radii of the board are visible at the
        // start of a match — fixed regardless of map size (see
        // Initialize), unlike maxOrthoSize below which scales with
        // mapRadius. Confirmed via three side-by-side screenshots
        // (Small/Medium/Large) that starting at maxOrthoSize (the whole-
        // map-fit ceiling) made every match's initial hex scale look
        // different depending on map size — this constant restores a
        // single, consistent starting zoom instead.
        private const float DefaultStartingOrthoSizePerHex = 7f;
        private const float DragThresholdPixels = 12f;
        private const float PinchZoomSpeed = 0.02f;
        // Starting guess, not verified against a real trackpad/mouse yet —
        // tune after trying it, same as every other feel-based constant in
        // this project.
        private const float ScrollZoomSpeed = 0.01f;
        // How long an eased PanTo takes to glide to its destination — see
        // PanTo's own comment for why this replaced an instant jump.
        private const float PanEaseDuration = 0.4f;

        // Fired the instant a completed gesture qualifies as a tap (press
        // + release under the drag threshold), not polled — a polled flag
        // would be timing-dependent on whether this component's Update or
        // a consumer's Update runs first in a given frame, since Unity
        // doesn't guarantee ordering between separate components. A direct
        // event invocation happens synchronously inside this Update, so
        // subscribers always see it the same frame regardless of order.
        public event Action<Vector2> Tapped;

        private readonly List<Vector2> activeTouchPositions = new(2);
        private Func<Vector2, bool> inputBlocker;
        private Camera orthoCamera;

        private float mapHalfExtentX;
        private float mapHalfExtentY;
        private float minOrthoSize;
        private float maxOrthoSize;

        // Single-pointer (one finger, or left mouse button) gesture state
        // — shared by touch and mouse since only one drives it per frame.
        private bool isPressed;
        private bool isDragging;
        private bool gestureBlocked;
        private Vector2 pressScreenPosition;
        private Vector2 lastScreenPosition;

        private bool pinchActive;
        private float previousPinchDistance;

        // Middle-mouse-drag pan is its own simple state, separate from the
        // primary pointer above — unlike a tap/touch drag there's no tap
        // to disambiguate, so it doesn't need the threshold machinery.
        private bool middleDragActive;
        private Vector2 lastMiddleDragPosition;

        // Active eased PanTo, if any — see PanTo/CancelPan.
        private Coroutine panRoutine;

        private void Awake()
        {
            orthoCamera = GetComponent<Camera>();
        }

        // Lets a consumer (MatchHud) reject gestures that start over its
        // own UI without this controller needing to know anything about
        // game-specific UI layout (e.g. MatchHud's still-IMGUI legacy
        // overlay Rect, which isn't covered by
        // EventSystem.IsPointerOverGameObject()).
        public void SetInputBlocker(Func<Vector2, bool> blocker)
        {
            inputBlocker = blocker;
        }

        // Called once per match start (including "new match" restarts,
        // which don't reload the scene) — resets zoom/pan bounds for the
        // new map and clears any in-progress gesture from the last match.
        public void Initialize(int mapRadius, float hexRadius)
        {
            // The zoom-out ceiling — scales with mapRadius since a
            // bigger map genuinely needs to zoom out further to fit on
            // screen. No longer used as the INITIAL zoom (see below) —
            // just the far end of the zoom range.
            maxOrthoSize = Mathf.Max(mapRadius * hexRadius * CameraPadding, 2f);
            // Roughly 3 hexes of radius visible at full zoom-in, clamped
            // so a tiny map (where maxOrthoSize is already small) can't
            // produce an inverted min > max range.
            minOrthoSize = Mathf.Min(maxOrthoSize, Mathf.Max(hexRadius * 3f, 2f));

            // True world-space half-extent of the hex-shaped map's
            // outermost tile centers, per HexLayout.AxialToWorld
            // (x = hexRadius*1.5*Q, y = hexRadius*sqrt(3)*(R+Q/2)) — NOT
            // the same as mapRadius*hexRadius, which is smaller in both
            // axes and clamps panning short of the real edges. Plus one
            // hexRadius of margin so the outermost hex's own visual edge
            // (not just its center) is reachable.
            mapHalfExtentX = hexRadius * 1.5f * mapRadius + hexRadius;
            mapHalfExtentY = hexRadius * Mathf.Sqrt(3f) * mapRadius + hexRadius;

            if (orthoCamera != null && orthoCamera.orthographic)
            {
                // Fixed starting zoom, not maxOrthoSize (whole-map-fit) —
                // that ceiling still scales with mapRadius so players can
                // pinch/scroll all the way out to frame an entire Medium
                // or Large map, but the DEFAULT view every match opens
                // on is the same hex scale no matter which map size was
                // picked. Clamped into this map's own valid zoom range
                // so a tiny custom map can't start outside its own bounds.
                orthoCamera.orthographicSize = Mathf.Clamp(hexRadius * DefaultStartingOrthoSizePerHex, minOrthoSize, maxOrthoSize);
                var position = orthoCamera.transform.position;
                orthoCamera.transform.position = new Vector3(0f, 0f, position.z);
            }

            CancelPrimaryPointer();
            pinchActive = false;
            middleDragActive = false;
            // The camera GameObject persists across matches (re-
            // Initialize'd, not recreated) — a pan left over from the
            // previous match would otherwise fight the position reset
            // just above.
            CancelPan();
        }

        private void Update()
        {
            if (orthoCamera == null || !orthoCamera.orthographic)
                return;

            var touchCount = CollectActiveTouches();

            if (touchCount >= 2)
            {
                // A second finger joining mid-gesture means it was never a
                // tap — cancel the single-pointer state without firing
                // Tapped, then let the pinch take over.
                CancelPrimaryPointer();
                HandlePinch(activeTouchPositions[0], activeTouchPositions[1]);
                return;
            }

            pinchActive = false;

            if (touchCount == 1)
            {
                ProcessPrimaryPointer(true, activeTouchPositions[0]);
                return;
            }

            // No touches this frame. If the primary pointer is still
            // "pressed," it can only be a touch that just lifted (mouse
            // handling below hasn't run yet this frame), so release it
            // now using the last known touch position.
            if (isPressed)
                ProcessPrimaryPointer(false, lastScreenPosition);

            HandleMouseInput();
        }

        private int CollectActiveTouches()
        {
            activeTouchPositions.Clear();
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
                return 0;

            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed)
                    activeTouchPositions.Add(touch.position.ReadValue());
            }

            return activeTouchPositions.Count;
        }

        private void HandleMouseInput()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var position = mouse.position.ReadValue();
            ProcessPrimaryPointer(mouse.leftButton.isPressed, position);
            HandleMiddleDrag(mouse, position);

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > Mathf.Epsilon)
                ApplyZoomDelta(-scroll * ScrollZoomSpeed, position);
        }

        private void HandleMiddleDrag(Mouse mouse, Vector2 position)
        {
            if (mouse.middleButton.wasPressedThisFrame)
            {
                middleDragActive = !IsBlocked(position);
                lastMiddleDragPosition = position;
            }
            else if (middleDragActive && mouse.middleButton.isPressed)
            {
                ApplyPan(lastMiddleDragPosition, position);
                lastMiddleDragPosition = position;
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                middleDragActive = false;
            }
        }

        // Drives both touch (1 finger) and mouse (left button) — the tap
        // vs. drag distinction only needs to exist once.
        private void ProcessPrimaryPointer(bool isDown, Vector2 currentPosition)
        {
            if (isDown)
            {
                if (!isPressed)
                {
                    isPressed = true;
                    isDragging = false;
                    pressScreenPosition = currentPosition;
                    gestureBlocked = IsBlocked(currentPosition);
                }
                else if (!gestureBlocked)
                {
                    if (!isDragging && Vector2.Distance(currentPosition, pressScreenPosition) > DragThresholdPixels)
                        isDragging = true;

                    if (isDragging)
                        ApplyPan(lastScreenPosition, currentPosition);
                }

                lastScreenPosition = currentPosition;
            }
            else
            {
                if (isPressed && !gestureBlocked && !isDragging)
                    Tapped?.Invoke(currentPosition);

                isPressed = false;
                isDragging = false;
            }
        }

        private void CancelPrimaryPointer()
        {
            isPressed = false;
            isDragging = false;
        }

        private void HandlePinch(Vector2 touchA, Vector2 touchB)
        {
            var distance = Vector2.Distance(touchA, touchB);
            if (!pinchActive)
            {
                // First frame of the pinch — just establish a baseline,
                // applying a delta here would cause a jump.
                pinchActive = true;
                previousPinchDistance = distance;
                return;
            }

            // Fingers moving apart (distance increasing) should zoom in
            // (orthographicSize decreasing), hence the negation. Anchored
            // on the pinch midpoint (see ApplyZoomDelta) so the point
            // between your fingers stays put as you zoom.
            ApplyZoomDelta(-(distance - previousPinchDistance) * PinchZoomSpeed, (touchA + touchB) * 0.5f);
            previousPinchDistance = distance;
        }

        private void ApplyPan(Vector2 fromScreen, Vector2 toScreen)
        {
            // Manual input always wins over an in-flight PanTo — without
            // this, a player dragging mid-glide would fight the
            // coroutine's own position writes every frame.
            CancelPan();
            var distanceFromCamera = Mathf.Abs(orthoCamera.transform.position.z);
            var fromWorld = orthoCamera.ScreenToWorldPoint(new Vector3(fromScreen.x, fromScreen.y, distanceFromCamera));
            var toWorld = orthoCamera.ScreenToWorldPoint(new Vector3(toScreen.x, toScreen.y, distanceFromCamera));
            orthoCamera.transform.position -= toWorld - fromWorld;
            ClampPosition();
        }

        // Anchored zoom: whatever world point currently sits under
        // anchorScreenPosition (the pinch midpoint, or the mouse cursor
        // for scroll-zoom) stays under that same screen position after
        // the size change, exactly like Google Maps/most map apps. This
        // is what makes the zoom pivot a single, predictable point
        // regardless of map size — the old version only changed
        // orthographicSize and left camera position untouched, which
        // pivoted around the screen center on paper, but ClampPosition
        // afterward would silently re-center small maps (little pan room
        // means clamping kicks in on almost every zoom step) while
        // rarely affecting large ones (lots of pan room), so the felt
        // pivot drifted depending on map size. Anchoring explicitly to a
        // screen position sidesteps that entirely — the math doesn't
        // depend on the map's clamp bounds at all, only clamping the
        // final result the same way every other mutator here already
        // does. Same ScreenToWorldPoint-diff technique as ApplyPan.
        private void ApplyZoomDelta(float sizeDelta, Vector2 anchorScreenPosition)
        {
            // Same reasoning as ApplyPan's own CancelPan call — a pinch/
            // scroll mid-glide shouldn't fight an in-flight PanTo either.
            CancelPan();
            var distanceFromCamera = Mathf.Abs(orthoCamera.transform.position.z);
            var anchorScreenPoint = new Vector3(anchorScreenPosition.x, anchorScreenPosition.y, distanceFromCamera);
            var worldBefore = orthoCamera.ScreenToWorldPoint(anchorScreenPoint);

            orthoCamera.orthographicSize = Mathf.Clamp(orthoCamera.orthographicSize + sizeDelta, minOrthoSize, maxOrthoSize);

            var worldAfter = orthoCamera.ScreenToWorldPoint(anchorScreenPoint);
            orthoCamera.transform.position += worldBefore - worldAfter;

            ClampPosition();
        }

        // Keeps the camera from panning the map fully out of view, using
        // the map's actual per-axis bounding half-extents (see
        // Initialize) rather than a single symmetric approximation.
        private void ClampPosition()
        {
            var viewHalfHeight = orthoCamera.orthographicSize;
            var viewHalfWidth = orthoCamera.orthographicSize * orthoCamera.aspect;
            var maxPanX = Mathf.Max(0f, mapHalfExtentX - viewHalfWidth);
            var maxPanY = Mathf.Max(0f, mapHalfExtentY - viewHalfHeight);

            var position = orthoCamera.transform.position;
            position.x = Mathf.Clamp(position.x, -maxPanX, maxPanX);
            position.y = Mathf.Clamp(position.y, -maxPanY, maxPanY);
            orthoCamera.transform.position = position;
        }

        // Shrinks the camera's own rendered viewport to leave room for a
        // persistent bottom UI dock (the dice bar) instead of drawing the
        // map full-screen underneath it — so a hex the player wants to
        // reach can never end up hidden behind the dock. Deliberately
        // leaves orthographicSize untouched: with pixelWidth unchanged
        // and only pixelHeight shrinking, Camera.aspect widens by
        // exactly the same ratio the visible height shrinks by, which
        // keeps world-units-per-pixel identical on both axes (a uniform
        // zoom-out, not a stretch/squish) — verified via the standard
        // orthographic aspect/halfWidth relationship, not guessed.
        //
        // Takes a fraction directly rather than a pixel amount divided
        // by Screen.height here — confirmed via a runtime log that under
        // Device Simulator, this camera's actual render target
        // (Camera.pixelRect) doesn't match what Screen.height reports at
        // all (pixelRect.height ~1004px against a reported Screen.height
        // of 2532), which silently produced a fraction ~40x too small.
        // The caller (MatchHudChrome.DiceBarReservedFraction) computes
        // this ratio entirely within UI canvas space instead, which
        // isn't subject to that mismatch.
        public void SetBottomReservedFraction(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            orthoCamera.rect = new Rect(0f, fraction, 1f, 1f - fraction);
        }

        // Eased recenter — e.g. a "locate my ship" HUD button, turn hand-
        // off, or a selected job/wormhole destination. Reuses the same
        // ClampPosition every pan/zoom mutator above already goes
        // through (applied every step of the glide, not just the final
        // frame), so this can never place the camera outside the map's
        // established pan bounds. Any manual pan/zoom cancels an
        // in-flight glide (see ApplyPan/ApplyZoomDelta) rather than
        // fighting it.
        public void PanTo(Vector3 worldPosition)
        {
            CancelPan();
            var target = new Vector3(worldPosition.x, worldPosition.y, orthoCamera.transform.position.z);
            panRoutine = StartCoroutine(PanToRoutine(target));
        }

        private IEnumerator PanToRoutine(Vector3 target)
        {
            var start = orthoCamera.transform.position;
            var elapsed = 0f;
            while (elapsed < PanEaseDuration)
            {
                elapsed += Time.deltaTime;
                var t = EaseOutCubic(Mathf.Clamp01(elapsed / PanEaseDuration));
                orthoCamera.transform.position = Vector3.Lerp(start, target, t);
                ClampPosition();
                yield return null;
            }

            orthoCamera.transform.position = target;
            ClampPosition();
            panRoutine = null;
        }

        private void CancelPan()
        {
            if (panRoutine == null)
                return;
            StopCoroutine(panRoutine);
            panRoutine = null;
        }

        // Ease-out (fast start, gentle settle) — matches the "arrival"
        // feel used for ship movement too (see MatchHud's own copy of
        // this curve), not a linear glide.
        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private bool IsBlocked(Vector2 screenPosition) => inputBlocker != null && inputBlocker(screenPosition);
    }
}
