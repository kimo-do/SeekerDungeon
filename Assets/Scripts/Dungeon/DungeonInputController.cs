using Cysharp.Threading.Tasks;
using SeekerDungeon.Audio;
using SeekerDungeon.Solana;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Text.RegularExpressions;

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
        [SerializeField] private LootSequenceController lootSequenceController;
        [SerializeField] private SeekerDungeon.Solana.LGGameHudUI gameHudUI;
        [SerializeField] private string exitSceneName = "MenuScene";

        private LGManager _lgManager;
        private LGPlayerController _localPlayerController;
        private float _nextInteractTime;
        private bool _isProcessingInteract;
        private bool _pointerPressed;
        private int _pressedPointerId = -1;
        private Vector2 _pressedScreenPosition;
        private bool _pressedOverUi;
        private bool _isHandlingDeathExitAlreadyApplied;

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

            if (gameHudUI == null)
            {
                gameHudUI = UnityEngine.Object.FindFirstObjectByType<SeekerDungeon.Solana.LGGameHudUI>();
            }
        }

        private void OnEnable()
        {
            if (_lgManager != null)
            {
                _lgManager.OnPlayerStateUpdated += HandlePlayerStateUpdated;
            }
        }

        private void OnDisable()
        {
            if (_lgManager != null)
            {
                _lgManager.OnPlayerStateUpdated -= HandlePlayerStateUpdated;
            }
        }

        private void HandlePlayerStateUpdated(Chaindepth.Accounts.PlayerAccount player)
        {
            ResolveLocalPlayerController();
            if (_localPlayerController == null || player == null) return;

            // Don't revert wielded items while an optimistic job is pending;
            // stale RPC reads would incorrectly clear them.
            if (dungeonManager != null &&
                (dungeonManager.HasOptimisticJob || dungeonManager.HasOptimisticBossFight))
                return;

            // If the player has no active jobs in the current room, hide wielded items
            var hasAnyJobHere = false;
            if (player.ActiveJobs != null)
            {
                foreach (var job in player.ActiveJobs)
                {
                    if (job != null &&
                        job.RoomX == player.CurrentRoomX &&
                        job.RoomY == player.CurrentRoomY)
                    {
                        hasAnyJobHere = true;
                        break;
                    }
                }
            }

            if (!hasAnyJobHere && (dungeonManager == null || !dungeonManager.IsLocalPlayerFightingBoss))
            {
                _localPlayerController.HideAllWieldedItems();
            }
        }

        private void ResolveLocalPlayerController()
        {
            if (_localPlayerController != null) return;
            _localPlayerController = UnityEngine.Object.FindFirstObjectByType<LGPlayerController>();
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
                    var wasRubbleBeforeInteraction = IsDoorRubbleInCurrentState(door.Direction);
                    var wasLockedBeforeInteraction = IsDoorLockedInCurrentState(door.Direction);
                    var wasEntranceStairsBeforeInteraction = IsDoorEntranceStairsInCurrentState(door.Direction);
                    var hadRoomBefore = TryGetCurrentRoomCoordinates(out var previousRoomX, out var previousRoomY);

                    GameplayActionLog.DoorClicked(
                        door.Direction.ToString(),
                        hadRoomBefore ? previousRoomX : -1,
                        hadRoomBefore ? previousRoomY : -1,
                        wasDoorOpenBeforeInteraction
                            ? "Open"
                            : wasRubbleBeforeInteraction
                                ? "Rubble"
                                : wasEntranceStairsBeforeInteraction
                                    ? "EntranceStairs"
                                    : "Solid",
                        _lgManager?.HasActiveJobInCurrentRoom((byte)door.Direction) ?? false);

                    if (wasEntranceStairsBeforeInteraction)
                    {
                        var shouldExitDungeon = gameHudUI == null ||
                                                await gameHudUI.ShowExitDungeonConfirmationAsync();
                        if (!shouldExitDungeon)
                        {
                            _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                            return;
                        }

                        await HandleDungeonExitAsync((byte)door.Direction);
                        _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                        return;
                    }

                    // ── Optimistic UI: immediately show job visuals if clicking rubble ──
                    // This makes the game feel responsive while we wait for the transaction.
                    if (wasRubbleBeforeInteraction && !wasDoorOpenBeforeInteraction)
                    {
                        ResolveLocalPlayerController();
                        if (_localPlayerController != null)
                        {
                            var equippedId = _lgManager.CurrentPlayerState != null
                                ? LGDomainMapper.ToItemId(_lgManager.CurrentPlayerState.EquippedItemId)
                                : ItemId.BronzePickaxe;
                            _localPlayerController.ShowWieldedItem(equippedId);
                            _localPlayerController.SetMiningAnimationState(true);
                            _localPlayerController.SetBossJobAnimationState(false);
                        }

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

                    // Tell DungeonManager about the optimistic job so stale
                    // snapshots from RefreshAllState do not revert the visuals.
                    if (wasRubbleBeforeInteraction && !wasDoorOpenBeforeInteraction && dungeonManager != null)
                    {
                        dungeonManager.SetOptimisticJobDirection(door.Direction);
                    }

                    // ── Execute the on-chain interaction ──
                    var doorResult = await _lgManager.InteractWithDoor((byte)door.Direction);
                    if (!doorResult.Success)
                    {
                        ShowDoorFailureToastIfApplicable(doorResult.Error);
                    }

                    GameplayActionLog.DoorTxResult(
                        door.Direction.ToString(),
                        doorResult.Success,
                        doorResult.Success ? doorResult.Signature : doorResult.Error);

                    if (doorResult.Success)
                    {
                        if (wasDoorOpenBeforeInteraction)
                        {
                            GameAudioManager.Instance?.PlayWorld(
                                WorldSfxId.DoorOpenOpen,
                                door.InteractWorldPosition);
                        }
                        else if (wasRubbleBeforeInteraction)
                        {
                            GameAudioManager.Instance?.PlayWorld(
                                WorldSfxId.DoorOpenRubble,
                                door.InteractWorldPosition);
                        }
                        else if (wasLockedBeforeInteraction)
                        {
                            GameAudioManager.Instance?.PlayWorld(
                                WorldSfxId.DoorUnlock,
                                door.InteractWorldPosition);
                        }
                    }

                    // ── Post-transaction: explicit state refresh ──
                    if (wasEntranceStairsBeforeInteraction && doorResult.Success)
                    {
                        await _lgManager.RefreshAllState();
                        await LoadExitSceneAsync();
                        _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                        return;
                    }

                    await _lgManager.FetchPlayerState();
                    var hasHelperStakeAfterInteraction = await _lgManager.HasHelperStakeInCurrentRoom((byte)door.Direction);
                    var playerMovedRooms = hadRoomBefore &&
                                           TryGetCurrentRoomCoordinates(out var currentRoomX, out var currentRoomY) &&
                                           (currentRoomX != previousRoomX || currentRoomY != previousRoomY);
                    var openDoorMoveAttempted = wasDoorOpenBeforeInteraction && doorResult.Success;
                    var shouldTransitionRoom = (playerMovedRooms || openDoorMoveAttempted) && dungeonManager != null;
                    ResolveLocalPlayerController();

                    {
                        TryGetCurrentRoomCoordinates(out var postRoomX, out var postRoomY);
                        GameplayActionLog.DoorPostState(
                            postRoomX, postRoomY,
                            playerMovedRooms,
                            shouldTransitionRoom,
                            hasHelperStakeAfterInteraction);
                    }

                    if (shouldTransitionRoom)
                    {
                        var (adjX, adjY) = hadRoomBefore
                            ? LGConfig.GetAdjacentCoords(previousRoomX, previousRoomY, (byte)door.Direction)
                            : (previousRoomX, previousRoomY);
                        GameplayActionLog.DoorOutcome($"Transitioning to ({adjX},{adjY})");

                        // Player moved rooms -- clear optimistic job state and hide any wielded item
                        if (dungeonManager != null)
                        {
                            dungeonManager.ClearOptimisticJobDirection();

                            // When the move TX succeeded but the RPC still returns
                            // the old position, tell DungeonManager the expected
                            // destination so the transition targets the correct room.
                            if (hadRoomBefore)
                            {
                                dungeonManager.SetOptimisticTargetRoom(adjX, adjY);
                            }
                        }

                        // Tell DungeonManager which door we exited through so the
                        // player spawns at the opposite door in the new room.
                        dungeonManager.SetRoomEntryFromExitDirection(door.Direction);

                        if (_localPlayerController != null)
                        {
                            _localPlayerController.HideAllWieldedItems();
                        }

                        await dungeonManager.TransitionToCurrentPlayerRoomAsync();
                    }
                    else if (doorResult.Success || hasHelperStakeAfterInteraction)
                    {
                        GameplayActionLog.DoorOutcome("Working job -- refreshing room snapshot");

                        // Player is working a rubble-clearing job -- refresh room state for timer.
                        // The optimistic direction stays active to protect against stale reads;
                        // it will auto-clear once real data confirms the job.
                        if (dungeonManager != null)
                        {
                            await dungeonManager.RefreshCurrentRoomSnapshotAsync();
                        }
                    }
                    else
                    {
                        GameplayActionLog.DoorOutcome("TX failed, no stake -- reverting optimistic UI");

                        // Transaction failed and player has no stake -- revert all
                        // optimistic visuals (position, wielded item, timer).
                        if (dungeonManager != null)
                        {
                            dungeonManager.ClearOptimisticJobDirection();
                        }

                        if (_localPlayerController != null)
                        {
                            _localPlayerController.HideAllWieldedItems();
                        }

                        // Push a clean snapshot so the player moves back to idle.
                        if (dungeonManager != null)
                        {
                            await dungeonManager.RefreshCurrentRoomSnapshotAsync();
                        }
                    }

                    _nextInteractTime = Time.unscaledTime + interactCooldownSeconds;
                    return;
                }

                var center = hit.GetComponentInParent<CenterInteractable>();
                if (center != null)
                {
                    // Check if center is a chest/boss before the TX so we know if loot animation applies
                    var roomState = _lgManager.CurrentRoomState;
                    var wasChestCenterBeforeInteraction = roomState != null &&
                                                         roomState.CenterType == LGConfig.CENTER_CHEST;
                    var wasAliveBossCenterBeforeInteraction = roomState != null &&
                                                              roomState.CenterType == LGConfig.CENTER_BOSS &&
                                                              !roomState.BossDefeated;
                    var wasLocalBossFighterBeforeInteraction = dungeonManager != null &&
                                                               dungeonManager.IsLocalPlayerFightingBoss;
                    var isLootableCenter = roomState != null &&
                        (roomState.CenterType == LGConfig.CENTER_CHEST ||
                         (roomState.CenterType == LGConfig.CENTER_BOSS && roomState.BossDefeated));

                    {
                        TryGetCurrentRoomCoordinates(out var ctrRoomX, out var ctrRoomY);
                        var ctrType = roomState == null ? "unknown"
                            : roomState.CenterType == LGConfig.CENTER_CHEST ? "Chest"
                            : roomState.CenterType == LGConfig.CENTER_BOSS ? (roomState.BossDefeated ? "Boss(dead)" : "Boss(alive)")
                            : "Empty";
                        GameplayActionLog.CenterClicked(ctrRoomX, ctrRoomY, ctrType);
                    }

                    // Subscribe to loot result temporarily if this is a lootable center
                    SeekerDungeon.Solana.LootResult capturedLootResult = null;
                    void OnLootResult(SeekerDungeon.Solana.LootResult result) { capturedLootResult = result; }
                    if (isLootableCenter)
                    {
                        _lgManager.OnChestLootResult += OnLootResult;
                    }

                    try
                    {
                        var centerResult = await _lgManager.InteractWithCenter();
                        GameplayActionLog.CenterTxResult(
                            centerResult.Success,
                            centerResult.Success ? centerResult.Signature : centerResult.Error);
                        if (centerResult.Success)
                        {
                            if (wasAliveBossCenterBeforeInteraction && roomController != null)
                            {
                                if (dungeonManager != null)
                                {
                                    dungeonManager.SetOptimisticBossFight();
                                }

                                ResolveLocalPlayerController();
                                if (_localPlayerController != null)
                                {
                                    var equippedId = _lgManager.CurrentPlayerState != null
                                        ? LGDomainMapper.ToItemId(_lgManager.CurrentPlayerState.EquippedItemId)
                                        : ItemId.None;
                                    if (ItemRegistry.IsWearable(equippedId))
                                    {
                                        _localPlayerController.ShowWieldedItem(equippedId);
                                    }

                                    _localPlayerController.SetMiningAnimationState(false);
                                    _localPlayerController.SetBossJobAnimationState(true);
                                }

                                ResolveLocalPlayerJobMover();
                                if (localPlayerJobMover != null &&
                                    roomController.TryGetBossStandPlacement(out var bossStandPosition, out _))
                                {
                                    localPlayerJobMover.MoveTo(bossStandPosition);
                                }
                                else if (localPlayerJobMover != null)
                                {
                                    Debug.LogWarning("[DungeonInput] Boss stand slot layer not configured; using center fallback position.");
                                    localPlayerJobMover.MoveTo(center.InteractWorldPosition);
                                }
                                else
                                {
                                    GameplayActionLog.Info("Center boss success but LocalPlayerJobMover is missing.");
                                }

                                // Ensure boss-fight placement/animation is reflected immediately
                                // instead of waiting for background polling to catch up.
                                if (dungeonManager != null)
                                {
                                    if (wasLocalBossFighterBeforeInteraction)
                                    {
                                        await WaitForBossStatePropagationAsync(roomState);
                                    }

                                    await dungeonManager.RefreshCurrentRoomSnapshotAsync();
                                }
                            }
                            // Do not move the local player for chest/boss-loot center actions.
                            // Movement is only needed when joining/ticking an alive boss fight.
                        }
                        else if (!centerResult.Success && wasAliveBossCenterBeforeInteraction && dungeonManager != null)
                        {
                            dungeonManager.ClearOptimisticBossFight();
                            ResolveLocalPlayerController();
                            if (_localPlayerController != null)
                            {
                                _localPlayerController.SetBossJobAnimationState(false);
                            }
                        }

                        if (!centerResult.Success)
                        {
                            ShowCenterFailureToastIfApplicable(centerResult.Error);
                        }

                        // Play chest open animation on the visual controller
                        var currentRoomAfterInteraction = _lgManager.CurrentRoomState;
                        var isChestCenterAfterInteraction = currentRoomAfterInteraction != null &&
                                                            currentRoomAfterInteraction.CenterType == LGConfig.CENTER_CHEST;
                        var alreadyLootedError = !centerResult.Success &&
                                                 !string.IsNullOrWhiteSpace(centerResult.Error) &&
                                                 centerResult.Error.IndexOf("AlreadyLooted", System.StringComparison.OrdinalIgnoreCase) >= 0;

                        if (roomController == null)
                        {
                            roomController = UnityEngine.Object.FindFirstObjectByType<RoomController>();
                        }

                        if (roomController != null &&
                            ((centerResult.Success && (wasChestCenterBeforeInteraction || isChestCenterAfterInteraction)) ||
                             (alreadyLootedError && (wasChestCenterBeforeInteraction || isChestCenterAfterInteraction))))
                        {
                            roomController.PlayChestOpenAnimation();
                        }

                        // Play loot reveal sequence
                        if (capturedLootResult != null && lootSequenceController != null)
                        {
                            System.Func<SeekerDungeon.Solana.ItemId, Vector3?> slotPosFunc = null;
                            if (gameHudUI != null)
                            {
                                slotPosFunc = (itemId) => gameHudUI.GetSlotScreenPosition(itemId);
                            }

                            lootSequenceController.PlayLootSequence(
                                capturedLootResult,
                                center.InteractWorldPosition,
                                slotPosFunc);
                        }
                    }
                    finally
                    {
                        if (isLootableCenter)
                        {
                            _lgManager.OnChestLootResult -= OnLootResult;
                        }
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

        private bool IsDoorRubbleInCurrentState(RoomDirection direction)
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

            return roomState.Walls[directionIndex] == LGConfig.WALL_RUBBLE;
        }

        private bool IsDoorEntranceStairsInCurrentState(RoomDirection direction)
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

            return roomState.Walls[directionIndex] == LGConfig.WALL_ENTRANCE_STAIRS;
        }

        private bool IsDoorLockedInCurrentState(RoomDirection direction)
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

            return roomState.Walls[directionIndex] == LGConfig.WALL_LOCKED;
        }

        private async UniTask LoadExitSceneAsync()
        {
            if (string.IsNullOrWhiteSpace(exitSceneName))
            {
                return;
            }

            await SceneLoadController.GetOrCreate().LoadSceneAsync(exitSceneName, LoadSceneMode.Single);
        }

        private async UniTask HandleDungeonExitAsync(byte direction)
        {
            var sceneLoadController = SceneLoadController.GetOrCreate();
            if (sceneLoadController != null)
            {
                sceneLoadController.SetTransitionText("Ending run...");
                await sceneLoadController.FadeToBlackAsync();
            }

            var exitResult = await _lgManager.InteractWithDoor(direction);
            GameplayActionLog.DoorTxResult(
                ((RoomDirection)direction).ToString(),
                exitResult.Success,
                exitResult.Success ? exitResult.Signature : exitResult.Error);

            if (!exitResult.Success)
            {
                Debug.LogWarning($"[DungeonInput] Exit dungeon failed: {exitResult.Error}");
                if (sceneLoadController != null)
                {
                    sceneLoadController.ClearTransitionText();
                    await sceneLoadController.FadeFromBlackAsync();
                }

                return;
            }

            GameAudioManager.Instance?.PlayWorld(WorldSfxId.StairsExit, transform.position);
            await _lgManager.RefreshAllState();
            await LoadExitSceneAsync();
        }

        public async UniTask ForceExitOnDeathAsync()
        {
            var sceneLoadController = SceneLoadController.GetOrCreate();
            if (sceneLoadController != null)
            {
                sceneLoadController.SetTransitionText("You died...");
                await sceneLoadController.FadeToBlackAsync();
            }

            var exitResult = await _lgManager.ForceExitOnDeath();
            if (!exitResult.Success)
            {
                Debug.LogWarning($"[DungeonInput] Force-exit on death failed: {exitResult.Error}");
                if (sceneLoadController != null)
                {
                    sceneLoadController.ClearTransitionText();
                    await sceneLoadController.FadeFromBlackAsync();
                }

                return;
            }

            await _lgManager.RefreshAllState();
            await LoadExitSceneAsync();
        }

        /// <summary>
        /// Local fallback path when on-chain boss tick already applied death-outcome
        /// (player removed from run, HP reset) and no explicit ForceExitOnDeath
        /// instruction is needed from the client.
        /// </summary>
        public async UniTask HandleDeathExitAlreadyAppliedAsync(string sourceTag = "unknown")
        {
            if (_isHandlingDeathExitAlreadyApplied)
            {
                return;
            }

            _isHandlingDeathExitAlreadyApplied = true;
            try
            {
                var sceneLoadController = SceneLoadController.GetOrCreate();
                if (sceneLoadController != null)
                {
                    sceneLoadController.SetTransitionText("You died...");
                    await sceneLoadController.FadeToBlackAsync();
                }

                await _lgManager.RefreshAllState();

                // Ensure main menu can still show a death-run panel even when
                // death resolution happened inside TickBossFight on-chain.
                if (!DungeonExtractionSummaryStore.HasPendingSummary)
                {
                    var totalScoreAfterRun = _lgManager.CurrentPlayerState?.TotalScore ?? 0UL;
                    DungeonExtractionSummaryStore.SetPending(new DungeonExtractionSummary
                    {
                        LootScore = 0UL,
                        TimeScore = 0UL,
                        RunScore = 0UL,
                        TotalScoreAfterRun = totalScoreAfterRun,
                        RunEndReason = DungeonRunEndReason.Death
                    });
                }

                Debug.Log($"[DungeonInput] Death exit already applied on-chain ({sourceTag}). Loading menu.");
                await LoadExitSceneAsync();
            }
            finally
            {
                _isHandlingDeathExitAlreadyApplied = false;
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

        private async UniTask WaitForBossStatePropagationAsync(Chaindepth.Accounts.RoomAccount roomBeforeInteraction)
        {
            if (_lgManager == null ||
                roomBeforeInteraction == null ||
                roomBeforeInteraction.CenterType != LGConfig.CENTER_BOSS ||
                roomBeforeInteraction.BossDefeated)
            {
                return;
            }

            const int maxAttempts = 8;
            const int delayMs = 140;
            var hpBefore = roomBeforeInteraction.BossCurrentHp;

            for (var attempt = 0; attempt < maxAttempts; attempt += 1)
            {
                var refreshedRoom = await _lgManager.FetchRoomState(
                    roomBeforeInteraction.X,
                    roomBeforeInteraction.Y,
                    fireEvent: false);
                if (refreshedRoom != null)
                {
                    if (refreshedRoom.CenterType != LGConfig.CENTER_BOSS ||
                        refreshedRoom.BossDefeated ||
                        refreshedRoom.BossCurrentHp < hpBefore)
                    {
                        return;
                    }
                }

                await UniTask.Delay(delayMs);
            }
        }

        private void ShowDoorFailureToastIfApplicable(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return;
            }

            var isMissingKey =
                error.IndexOf("Missing required key", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("MissingRequiredKey", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("InsufficientItemAmount", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isMissingKey)
            {
                return;
            }

            var keyNameRaw = ExtractRegexGroup(error, @"Missing required key:\s*(.+?)\s*\(for");
            var doorNameRaw = ExtractRegexGroup(error, @"\(for\s+(.+?)\)");
            var keyName = HumanizeToken(keyNameRaw);
            var doorName = HumanizeToken(doorNameRaw);

            var toastMessage = !string.IsNullOrWhiteSpace(keyName) && !string.IsNullOrWhiteSpace(doorName)
                ? $"You need {keyName} to open {doorName}"
                : "You need the correct key to open this door";

            if (gameHudUI == null)
            {
                gameHudUI = UnityEngine.Object.FindFirstObjectByType<LGGameHudUI>();
            }

            if (gameHudUI != null)
            {
                gameHudUI.ShowCenterToast(toastMessage, 1.5f);
            }
        }

        private void ShowCenterFailureToastIfApplicable(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return;
            }

            string toastMessage = null;
            if (error.IndexOf("NotBossFighter", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("Only players who joined this fight can loot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("Only players who joined this boss fight can loot", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                toastMessage = "You did not contribute to this boss";
            }
            else if (error.IndexOf("AlreadyLooted", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                toastMessage = "This has already been looted";
            }

            if (string.IsNullOrWhiteSpace(toastMessage))
            {
                return;
            }

            if (gameHudUI == null)
            {
                gameHudUI = UnityEngine.Object.FindFirstObjectByType<LGGameHudUI>();
            }

            if (gameHudUI != null)
            {
                gameHudUI.ShowCenterToast(toastMessage, 1.5f);
            }
        }

        private static string ExtractRegexGroup(string source, string pattern)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            var match = Regex.Match(source, pattern);
            if (!match.Success || match.Groups.Count < 2)
            {
                return string.Empty;
            }

            return match.Groups[1].Value.Trim();
        }

        private static string HumanizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var withSpaces = Regex.Replace(value.Trim(), "([a-z])([A-Z])", "$1 $2");
            return withSpaces.Replace("_", " ");
        }
    }
}
