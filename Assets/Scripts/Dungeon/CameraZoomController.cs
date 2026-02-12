using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Cinemachine;
using System.Collections.Generic;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Handles camera zoom (scroll / pinch) and pan (drag).
    /// Only writes to panTarget.position during an active drag gesture or an explicit snap.
    /// Never touches panTarget outside of those two cases -- Cinemachine owns it otherwise.
    /// </summary>
    public sealed class CameraZoomController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform panTarget;

        [Header("Zoom")]
        [SerializeField] private float minZoom = 1.2f;
        [SerializeField] private float maxZoom = 6.0f;
        [SerializeField] private float mouseWheelZoomStep = 0.45f;
        [SerializeField] private float pinchZoomSensitivity = 0.012f;
        [SerializeField] private float zoomLerpSpeed = 12.0f;

        [Header("Pan")]
        [SerializeField] private float dragPanSensitivity = 1.0f;
        [SerializeField] private Vector2 panBoundsX = new(-3f, 3f);
        [SerializeField] private Vector2 panBoundsY = new(-3f, 3f);

        /// <summary>Minimum pixel distance before a press becomes a drag.</summary>
        private const float DragThresholdPixelsSq = 10f * 10f;

        // ── Zoom state ──
        private float _targetZoom;

        // ── Pan / drag state ──
        private bool _pointerDown;
        private bool _isDragging; // true only after pointer moved past threshold
        private int _activePointerId = int.MinValue;
        private Vector2 _pointerDownScreenPos;
        private Vector2 _lastPointerScreenPos;
        private bool _pointerStartedOverUi;

        public bool IsPanning => _isDragging;

        // ──────────────────────────────────────────────

        private void Awake()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            if (cinemachineCamera == null)
                cinemachineCamera = FindObjectOfType<CinemachineCamera>();

            if (panTarget == null && cinemachineCamera != null)
                panTarget = cinemachineCamera.Follow;

            if (panTarget == null)
            {
                var go = new GameObject("CameraPanTarget");
                go.transform.position = worldCamera != null
                    ? new Vector3(worldCamera.transform.position.x, worldCamera.transform.position.y, 0f)
                    : Vector3.zero;
                panTarget = go.transform;
            }

            if (cinemachineCamera != null && cinemachineCamera.Follow != panTarget)
                cinemachineCamera.Follow = panTarget;

            _targetZoom = GetCurrentZoom();
        }

        private void Update()
        {
            // ── Zoom (always active) ──
            HandleZoomInput();
            ApplyZoomSmoothing();

            // ── Pan: only process when pointer is inside the game view ──
            if (!IsMouseInsideGameView())
            {
                if (_pointerDown)
                    CancelDrag();
                return;
            }

            HandlePanInput();
        }

        // ═══════════════════════════════════════════
        //  ZOOM
        // ═══════════════════════════════════════════

        private void HandleZoomInput()
        {
            // Mouse wheel
            float scrollDelta = 0f;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                scrollDelta = Mouse.current.scroll.ReadValue().y;
#else
            scrollDelta = Input.mouseScrollDelta.y;
#endif
            if (Mathf.Abs(scrollDelta) > 0.01f)
                _targetZoom = Mathf.Clamp(_targetZoom - Mathf.Sign(scrollDelta) * mouseWheelZoomStep, minZoom, maxZoom);

            // Pinch
            HandlePinchZoom();
        }

        private void HandlePinchZoom()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current == null) return;
            var touches = Touchscreen.current.touches;
            UnityEngine.InputSystem.Controls.TouchControl t0 = null, t1 = null;
            int count = 0;
            for (int i = 0; i < touches.Count; i++)
            {
                if (!touches[i].isInProgress) continue;
                if (count == 0) t0 = touches[i];
                else if (count == 1) t1 = touches[i];
                if (++count >= 2) break;
            }
            if (count < 2) return;
            var curDist = Vector2.Distance(t0.position.ReadValue(), t1.position.ReadValue());
            var prevDist = Vector2.Distance(
                t0.position.ReadValue() - t0.delta.ReadValue(),
                t1.position.ReadValue() - t1.delta.ReadValue());
            var delta = curDist - prevDist;
