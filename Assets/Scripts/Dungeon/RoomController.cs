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
        [Header("Center Visuals")]
        [SerializeField] private GameObject centerEmptyVisualRoot;
        [SerializeField] private GameObject centerChestVisualRoot;
        [SerializeField] private GameObject centerBossVisualRoot;
        [SerializeField] private GameObject centerFallbackVisualRoot;
        [SerializeField] private DungeonChestVisualController chestVisualController;
        [SerializeField] private DungeonBossVisualController bossVisualController;

        private readonly Dictionary<RoomDirection, DoorOccupantLayer2D> _doorLayerByDirection = new();
        private readonly Dictionary<RoomDirection, DoorVisualController> _doorVisualByDirection = new();

        private void Awake()
        {
            BuildDoorLayerIndex();
        }

        public void ApplySnapshot(DungeonRoomSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Room == null)
            {
                return;
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

        public void SetBossOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            var count = occupants?.Count ?? 0;
            Debug.Log($"[RoomController] Boss occupants={count}");
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
            }
        }
    }
}
