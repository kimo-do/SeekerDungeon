using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeekerDungeon.Dungeon
{
    [RequireComponent(typeof(Camera))]
    public sealed class CameraZoomController : MonoBehaviour
    {
        [Header("Zoom Limits")]
        [SerializeField] private float minZoom = 1.2f;
        [SerializeField] private float maxZoom = 6.0f;

        [Header("Input")]
        [SerializeField] private float mouseWheelZoomStep = 0.45f;
        [SerializeField] private float pinchZoomSensitivity = 0.012f;

        [Header("Smoothing")]
        [SerializeField] private float zoomLerpSpeed = 12.0f;

        private Camera _camera;
        private float _targetZoom;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _targetZoom = GetCurrentZoom();
        }

        private void Update()
        {
            HandleMouseWheelZoom();
            HandlePinchZoom();
            ApplyZoomSmoothing();
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

        private void HandlePinchZoom()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current == null)
            {
                return;
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
                return;
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
                return;
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
                return;
            }

            _targetZoom = Mathf.Clamp(_targetZoom - pinchDelta * pinchZoomSensitivity, minZoom, maxZoom);
        }

        private void ApplyZoomSmoothing()
        {
            var currentZoom = GetCurrentZoom();
            var nextZoom = Mathf.Lerp(currentZoom, _targetZoom, zoomLerpSpeed * Time.unscaledDeltaTime);
            SetCurrentZoom(nextZoom);
        }

        private float GetCurrentZoom()
        {
            return _camera.orthographic ? _camera.orthographicSize : _camera.fieldOfView;
        }

        private void SetCurrentZoom(float zoom)
        {
            if (_camera.orthographic)
            {
                _camera.orthographicSize = zoom;
                return;
            }

            _camera.fieldOfView = zoom;
        }
    }
}
