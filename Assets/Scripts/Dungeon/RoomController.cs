using System;
using System.Collections.Generic;
using SeekerDungeon.Solana;
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

    public sealed class RoomController : MonoBehaviour
    {
        [SerializeField] private List<DoorLayerBinding> doorLayers = new();
        [Header("Backgrounds")]
        [SerializeField] private List<GameObject> roomBackgrounds = new();
        [Header("Occupant Visuals")]
        [SerializeField] private Transform occupantVisualSpawnRoot;
        [SerializeField] private RoomIdleOccupantLayer2D idleOccupantLayer;
        [Header("Center Visuals")]
        [SerializeField] private GameObject centerEmptyVisualRoot;
        [SerializeField] private GameObject centerChestVisualRoot;
        [SerializeField] private GameObject centerBossVisualRoot;
        [SerializeField] private GameObject centerFallbackVisualRoot;
        [SerializeField] private DungeonChestVisualController chestVisualController;
        [SerializeField] private DungeonBossVisualController bossVisualController;

        private readonly Dictionary<RoomDirection, DoorOccupantLayer2D> _doorLayerByDirection = new();
        private readonly Dictionary<RoomDirection, DoorVisualController> _doorVisualByDirection = new();
        private bool _hasBackgroundRoomKey;
        private int _backgroundRoomX;
        private int _backgroundRoomY;

        private void Awake()
        {
            EnsureOccupantVisualSpawnRoot();
            BuildDoorLayerIndex();
        }

        public void ApplySnapshot(DungeonRoomSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Room == null)
            {
                return;
            }

            ApplyBackgroundForRoom(snapshot.Room);

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
        }

        public void ApplyDoorState(RoomDirection direction, DoorJobView door)
        {
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

        public void SetBossOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            var count = occupants?.Count ?? 0;
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
                bossVisualController.Apply(monster, bossOccupants ?? Array.Empty<DungeonOccupantVisual>());
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

            foreach (var binding in doorLayers)
            {
                if (binding == null)
                {
                    continue;
                }

                _doorLayerByDirection[binding.Direction] = binding.OccupantLayer;
                _doorVisualByDirection[binding.Direction] = binding.VisualController;

                if (binding.OccupantLayer != null)
                {
                    binding.OccupantLayer.SetVisualSpawnRoot(occupantVisualSpawnRoot);
                }
            }

            if (idleOccupantLayer != null)
            {
                idleOccupantLayer.SetVisualSpawnRoot(occupantVisualSpawnRoot);
            }
        }

        private void ApplyBackgroundForRoom(RoomView room)
        {
            if (roomBackgrounds == null || roomBackgrounds.Count == 0)
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
            for (var index = 0; index < roomBackgrounds.Count; index += 1)
            {
                if (roomBackgrounds[index] != null)
                {
                    validIndices.Add(index);
                }
            }

            if (validIndices.Count == 0)
            {
                return;
            }

            var selectedIndex = validIndices[UnityEngine.Random.Range(0, validIndices.Count)];
            for (var index = 0; index < roomBackgrounds.Count; index += 1)
            {
                var background = roomBackgrounds[index];
                if (background == null)
                {
                    continue;
                }

                background.SetActive(index == selectedIndex);
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
    }
}
