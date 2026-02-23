using System.Collections.Generic;
using DG.Tweening;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [System.Serializable]
    public sealed class CenterLootVariantBinding
    {
        [SerializeField] private RoomCenterType centerType = RoomCenterType.Chest;
        [SerializeField] private DungeonChestVisualController chestVisualController;

        public RoomCenterType CenterType => centerType;
        public DungeonChestVisualController ChestVisualController => chestVisualController;
    }

    public sealed class DungeonCenterLootVisualController : MonoBehaviour
    {
        [SerializeField] private bool logDebugMessages;
        [SerializeField] private DungeonChestVisualController defaultChestVisualController;
        [SerializeField] private GameObject defaultClosedVisual;
        [SerializeField] private GameObject defaultOpenVisual;
        [SerializeField] private List<CenterLootVariantBinding> variantBindings = new();
        [SerializeField] private float openPopScaleMultiplier = 1.08f;
        [SerializeField] private float openPopDurationSeconds = 0.2f;

        private bool _isOpen;
        private bool _hasRoomKey;
        private int _roomX;
        private int _roomY;
        private bool _optimisticOpenedForRoom;
        private GameObject _activeClosedVisual;
        private GameObject _activeOpenVisual;
        private DungeonChestVisualController _activeChestVisualController;
        private readonly Dictionary<Transform, Vector3> _baseScaleByTransform = new();

        public void Apply(RoomView room)
        {
            if (room == null)
            {
                return;
            }

            var roomChanged = !_hasRoomKey || _roomX != room.X || _roomY != room.Y;
            if (roomChanged)
            {
                _hasRoomKey = true;
                _roomX = room.X;
                _roomY = room.Y;
                _optimisticOpenedForRoom = false;
            }

            if (!room.HasChest())
            {
                DeactivateAllChestControllers(null);
                DeactivateAllChestVisuals();
                _isOpen = false;
                return;
            }

            ResolveVisualPair(
                room.CenterType,
                out var resolvedController,
                out var closedVisual,
                out var openVisual);
            _activeChestVisualController = resolvedController;
            _activeClosedVisual = closedVisual;
            _activeOpenVisual = openVisual;

            if (_activeChestVisualController != null)
            {
                DeactivateAllChestVisuals();
                DeactivateAllChestControllers(_activeChestVisualController);
                _activeChestVisualController.Apply(room);
                return;
            }

            DeactivateAllChestControllers(null);
            var shouldBeOpen = room.HasLocalPlayerLooted || _optimisticOpenedForRoom;
            SetOpen(shouldBeOpen, animate: false);

            if (logDebugMessages)
            {
                Debug.Log(
                    $"[DungeonCenterLootVisual] centerType={room.CenterType} " +
                    $"looted={room.HasLocalPlayerLooted} optimistic={_optimisticOpenedForRoom} open={_isOpen}");
            }
        }

        public void PlayOpenAnimation()
        {
            _optimisticOpenedForRoom = true;
            if (_activeChestVisualController != null)
            {
                _activeChestVisualController.PlayOpenAnimation();
                return;
            }

            if (_activeClosedVisual == null && _activeOpenVisual == null)
            {
                ResolveVisualPair(
                    RoomCenterType.Chest,
                    out _activeChestVisualController,
                    out _activeClosedVisual,
                    out _activeOpenVisual);
                if (_activeChestVisualController != null)
                {
                    _activeChestVisualController.PlayOpenAnimation();
                    return;
                }
            }

            if (_isOpen)
            {
                return;
            }

            SetOpen(true, animate: true);
        }

        private void ResolveVisualPair(
            RoomCenterType centerType,
            out DungeonChestVisualController chestVisualController,
            out GameObject closedVisual,
            out GameObject openVisual)
        {
            chestVisualController = defaultChestVisualController;
            closedVisual = defaultClosedVisual;
            openVisual = defaultOpenVisual;

            if (variantBindings == null)
            {
                return;
            }

            for (var index = 0; index < variantBindings.Count; index += 1)
            {
                var binding = variantBindings[index];
                if (binding == null || binding.CenterType != centerType)
                {
                    continue;
                }

                if (binding.ChestVisualController != null)
                {
                    chestVisualController = binding.ChestVisualController;
                }
                return;
            }
        }

        private void DeactivateAllChestControllers(DungeonChestVisualController keepActive)
        {
            if (defaultChestVisualController != null && defaultChestVisualController != keepActive)
            {
                defaultChestVisualController.HideVisualsAndReset();
            }

            if (variantBindings == null)
            {
                return;
            }

            for (var index = 0; index < variantBindings.Count; index += 1)
            {
                var controller = variantBindings[index]?.ChestVisualController;
                if (controller != null && controller != keepActive)
                {
                    controller.HideVisualsAndReset();
                }
            }
        }

        private void SetOpen(bool open, bool animate)
        {
            _isOpen = open;
            DeactivateAllChestVisuals();

            if (_activeClosedVisual != null)
            {
                _activeClosedVisual.SetActive(!open);
            }

            if (_activeOpenVisual != null)
            {
                _activeOpenVisual.SetActive(open);
            }

            if (!open || _activeOpenVisual == null)
            {
                return;
            }

            var target = _activeOpenVisual.transform;
            var baseScale = GetBaseScale(target);
            if (!animate)
            {
                target.localScale = baseScale;
                return;
            }

            target.DOKill();
            target.localScale = baseScale;
            target
                .DOPunchScale(baseScale * (openPopScaleMultiplier - 1f), openPopDurationSeconds, 1, 0f)
                .SetEase(Ease.OutQuad);
        }

        private void DeactivateAllChestVisuals()
        {
            if (defaultClosedVisual != null)
            {
                defaultClosedVisual.SetActive(false);
            }
            if (defaultOpenVisual != null)
            {
                defaultOpenVisual.SetActive(false);
            }

            if (variantBindings == null)
            {
                return;
            }
        }

        private Vector3 GetBaseScale(Transform target)
        {
            if (target == null)
            {
                return Vector3.one;
            }

            if (_baseScaleByTransform.TryGetValue(target, out var baseScale))
            {
                return baseScale;
            }

            baseScale = target.localScale;
            _baseScaleByTransform[target] = baseScale;
            return baseScale;
        }

        private void OnDisable()
        {
            _isOpen = false;
            _optimisticOpenedForRoom = false;
            _activeChestVisualController = null;
            _activeClosedVisual = null;
            _activeOpenVisual = null;
            DeactivateAllChestControllers(null);
            DeactivateAllChestVisuals();
        }
    }
}
