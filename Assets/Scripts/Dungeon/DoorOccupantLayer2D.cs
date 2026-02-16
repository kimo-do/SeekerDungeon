using System;
using System.Collections.Generic;
using DG.Tweening;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public enum OccupantFacingDirection
    {
        Right = 0,
        Left = 1
    }

    [System.Serializable]
    public sealed class DoorOccupantSlot
    {
        [SerializeField] private Transform anchor;
        [SerializeField] private OccupantFacingDirection facingDirection = OccupantFacingDirection.Right;

        public Transform Anchor => anchor;
        public OccupantFacingDirection FacingDirection => facingDirection;
    }

    public sealed class DoorOccupantLayer2D : MonoBehaviour
    {
        [SerializeField] private DoorOccupantSlot[] visualSlots;
        [SerializeField] private GameObject occupantVisualPrefab;
        [SerializeField] private Transform visualSpawnRoot;
        [Header("Arrival")]
        [Tooltip("Where the local player appears when traveling through this door into the room.")]
        [SerializeField] private Transform arrivalAnchor;
        [Header("Spawn Pop")]
        [SerializeField] private float spawnPopDuration = 0.1f;
        [SerializeField] private float spawnPopStartScaleMultiplier = 0.82f;
        [SerializeField] private float spawnStaggerSeconds = 0.05f;
        private const int MaxVisibleOccupants = 5;

        private readonly Dictionary<string, DoorOccupantVisual2D> _activeByOccupantKey = new();
        private readonly Queue<DoorOccupantVisual2D> _pooledVisuals = new();
        private readonly List<string> _releaseBuffer = new();
        private bool _suppressSpawnPop;

        public void SetVisualSpawnRoot(Transform spawnRoot)
        {
            visualSpawnRoot = spawnRoot;
        }

        public void SetSuppressSpawnPop(bool suppress)
        {
            _suppressSpawnPop = suppress;
        }

        /// <summary>
        /// Returns the dedicated arrival anchor position for this door.
        /// Used when the local player enters the room through this door.
        /// </summary>
        public bool TryGetArrivalPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (arrivalAnchor == null)
            {
                return false;
            }

            worldPosition = arrivalAnchor.position;
            return true;
        }

        public bool TryGetLocalPlayerStandPosition(out Vector3 worldPosition)
        {
            return TryGetLocalPlayerStandPlacement(out worldPosition, out _);
        }

        public bool TryGetLocalPlayerStandPlacement(
            out Vector3 worldPosition,
            out OccupantFacingDirection facingDirection)
        {
            worldPosition = default;
            facingDirection = OccupantFacingDirection.Right;
            if (visualSlots == null || visualSlots.Length == 0)
            {
                return false;
            }

            var occupiedVisibleCount = _activeByOccupantKey.Count;
            var preferredIndex = occupiedVisibleCount >= visualSlots.Length
                ? 0
                : Mathf.Clamp(occupiedVisibleCount, 0, visualSlots.Length - 1);

            if (TryGetSlotPlacement(preferredIndex, out worldPosition, out facingDirection))
            {
                return true;
            }

            for (var index = 0; index < visualSlots.Length; index += 1)
            {
                if (TryGetSlotPlacement(index, out worldPosition, out facingDirection))
                {
                    return true;
                }
            }

            return false;
        }

        public void SetOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            if (occupantVisualPrefab == null || visualSlots == null || visualSlots.Length == 0 || occupants == null)
            {
                ReleaseAllActiveVisuals();
                return;
            }

            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            var newVisualIndex = 0;
            var visibleCount = Mathf.Min(MaxVisibleOccupants, visualSlots.Length, occupants.Count);
            for (var index = 0; index < visibleCount; index += 1)
            {
                var slot = visualSlots[index];
                if (slot == null || slot.Anchor == null)
                {
                    continue;
                }

                // Apply slot-facing to anchor so entire slot scale X is -1 when facing left
                var anchor = slot.Anchor;
                var anchorScale = anchor.localScale;
                anchorScale.x = slot.FacingDirection == OccupantFacingDirection.Left ? -1f : 1f;
                anchor.localScale = anchorScale;

                var occupant = occupants[index];
                var key = ResolveOccupantKey(occupant, index);
                if (!usedKeys.Add(key))
                {
                    key = $"{key}#{index}";
                    usedKeys.Add(key);
                }

                var isNewVisual = !_activeByOccupantKey.TryGetValue(key, out var visual) || visual == null;
                if (isNewVisual)
                {
                    visual = GetOrCreateVisual();
                    if (visual == null)
                    {
                        continue;
                    }

                    _activeByOccupantKey[key] = visual;
                }

                var visualTransform = visual.transform;
                var parent = visualSpawnRoot != null ? visualSpawnRoot : null;
                if (visualTransform.parent != parent)
                {
                    visualTransform.SetParent(parent, true);
                }

                visualTransform.SetPositionAndRotation(slot.Anchor.position, Quaternion.identity);
                visual.Bind(occupant, index, slot.FacingDirection);

                var shouldPlaySpawnPop = isNewVisual &&
                                         !_suppressSpawnPop &&
                                         !OccupantSpawnPopTracker.HasSeen(key);
                if (shouldPlaySpawnPop)
                {
                    var spawnDelay = newVisualIndex * spawnStaggerSeconds;
                    visual.PlaySpawnPop(spawnPopDuration, spawnPopStartScaleMultiplier, spawnDelay);
                    newVisualIndex += 1;
                }

                OccupantSpawnPopTracker.MarkSeen(key);
            }

            ReleaseUnusedVisuals(usedKeys);

            // Hook for overflow visuals (e.g. +95 near door) can be added here later.
        }

        private void OnDisable()
        {
            ReleaseAllActiveVisuals();
        }

        private DoorOccupantVisual2D GetOrCreateVisual()
        {
            while (_pooledVisuals.Count > 0)
            {
                var pooled = _pooledVisuals.Dequeue();
                if (pooled == null)
                {
                    continue;
                }

                pooled.gameObject.SetActive(true);
                return pooled;
            }

            var visualObject = Instantiate(
                occupantVisualPrefab,
                Vector3.zero,
                Quaternion.identity,
                visualSpawnRoot);

            var visual = visualObject.GetComponent<DoorOccupantVisual2D>();
            if (visual != null)
            {
                return visual;
            }

            visual = visualObject.AddComponent<DoorOccupantVisual2D>();
            return visual;
        }

        private void ReleaseUnusedVisuals(HashSet<string> usedKeys)
        {
            _releaseBuffer.Clear();
            foreach (var key in _activeByOccupantKey.Keys)
            {
                if (!usedKeys.Contains(key))
                {
                    _releaseBuffer.Add(key);
                }
            }

            for (var i = 0; i < _releaseBuffer.Count; i += 1)
            {
                var key = _releaseBuffer[i];
                if (_activeByOccupantKey.TryGetValue(key, out var visual))
                {
                    ReturnVisualToPool(visual);
                }

                _activeByOccupantKey.Remove(key);
            }
        }

        private void ReleaseAllActiveVisuals()
        {
            foreach (var visual in _activeByOccupantKey.Values)
            {
                ReturnVisualToPool(visual);
            }

            _activeByOccupantKey.Clear();
            _releaseBuffer.Clear();
        }

        private void ReturnVisualToPool(DoorOccupantVisual2D visual)
        {
            if (visual == null)
            {
                return;
            }

            visual.ResetForPool();
            visual.gameObject.SetActive(false);
            _pooledVisuals.Enqueue(visual);
        }

        private static string ResolveOccupantKey(DungeonOccupantVisual occupant, int index)
        {
            if (!string.IsNullOrWhiteSpace(occupant.WalletKey))
            {
                return occupant.WalletKey;
            }

            return $"{occupant.DisplayName}_{index}";
        }

        private bool TryGetSlotPlacement(
            int index,
            out Vector3 worldPosition,
            out OccupantFacingDirection facingDirection)
        {
            worldPosition = default;
            facingDirection = OccupantFacingDirection.Right;
            if (index < 0 || index >= visualSlots.Length)
            {
                return false;
            }

            var slot = visualSlots[index];
            if (slot == null || slot.Anchor == null)
            {
                return false;
            }

            worldPosition = slot.Anchor.position;
            facingDirection = slot.FacingDirection;
            return true;
        }
    }

    public sealed class DoorOccupantVisual2D : MonoBehaviour
    {
        private const int BaseSortingOrder = 10;
        private const float MinScaleMultiplier = 0.05f;

        private LGPlayerController _playerController;
        private SpriteRenderer[] _spriteRenderers;
        private Tween _spawnTween;
        private Vector3 _baseScale = Vector3.one;

        private void Awake()
        {
            _playerController = GetComponent<LGPlayerController>();
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            _baseScale = transform.localScale;
        }

        private void OnDisable()
        {
            KillSpawnTween();
        }

        public void Bind(
            DungeonOccupantVisual occupant,
            int stackIndex,
            OccupantFacingDirection facingDirection)
        {
            transform.rotation = Quaternion.identity;

            if (_playerController != null)
            {
                _playerController.ApplySkin(occupant.SkinId);
                _playerController.SetDisplayName(occupant.DisplayName);
                _playerController.SetDisplayNameVisible(true);
                if (ItemRegistry.IsWearable(occupant.EquippedItemId))
                {
                    _playerController.ShowWieldedItem(occupant.EquippedItemId);
                }
                else
                {
                    _playerController.HideAllWieldedItems();
                }
            }

            ApplyFacing(facingDirection);

            if (_spriteRenderers != null)
            {
                var sortingOrder = BaseSortingOrder + stackIndex;
                foreach (var spriteRenderer in _spriteRenderers)
                {
                    if (spriteRenderer == null)
                    {
                        continue;
                    }

                    spriteRenderer.sortingOrder = sortingOrder;
                }
            }

            gameObject.name = $"Occupant_{occupant.DisplayName}";
        }

        public void PlaySpawnPop(float duration, float startScaleMultiplier, float delaySeconds = 0f)
        {
            KillSpawnTween();

            var targetScale = transform.localScale;
            var clampedMultiplier = Mathf.Max(MinScaleMultiplier, startScaleMultiplier);
            var startScale = targetScale * clampedMultiplier;
            startScale.x = Mathf.Sign(targetScale.x) * Mathf.Abs(targetScale.x) * clampedMultiplier;
            transform.localScale = startScale;

            _spawnTween = transform
                .DOScale(targetScale, duration)
                .SetEase(Ease.OutQuad)
                .SetDelay(Mathf.Max(0f, delaySeconds))
                .SetUpdate(true);
        }

        public void ResetForPool()
        {
            KillSpawnTween();
            transform.localScale = _baseScale;
        }

        private void ApplyFacing(OccupantFacingDirection facingDirection)
        {
            var localScale = transform.localScale;
            var absoluteX = Mathf.Abs(localScale.x);
            localScale.x = facingDirection == OccupantFacingDirection.Right
                ? absoluteX
                : -absoluteX;
            transform.localScale = localScale;
        }

        private void KillSpawnTween()
        {
            if (_spawnTween == null)
            {
                return;
            }

            _spawnTween.Kill();
            _spawnTween = null;
        }
    }
}
