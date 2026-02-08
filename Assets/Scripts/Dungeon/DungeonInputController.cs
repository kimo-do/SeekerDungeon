using Cysharp.Threading.Tasks;
using SeekerDungeon.Solana;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonInputController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private LayerMask interactableMask = ~0;
        [SerializeField] private float interactCooldownSeconds = 0.15f;

        private LGManager _lgManager;
        private float _nextInteractTime;
        private bool _isProcessingInteract;

        private void Awake()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            _lgManager = LGManager.Instance;
            if (_lgManager == null)
            {
                _lgManager = FindObjectOfType<LGManager>();
            }
        }

        private void Update()
        {
            if (_isProcessingInteract || Time.unscaledTime < _nextInteractTime)
            {
                return;
            }

            if (!TryGetPointerDownPosition(out var pointerPosition, out var pointerId))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId))
            {
                return;
            }

            TryHandleInteract(pointerPosition).Forget();
        }

        private async UniTaskVoid TryHandleInteract(Vector2 screenPosition)
        {
            if (_lgManager == null)
            {
                return;
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
                if (worldCamera == null)
                {
                    return;
                }
            }

            var worldPoint = worldCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 0f));
            var hit = Physics2D.OverlapPoint(new Vector2(worldPoint.x, worldPoint.y), interactableMask);
            if (hit == null)
            {
                return;
            }

            _isProcessingInteract = true;
            try
            {
                var door = hit.GetComponentInParent<DoorInteractable>();
                if (door != null)
                {
                    await _lgManager.InteractWithDoor((byte)door.Direction);
                    _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                    return;
                }

                var center = hit.GetComponentInParent<CenterInteractable>();
                if (center != null)
                {
                    await _lgManager.InteractWithCenter();
                    _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                }
            }
            finally
            {
                _isProcessingInteract = false;
            }
        }

        private static bool TryGetPointerDownPosition(out Vector2 position, out int pointerId)
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
    }
}