#else
            if (Input.touchCount < 2) return;
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);
            var curDist = Vector2.Distance(t0.position, t1.position);
            var prevDist = Vector2.Distance(t0.position - t0.deltaPosition, t1.position - t1.deltaPosition);
            var delta = curDist - prevDist;
#endif
            if (Mathf.Abs(delta) > 0.01f)
                _targetZoom = Mathf.Clamp(_targetZoom - delta * pinchZoomSensitivity, minZoom, maxZoom);

            // Cancel any single-finger drag when pinching
            if (_pointerDown) CancelDrag();
        }

        private void ApplyZoomSmoothing()
        {
            var cur = GetCurrentZoom();
            if (Mathf.Abs(cur - _targetZoom) < 0.001f)
                SetCurrentZoom(_targetZoom);
            else
                SetCurrentZoom(Mathf.Lerp(cur, _targetZoom, zoomLerpSpeed * Time.unscaledDeltaTime));
        }

        // ═══════════════════════════════════════════
        //  PAN (drag only)
        // ═══════════════════════════════════════════

        private void HandlePanInput()
        {
            // ── Pointer down ──
            if (!_pointerDown && TryGetPointerDown(out var downPos, out var downId))
            {
                _pointerDown = true;
                _isDragging = false;
                _activePointerId = downId;
                _pointerDownScreenPos = downPos;
                _lastPointerScreenPos = downPos;
                _pointerStartedOverUi = IsPointerOverUi(downId);
                return;
            }

            if (!_pointerDown) return;

            // ── Pointer up ──
            if (TryGetPointerUp(out _, out var upId) && upId == _activePointerId)
            {
                CancelDrag();
                return;
            }

            // ── Pointer held: compute drag ──
            if (!TryGetPointerPosition(_activePointerId, out var currentScreenPos))
                return;

            if (_pointerStartedOverUi)
            {
                _lastPointerScreenPos = currentScreenPos;
                return;
            }

            // Has the pointer moved far enough to count as a drag?
            if (!_isDragging)
            {
                if ((currentScreenPos - _pointerDownScreenPos).sqrMagnitude < DragThresholdPixelsSq)
                {
                    _lastPointerScreenPos = currentScreenPos;
                    return;
                }
                _isDragging = true;
            }

            // Apply drag delta to panTarget directly -- no lerp, no smoothing.
            if (TryScreenToWorldOnPanPlane(_lastPointerScreenPos, out var prevWorld) &&
                TryScreenToWorldOnPanPlane(currentScreenPos, out var curWorld))
            {
                var worldDelta = prevWorld - curWorld;
                var pos = panTarget.position + worldDelta * dragPanSensitivity;
                pos = ClampPan(pos);
                panTarget.position = pos;
            }

            _lastPointerScreenPos = currentScreenPos;
        }

        private void CancelDrag()
        {
            _pointerDown = false;
            _isDragging = false;
            _activePointerId = int.MinValue;
            _pointerStartedOverUi = false;
        }

        // ═══════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════

        /// <summary>
        /// Instantly move the camera to a world position. Called once per room transition.
        /// Sets panTarget directly -- no lerp.
        /// </summary>
        public void SnapToWorldPositionInstant(Vector3 worldPosition)
        {
            if (panTarget == null) return;
            worldPosition.z = panTarget.position.z;
            panTarget.position = ClampPan(worldPosition);
        }

        // ═══════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════

        private Vector3 ClampPan(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, panBoundsX.x, panBoundsX.y);
            p.y = Mathf.Clamp(p.y, panBoundsY.x, panBoundsY.y);
            return p;
        }

        private float GetCurrentZoom()
        {
            if (cinemachineCamera != null)
            {
                var lens = cinemachineCamera.Lens;
                return (lens.Orthographic || (worldCamera != null && worldCamera.orthographic))
                    ? lens.OrthographicSize
                    : lens.FieldOfView;
            }
            if (worldCamera != null)
                return worldCamera.orthographic ? worldCamera.orthographicSize : worldCamera.fieldOfView;
            return 5f;
        }

        private void SetCurrentZoom(float zoom)
        {
            if (cinemachineCamera != null)
            {
                var lens = cinemachineCamera.Lens;
                if (lens.Orthographic || (worldCamera != null && worldCamera.orthographic))
                    lens.OrthographicSize = zoom;
                else
                    lens.FieldOfView = zoom;
                cinemachineCamera.Lens = lens;
                return;
            }
            if (worldCamera == null) return;
            if (worldCamera.orthographic) worldCamera.orthographicSize = zoom;
            else worldCamera.fieldOfView = zoom;
        }

        private bool TryScreenToWorldOnPanPlane(Vector2 screenPt, out Vector3 worldPt)
        {
            worldPt = default;
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
                if (worldCamera == null) return false;
            }
            var z = panTarget != null ? panTarget.position.z : 0f;
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
            var ray = worldCamera.ScreenPointToRay(screenPt);
            if (!plane.Raycast(ray, out var enter)) return false;
            worldPt = ray.GetPoint(enter);
            return true;
        }

        private static bool IsMouseInsideGameView()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return false;
            var mp = Mouse.current.position.ReadValue();
