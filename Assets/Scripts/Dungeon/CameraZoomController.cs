using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Cinemachine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeekerDungeon.Dungeon
{
    public sealed class CameraZoomController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform panTarget;

        [Header("Zoom Limits")]
        [SerializeField] private float minZoom = 1.2f;
        [SerializeField] private float maxZoom = 6.0f;

        [Header("Input")]
        [SerializeField] private float mouseWheelZoomStep = 0.45f;
        [SerializeField] private float pinchZoomSensitivity = 0.012f;
        [SerializeField] private float dragPanSensitivity = 1.0f;

        [Header("Smoothing")]
        [SerializeField] private float zoomLerpSpeed = 12.0f;
        [SerializeField] private float panLerpSpeed = 20.0f;
        [Header("Pan Bounds")]
        [SerializeField] private Vector2 panBoundsX = new Vector2(-3f, 3f);
        [SerializeField] private Vector2 panBoundsY = new Vector2(-3f, 3f);

        private float _targetZoom;
        private Vector3 _targetPanPosition;
        private bool _isPanning;
        private int _activePointerId = int.MinValue;
        private Vector2 _lastPointerScreenPosition;
        private bool _pointerStartedOverUi;

        public bool IsPanning => _isPanning;

        private void Awake()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (cinemachineCamera == null)
            {
                cinemachineCamera = FindObjectOfType<CinemachineCamera>();
            }

            if (panTarget == null && cinemachineCamera != null)
            {
                panTarget = cinemachineCamera.Follow;
            }

            if (panTarget == null)
            {
                var go = new GameObject("CameraPanTarget");
                var startPosition = Vector3.zero;
                if (worldCamera != null)
                {
                    startPosition = worldCamera.transform.position;
                }

                startPosition.z = 0f;
                go.transform.position = startPosition;
                panTarget = go.transform;
            }

            if (cinemachineCamera != null && cinemachineCamera.Follow != panTarget)
            {
                cinemachineCamera.Follow = panTarget;
            }

            _targetPanPosition = panTarget.position;
            _targetPanPosition = ClampPanPosition(_targetPanPosition);
            panTarget.position = _targetPanPosition;
            _targetZoom = GetCurrentZoom();
        }

        private void Update()
        {
            HandleMouseWheelZoom();
            var pinching = HandlePinchZoom();
            HandlePan(pinching);
            ApplyCameraSmoothing();
        }

        private void HandleMouseWheelZoom()
        {
            float scrollDelta;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return;
            }

            scrollDelta = Mouse.current.scroll.ReadValue().y;
#else
            scrollDelta = Input.mouseScrollDelta.y;
#endif

            if (Mathf.Abs(scrollDelta) < 0.01f)
            {
                return;
            }

            _targetZoom = Mathf.Clamp(_targetZoom - Mathf.Sign(scrollDelta) * mouseWheelZoomStep, minZoom, maxZoom);
        }

        private bool HandlePinchZoom()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current == null)
            {
                return false;
            }

            var touches = Touchscreen.current.touches;
            var activeTouchCount = 0;
            var firstTouch = default(UnityEngine.InputSystem.Controls.TouchControl);
            var secondTouch = default(UnityEngine.InputSystem.Controls.TouchControl);

            for (var i = 0; i < touches.Count; i += 1)
            {
                if (!touches[i].isInProgress)
                {
                    continue;
                }

                if (activeTouchCount == 0)
                {
                    firstTouch = touches[i];
                }
                else if (activeTouchCount == 1)
                {
                    secondTouch = touches[i];
                }

                activeTouchCount += 1;
                if (activeTouchCount >= 2)
                {
                    break;
                }
            }

            if (activeTouchCount < 2)
            {
                return false;
            }

            var currentDistance = Vector2.Distance(
                firstTouch.position.ReadValue(),
                secondTouch.position.ReadValue());

            var previousDistance = Vector2.Distance(
                firstTouch.position.ReadValue() - firstTouch.delta.ReadValue(),
                secondTouch.position.ReadValue() - secondTouch.delta.ReadValue());

            var pinchDelta = currentDistance - previousDistance;
#else
            if (Input.touchCount < 2)
            {
                return false;
            }

            var firstTouch = Input.GetTouch(0);
            var secondTouch = Input.GetTouch(1);

            var currentDistance = Vector2.Distance(firstTouch.position, secondTouch.position);
            var previousDistance = Vector2.Distance(
                firstTouch.position - firstTouch.deltaPosition,
                secondTouch.position - secondTouch.deltaPosition);

            var pinchDelta = currentDistance - previousDistance;
