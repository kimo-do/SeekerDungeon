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
        [SerializeField] private float maxTapMovementPixels = 18f;
        [SerializeField] private LocalPlayerJobMover localPlayerJobMover;
        [SerializeField] private RoomController roomController;
        [SerializeField] private DungeonManager dungeonManager;

        private LGManager _lgManager;
        private float _nextInteractTime;
        private bool _isProcessingInteract;
        private bool _pointerPressed;
        private int _pressedPointerId = -1;
        private Vector2 _pressedScreenPosition;
        private bool _pressedOverUi;

        private void Awake()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            _lgManager = LGManager.Instance;
            if (_lgManager == null)
            {
                _lgManager = UnityEngine.Object.FindFirstObjectByType<LGManager>();
            }

            if (localPlayerJobMover == null)
            {
                ResolveLocalPlayerJobMover();
            }

            if (roomController == null)
            {
                roomController = UnityEngine.Object.FindFirstObjectByType<RoomController>();
            }

            if (dungeonManager == null)
            {
                dungeonManager = UnityEngine.Object.FindFirstObjectByType<DungeonManager>();
            }
        }

        private void Update()
        {
            if (_isProcessingInteract || Time.unscaledTime < _nextInteractTime)
            {
                return;
            }

            if (TryGetPointerDownPosition(out var downPosition, out var downPointerId))
            {
                _pointerPressed = true;
                _pressedPointerId = downPointerId;
                _pressedScreenPosition = downPosition;
                _pressedOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(downPointerId);
                return;
            }

            if (!_pointerPressed)
            {
                return;
            }

            if (!TryGetPointerUpPosition(out var upPosition, out var upPointerId))
            {
                return;
            }

            if (upPointerId != _pressedPointerId)
            {
                return;
            }

            var movedDistance = Vector2.Distance(_pressedScreenPosition, upPosition);
            var releasedOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(upPointerId);
            var shouldInteract = !_pressedOverUi && !releasedOverUi && movedDistance <= maxTapMovementPixels;

            _pointerPressed = false;
            _pressedPointerId = -1;
            _pressedOverUi = false;

            if (!shouldInteract)
            {
                return;
            }

            TryHandleInteract(upPosition).Forget();
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
                    var wasDoorOpenBeforeInteraction = IsDoorOpenInCurrentState(door.Direction);
                    var hadRoomBefore = TryGetCurrentRoomCoordinates(out var previousRoomX, out var previousRoomY);
                    var signature = await _lgManager.InteractWithDoor((byte)door.Direction);
                    await _lgManager.FetchPlayerState();
                    var hasHelperStakeAfterInteraction = await _lgManager.HasHelperStakeInCurrentRoom((byte)door.Direction);
                    var playerMovedRooms = hadRoomBefore &&
                                           TryGetCurrentRoomCoordinates(out var currentRoomX, out var currentRoomY) &&
                                           (currentRoomX != previousRoomX || currentRoomY != previousRoomY);
                    var openDoorMoveAttempted = wasDoorOpenBeforeInteraction && !string.IsNullOrWhiteSpace(signature);
                    var shouldTransitionRoom = (playerMovedRooms || openDoorMoveAttempted) && dungeonManager != null;
                    if (shouldTransitionRoom)
                    {
                        await dungeonManager.TransitionToCurrentPlayerRoomAsync();
                    }
                    else if (!string.IsNullOrWhiteSpace(signature) || hasHelperStakeAfterInteraction)
                    {
                        if (localPlayerJobMover != null)
                        {
                            if (roomController != null &&
                                roomController.TryGetDoorStandPosition(door.Direction, out var standPosition))
                            {
                                localPlayerJobMover.MoveTo(standPosition);
                            }
                            else
                            {
                                localPlayerJobMover.MoveTo(door.InteractWorldPosition);
                            }
                        }
                    }

                    _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                    return;
                }

                var center = hit.GetComponentInParent<CenterInteractable>();
                if (center != null)
                {
                    var signature = await _lgManager.InteractWithCenter();
                    if (!string.IsNullOrWhiteSpace(signature) && localPlayerJobMover != null)
                    {
                        localPlayerJobMover.MoveTo(center.InteractWorldPosition);
                    }

                    _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                }
            }
            finally
            {
                _isProcessingInteract = false;
            }
        }

        private void ResolveLocalPlayerJobMover()
        {
            if (localPlayerJobMover != null)
            {
                return;
            }

            var playerControllers = UnityEngine.Object.FindObjectsByType<LGPlayerController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (var index = 0; index < playerControllers.Length; index += 1)
            {
                var playerController = playerControllers[index];
                if (playerController == null)
                {
                    continue;
                }

                if (playerController.GetComponentInParent<DoorOccupantVisual2D>() != null)
                {
                    continue;
                }

                localPlayerJobMover = playerController.GetComponent<LocalPlayerJobMover>();
                if (localPlayerJobMover == null)
                {
                    localPlayerJobMover = playerController.gameObject.AddComponent<LocalPlayerJobMover>();
                }

                return;
            }

            localPlayerJobMover = UnityEngine.Object.FindFirstObjectByType<LocalPlayerJobMover>();
        }

        private bool TryGetCurrentRoomCoordinates(out int roomX, out int roomY)
        {
            roomX = default;
            roomY = default;

            var playerState = _lgManager?.CurrentPlayerState;
            if (playerState == null)
            {
                return false;
            }

            roomX = playerState.CurrentRoomX;
            roomY = playerState.CurrentRoomY;
            return true;
        }

        private bool IsDoorOpenInCurrentState(RoomDirection direction)
        {
            var roomState = _lgManager?.CurrentRoomState;
            if (roomState?.Walls == null)
            {
                return false;
            }

            var directionIndex = (int)direction;
            if (directionIndex < 0 || directionIndex >= roomState.Walls.Length)
            {
                return false;
            }

            return roomState.Walls[directionIndex] == LGConfig.WALL_OPEN;
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

        private static bool TryGetPointerUpPosition(out Vector2 position, out int pointerId)
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
    }
}
