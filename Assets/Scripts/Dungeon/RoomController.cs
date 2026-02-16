using System;
using System.Collections.Generic;
using SeekerDungeon.Solana;
using TMPro;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [Serializable]
    public sealed class DoorLayerBinding
    {
        [SerializeField] private RoomDirection direction;
        [SerializeField] private DoorOccupantLayer2D occupantLayer;
        [SerializeField] private DoorVisualController visualController;

        public RoomDirection Direction => direction;
        public DoorOccupantLayer2D OccupantLayer => occupantLayer;
        public DoorVisualController VisualController => visualController;
    }

    [Serializable]
    public sealed class RoomBackgroundPool
    {
        [SerializeField] private string id = "default";
        [SerializeField] private List<GameObject> backgrounds = new();

        public string Id => id;
        public List<GameObject> Backgrounds => backgrounds;
    }

    public sealed class RoomController : MonoBehaviour
    {
        [SerializeField] private List<DoorLayerBinding> doorLayers = new();
        [Header("Door Job Timers")]
        [SerializeField] private GameObject timerCanvasPrefab;
        [SerializeField] private Vector3 timerWorldOffset = new(0f, 1.2f, 0f);
        [SerializeField] private float slotSecondsEstimate = 0.4f;
        [Header("Backgrounds")]
        [SerializeField] private List<GameObject> roomBackgrounds = new();
        [SerializeField] private List<GameObject> starterRoomBackgrounds = new();
        [SerializeField] private List<RoomBackgroundPool> additionalBackgroundPools = new();
        [Header("Occupant Visuals")]
        [SerializeField] private Transform occupantVisualSpawnRoot;
        [SerializeField] private RoomIdleOccupantLayer2D idleOccupantLayer;
        [SerializeField] private DoorOccupantLayer2D bossOccupantLayer;
        [Header("Center Visuals")]
        [SerializeField] private GameObject centerEmptyVisualRoot;
        [SerializeField] private GameObject centerChestVisualRoot;
        [SerializeField] private GameObject centerBossVisualRoot;
        [SerializeField] private GameObject centerFallbackVisualRoot;
        [SerializeField] private DungeonChestVisualController chestVisualController;
        [SerializeField] private DungeonBossVisualController bossVisualController;
        [SerializeField] private CenterInteractable centerInteractable;
        [Header("Ambient")]
        [SerializeField] private RoomAmbientRatSpawner ambientRatSpawner;

        private readonly Dictionary<RoomDirection, DoorOccupantLayer2D> _doorLayerByDirection = new();
        private readonly Dictionary<RoomDirection, DoorVisualController> _doorVisualByDirection = new();
        private readonly Dictionary<RoomDirection, VisualInteractable> _doorInteractableByDirection = new();
        private readonly Dictionary<RoomDirection, DoorTimerView> _doorTimers = new();
        private VisualInteractable _centerVisualInteractable;
        private Transform _timerRoot;
        private bool _hasBackgroundRoomKey;
        private int _backgroundRoomX;
        private int _backgroundRoomY;
        private bool _hasAppliedFirstSnapshot;
        private ulong _lastKnownSlot;

        private void Awake()
        {
            EnsureOccupantVisualSpawnRoot();
            ResolveBossOccupantLayer();
            BuildDoorLayerIndex();

            if (centerInteractable == null)
            {
                centerInteractable = UnityEngine.Object.FindFirstObjectByType<CenterInteractable>();
            }

            if (ambientRatSpawner == null)
            {
                ambientRatSpawner = GetComponentInChildren<RoomAmbientRatSpawner>(true);
            }
        }

        public void ApplySnapshot(DungeonRoomSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Room == null)
            {
                return;
            }

            ApplyBackgroundForRoom(snapshot.Room);
            if (ambientRatSpawner != null)
            {
                ambientRatSpawner.ApplyRoom(snapshot.Room);
            }

            if (snapshot.Room.Doors != null)
            {
                foreach (var door in snapshot.Room.Doors)
                {
                    ApplyDoorState(door.Key, door.Value);
                }
            }

            if (snapshot.DoorOccupants != null)
            {
                foreach (var doorOccupants in snapshot.DoorOccupants)
                {
                    SetDoorOccupants(doorOccupants.Key, doorOccupants.Value);
                }
            }

            SetIdleOccupants(snapshot.IdleOccupants);

            ApplyCenterState(snapshot.Room, snapshot.BossOccupants);
            SetBossOccupants(snapshot.BossOccupants);
            UpdateInteractableStates(snapshot);

            // After the first snapshot for a room, suppress pop-in on subsequent updates
            if (!_hasAppliedFirstSnapshot)
            {
                _hasAppliedFirstSnapshot = true;
                SetAllLayersSuppressSpawnPop(true);

                // Re-resolve interactable renderers now that door visuals are in the correct state
                RefreshAllInteractableRenderers();
            }
        }

        public void ApplyDoorState(RoomDirection direction, DoorJobView door)
        {
            UpdateDoorTimer(direction, door);

            if (_doorVisualByDirection.TryGetValue(direction, out var visualController) && visualController != null)
            {
                visualController.ApplyDoorState(door);
                return;
            }

            Debug.Log($"[RoomController] Door {direction}: state={door.WallState} helpers={door.HelperCount} progress={door.Progress}/{door.RequiredProgress} complete={door.IsCompleted}");
        }

        public void SetDoorOccupants(RoomDirection direction, IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            if (_doorLayerByDirection.TryGetValue(direction, out var layer) && layer != null)
            {
                layer.SetOccupants(occupants ?? Array.Empty<DungeonOccupantVisual>());
                return;
            }

            var count = occupants?.Count ?? 0;
            Debug.Log($"[RoomController] No door layer assigned for {direction}. Occupants={count}");
        }

        public bool TryGetDoorStandPosition(RoomDirection direction, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!_doorLayerByDirection.TryGetValue(direction, out var layer) || layer == null)
            {
                return false;
            }

            return layer.TryGetLocalPlayerStandPosition(out worldPosition);
        }

        public bool TryGetDoorStandPlacement(
            RoomDirection direction,
            out Vector3 worldPosition,
            out OccupantFacingDirection facingDirection)
        {
            worldPosition = default;
            facingDirection = OccupantFacingDirection.Right;
            if (!_doorLayerByDirection.TryGetValue(direction, out var layer) || layer == null)
            {
                return false;
            }

            return layer.TryGetLocalPlayerStandPlacement(out worldPosition, out facingDirection);
        }

        /// <summary>
        /// Returns the dedicated arrival position for a door and the facing
        /// direction the player should have when entering through it.
        /// Falls back to <see cref="TryGetDoorStandPosition"/> when no
        /// arrival anchor is configured.
        /// </summary>
        public bool TryGetDoorArrivalPosition(
            RoomDirection direction,
            out Vector3 worldPosition,
            out OccupantFacingDirection facing)
        {
            worldPosition = default;
            facing = GetArrivalFacing(direction);

            if (!_doorLayerByDirection.TryGetValue(direction, out var layer) || layer == null)
            {
                return false;
            }

            if (layer.TryGetArrivalPosition(out worldPosition))
            {
                return true;
            }

            // Fallback: use the first visual slot position.
            return layer.TryGetLocalPlayerStandPosition(out worldPosition);
        }

        /// <summary>
        /// Determines which way the player should face when arriving through
        /// a given door. Coming from the West door means the player walked
        /// rightward, so face Right, and vice versa.
        /// </summary>
        private static OccupantFacingDirection GetArrivalFacing(RoomDirection entryDoor)
        {
            return entryDoor switch
            {
                RoomDirection.West => OccupantFacingDirection.Right,   // entered from left side, face right
                RoomDirection.East => OccupantFacingDirection.Left,    // entered from right side, face left
                RoomDirection.South => OccupantFacingDirection.Right,  // entered from bottom, default face right
                RoomDirection.North => OccupantFacingDirection.Right,  // entered from top, default face right
                _ => OccupantFacingDirection.Right
            };
        }

        public bool TryGetIdleStandPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (idleOccupantLayer == null)
            {
                return false;
            }

            return idleOccupantLayer.TryGetLocalPlayerSpawnPosition(out worldPosition);
        }

        public bool TryGetCenterStandPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (bossVisualController != null &&
                bossVisualController.TryGetBossStandPosition(out worldPosition))
            {
                return true;
            }

            ResolveBossOccupantLayer();
            if (bossOccupantLayer != null && bossOccupantLayer.TryGetLocalPlayerStandPosition(out worldPosition))
            {
                return true;
            }

            if (centerInteractable == null)
            {
                centerInteractable = UnityEngine.Object.FindFirstObjectByType<CenterInteractable>();
            }

            if (centerInteractable == null)
            {
                return false;
            }

            worldPosition = centerInteractable.InteractWorldPosition;
            return true;
        }

        public bool TryGetBossStandPlacement(out Vector3 worldPosition, out OccupantFacingDirection facingDirection)
        {
            worldPosition = default;
            facingDirection = OccupantFacingDirection.Right;
            if (bossVisualController != null &&
                bossVisualController.TryGetBossStandPlacement(out worldPosition, out facingDirection))
            {
                return true;
            }

            ResolveBossOccupantLayer();
            if (bossOccupantLayer == null)
            {
                return false;
            }

            return bossOccupantLayer.TryGetLocalPlayerStandPlacement(out worldPosition, out facingDirection);
        }

        public void SetBossOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            var count = occupants?.Count ?? 0;
            if (bossVisualController != null)
            {
                bossVisualController.SetBossOccupants(occupants ?? Array.Empty<DungeonOccupantVisual>());
            }

            ResolveBossOccupantLayer();
            if (bossOccupantLayer != null)
            {
                bossOccupantLayer.SetOccupants(occupants ?? Array.Empty<DungeonOccupantVisual>());
                return;
            }

            Debug.Log($"[RoomController] Boss occupants={count}");
        }

        public void PrepareForRoomTransition()
        {
            SetDoorOccupants(RoomDirection.North, Array.Empty<DungeonOccupantVisual>());
            SetDoorOccupants(RoomDirection.South, Array.Empty<DungeonOccupantVisual>());
            SetDoorOccupants(RoomDirection.East, Array.Empty<DungeonOccupantVisual>());
            SetDoorOccupants(RoomDirection.West, Array.Empty<DungeonOccupantVisual>());
            SetIdleOccupants(Array.Empty<DungeonOccupantVisual>());
            SetBossOccupants(Array.Empty<DungeonOccupantVisual>());
            SetAllDoorTimersVisible(false);
            SetAllInteractablesOff();

            // Allow pop-in animation on the next room's first snapshot
            _hasAppliedFirstSnapshot = false;
            SetAllLayersSuppressSpawnPop(false);
        }

        private void SetAllInteractablesOff()
        {
            foreach (var vi in _doorInteractableByDirection.Values)
            {
                if (vi != null)
                {
                    vi.Interactable = false;
                }
            }

            if (_centerVisualInteractable != null)
            {
                _centerVisualInteractable.Interactable = false;
            }

            // Clear cached center interactable so it gets re-resolved for the next room
            _centerVisualInteractable = null;
        }

        private void RefreshAllInteractableRenderers()
        {
            foreach (var vi in _doorInteractableByDirection.Values)
            {
                if (vi != null)
                {
                    vi.Refresh();
                }
            }
        }

        public void SetIdleOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            if (idleOccupantLayer == null)
            {
                return;
            }

            idleOccupantLayer.SetOccupants(occupants ?? Array.Empty<DungeonOccupantVisual>());
        }

        private void ApplyCenterState(RoomView room, IReadOnlyList<DungeonOccupantVisual> bossOccupants)
        {
            if (room == null)
            {
                return;
            }

            SetOnlyCenterVisualActive(ResolveCenterVisual(room.CenterType));

            if (room.CenterType == RoomCenterType.Chest && chestVisualController != null)
            {
                chestVisualController.Apply(room);
            }

            if (room.CenterType == RoomCenterType.Boss && bossVisualController != null)
            {
                room.TryGetMonster(out var monster);
                if (monster != null)
                {
                    Debug.Log(
                        $"[RoomController] Boss center apply: id={monster.MonsterId} " +
                        $"hp={monster.CurrentHp}/{monster.MaxHp} dead={monster.IsDead}");
                }
                else
                {
                    Debug.Log("[RoomController] Boss center apply: monster missing in RoomView.");
                }
                bossVisualController.Apply(monster, bossOccupants ?? Array.Empty<DungeonOccupantVisual>());
            }
        }

        /// <summary>
        /// Triggers the chest open animation on the chest visual controller.
        /// Called after a successful loot transaction.
        /// </summary>
        public void PlayChestOpenAnimation()
        {
            if (chestVisualController != null)
            {
                chestVisualController.PlayOpenAnimation();
            }
        }

        private GameObject ResolveCenterVisual(RoomCenterType centerType)
        {
            return centerType switch
            {
                RoomCenterType.Empty => centerEmptyVisualRoot,
                RoomCenterType.Chest => centerChestVisualRoot,
                RoomCenterType.Boss => centerBossVisualRoot,
                _ => centerFallbackVisualRoot
            };
        }

        private void SetOnlyCenterVisualActive(GameObject activeVisual)
        {
            SetActive(centerEmptyVisualRoot, centerEmptyVisualRoot == activeVisual);
            SetActive(centerChestVisualRoot, centerChestVisualRoot == activeVisual);
            SetActive(centerBossVisualRoot, centerBossVisualRoot == activeVisual);
            SetActive(centerFallbackVisualRoot, centerFallbackVisualRoot == activeVisual);
        }

        private static void SetActive(GameObject gameObject, bool isActive)
        {
            if (gameObject == null)
            {
                return;
            }

            gameObject.SetActive(isActive);
        }

        private void BuildDoorLayerIndex()
        {
            _doorLayerByDirection.Clear();
            _doorVisualByDirection.Clear();
            _doorInteractableByDirection.Clear();
            ResolveBossOccupantLayer();

            foreach (var binding in doorLayers)
            {
                if (binding == null)
                {
                    continue;
                }

                _doorLayerByDirection[binding.Direction] = binding.OccupantLayer;
                _doorVisualByDirection[binding.Direction] = binding.VisualController;

                // Find VisualInteractable on the same door hierarchy
                if (binding.VisualController != null)
                {
                    var interactable = binding.VisualController.GetComponentInChildren<VisualInteractable>(true);
                    if (interactable == null)
                    {
                        interactable = binding.VisualController.GetComponentInParent<VisualInteractable>();
                    }

                    if (interactable != null)
                    {
                        _doorInteractableByDirection[binding.Direction] = interactable;
                    }
                }

                if (binding.OccupantLayer != null)
                {
                    binding.OccupantLayer.SetVisualSpawnRoot(occupantVisualSpawnRoot);
                }
            }

            if (idleOccupantLayer != null)
            {
                idleOccupantLayer.SetVisualSpawnRoot(occupantVisualSpawnRoot);
            }

            if (bossOccupantLayer != null)
            {
                bossOccupantLayer.SetVisualSpawnRoot(occupantVisualSpawnRoot);
            }

            if (bossVisualController != null)
            {
                bossVisualController.SetBossOccupantVisualSpawnRoot(occupantVisualSpawnRoot);
            }
        }

        private void ResolveBossOccupantLayer()
        {
            if (bossOccupantLayer != null)
            {
                return;
            }

            if (centerBossVisualRoot == null)
            {
                return;
            }

            bossOccupantLayer = centerBossVisualRoot.GetComponentInChildren<DoorOccupantLayer2D>(true);
        }

        private void ApplyBackgroundForRoom(RoomView room)
        {
            var candidatePool = ResolveBackgroundPool(room);
            if (candidatePool == null || candidatePool.Count == 0)
            {
                return;
            }

            if (_hasBackgroundRoomKey && _backgroundRoomX == room.X && _backgroundRoomY == room.Y)
            {
                return;
            }

            _backgroundRoomX = room.X;
            _backgroundRoomY = room.Y;
            _hasBackgroundRoomKey = true;

            var validIndices = new List<int>();
            for (var index = 0; index < candidatePool.Count; index += 1)
            {
                if (candidatePool[index] != null)
                {
                    validIndices.Add(index);
                }
            }

            if (validIndices.Count == 0)
            {
                return;
            }

            var selectedIndex = validIndices[UnityEngine.Random.Range(0, validIndices.Count)];
            for (var index = 0; index < candidatePool.Count; index += 1)
            {
                var background = candidatePool[index];
                if (background == null)
                {
                    continue;
                }

                background.SetActive(index == selectedIndex);
            }

            // Ensure backgrounds from other pools are not left visible.
            SetAllBackgroundsInactiveExcept(candidatePool, selectedIndex);
        }

        private List<GameObject> ResolveBackgroundPool(RoomView room)
        {
            if (room != null &&
                room.X == LGConfig.START_X &&
                room.Y == LGConfig.START_Y &&
                starterRoomBackgrounds != null &&
                starterRoomBackgrounds.Count > 0)
            {
                return starterRoomBackgrounds;
            }

            if (additionalBackgroundPools != null && additionalBackgroundPools.Count > 0)
            {
                for (var poolIndex = 0; poolIndex < additionalBackgroundPools.Count; poolIndex += 1)
                {
                    var pool = additionalBackgroundPools[poolIndex];
                    if (pool == null || pool.Backgrounds == null || pool.Backgrounds.Count == 0)
                    {
                        continue;
                    }

                    if (string.Equals(pool.Id, "starter", StringComparison.OrdinalIgnoreCase) &&
                        room != null &&
                        room.X == LGConfig.START_X &&
                        room.Y == LGConfig.START_Y)
                    {
                        return pool.Backgrounds;
                    }
                }
            }

            return roomBackgrounds;
        }

        private void SetAllBackgroundsInactiveExcept(List<GameObject> activePool, int activeIndex)
        {
            SetPoolVisibility(roomBackgrounds, activePool, activeIndex);
            SetPoolVisibility(starterRoomBackgrounds, activePool, activeIndex);

            if (additionalBackgroundPools == null)
            {
                return;
            }

            for (var poolIndex = 0; poolIndex < additionalBackgroundPools.Count; poolIndex += 1)
            {
                var pool = additionalBackgroundPools[poolIndex];
                if (pool?.Backgrounds == null)
                {
                    continue;
                }

                SetPoolVisibility(pool.Backgrounds, activePool, activeIndex);
            }
        }

        private static void SetPoolVisibility(List<GameObject> pool, List<GameObject> activePool, int activeIndex)
        {
            if (pool == null)
            {
                return;
            }

            for (var index = 0; index < pool.Count; index += 1)
            {
                var background = pool[index];
                if (background == null)
                {
                    continue;
                }

                var shouldBeActive = ReferenceEquals(pool, activePool) && index == activeIndex;
                background.SetActive(shouldBeActive);
            }
        }

        private void EnsureOccupantVisualSpawnRoot()
        {
            if (occupantVisualSpawnRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("RoomOccupantVisuals");
            rootObject.transform.SetParent(null, false);
            rootObject.transform.position = Vector3.zero;
            rootObject.transform.rotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;
            occupantVisualSpawnRoot = rootObject.transform;
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            foreach (var timerView in _doorTimers.Values)
            {
                if (timerView == null || timerView.Root == null || !timerView.Root.activeSelf)
                {
                    continue;
                }

                // Keep position synced with the door anchor (timer is not parented to it)
                if (timerView.Anchor != null)
                {
                    timerView.Root.transform.position = timerView.Anchor.position + timerWorldOffset;
                }

                if (timerView.Label == null || timerView.SecondsRemaining <= 0f)
                {
                    continue;
                }

                timerView.SecondsRemaining = Mathf.Max(0f, timerView.SecondsRemaining - deltaTime);
                timerView.Label.text = FormatRemainingTime(timerView.SecondsRemaining);
            }
        }

        private void UpdateDoorTimer(RoomDirection direction, DoorJobView door)
        {
            UpdateDoorTimerWithSlot(direction, door, _lastKnownSlot);
        }

        /// <summary>
        /// Update the timer using a specific slot value for accurate time calculation.
        /// </summary>
        public void UpdateDoorTimerWithSlot(RoomDirection direction, DoorJobView door, ulong currentSlot)
        {
            if (door == null || timerCanvasPrefab == null)
            {
                return;
            }

            var isActiveJob = door.WallState == RoomWallState.Rubble &&
                              !door.IsCompleted &&
                              door.HelperCount > 0 &&
                              door.RequiredProgress > 0 &&
                              door.StartSlot > 0;

            var timerView = GetOrCreateDoorTimer(direction);
            if (timerView == null || timerView.Root == null)
            {
                return;
            }

            timerView.Root.SetActive(isActiveJob);
            if (!isActiveJob || timerView.Label == null)
            {
                timerView.SecondsRemaining = 0f;
                return;
            }

            // Calculate remaining time based on slots
            // effectiveProgress = (currentSlot - startSlot) * helperCount
            // remainingSlots = (requiredProgress - effectiveProgress) / helperCount
            // remainingSeconds = remainingSlots * slotSecondsEstimate
            var elapsedSlots = currentSlot > door.StartSlot ? currentSlot - door.StartSlot : 0UL;
            var effectiveProgress = elapsedSlots * door.HelperCount;
            var remainingProgress = door.RequiredProgress > effectiveProgress
                ? door.RequiredProgress - effectiveProgress
                : 0UL;

            var remainingSlots = door.HelperCount > 0
                ? (float)remainingProgress / door.HelperCount
                : 0f;
            var secondsRemaining = remainingSlots * Mathf.Max(0.01f, slotSecondsEstimate);

            timerView.SecondsRemaining = secondsRemaining;
            timerView.Label.text = FormatRemainingTime(secondsRemaining);
        }

        /// <summary>
        /// The last slot value set via <see cref="SetCurrentSlot"/>.
        /// Used by DungeonManager for optimistic timer estimation.
        /// </summary>
        public ulong LastKnownSlot => _lastKnownSlot;

        /// <summary>
        /// Set the current slot value for timer calculations.
        /// </summary>
        public void SetCurrentSlot(ulong slot)
        {
            _lastKnownSlot = slot;
        }

        private DoorTimerView GetOrCreateDoorTimer(RoomDirection direction)
        {
            if (_doorTimers.TryGetValue(direction, out var existing) && existing?.Root != null)
            {
                // Re-resolve anchor in case the door layout changed
                existing.Anchor = ResolveDoorTimerAnchor(direction);
                return existing;
            }

            if (timerCanvasPrefab == null)
            {
                return null;
            }

            var anchor = ResolveDoorTimerAnchor(direction);
            if (anchor == null)
            {
                return null;
            }

            EnsureTimerRoot();

            var root = Instantiate(timerCanvasPrefab, anchor.position + timerWorldOffset, Quaternion.identity, _timerRoot);
            root.name = $"{timerCanvasPrefab.name}_{direction}";

            var label = root.GetComponentInChildren<TMP_Text>(true);
            var timerView = new DoorTimerView(root, label, anchor);
            _doorTimers[direction] = timerView;
            root.SetActive(false);
            return timerView;
        }

        private void EnsureTimerRoot()
        {
            if (_timerRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("DoorTimerLabels");
            rootObject.transform.SetParent(null, false);
            rootObject.transform.position = Vector3.zero;
            rootObject.transform.rotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;
            _timerRoot = rootObject.transform;
        }

        private Transform ResolveDoorTimerAnchor(RoomDirection direction)
        {
            if (_doorLayerByDirection.TryGetValue(direction, out var layer) && layer != null)
            {
                return layer.transform;
            }

            if (_doorVisualByDirection.TryGetValue(direction, out var visual) && visual != null)
            {
                return visual.transform;
            }

            return transform;
        }

        private void SetAllLayersSuppressSpawnPop(bool suppress)
        {
            foreach (var layer in _doorLayerByDirection.Values)
            {
                if (layer != null)
                {
                    layer.SetSuppressSpawnPop(suppress);
                }
            }

            if (idleOccupantLayer != null)
            {
                idleOccupantLayer.SetSuppressSpawnPop(suppress);
            }
        }

        private void UpdateInteractableStates(DungeonRoomSnapshot snapshot)
        {
            var room = snapshot.Room;
            var activeJobDirs = snapshot.LocalPlayerActiveJobDirections;

            // --- Doors / rubble ---
            if (room.Doors != null)
            {
                foreach (var kvp in room.Doors)
                {
                    // Prefer the VisualInteractable on whichever state visual
                    // is currently active (resolved by DoorVisualController each
                    // time ApplyDoorState swaps children). Fall back to the
                    // cached reference from BuildDoorLayerIndex for doors that
                    // have a single shared VisualInteractable higher up.
                    VisualInteractable vi = null;
                    if (_doorVisualByDirection.TryGetValue(kvp.Key, out var dvc) && dvc != null)
                    {
                        vi = dvc.ActiveVisualInteractable;
                    }

                    if (vi == null)
                    {
                        _doorInteractableByDirection.TryGetValue(kvp.Key, out vi);
                    }

                    if (vi == null)
                    {
                        continue;
                    }

                    // Keep the cached reference in sync so RefreshAllInteractableRenderers
                    // and SetAllInteractablesOff operate on the correct component.
                    _doorInteractableByDirection[kvp.Key] = vi;

                    var door = kvp.Value;
                    var localPlayerWorking = activeJobDirs != null && activeJobDirs.Contains(kvp.Key);

                    // Interactable when the player can perform an onchain action right now:
                    //  - Open door: can walk through
                    //  - Locked door: can unlock with key
                    //  - Rubble, not yet working on it: can start clearing
                    //  - Anything else (solid wall, already working, completed): not interactable
                    var canInteract = door.IsOpen ||
                                     door.IsLocked ||
                                     door.IsEntranceStairs ||
                                     (door.IsRubble && !door.IsCompleted && !localPlayerWorking);
                    vi.Interactable = canInteract;
                }
            }

            // --- Center (chest / boss) ---
            ResolveCenterVisualInteractable();
            if (_centerVisualInteractable != null)
            {
                var canInteractCenter = false;

                if (room.HasChest() && !room.HasLocalPlayerLooted)
                {
                    canInteractCenter = true;
                }

                if (room.HasBoss() && room.TryGetMonster(out var monster) && !monster.IsDead && !snapshot.LocalPlayerFightingBoss)
                {
                    canInteractCenter = true;
                }

                _centerVisualInteractable.Interactable = canInteractCenter;
            }
        }

        private void ResolveCenterVisualInteractable()
        {
            if (_centerVisualInteractable != null)
            {
                return;
            }

            // Check chest visual root first, then boss, then the center interactable itself
            if (centerChestVisualRoot != null)
            {
                _centerVisualInteractable = centerChestVisualRoot.GetComponentInChildren<VisualInteractable>(true);
            }

            if (_centerVisualInteractable == null && centerBossVisualRoot != null)
            {
                _centerVisualInteractable = centerBossVisualRoot.GetComponentInChildren<VisualInteractable>(true);
            }

            if (_centerVisualInteractable == null && centerInteractable != null)
            {
                _centerVisualInteractable = centerInteractable.GetComponentInChildren<VisualInteractable>(true);
            }
        }

        private void SetAllDoorTimersVisible(bool isVisible)
        {
            foreach (var timer in _doorTimers.Values)
            {
                if (timer?.Root == null)
                {
                    continue;
                }

                timer.Root.SetActive(isVisible);
            }
        }

        private static string FormatRemainingTime(float secondsRemaining)
        {
            var clampedSeconds = Mathf.Max(0f, secondsRemaining);
            var totalSeconds = Mathf.CeilToInt(clampedSeconds);
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private sealed class DoorTimerView
        {
            public DoorTimerView(GameObject root, TMP_Text label, Transform anchor)
            {
                Root = root;
                Label = label;
                Anchor = anchor;
            }

            public GameObject Root { get; }
            public TMP_Text Label { get; }
            public Transform Anchor { get; set; }
            public float SecondsRemaining { get; set; }
        }
    }
}