#endif

            if (Mathf.Abs(pinchDelta) < 0.01f)
            {
                return true;
            }

            _targetZoom = Mathf.Clamp(_targetZoom - pinchDelta * pinchZoomSensitivity, minZoom, maxZoom);
            return true;
        }

        private void HandlePan(bool pinching)
        {
            if (pinching)
            {
                EndPan();
                return;
            }

            if (TryGetPointerDown(out var downPosition, out var downPointerId))
            {
                _isPanning = true;
                _activePointerId = downPointerId;
                _lastPointerScreenPosition = downPosition;
                _pointerStartedOverUi = IsPointerOverUi(downPointerId);
                return;
            }

            if (!_isPanning)
            {
                return;
            }

            if (TryGetPointerPosition(_activePointerId, out var currentScreenPosition))
            {
                if (!_pointerStartedOverUi && TryScreenToPanPlane(_lastPointerScreenPosition, out var previousWorld) &&
                    TryScreenToPanPlane(currentScreenPosition, out var currentWorld))
                {
                    var worldDelta = previousWorld - currentWorld;
                    _targetPanPosition += worldDelta * dragPanSensitivity;
                    _targetPanPosition = ClampPanPosition(_targetPanPosition);
                }

                _lastPointerScreenPosition = currentScreenPosition;
            }

            if (TryGetPointerUp(out _, out var upPointerId) && upPointerId == _activePointerId)
            {
                EndPan();
            }
        }

        private void ApplyCameraSmoothing()
        {
            var currentZoom = GetCurrentZoom();
            var nextZoom = Mathf.Lerp(currentZoom, _targetZoom, zoomLerpSpeed * Time.unscaledDeltaTime);
            SetCurrentZoom(nextZoom);

            if (panTarget != null)
            {
                _targetPanPosition = ClampPanPosition(_targetPanPosition);
                panTarget.position = Vector3.Lerp(
                    panTarget.position,
                    _targetPanPosition,
                    panLerpSpeed * Time.unscaledDeltaTime);
            }
        }

        private Vector3 ClampPanPosition(Vector3 position)
        {
            position.x = Mathf.Clamp(position.x, panBoundsX.x, panBoundsX.y);
            position.y = Mathf.Clamp(position.y, panBoundsY.x, panBoundsY.y);
            return position;
        }

        private float GetCurrentZoom()
        {
            if (cinemachineCamera != null)
            {
                var lens = cinemachineCamera.Lens;
                if (lens.Orthographic || (worldCamera != null && worldCamera.orthographic))
                {
                    return lens.OrthographicSize;
                }

                return lens.FieldOfView;
            }

            if (worldCamera != null)
            {
                return worldCamera.orthographic ? worldCamera.orthographicSize : worldCamera.fieldOfView;
            }

            return 5f;
        }

        private void SetCurrentZoom(float zoom)
        {
            if (cinemachineCamera != null)
            {
                var lens = cinemachineCamera.Lens;
                if (lens.Orthographic || (worldCamera != null && worldCamera.orthographic))
                {
                    lens.OrthographicSize = zoom;
                }
                else
                {
                    lens.FieldOfView = zoom;
                }

                cinemachineCamera.Lens = lens;
                return;
            }

            if (worldCamera == null)
            {
                return;
            }

            if (worldCamera.orthographic)
            {
                worldCamera.orthographicSize = zoom;
                return;
            }

            worldCamera.fieldOfView = zoom;
        }

        private bool TryScreenToPanPlane(Vector2 screenPoint, out Vector3 worldPoint)
        {
            worldPoint = default;

            if (worldCamera == null)
            {
                return false;
            }

            var targetPlaneZ = panTarget != null ? panTarget.position.z : 0f;
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, targetPlaneZ));
            var ray = worldCamera.ScreenPointToRay(screenPoint);
            if (!plane.Raycast(ray, out var enter))
            {
                return false;
            }

            worldPoint = ray.GetPoint(enter);
            return true;
        }

        private static bool IsPointerOverUi(int pointerId)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            return EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        private void EndPan()
        {
            _isPanning = false;
            _activePointerId = int.MinValue;
            _pointerStartedOverUi = false;
        }

        private static bool TryGetPointerDown(out Vector2 position, out int pointerId)
        {
            position = default;
            pointerId = -1;

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var primaryTouch = Touchscreen.current.primaryTouch;
                if (primaryTouch.press.wasPressedThisFrame)
                {
                    position = primaryTouch.position.ReadValue();
                    pointerId = primaryTouch.touchId.ReadValue();
                    return true;
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = -1;
                return true;
            }
#else
            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    position = touch.position;
                    pointerId = touch.fingerId;
                    return true;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                pointerId = -1;
                return true;
            }
#endif

            return false;
        }

        private static bool TryGetPointerUp(out Vector2 position, out int pointerId)
        {
            position = default;
            pointerId = -1;

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                var primaryTouch = Touchscreen.current.primaryTouch;
                if (primaryTouch.press.wasReleasedThisFrame)
                {
                    position = primaryTouch.position.ReadValue();
                    pointerId = primaryTouch.touchId.ReadValue();
                    return true;
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = -1;
                return true;
            }
#else
            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    position = touch.position;
                    pointerId = touch.fingerId;
                    return true;
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                position = Input.mousePosition;
                pointerId = -1;
                return true;
            }
#endif

            return false;
        }

        private static bool TryGetPointerPosition(int pointerId, out Vector2 position)
        {
            position = default;

#if ENABLE_INPUT_SYSTEM
            if (pointerId >= 0 && Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (var i = 0; i < touches.Count; i += 1)
                {
                    if (!touches[i].isInProgress)
                    {
                        continue;
                    }

                    if (touches[i].touchId.ReadValue() != pointerId)
                    {
                        continue;
                    }

                    position = touches[i].position.ReadValue();
                    return true;
                }
            }

            if (pointerId < 0 && Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }
#else
            if (pointerId >= 0)
            {
                for (var i = 0; i < Input.touchCount; i += 1)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.fingerId != pointerId || touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        continue;
                    }

                    position = touch.position;
                    return true;
                }
            }

            if (pointerId < 0 && Input.GetMouseButton(0))
            {
                position = Input.mousePosition;
                return true;
            }
#endif

            return false;
        }
    }
}