#else
            var mp = (Vector2)Input.mousePosition;
#endif
            return mp.x >= 0f && mp.x <= Screen.width && mp.y >= 0f && mp.y <= Screen.height;
        }

        private static bool IsPointerOverUi(int pointerId)
        {
            if (EventSystem.current == null) return false;
            if (TryGetPointerPosition(pointerId, out var pos))
            {
                var data = new PointerEventData(EventSystem.current) { position = pos };
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(data, results);
                return results.Count > 0;
            }
            return pointerId < 0
                ? EventSystem.current.IsPointerOverGameObject()
                : EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        // ── Pointer abstraction ──

        private static bool TryGetPointerDown(out Vector2 pos, out int id)
        {
            pos = default; id = -1;
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var pt = Touchscreen.current.primaryTouch;
                if (pt.press.wasPressedThisFrame) { pos = pt.position.ReadValue(); id = pt.touchId.ReadValue(); return true; }
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) { pos = Mouse.current.position.ReadValue(); id = -1; return true; }
#else
            if (Input.touchCount > 0) { var t = Input.GetTouch(0); if (t.phase == TouchPhase.Began) { pos = t.position; id = t.fingerId; return true; } }
            if (Input.GetMouseButtonDown(0)) { pos = Input.mousePosition; id = -1; return true; }
#endif
            return false;
        }

        private static bool TryGetPointerUp(out Vector2 pos, out int id)
        {
            pos = default; id = -1;
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var pt = Touchscreen.current.primaryTouch;
                if (pt.press.wasReleasedThisFrame) { pos = pt.position.ReadValue(); id = pt.touchId.ReadValue(); return true; }
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) { pos = Mouse.current.position.ReadValue(); id = -1; return true; }
#else
            if (Input.touchCount > 0) { var t = Input.GetTouch(0); if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { pos = t.position; id = t.fingerId; return true; } }
            if (Input.GetMouseButtonUp(0)) { pos = Input.mousePosition; id = -1; return true; }
#endif
            return false;
        }

        private static bool TryGetPointerPosition(int pointerId, out Vector2 pos)
        {
            pos = default;
#if ENABLE_INPUT_SYSTEM
            if (pointerId >= 0 && Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].isInProgress && touches[i].touchId.ReadValue() == pointerId) { pos = touches[i].position.ReadValue(); return true; }
                }
            }
            if (pointerId < 0 && Mouse.current != null && Mouse.current.leftButton.isPressed) { pos = Mouse.current.position.ReadValue(); return true; }
#else
            if (pointerId >= 0) { for (int i = 0; i < Input.touchCount; i++) { var t = Input.GetTouch(i); if (t.fingerId == pointerId && t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled) { pos = t.position; return true; } } }
            if (pointerId < 0 && Input.GetMouseButton(0)) { pos = Input.mousePosition; return true; }
#endif
            return false;
        }
    }
}
