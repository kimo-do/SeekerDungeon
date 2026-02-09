using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SeekerDungeon;
using SeekerDungeon.Solana;
using Solana.Unity.SDK;
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

        public event Action<DungeonRoomSnapshot> OnRoomSnapshotUpdated;
        public event Action<DoorOccupancyDelta> OnDoorOccupancyDelta;

        private int _currentRoomX = LGConfig.START_X;
        private int _currentRoomY = LGConfig.START_Y;
        private readonly Dictionary<RoomDirection, List<DungeonOccupantVisual>> _doorOccupants = new();
        private readonly List<DungeonOccupantVisual> _bossOccupants = new();
        private readonly List<DungeonOccupantVisual> _idleOccupants = new();
        private bool _releasedGameplayDoorsReadyHold;
        private DungeonOccupantVisual _localRoomOccupant;
        private string _localPlacementSignature = string.Empty;

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

            await _lgManager.RefreshAllState();
            SyncLocalPlayerVisual();
            await ResolveCurrentRoomCoordinatesAsync();
            await RefreshCurrentRoomSnapshotAsync();

            await _lgManager.StartRoomOccupantSubscriptions(_currentRoomX, _currentRoomY);
        }

        public async UniTask RefreshCurrentRoomSnapshotAsync()
        {
            if (_lgManager == null)
            {
                return;
            }

            var room = await _lgManager.FetchRoomState(_currentRoomX, _currentRoomY);
            if (room == null)
            {
                LogError($"Room not found at ({_currentRoomX}, {_currentRoomY}).");
                return;
            }

            var roomView = room.ToRoomView();
            var occupants = await _lgManager.FetchRoomOccupants(_currentRoomX, _currentRoomY);
            ApplySnapshot(roomView, occupants);
        }

        public async UniTask TransitionToCurrentPlayerRoomAsync()
        {
            var sceneLoadController = SceneLoadController.GetOrCreate();
            await sceneLoadController.FadeToBlackAsync();

            try
            {
                _localPlacementSignature = string.Empty;
                _roomController?.PrepareForRoomTransition();
                await ResolveCurrentRoomCoordinatesAsync();
                await RefreshCurrentRoomSnapshotAsync();
                SyncLocalPlayerVisual();
                if (_lgManager != null)
                {
                    await _lgManager.StartRoomOccupantSubscriptions(_currentRoomX, _currentRoomY);
                }
            }
            finally
            {
                await sceneLoadController.FadeFromBlackAsync();
            }
        }

        private async UniTask ResolveCurrentRoomCoordinatesAsync()
        {
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
            if (roomAccount == null)
            {
                return;
            }

            if (roomAccount.X != _currentRoomX || roomAccount.Y != _currentRoomY)
            {
                return;
            }

            var roomView = roomAccount.ToRoomView();
            var snapshot = BuildSnapshot(roomView);
            PushSnapshot(snapshot);
        }

        private void HandleRoomOccupantsUpdated(IReadOnlyList<RoomOccupantView> occupants)
        {
            if (occupants == null)
            {
                return;
            }

            ApplyOccupants(occupants);

            var roomView = _lgManager.GetCurrentRoomView();
            if (roomView != null && roomView.X == _currentRoomX && roomView.Y == _currentRoomY)
            {
                var snapshot = BuildSnapshot(roomView);
                PushSnapshot(snapshot);
            }
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
            var doorSnapshot = new Dictionary<RoomDirection, IReadOnlyList<DungeonOccupantVisual>>
            {
                [RoomDirection.North] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.North]),
                [RoomDirection.South] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.South]),
                [RoomDirection.East] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.East]),
                [RoomDirection.West] = new List<DungeonOccupantVisual>(_doorOccupants[RoomDirection.West])
            };

            return new DungeonRoomSnapshot
            {
                Room = roomView,
                DoorOccupants = doorSnapshot,
                BossOccupants = new List<DungeonOccupantVisual>(_bossOccupants),
                IdleOccupants = new List<DungeonOccupantVisual>(_idleOccupants)
            };
        }

        private void PushSnapshot(DungeonRoomSnapshot snapshot)
        {
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

            var sceneLoadController = SceneLoadController.Instance;
            if (sceneLoadController == null)
            {
                return;
            }

            sceneLoadController.ReleaseBlackScreen("gameplay_doors_ready");
            _releasedGameplayDoorsReadyHold = true;
        }

        private DungeonOccupantVisual ToDungeonOccupantVisual(RoomOccupantView occupant)
        {
            PlayerSkinId skin;
            if (occupant.SkinId < 0 || occupant.SkinId > ushort.MaxValue)
            {
                skin = PlayerSkinId.Goblin;
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
                localPlayerController.ApplySkin((PlayerSkinId)skinId.Value);
            }

            localPlayerController.transform.rotation = Quaternion.identity;
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
            var placementSignature = $"{snapshot.Room.X}:{snapshot.Room.Y}:{activity}:{activityDirection}";
            if (string.Equals(placementSignature, _localPlacementSignature, StringComparison.Ordinal))
            {
                return;
            }

            if (!TryResolveLocalPlacement(activity, activityDirection, out var worldPosition))
            {
                return;
            }

            var currentPosition = localPlayerController.transform.position;
            localPlayerController.transform.position = new Vector3(worldPosition.x, worldPosition.y, currentPosition.z);
            localPlayerController.transform.rotation = Quaternion.identity;
            if (!localPlayerController.gameObject.activeSelf)
            {
                localPlayerController.gameObject.SetActive(true);
            }

            if (cameraZoomController != null)
            {
                cameraZoomController.SnapToWorldPositionInstant(localPlayerController.transform.position);
            }

            _localPlacementSignature = placementSignature;
        }

        private bool TryResolveLocalPlacement(
            OccupantActivity activity,
            RoomDirection? activityDirection,
            out Vector3 worldPosition)
        {
            worldPosition = default;

            if (activity == OccupantActivity.DoorJob && activityDirection.HasValue)
            {
                if (_roomController != null &&
                    _roomController.TryGetDoorStandPosition(activityDirection.Value, out worldPosition))
                {
                    return true;
                }
            }

            if (activity == OccupantActivity.BossFight)
            {
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

        private string ResolveLocalWalletKey()
        {
            var walletKey = Web3.Wallet?.Account?.PublicKey?.Key;
            if (!string.IsNullOrWhiteSpace(walletKey))
            {
                return walletKey;
            }

            return _lgManager?.CurrentPlayerState?.Owner?.Key ?? string.Empty;
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
