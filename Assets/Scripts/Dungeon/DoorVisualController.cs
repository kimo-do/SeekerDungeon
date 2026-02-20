using System;
using System.Collections.Generic;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [Serializable]
    public sealed class DoorStateVisualEntry
    {
        [SerializeField] private RoomWallState wallState;
        [SerializeField] private GameObject visualRoot;

        public RoomWallState WallState => wallState;
        public GameObject VisualRoot => visualRoot;
    }

    [Serializable]
    public sealed class LockedDoorVisualEntry
    {
        [SerializeField] private byte lockKind;
        [SerializeField] private GameObject visualRoot;

        public byte LockKind => lockKind;
        public GameObject VisualRoot => visualRoot;
    }

    public sealed class DoorVisualController : MonoBehaviour
    {
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private List<DoorStateVisualEntry> stateVisuals = new();
        [SerializeField] private List<LockedDoorVisualEntry> lockedDoorVisuals = new();
        [SerializeField] private GameObject entranceStairsVisualRoot;
        [SerializeField] private GameObject fallbackVisualRoot;

        private readonly Dictionary<RoomWallState, GameObject> _visualByState = new();
        private readonly Dictionary<byte, GameObject> _lockedVisualByKind = new();
        private RubbleJobVfxController[] _rubbleJobVfxControllers = Array.Empty<RubbleJobVfxController>();

        /// <summary>
        /// The VisualInteractable on the currently active state visual, or null.
        /// Updated every time ApplyDoorState swaps the active child.
        /// </summary>
        public VisualInteractable ActiveVisualInteractable { get; private set; }

        private void Awake()
        {
            RebuildStateIndex();
            CacheRubbleVfxControllers();
            SetRubbleJobVfxActive(false);
        }

        public void ApplyDoorState(DoorJobView door)
        {
            if (door == null)
            {
                return;
            }

            var targetVisual = ResolveVisual(door);
            var requestedVisualName = targetVisual != null ? targetVisual.name : "null";
            if (targetVisual != null && !IsLocalVisual(targetVisual))
            {
                targetVisual = null;
            }

            var fallbackVisual = IsLocalVisual(fallbackVisualRoot)
                ? fallbackVisualRoot
                : ResolveFirstLocalFallbackVisual();

            SetAllKnownDoorVisualsActiveState(targetVisual);

            if (fallbackVisual != null)
            {
                var useFallback = targetVisual == null;
                // If fallback is the same object as the selected visual, do not
                // overwrite the active state we just set in SetAllKnownDoorVisualsActiveState.
                if (!ReferenceEquals(fallbackVisual, targetVisual))
                {
                    fallbackVisual.SetActive(useFallback);
                }
                if (useFallback)
                {
                    targetVisual = fallbackVisual;
                }
            }

            // Resolve the VisualInteractable on whichever state visual is now active.
            ActiveVisualInteractable = targetVisual != null
                ? targetVisual.GetComponentInChildren<VisualInteractable>(true)
                : null;

            var isRubbleJobActive = door.WallState == RoomWallState.Rubble &&
                                    !door.IsCompleted &&
                                    door.HelperCount > 0 &&
                                    door.RequiredProgress > 0 &&
                                    door.StartSlot > 0;
            SetRubbleJobVfxActive(isRubbleJobActive);

            if (enableDebugLogs && (door.WallState == RoomWallState.Locked || targetVisual == null))
            {
                var chosenVisualName = targetVisual != null ? targetVisual.name : "null";
                var fallbackVisualName = fallbackVisual != null ? fallbackVisual.name : "null";
                Debug.Log(
                    $"[DoorVisualController] ApplyDoorState door={name} dir={door.Direction} wall={door.WallState} lockKind={door.LockKind} " +
                    $"requestedVisual={requestedVisualName} chosenVisual={chosenVisualName} fallback={fallbackVisualName} " +
                    $"activeChildren=[{GetActiveChildNames()}] stateMap=[{DescribeStateMap()}] lockMap=[{DescribeLockMap()}] " +
                    $"visualActive=[{DescribeVisualActiveFlags()}]");
            }
        }

        private void RebuildStateIndex()
        {
            _visualByState.Clear();
            _lockedVisualByKind.Clear();

            foreach (var stateVisual in stateVisuals)
            {
                if (stateVisual == null || stateVisual.VisualRoot == null)
                {
                    continue;
                }

                if (!IsLocalVisual(stateVisual.VisualRoot))
                {
                    Debug.LogWarning(
                        $"[DoorVisualController] Ignoring non-local state visual '{stateVisual.VisualRoot.name}' on '{name}'.");
                    continue;
                }

                _visualByState[stateVisual.WallState] = stateVisual.VisualRoot;
            }

            if (entranceStairsVisualRoot != null &&
                !_visualByState.ContainsKey(RoomWallState.EntranceStairs))
            {
                _visualByState[RoomWallState.EntranceStairs] = entranceStairsVisualRoot;
            }

            if (!_visualByState.ContainsKey(RoomWallState.Locked) &&
                _visualByState.TryGetValue(RoomWallState.Solid, out var solidVisual))
            {
                _visualByState[RoomWallState.Locked] = solidVisual;
            }

            foreach (var lockedVisual in lockedDoorVisuals)
            {
                if (lockedVisual == null || lockedVisual.VisualRoot == null)
                {
                    continue;
                }

                if (!IsLocalVisual(lockedVisual.VisualRoot))
                {
                    Debug.LogWarning(
                        $"[DoorVisualController] Ignoring non-local locked visual '{lockedVisual.VisualRoot.name}' on '{name}'.");
                    continue;
                }

                _lockedVisualByKind[lockedVisual.LockKind] = lockedVisual.VisualRoot;
            }
        }

        private void CacheRubbleVfxControllers()
        {
            _rubbleJobVfxControllers = GetComponentsInChildren<RubbleJobVfxController>(true);
        }

        private void SetRubbleJobVfxActive(bool active)
        {
            if (_rubbleJobVfxControllers == null || _rubbleJobVfxControllers.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _rubbleJobVfxControllers.Length; i += 1)
            {
                var controller = _rubbleJobVfxControllers[i];
                if (controller == null)
                {
                    continue;
                }

                controller.SetJobActive(active);
            }
        }

        private GameObject ResolveVisual(DoorJobView door)
        {
            var wallState = door.WallState;

            if (wallState == RoomWallState.Locked &&
                _lockedVisualByKind.TryGetValue(door.LockKind, out var lockedVisual) &&
                lockedVisual != null)
            {
                return lockedVisual;
            }

            if (_visualByState.TryGetValue(wallState, out var visual))
            {
                return visual;
            }

            if (wallState == RoomWallState.EntranceStairs &&
                _visualByState.TryGetValue(RoomWallState.Open, out var openDoorVisual))
            {
                return openDoorVisual;
            }

            return null;
        }

        private bool IsLocalVisual(GameObject visualRoot)
        {
            return visualRoot != null && visualRoot.transform.IsChildOf(transform);
        }

        private GameObject ResolveFirstLocalFallbackVisual()
        {
            foreach (var visual in _visualByState.Values)
            {
                if (IsLocalVisual(visual))
                {
                    return visual;
                }
            }

            foreach (var visual in _lockedVisualByKind.Values)
            {
                if (IsLocalVisual(visual))
                {
                    return visual;
                }
            }

            return null;
        }

        private string DescribeStateMap()
        {
            var entries = new List<string>();
            foreach (var kvp in _visualByState)
            {
                var visualName = kvp.Value != null ? kvp.Value.name : "null";
                var local = IsLocalVisual(kvp.Value) ? "local" : "nonlocal";
                entries.Add($"{kvp.Key}:{visualName}:{local}");
            }

            return string.Join(", ", entries);
        }

        private string DescribeLockMap()
        {
            var entries = new List<string>();
            foreach (var kvp in _lockedVisualByKind)
            {
                var visualName = kvp.Value != null ? kvp.Value.name : "null";
                var local = IsLocalVisual(kvp.Value) ? "local" : "nonlocal";
                entries.Add($"{kvp.Key}:{visualName}:{local}");
            }

            return string.Join(", ", entries);
        }

        private string GetActiveChildNames()
        {
            var active = new List<string>();
            for (var index = 0; index < transform.childCount; index += 1)
            {
                var child = transform.GetChild(index);
                if (child != null && child.gameObject.activeSelf)
                {
                    active.Add(child.gameObject.name);
                }
            }

            return string.Join(", ", active);
        }

        private string DescribeVisualActiveFlags()
        {
            var entries = new List<string>();
            var handled = new HashSet<GameObject>();

            foreach (var visual in _visualByState.Values)
            {
                if (visual == null || !handled.Add(visual))
                {
                    continue;
                }

                entries.Add($"{visual.name}:self={visual.activeSelf}:hier={visual.activeInHierarchy}");
            }

            foreach (var visual in _lockedVisualByKind.Values)
            {
                if (visual == null || !handled.Add(visual))
                {
                    continue;
                }

                entries.Add($"{visual.name}:self={visual.activeSelf}:hier={visual.activeInHierarchy}");
            }

            return string.Join(", ", entries);
        }

        private void SetAllKnownDoorVisualsActiveState(GameObject targetVisual)
        {
            var handled = new HashSet<GameObject>();

            foreach (var stateVisual in _visualByState.Values)
            {
                if (stateVisual == null || !handled.Add(stateVisual))
                {
                    continue;
                }

                stateVisual.SetActive(stateVisual == targetVisual);
            }

            foreach (var lockedVisual in _lockedVisualByKind.Values)
            {
                if (lockedVisual == null || !handled.Add(lockedVisual))
                {
                    continue;
                }

                lockedVisual.SetActive(lockedVisual == targetVisual);
            }
        }
    }
}
