using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SeekerDungeon;
using SeekerDungeon.Solana;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonManager : MonoBehaviour
    {
        [Header("References")]
        private LGManager _lgManager;
        private RoomController _roomController;
        [SerializeField] private RoomController roomControllerPrefab;
        [SerializeField] private LGPlayerController localPlayerController;
        [SerializeField] private LGPlayerController localPlayerPrefab;
        [SerializeField] private Transform localPlayerSpawnPoint;
        [SerializeField] private CameraZoomController cameraZoomController;
        [SerializeField] private DungeonJobAutoCompleter jobAutoCompleter;

        public event Action<DungeonRoomSnapshot> OnRoomSnapshotUpdated;
        public event Action<DoorOccupancyDelta> OnDoorOccupancyDelta;

        private int _currentRoomX = LGConfig.START_X;
        private int _currentRoomY = LGConfig.START_Y;
        private readonly Dictionary<RoomDirection, List<DungeonOccupantVisual>> _doorOccupants = new();
        private readonly List<DungeonOccupantVisual> _bossOccupants = new();
        private readonly List<DungeonOccupantVisual> _idleOccupants = new();
        private bool _releasedGameplayDoorsReadyHold;
        private bool _deferHoldRelease;
        private bool _isExplicitRefreshing;
        private DungeonRoomSnapshot _lastDeferredSnapshot;
        private DungeonOccupantVisual _localRoomOccupant;
        private string _localPlacementSignature = string.Empty;
        private bool _hasSnappedCameraForRoom;
        private int _lastTransitionTextIndex = -1;
        private bool _hasAppliedLocalVisualState;
        private PlayerSkinId _lastAppliedLocalSkin = PlayerSkinId.CheekyGoblin;
        private string _lastAppliedLocalDisplayName = string.Empty;
        private bool _isLocalPlayerFightingBoss;

        // Optimistic job state: bridges the gap between a confirmed JoinJob TX
        // and the RPC returning updated data. Prevents stale reads from reverting
        // the player's visual position and wielded item.
        private RoomDirection? _optimisticJobDirection;
        private float _optimisticJobSetTime;
        private const float OptimisticJobTimeoutSeconds = 15f;
        private bool _optimisticBossFight;
        private float _optimisticBossFightSetTime;

        // Optimistic target room: after a confirmed MovePlayer TX the RPC may
        // still return the old position. This override ensures the room
        // transition targets the correct destination instead of the stale one.
        private (int x, int y)? _optimisticTargetRoom;
        private const int RoomFetchRetryAttempts = 8;
        private const int RoomFetchRetryDelayMs = 180;

        // Entry direction: when the local player walks through a door (e.g. West),
        // they should appear at the opposite door (East) in the new room. This is
        // consumed on the first placement after a room transition.
        private RoomDirection? _roomEntryDirection;

        private static readonly string[] TransitionTexts = new[]
        {
            "You venture deeper into the caves...",
            "The air grows cold as you press on...",
            "Strange echoes bounce off the walls...",
            "Your torch flickers in the darkness...",
            "Dust falls from the ceiling above...",
            "The ground trembles beneath your feet...",
            "A distant rumble echoes through the tunnels...",
            "The shadows seem to shift and whisper...",
            "You tighten your grip and push forward...",
            "Something stirs in the darkness ahead..."
        };

        private void Awake()
        {
            if (_lgManager == null)
            {
                _lgManager = LGManager.Instance;
            }

            if (_lgManager == null)
            {
                _lgManager = UnityEngine.Object.FindFirstObjectByType<LGManager>();
            }

            EnsureDoorCollections();
            EnsureRoomController();
            EnsureLocalPlayerController();
            if (cameraZoomController == null)
            {
                cameraZoomController = UnityEngine.Object.FindFirstObjectByType<CameraZoomController>();
            }

            if (jobAutoCompleter == null)
            {
                jobAutoCompleter = UnityEngine.Object.FindFirstObjectByType<DungeonJobAutoCompleter>();
            }
        }

        private void OnEnable()
        {
            if (_lgManager != null)
            {
                _lgManager.OnRoomStateUpdated += HandleRoomStateUpdated;
                _lgManager.OnRoomOccupantsUpdated += HandleRoomOccupantsUpdated;
            }
        }

        private void OnDisable()
        {
            if (_lgManager != null)
            {
                _lgManager.OnRoomStateUpdated -= HandleRoomStateUpdated;
                _lgManager.OnRoomOccupantsUpdated -= HandleRoomOccupantsUpdated;
            }
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        public async UniTask InitializeAsync()
        {
            if (_lgManager == null)
            {
                LogError("LGManager not found in scene.");
                return;
            }

            EnsureRoomController();
            _hasSnappedCameraForRoom = false;
            _deferHoldRelease = true;
            OccupantSpawnPopTracker.Reset();

            try
            {
                await _lgManager.RefreshAllState();
                SyncLocalPlayerVisual();
                await ResolveCurrentRoomCoordinatesAsync();
                await RefreshCurrentRoomSnapshotAsync();

                // Clean up any stale active jobs (in this room or others) before
                // the player can interact. This handles tick + complete + claim
                // for ready rubble jobs and claim-only for already-opened walls.
                var cleaned = await TryCleanupAllActiveJobsAsync();
                if (cleaned)
                {
                    await RefreshCurrentRoomSnapshotAsync();
                }

                await _lgManager.StartRoomOccupantSubscriptions(_currentRoomX, _currentRoomY);
            }
            finally
            {
                FlushDeferredHoldRelease();
            }

            // Start the background auto-completer now that init is done.
            if (jobAutoCompleter != null)
            {
                jobAutoCompleter.StartLoop();
            }
        }

        public async UniTask RefreshCurrentRoomSnapshotAsync()
        {
            await TryRefreshCurrentRoomSnapshotAsync(RoomFetchRetryAttempts, RoomFetchRetryDelayMs);
        }

        private async UniTask<bool> TryRefreshCurrentRoomSnapshotAsync(int maxFetchAttempts, int retryDelayMs)
        {
            if (_lgManager == null)
            {
                return false;
            }

            // Prevent event-driven rebuilds while we do a controlled refresh.
            // FetchRoomState/FetchRoomOccupants fire events that trigger
            // HandleRoomStateUpdated/HandleRoomOccupantsUpdated. We skip
            // those to avoid duplicate snapshot rebuilds.
            _isExplicitRefreshing = true;
            try
            {
                Chaindepth.Accounts.RoomAccount room = null;
                for (var attempt = 0; attempt < Math.Max(1, maxFetchAttempts); attempt += 1)
                {
                    room = await _lgManager.FetchRoomState(_currentRoomX, _currentRoomY);
                    if (room != null)
                    {
                        break;
                    }

                    // Re-resolve room coordinates during eventual-consistency windows
                    // so we don't get stuck querying a stale target.
                    await _lgManager.FetchPlayerState();
                    await ResolveCurrentRoomCoordinatesAsync();
                    if (attempt < maxFetchAttempts - 1)
                    {
                        await UniTask.Delay(Math.Max(1, retryDelayMs));
                    }
                }

                if (room == null)
                {
                    LogError($"Room not found at ({_currentRoomX}, {_currentRoomY}).");
                    GameplayActionLog.Error($"Room PDA not initialized at ({_currentRoomX}, {_currentRoomY})");
                    return false;
                }

                var localWallet = ResolveLocalWalletPublicKey();
                var roomView = room.ToRoomView(localWallet);

                // Check loot receipt PDA to determine if local player already looted
                roomView.HasLocalPlayerLooted = await _lgManager.CheckHasLocalPlayerLooted();

                // Fetch current slot for accurate timer calculation
                var currentSlot = await FetchCurrentSlotAsync();
                if (_roomController != null && currentSlot > 0)
                {
                    _roomController.SetCurrentSlot(currentSlot);
                }

                var occupants = await _lgManager.FetchRoomOccupants(_currentRoomX, _currentRoomY);
                ApplySnapshot(roomView, occupants);
                return true;
            }
            finally
            {
                _isExplicitRefreshing = false;
            }
        }

        /// <summary>
        /// Check all four directions for rubble jobs that are ready to complete
        /// (slot-based progress >= required) and finalize them via TickJob + CompleteJob.
        /// Returns true if any job was completed and the room snapshot should be refreshed.
        /// </summary>
        /// <summary>
        /// Iterate <c>PlayerAccount.ActiveJobs</c> and clean up every stale job
        /// the player has, regardless of which room it is in.
        /// <para>For each active job:</para>
        /// <list type="bullet">
        ///   <item>If the wall is already open / job_completed: just claim.</item>
        ///   <item>If the wall is rubble and progress is ready: tick, complete, claim.</item>
        /// </list>
        /// Returns true if anything was cleaned up.
        /// </summary>
        private async UniTask<bool> TryCleanupAllActiveJobsAsync()
        {
            if (_lgManager == null || Web3.Wallet?.Account == null)
            {
                return false;
            }

            var player = _lgManager.CurrentPlayerState;
            if (player?.ActiveJobs == null || player.ActiveJobs.Length == 0)
            {
                return false;
            }

            var currentSlot = await FetchCurrentSlotAsync();
            var anyCleaned = false;

            // Take a snapshot of the array; entries may be removed on-chain
            // as we iterate, so we work from a copy.
            var jobs = player.ActiveJobs;
            foreach (var job in jobs)
            {
                if (job == null)
                {
                    continue;
                }

                var roomX = (int)job.RoomX;
                var roomY = (int)job.RoomY;
                var dir = job.Direction;
                var dirIndex = (int)dir;
                var directionName = LGConfig.GetDirectionName(dir);
                var label = $"({roomX},{roomY}) {directionName}";

                // Determine whether the job's room uses the current-room or
                // cross-room LGManager helpers.
                var isCurrentRoom = player.CurrentRoomX == roomX &&
                                    player.CurrentRoomY == roomY;

                Log($"Checking active job at {label} (current-room={isCurrentRoom})");

                // Fetch the job's room state (silently, no events).
                Chaindepth.Accounts.RoomAccount room;
                try
                {
                    room = await _lgManager.FetchRoomState(roomX, roomY, fireEvent: false);
                }
                catch (Exception ex)
                {
                    Log($"Failed to fetch room for job at {label}: {ex.Message}");
                    continue;
                }

                if (room == null || room.Walls == null || dirIndex >= room.Walls.Length)
                {
                    continue;
                }

                var wallState = room.Walls[dirIndex];
                var jobCompleted = room.JobCompleted != null &&
                                   room.JobCompleted.Length > dirIndex &&
                                   room.JobCompleted[dirIndex];

                // ── Case 1: Wall open or job_completed flag set → just claim ──
                if (wallState == LGConfig.WALL_OPEN || jobCompleted)
                {
                    Log($"Job at {label} is completed (wall={wallState}, flag={jobCompleted}), claiming...");
                    try
                    {
                        var claimResult = isCurrentRoom
                            ? await _lgManager.ClaimJobReward(dir)
                            : await _lgManager.ClaimJobRewardForRoom(dir, roomX, roomY);

                        if (!claimResult.Success)
                        {
                            Log($"Claim TX failed for {label}");
                        }
                        else
                        {
                            anyCleaned = true;
                            Log($"Claimed reward for {label}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Claim failed for {label}: {ex.Message}");
                    }

                    continue;
                }

                // ── Case 2: Wall is rubble and progress ready → complete + claim ──
                // CompleteJob now auto-ticks on-chain, so no separate TickJob needed.
                if (wallState == LGConfig.WALL_RUBBLE && currentSlot > 0)
                {
                    var helperCount = dirIndex < room.HelperCounts.Length
                        ? room.HelperCounts[dirIndex]
                        : 0U;
                    var startSlot = dirIndex < room.StartSlot.Length
                        ? room.StartSlot[dirIndex]
                        : 0UL;
                    var requiredProgress = dirIndex < room.BaseSlots.Length
                        ? room.BaseSlots[dirIndex]
                        : 0UL;

                    if (helperCount == 0 || startSlot == 0 || requiredProgress == 0)
                    {
                        continue;
                    }

                    var elapsed = currentSlot > startSlot ? currentSlot - startSlot : 0UL;
                    var effective = elapsed * helperCount;
                    if (effective < requiredProgress)
                    {
                        Log($"Job at {label} not ready yet ({effective}/{requiredProgress})");
                        continue;
                    }

                    Log($"Job at {label} ready ({effective}/{requiredProgress}), finalizing...");

                    try
                    {
                        // Step 1: Complete (auto-ticks + frees job slot on-chain)
                        var completeResult = isCurrentRoom
                            ? await _lgManager.CompleteJob(dir)
                            : await _lgManager.CompleteJobForRoom(dir, roomX, roomY);
                        if (!completeResult.Success)
                        {
                            Log($"CompleteJob TX failed for {label}, aborting");
                            continue;
                        }

                        // Step 2: Claim (token payout + HelperStake closure)
                        var claimResult = isCurrentRoom
                            ? await _lgManager.ClaimJobReward(dir)
                            : await _lgManager.ClaimJobRewardForRoom(dir, roomX, roomY);
                        if (!claimResult.Success)
                        {
                            Log($"ClaimJobReward TX failed for {label} (non-fatal)");
                        }

                        anyCleaned = true;
                        Log($"Finalized and claimed job at {label}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Finalize failed for {label}: {ex.Message}");
                    }
                }
            }

            // Single controlled state refresh after all cleanup TXs
            if (anyCleaned)
            {
                await _lgManager.RefreshAllState();
            }

            return anyCleaned;
        }

        private async UniTask<ulong> FetchCurrentSlotAsync()
        {
            var rpc = Web3.Wallet?.ActiveRpcClient;
            if (rpc == null)
            {
                return 0UL;
            }

            try
            {
                var slotResult = await rpc.GetSlotAsync(global::Solana.Unity.Rpc.Types.Commitment.Confirmed);
                if (slotResult.WasSuccessful && slotResult.Result > 0)
                {
                    return slotResult.Result;
                }
            }
            catch (System.Exception ex)
            {
                LogError($"Failed to fetch current slot: {ex.Message}");
            }

            return 0UL;
        }

        public async UniTask TransitionToCurrentPlayerRoomAsync()
        {
            var prevX = _currentRoomX;
            var prevY = _currentRoomY;
            var transitionSucceeded = false;

            var sceneLoadController = SceneLoadController.GetOrCreate();
            await sceneLoadController.FadeToBlackAsync();

            // Show random flavor text while the screen is black
            sceneLoadController.SetTransitionText(PickTransitionText());

            try
            {
                _localPlacementSignature = string.Empty;
                _hasSnappedCameraForRoom = false;
                _roomController?.PrepareForRoomTransition();
                await ResolveCurrentRoomCoordinatesAsync();

                // Patch CurrentPlayerState so that LGManager (and the input
                // controller) immediately see the correct room coordinates.
                // Without this, RPC read-after-write lag keeps the old position
                // and causes every second door-click to target the wrong room.
                if (_lgManager?.CurrentPlayerState != null)
                {
                    _lgManager.CurrentPlayerState.CurrentRoomX = (sbyte)_currentRoomX;
                    _lgManager.CurrentPlayerState.CurrentRoomY = (sbyte)_currentRoomY;
                }

                GameplayActionLog.RoomTransitionStart(prevX, prevY, _currentRoomX, _currentRoomY);

                var refreshed = await TryRefreshCurrentRoomSnapshotAsync(RoomFetchRetryAttempts, RoomFetchRetryDelayMs);
                if (!refreshed)
                {
                    // If the optimistic target read missed, re-sync from player state
                    // before giving up. This avoids getting stuck on stale room coords.
                    await _lgManager.FetchPlayerState();
                    await ResolveCurrentRoomCoordinatesAsync();
                    refreshed = await TryRefreshCurrentRoomSnapshotAsync(RoomFetchRetryAttempts, RoomFetchRetryDelayMs);
                    if (!refreshed)
                    {
                        // Keep visual and interactive state aligned with the rendered room
                        // when target-room reads are still unavailable.
                        _currentRoomX = prevX;
                        _currentRoomY = prevY;
                        if (_lgManager?.CurrentPlayerState != null)
                        {
                            _lgManager.CurrentPlayerState.CurrentRoomX = (sbyte)prevX;
                            _lgManager.CurrentPlayerState.CurrentRoomY = (sbyte)prevY;
                        }

                        await TryRefreshCurrentRoomSnapshotAsync(2, 120);
                        return;
                    }
                }

                // Clean up any stale active jobs before revealing the new room.
                var cleaned = await TryCleanupAllActiveJobsAsync();
                if (cleaned)
                {
                    await RefreshCurrentRoomSnapshotAsync();
                }

                SyncLocalPlayerVisual();
                if (_lgManager != null)
                {
                    await _lgManager.StartRoomOccupantSubscriptions(_currentRoomX, _currentRoomY);
                }

                transitionSucceeded = true;
            }
            finally
            {
                GameplayActionLog.RoomTransitionEnd(
                    _currentRoomX, _currentRoomY, transitionSucceeded);

                sceneLoadController.ClearTransitionText();
                await sceneLoadController.FadeFromBlackAsync();
            }
        }

        private string PickTransitionText()
        {
            if (TransitionTexts.Length == 0)
            {
                return string.Empty;
            }

            if (TransitionTexts.Length == 1)
            {
                return TransitionTexts[0];
            }

            int index;
            do
            {
                index = UnityEngine.Random.Range(0, TransitionTexts.Length);
            } while (index == _lastTransitionTextIndex);

            _lastTransitionTextIndex = index;
            return TransitionTexts[index];
        }

        /// <summary>
        /// True while an optimistic job direction is set (TX confirmed but RPC
        /// has not yet returned the updated state).
        /// </summary>
        public bool HasOptimisticJob => _optimisticJobDirection.HasValue;

        public bool HasOptimisticBossFight =>
            _optimisticBossFight &&
            Time.unscaledTime - _optimisticBossFightSetTime <= OptimisticJobTimeoutSeconds;

        public bool IsLocalPlayerFightingBoss => _isLocalPlayerFightingBoss || HasOptimisticBossFight;

        /// <summary>
        /// Mark a direction as optimistically joined so stale RPC reads do not
        /// revert the player's visual position or wielded item.
        /// </summary>
        public void SetOptimisticJobDirection(RoomDirection direction)
        {
            _optimisticJobDirection = direction;
            _optimisticJobSetTime = Time.unscaledTime;
        }

        /// <summary>
        /// Clear the optimistic override (e.g. TX failed or real data confirmed).
        /// </summary>
        public void ClearOptimisticJobDirection()
        {
            _optimisticJobDirection = null;
        }

        public void SetOptimisticBossFight()
        {
            _optimisticBossFight = true;
            _optimisticBossFightSetTime = Time.unscaledTime;
        }

        public void ClearOptimisticBossFight()
        {
            _optimisticBossFight = false;
        }

        /// <summary>
        /// Set the expected destination room after a confirmed MovePlayer TX
        /// so the transition does not use stale RPC coordinates.
        /// Consumed (cleared) on the next call to ResolveCurrentRoomCoordinatesAsync.
        /// </summary>
        public void SetOptimisticTargetRoom(int x, int y)
        {
            _optimisticTargetRoom = (x, y);
        }

        /// <summary>
        /// Tell DungeonManager which door the player entered the new room from.
        /// The direction should be the exit direction (e.g. West); internally
        /// the opposite is stored so the player spawns at the correct entrance
        /// door (East).
        /// </summary>
        public void SetRoomEntryFromExitDirection(RoomDirection exitDirection)
        {
            _roomEntryDirection = GetOppositeDirection(exitDirection);
        }

        private static RoomDirection GetOppositeDirection(RoomDirection direction)
        {
            return direction switch
            {
                RoomDirection.North => RoomDirection.South,
                RoomDirection.South => RoomDirection.North,
                RoomDirection.East => RoomDirection.West,
                RoomDirection.West => RoomDirection.East,
                _ => direction
            };
        }

        private async UniTask ResolveCurrentRoomCoordinatesAsync()
        {
            // Optimistic target room from a recently confirmed MovePlayer TX.
            // Consume it immediately so it is only used for the first transition
            // after the move; subsequent resolves fall back to real data.
            if (_optimisticTargetRoom.HasValue)
            {
                _currentRoomX = _optimisticTargetRoom.Value.x;
                _currentRoomY = _optimisticTargetRoom.Value.y;
                _optimisticTargetRoom = null;
                return;
            }

            if (_lgManager.CurrentPlayerState != null)
            {
                _currentRoomX = _lgManager.CurrentPlayerState.CurrentRoomX;
                _currentRoomY = _lgManager.CurrentPlayerState.CurrentRoomY;
                return;
            }

            await _lgManager.FetchPlayerState();
            if (_lgManager.CurrentPlayerState != null)
            {
                _currentRoomX = _lgManager.CurrentPlayerState.CurrentRoomX;
                _currentRoomY = _lgManager.CurrentPlayerState.CurrentRoomY;
            }
        }

        private void HandleRoomStateUpdated(Chaindepth.Accounts.RoomAccount roomAccount)
        {
            if (_isExplicitRefreshing)
            {
                return;
            }

            if (roomAccount == null)
            {
                return;
            }

            if (roomAccount.X != _currentRoomX || roomAccount.Y != _currentRoomY)
            {
                return;
            }

            var localWallet = ResolveLocalWalletPublicKey();
            var roomView = roomAccount.ToRoomView(localWallet);

            // Check loot receipt async, then update snapshot
            HandleRoomStateUpdatedAsync(roomView).Forget();
        }

        private async UniTaskVoid HandleRoomStateUpdatedAsync(RoomView roomView)
        {
            if (roomView == null)
            {
                return;
            }

            roomView.HasLocalPlayerLooted = await _lgManager.CheckHasLocalPlayerLooted();
            if (roomView.X != _currentRoomX || roomView.Y != _currentRoomY)
            {
                Log($"Dropped stale room-state snapshot for ({roomView.X},{roomView.Y}); current=({_currentRoomX},{_currentRoomY})");
                return;
            }

            var snapshot = BuildSnapshot(roomView);
            PushSnapshot(snapshot);
        }

        private void HandleRoomOccupantsUpdated(IReadOnlyList<RoomOccupantView> occupants)
        {
            if (_isExplicitRefreshing)
            {
                return;
            }

            if (occupants == null)
            {
                return;
            }

            ApplyOccupants(occupants);

            var roomView = _lgManager.GetCurrentRoomView();
            if (roomView != null && roomView.X == _currentRoomX && roomView.Y == _currentRoomY)
            {
                HandleRoomOccupantsUpdatedAsync(roomView).Forget();
            }
        }

        private async UniTaskVoid HandleRoomOccupantsUpdatedAsync(RoomView roomView)
        {
            if (roomView == null)
            {
                return;
            }

            roomView.HasLocalPlayerLooted = await _lgManager.CheckHasLocalPlayerLooted();
            if (roomView.X != _currentRoomX || roomView.Y != _currentRoomY)
            {
                Log($"Dropped stale occupant-derived snapshot for ({roomView.X},{roomView.Y}); current=({_currentRoomX},{_currentRoomY})");
                return;
            }

            var snapshot = BuildSnapshot(roomView);
            PushSnapshot(snapshot);
        }

        private void ApplySnapshot(RoomView roomView, IReadOnlyList<RoomOccupantView> occupants)
        {
            if (roomView == null)
            {
                return;
            }

            _currentRoomX = roomView.X;
            _currentRoomY = roomView.Y;
            ApplyOccupants(occupants ?? Array.Empty<RoomOccupantView>());

            var snapshot = BuildSnapshot(roomView);
            PushSnapshot(snapshot);
        }

        private void ApplyOccupants(IReadOnlyList<RoomOccupantView> occupants)
        {
            var newDoorOccupants = new Dictionary<RoomDirection, List<DungeonOccupantVisual>>
            {
                [RoomDirection.North] = new List<DungeonOccupantVisual>(),
                [RoomDirection.South] = new List<DungeonOccupantVisual>(),
                [RoomDirection.East] = new List<DungeonOccupantVisual>(),
                [RoomDirection.West] = new List<DungeonOccupantVisual>()
            };
            _bossOccupants.Clear();
            _idleOccupants.Clear();
            _localRoomOccupant = null;
            var localWalletKey = ResolveLocalWalletKey();

            foreach (var occupant in occupants)
            {
                var visual = ToDungeonOccupantVisual(occupant);
                if (!string.IsNullOrWhiteSpace(localWalletKey) &&
                    string.Equals(visual.WalletKey, localWalletKey, StringComparison.Ordinal))
                {
                    _localRoomOccupant = visual;
                    if (visual.IsFightingBoss || visual.Activity == OccupantActivity.BossFight)
                    {
                        ClearOptimisticBossFight();
                    }
                    continue;
                }

                if (occupant.Activity == OccupantActivity.BossFight)
                {
                    _bossOccupants.Add(visual);
                    continue;
                }

                if (occupant.Activity == OccupantActivity.DoorJob && occupant.ActivityDirection != null)
                {
                    var direction = occupant.ActivityDirection.Value;
                    newDoorOccupants[direction].Add(visual);
                    continue;
                }

                _idleOccupants.Add(visual);
            }

            foreach (var direction in newDoorOccupants.Keys)
            {
                EmitDoorDelta(direction, _doorOccupants[direction], newDoorOccupants[direction]);
                _doorOccupants[direction].Clear();
                _doorOccupants[direction].AddRange(newDoorOccupants[direction]);

            }
        }

        private void EmitDoorDelta(RoomDirection direction, IReadOnlyList<DungeonOccupantVisual> previous, IReadOnlyList<DungeonOccupantVisual> current)
        {
            var previousLookup = new Dictionary<string, DungeonOccupantVisual>();
            foreach (var occupant in previous)
            {
                if (string.IsNullOrWhiteSpace(occupant.WalletKey))
                {
                    continue;
                }

                previousLookup[occupant.WalletKey] = occupant;
            }

            var currentLookup = new Dictionary<string, DungeonOccupantVisual>();
            foreach (var occupant in current)
            {
                if (string.IsNullOrWhiteSpace(occupant.WalletKey))
                {
                    continue;
                }

                currentLookup[occupant.WalletKey] = occupant;
            }

            var joined = new List<DungeonOccupantVisual>();
            var left = new List<DungeonOccupantVisual>();

            foreach (var wallet in currentLookup.Keys)
            {
                if (!previousLookup.ContainsKey(wallet))
                {
                    joined.Add(currentLookup[wallet]);
                }
            }

            foreach (var wallet in previousLookup.Keys)
            {
                if (!currentLookup.ContainsKey(wallet))
                {
                    left.Add(previousLookup[wallet]);
                }
            }

            if (joined.Count == 0 && left.Count == 0)
            {
                return;
            }

            OnDoorOccupancyDelta?.Invoke(new DoorOccupancyDelta
            {
                Direction = direction,
                Joined = joined,
                Left = left
            });
        }

        private DungeonRoomSnapshot BuildSnapshot(RoomView roomView)
        {
            // Inject optimistic door data so the timer shows while the RPC
            // still returns stale helper counts / start slots.
            roomView = ApplyOptimisticDoorOverride(roomView);

            var doorSnapshot = new Dictionary<RoomDirection, IReadOnlyList<DungeonOccupantVisual>>
            {
                [RoomDirection.North] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.North]),
                [RoomDirection.South] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.South]),
                [RoomDirection.East] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.East]),
                [RoomDirection.West] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.West])
            };

            var activeJobDirections = ResolveLocalPlayerActiveJobDirections(roomView.X, roomView.Y);
            var fightingBoss = _localRoomOccupant?.IsFightingBoss ?? false;
            if (roomView.TryGetMonster(out var monster) && monster != null && monster.IsDead)
            {
                fightingBoss = false;
                ClearOptimisticBossFight();
            }
            if (!fightingBoss && HasOptimisticBossFight)
            {
                fightingBoss = true;
            }

            return new DungeonRoomSnapshot
            {
                Room = roomView,
                DoorOccupants = doorSnapshot,
                BossOccupants = new List<DungeonOccupantVisual>(_bossOccupants),
                IdleOccupants = new List<DungeonOccupantVisual>(_idleOccupants),
                LocalPlayerActiveJobDirections = activeJobDirections,
                LocalPlayerFightingBoss = fightingBoss
            };
        }

        /// <summary>
        /// If an optimistic job direction is set and the real door data for that
        /// direction has no helpers yet (stale), build an estimated DoorJobView
        /// so the rubble timer starts immediately after joining.
        /// </summary>
        private RoomView ApplyOptimisticDoorOverride(RoomView roomView)
        {
            if (!_optimisticJobDirection.HasValue || roomView?.Doors == null)
            {
                return roomView;
            }

            var dir = _optimisticJobDirection.Value;
            if (!roomView.Doors.TryGetValue(dir, out var existing))
            {
                return roomView;
            }

            // If real data already has helpers and a start slot, no override needed.
            if (existing.HelperCount > 0 && existing.StartSlot > 0)
            {
                return roomView;
            }

            // Estimate the start slot from the last known slot on the room controller.
            var estimatedSlot = _roomController != null ? _roomController.LastKnownSlot : 0UL;
            if (estimatedSlot == 0)
            {
                return roomView;
            }

            var optimisticDoor = new DoorJobView
            {
                Direction = dir,
                WallState = existing.WallState,
                HelperCount = Math.Max(1U, existing.HelperCount + 1),
                Progress = existing.Progress,
                RequiredProgress = existing.RequiredProgress,
                StartSlot = existing.StartSlot > 0 ? existing.StartSlot : estimatedSlot,
                IsCompleted = false
            };

            var newDoors = new Dictionary<RoomDirection, DoorJobView>(roomView.Doors);
            newDoors[dir] = optimisticDoor;

            var overridden = new RoomView
            {
                X = roomView.X,
                Y = roomView.Y,
                CenterType = roomView.CenterType,
                LootedCount = roomView.LootedCount,
                HasLocalPlayerLooted = roomView.HasLocalPlayerLooted,
                CreatedBy = roomView.CreatedBy,
                Doors = newDoors
            };

            // Carry over the monster if present.
            if (roomView.TryGetMonster(out var monster))
            {
                overridden.SetMonster(monster);
            }

            return overridden;
        }

        private HashSet<RoomDirection> ResolveLocalPlayerActiveJobDirections(int roomX, int roomY)
        {
            var result = new HashSet<RoomDirection>();
            var playerState = _lgManager?.CurrentPlayerState;
            if (playerState?.ActiveJobs != null)
            {
                foreach (var job in playerState.ActiveJobs)
                {
                    if (job == null || job.RoomX != roomX || job.RoomY != roomY)
                    {
                        continue;
                    }

                    if (LGDomainMapper.TryToDirection(job.Direction, out var direction))
                    {
                        result.Add(direction);
                    }
                }
            }

            // If real data now confirms the optimistic job, clear the override.
            if (_optimisticJobDirection.HasValue && result.Contains(_optimisticJobDirection.Value))
            {
                _optimisticJobDirection = null;
            }

            // Safety timeout so the override cannot get stuck forever.
            if (_optimisticJobDirection.HasValue &&
                Time.unscaledTime - _optimisticJobSetTime > OptimisticJobTimeoutSeconds)
            {
                _optimisticJobDirection = null;
            }

            // Include the optimistic direction if still pending confirmation.
            if (_optimisticJobDirection.HasValue)
            {
                result.Add(_optimisticJobDirection.Value);
            }

            return result;
        }

        private void PushSnapshot(DungeonRoomSnapshot snapshot)
        {
            if (snapshot?.Room == null)
            {
                return;
            }

            if (snapshot.Room.X != _currentRoomX || snapshot.Room.Y != _currentRoomY)
            {
                Log($"Ignored stale push snapshot room=({snapshot.Room.X},{snapshot.Room.Y}) current=({_currentRoomX},{_currentRoomY})");
                return;
            }

            _isLocalPlayerFightingBoss = snapshot?.LocalPlayerFightingBoss ?? false;
            UpdateLocalPlayerPlacement(snapshot);
            TryReleaseGameplayDoorsReadyHold(snapshot);
            _roomController?.ApplySnapshot(snapshot);
            OnRoomSnapshotUpdated?.Invoke(snapshot);

            Log($"Snapshot updated room=({snapshot.Room.X},{snapshot.Room.Y}) N={snapshot.DoorOccupants[RoomDirection.North].Count} S={snapshot.DoorOccupants[RoomDirection.South].Count} E={snapshot.DoorOccupants[RoomDirection.East].Count} W={snapshot.DoorOccupants[RoomDirection.West].Count} B={snapshot.BossOccupants.Count} I={snapshot.IdleOccupants.Count}");
        }

        private void TryReleaseGameplayDoorsReadyHold(DungeonRoomSnapshot snapshot)
        {
            if (_releasedGameplayDoorsReadyHold)
            {
                return;
            }

            if (snapshot?.Room?.Doors == null || snapshot.Room.Doors.Count == 0)
            {
                return;
            }

            if (_deferHoldRelease)
            {
                _lastDeferredSnapshot = snapshot;
                return;
            }

            var sceneLoadController = SceneLoadController.Instance;
            if (sceneLoadController == null)
            {
                return;
            }

            sceneLoadController.ReleaseBlackScreen("gameplay_doors_ready");
            _releasedGameplayDoorsReadyHold = true;
        }

        private void FlushDeferredHoldRelease()
        {
            _deferHoldRelease = false;
            if (_lastDeferredSnapshot != null)
            {
                TryReleaseGameplayDoorsReadyHold(_lastDeferredSnapshot);
                _lastDeferredSnapshot = null;
            }
        }

        private DungeonOccupantVisual ToDungeonOccupantVisual(RoomOccupantView occupant)
        {
            PlayerSkinId skin;
            if (occupant.SkinId < 0 || occupant.SkinId > ushort.MaxValue)
            {
                skin = PlayerSkinId.CheekyGoblin;
            }
            else
            {
                // Avoid Enum.IsDefined boxing mismatch (int vs ushort).
                skin = (PlayerSkinId)(ushort)occupant.SkinId;
            }

            var walletKey = occupant.Wallet?.Key ?? string.Empty;
            return new DungeonOccupantVisual
            {
                WalletKey = walletKey,
                DisplayName = ShortWallet(walletKey),
                SkinId = skin,
                EquippedItemId = occupant.EquippedItemId,
                Activity = occupant.Activity,
                ActivityDirection = occupant.ActivityDirection,
                IsFightingBoss = occupant.IsFightingBoss
            };
        }

        private static string ShortWallet(string wallet)
        {
            if (string.IsNullOrWhiteSpace(wallet) || wallet.Length < 10)
            {
                return "Unknown";
            }

            return $"{wallet.Substring(0, 4)}...{wallet.Substring(wallet.Length - 4)}";
        }

        private void EnsureDoorCollections()
        {
            if (_doorOccupants.Count > 0)
            {
                return;
            }

            _doorOccupants[RoomDirection.North] = new List<DungeonOccupantVisual>();
            _doorOccupants[RoomDirection.South] = new List<DungeonOccupantVisual>();
            _doorOccupants[RoomDirection.East] = new List<DungeonOccupantVisual>();
            _doorOccupants[RoomDirection.West] = new List<DungeonOccupantVisual>();

        }

        private void EnsureRoomController()
        {
            if (_roomController != null)
            {
                return;
            }

            _roomController = UnityEngine.Object.FindFirstObjectByType<RoomController>();
            if (_roomController != null)
            {
                return;
            }

            if (roomControllerPrefab == null)
            {
                Log("RoomController prefab not assigned yet. Dungeon visual spawning is scaffold-only for now.");
                return;
            }

            _roomController = Instantiate(roomControllerPrefab, transform);
            _roomController.name = roomControllerPrefab.name;
        }

        private void EnsureLocalPlayerController()
        {
            if (localPlayerController != null)
            {
                if (!localPlayerController.gameObject.activeSelf)
                {
                    localPlayerController.gameObject.SetActive(true);
                }
                return;
            }

            var playerControllers = UnityEngine.Object.FindObjectsByType<LGPlayerController>(
                FindObjectsInactive.Include,
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

                localPlayerController = playerController;
                _hasAppliedLocalVisualState = false;
                if (!localPlayerController.gameObject.activeSelf)
                {
                    localPlayerController.gameObject.SetActive(true);
                }

                return;
            }

            if (localPlayerPrefab == null)
            {
                Log("No local player found in GameScene. Assign localPlayerPrefab on DungeonManager to auto-spawn one.");
                return;
            }

            var spawnPosition = localPlayerSpawnPoint != null ? localPlayerSpawnPoint.position : Vector3.zero;
            var spawnedPlayer = Instantiate(localPlayerPrefab, spawnPosition, Quaternion.identity);
            localPlayerController = spawnedPlayer;
            _hasAppliedLocalVisualState = false;
        }

        private void SyncLocalPlayerVisual()
        {
            EnsureLocalPlayerController();
            if (localPlayerController == null)
            {
                return;
            }

            var skinId = _lgManager?.CurrentProfileState?.SkinId;
            if (skinId.HasValue)
            {
                var desiredSkin = (PlayerSkinId)skinId.Value;
                if (!_hasAppliedLocalVisualState || _lastAppliedLocalSkin != desiredSkin)
                {
                    localPlayerController.ApplySkin(desiredSkin);
                    _lastAppliedLocalSkin = desiredSkin;
                }
            }

            var desiredDisplayName = ResolveLocalDisplayName();
            if (!_hasAppliedLocalVisualState ||
                !string.Equals(_lastAppliedLocalDisplayName, desiredDisplayName, StringComparison.Ordinal))
            {
                localPlayerController.SetDisplayName(desiredDisplayName);
                _lastAppliedLocalDisplayName = desiredDisplayName;
            }

            localPlayerController.SetDisplayNameVisible(true);
            localPlayerController.transform.rotation = Quaternion.identity;
            _hasAppliedLocalVisualState = true;
        }

        /// <summary>
        /// Flips the local player's x-scale to match the given facing direction.
        /// Uses the same convention as <see cref="DoorOccupantVisual2D"/>: positive
        /// x = Right, negative x = Left.
        /// </summary>
        private static void ApplyLocalPlayerFacing(
            LGPlayerController controller,
            OccupantFacingDirection facing)
        {
            if (controller == null)
            {
                return;
            }

            var localScale = controller.transform.localScale;
            var absX = Mathf.Abs(localScale.x);
            localScale.x = facing == OccupantFacingDirection.Right ? absX : -absX;
            controller.transform.localScale = localScale;
        }

        private void UpdateLocalPlayerPlacement(DungeonRoomSnapshot snapshot)
        {
            if (snapshot?.Room == null)
            {
                return;
            }

            EnsureLocalPlayerController();
            if (localPlayerController == null)
            {
                return;
            }

            SyncLocalPlayerVisual();

            var activity = _localRoomOccupant?.Activity ?? OccupantActivity.Idle;
            var activityDirection = _localRoomOccupant?.ActivityDirection;
            if (_localRoomOccupant == null &&
                TryResolveLocalActivityFromPlayerState(snapshot.Room.X, snapshot.Room.Y, out var fallbackActivity, out var fallbackDirection))
            {
                activity = fallbackActivity;
                activityDirection = fallbackDirection;
            }

            // Fast-path UX fix: room wall state can update before occupant/player
            // activity snapshots. If the door is no longer rubble, stop job visuals
            // immediately so weapon swings do not continue after completion.
            if (activity == OccupantActivity.DoorJob && activityDirection.HasValue)
            {
                if (snapshot.Room.Doors == null ||
                    !snapshot.Room.Doors.TryGetValue(activityDirection.Value, out var activeDoor) ||
                    activeDoor == null ||
                    !activeDoor.IsRubble)
                {
                    activity = OccupantActivity.Idle;
                    activityDirection = null;
                    ClearOptimisticJobDirection();
                }
            }

            if (snapshot.Room.TryGetMonster(out var monster) && monster != null && monster.IsDead)
            {
                activity = OccupantActivity.Idle;
                activityDirection = null;
                ClearOptimisticBossFight();
            }

            // Optimistic override: keep player at the door while waiting for
            // the RPC to return updated state after a confirmed JoinJob TX.
            if (activity == OccupantActivity.Idle && _optimisticJobDirection.HasValue)
            {
                activity = OccupantActivity.DoorJob;
                activityDirection = _optimisticJobDirection.Value;
            }
            else if (activity == OccupantActivity.Idle && HasOptimisticBossFight)
            {
                activity = OccupantActivity.BossFight;
            }

            ApplyLocalPlayerWieldedState(activity);

            var placementSignature = $"{snapshot.Room.X}:{snapshot.Room.Y}:{activity}:{activityDirection}";

            // Entry-direction override: when the player just entered this room
            // through a door, place them at the dedicated arrival anchor and
            // face the correct direction. Consumed on first use.
            if (activity == OccupantActivity.Idle && _roomEntryDirection.HasValue)
            {
                var entryDir = _roomEntryDirection.Value;
                _roomEntryDirection = null;

                if (_roomController != null &&
                    _roomController.TryGetDoorArrivalPosition(entryDir, out var entryPosition, out var arrivalFacing))
                {
                    var currentPos = localPlayerController.transform.position;
                    localPlayerController.transform.position = new Vector3(entryPosition.x, entryPosition.y, currentPos.z);
                    localPlayerController.transform.rotation = Quaternion.identity;
                    ApplyLocalPlayerFacing(localPlayerController, arrivalFacing);

                    if (!localPlayerController.gameObject.activeSelf)
                    {
                        localPlayerController.gameObject.SetActive(true);
                    }

                    if (!_hasSnappedCameraForRoom && cameraZoomController != null)
                    {
                        cameraZoomController.SnapToWorldPositionInstant(localPlayerController.transform.position);
                        _hasSnappedCameraForRoom = true;
                    }

                    _localPlacementSignature = placementSignature;
                    return;
                }
            }
            if (string.Equals(placementSignature, _localPlacementSignature, StringComparison.Ordinal))
            {
                return;
            }

            // When the player transitions from a DoorJob to Idle in the same
            // room it means a job just completed. Keep them where they are
            // instead of teleporting to a random idle position.
            if (activity == OccupantActivity.Idle &&
                !string.IsNullOrEmpty(_localPlacementSignature) &&
                _localPlacementSignature.Contains(":DoorJob:"))
            {
                var previousRoomPrefix = $"{snapshot.Room.X}:{snapshot.Room.Y}:";
                if (_localPlacementSignature.StartsWith(previousRoomPrefix, StringComparison.Ordinal))
                {
                    _localPlacementSignature = placementSignature;
                    return;
                }
            }

            if (!TryResolveLocalPlacement(activity, activityDirection, out var worldPosition, out var facingOverride))
            {
                return;
            }

            var currentPosition = localPlayerController.transform.position;
            localPlayerController.transform.position = new Vector3(worldPosition.x, worldPosition.y, currentPosition.z);
            localPlayerController.transform.rotation = Quaternion.identity;
            if (facingOverride.HasValue)
            {
                ApplyLocalPlayerFacing(localPlayerController, facingOverride.Value);
            }
            if (!localPlayerController.gameObject.activeSelf)
            {
                localPlayerController.gameObject.SetActive(true);
            }

            // Only snap camera on the first placement after a room transition
            if (!_hasSnappedCameraForRoom && cameraZoomController != null)
            {
                cameraZoomController.SnapToWorldPositionInstant(localPlayerController.transform.position);
                _hasSnappedCameraForRoom = true;
            }

            _localPlacementSignature = placementSignature;
        }

        private bool TryResolveLocalPlacement(
            OccupantActivity activity,
            RoomDirection? activityDirection,
            out Vector3 worldPosition,
            out OccupantFacingDirection? facingOverride)
        {
            worldPosition = default;
            facingOverride = null;

            if (activity == OccupantActivity.DoorJob && activityDirection.HasValue)
            {
                if (_roomController != null &&
                    _roomController.TryGetDoorStandPlacement(
                        activityDirection.Value,
                        out worldPosition,
                        out var standFacing))
                {
                    facingOverride = standFacing;
                    return true;
                }
            }

            if (activity == OccupantActivity.BossFight)
            {
                if (_roomController != null &&
                    _roomController.TryGetBossStandPlacement(out worldPosition, out var bossFacing))
                {
                    facingOverride = bossFacing;
                    return true;
                }

                if (_roomController != null && _roomController.TryGetCenterStandPosition(out worldPosition))
                {
                    return true;
                }
            }

            if (_roomController != null && _roomController.TryGetIdleStandPosition(out worldPosition))
            {
                return true;
            }

            if (localPlayerSpawnPoint != null)
            {
                worldPosition = localPlayerSpawnPoint.position;
                return true;
            }

            worldPosition = localPlayerController != null ? localPlayerController.transform.position : Vector3.zero;
            return localPlayerController != null;
        }

        private bool TryResolveLocalActivityFromPlayerState(
            int roomX,
            int roomY,
            out OccupantActivity activity,
            out RoomDirection? activityDirection)
        {
            activity = OccupantActivity.Idle;
            activityDirection = null;

            var playerState = _lgManager?.CurrentPlayerState;
            if (playerState?.ActiveJobs == null || playerState.ActiveJobs.Length == 0)
            {
                return false;
            }

            for (var index = playerState.ActiveJobs.Length - 1; index >= 0; index -= 1)
            {
                var activeJob = playerState.ActiveJobs[index];
                if (activeJob == null || activeJob.RoomX != roomX || activeJob.RoomY != roomY)
                {
                    continue;
                }

                if (!LGDomainMapper.TryToDirection(activeJob.Direction, out var mappedDirection))
                {
                    continue;
                }

                activity = OccupantActivity.DoorJob;
                activityDirection = mappedDirection;
                return true;
            }

            return false;
        }

        private void ApplyLocalPlayerWieldedState(OccupantActivity activity)
        {
            if (localPlayerController == null)
            {
                return;
            }

            if (_lgManager?.CurrentPlayerState == null)
            {
                localPlayerController.SetCombatHealth(0, 0, false);
                return;
            }

            var playerState = _lgManager.CurrentPlayerState;
            localPlayerController.SetCombatHealth(
                playerState.CurrentHp,
                playerState.MaxHp,
                activity == OccupantActivity.BossFight);
            localPlayerController.SetMiningAnimationState(activity == OccupantActivity.DoorJob);
            localPlayerController.SetBossJobAnimationState(activity == OccupantActivity.BossFight);

            if (activity == OccupantActivity.DoorJob || activity == OccupantActivity.BossFight)
            {
                var equippedId = LGDomainMapper.ToItemId(playerState.EquippedItemId);
                if (ItemRegistry.IsWearable(equippedId))
                {
                    localPlayerController.ShowWieldedItem(equippedId);
                    return;
                }
            }

            localPlayerController.HideAllWieldedItems();
        }

        private string ResolveLocalWalletKey()
        {
            var walletKey = Web3.Wallet?.Account?.PublicKey?.Key;
            if (!string.IsNullOrWhiteSpace(walletKey))
            {
                return walletKey;
            }

            return _lgManager?.CurrentPlayerState?.Owner?.Key ?? string.Empty;
        }

        private PublicKey ResolveLocalWalletPublicKey()
        {
            var pubKey = Web3.Wallet?.Account?.PublicKey;
            if (pubKey != null)
            {
                return pubKey;
            }

            return _lgManager?.CurrentPlayerState?.Owner;
        }

        private string ResolveLocalDisplayName()
        {
            var profileName = _lgManager?.CurrentProfileState?.DisplayName;
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                return profileName.Trim();
            }

            return ShortWallet(ResolveLocalWalletKey());
        }

        private void Log(string message)
        {
            Debug.Log($"[DungeonManager] {message}");
        }

        private static void LogError(string message)
        {
            Debug.LogError($"[DungeonManager] {message}");
        }
    }
}
