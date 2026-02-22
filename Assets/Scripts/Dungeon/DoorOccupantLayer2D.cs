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
        [Header("Presence")]
        [SerializeField] private float activeThresholdSeconds = 60f;
        [SerializeField] private float idleThresholdSeconds = 180f;
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

            var nowRealtime = Time.realtimeSinceStartup;
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
                var presenceState = OccupantPresenceTracker.UpdateAndGetState(
                    occupant.WalletKey,
                    BuildPresenceSignature(occupant, "door"),
                    nowRealtime,
                    activeThresholdSeconds,
                    idleThresholdSeconds,
                    occupant.LastActionAgeSecondsEstimate);

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
                visual.Bind(occupant, index, slot.FacingDirection, presenceState);

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

        private static string BuildPresenceSignature(DungeonOccupantVisual occupant, string layerTag)
        {
            if (occupant == null)
            {
                return layerTag ?? "unknown";
            }

            return string.Concat(
                layerTag ?? string.Empty,
                "|skin:", ((int)occupant.SkinId).ToString(),
                "|item:", ((int)occupant.EquippedItemId).ToString(),
                "|act:", ((int)occupant.Activity).ToString(),
                "|dir:", occupant.ActivityDirection.HasValue ? ((int)occupant.ActivityDirection.Value).ToString() : "-",
                "|boss:", occupant.IsFightingBoss ? "1" : "0",
                "|last:", occupant.LastActiveSlot.ToString());
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
        private static readonly Color ActiveNameColor = Color.white;
        private static readonly Color IdleNameColor = new(0.84f, 0.84f, 0.84f, 1f);
        private static readonly Color AfkNameColor = new(0.68f, 0.68f, 0.68f, 1f);

        private LGPlayerController _playerController;
        private SpriteRenderer[] _spriteRenderers;
        private int[] _baseSortingOrders;
        private Tween _spawnTween;
        private Vector3 _baseScale = Vector3.one;

        public string BoundWalletKey { get; private set; }
        public string BoundDisplayName { get; private set; }
        public OccupantPresenceState BoundPresenceState { get; private set; } = OccupantPresenceState.Active;

        private void Awake()
        {
            _playerController = GetComponent<LGPlayerController>();
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (_spriteRenderers != null && _spriteRenderers.Length > 0)
            {
                _baseSortingOrders = new int[_spriteRenderers.Length];
                for (var index = 0; index < _spriteRenderers.Length; index += 1)
                {
                    var spriteRenderer = _spriteRenderers[index];
                    _baseSortingOrders[index] = spriteRenderer != null ? spriteRenderer.sortingOrder : 0;
                }
            }
            _baseScale = transform.localScale;
        }

        private void OnDisable()
        {
            KillSpawnTween();
        }

        public void Bind(
            DungeonOccupantVisual occupant,
            int stackIndex,
            OccupantFacingDirection facingDirection,
            OccupantPresenceState presenceState = OccupantPresenceState.Active)
        {
            transform.rotation = Quaternion.identity;
            BoundPresenceState = presenceState;
            BoundWalletKey = occupant?.WalletKey ?? string.Empty;
            BoundDisplayName = occupant?.DisplayName ?? string.Empty;
            var displayName = ResolveDisplayNameForState(occupant, presenceState);
            var nameColor = ResolveNameColor(presenceState);

            if (_playerController != null)
            {
                _playerController.ApplySkin(occupant.SkinId);
                _playerController.SetDisplayName(displayName);
                _playerController.SetDisplayNameVisible(true);
                _playerController.SetLocalPlayerNameStyle(false);
                _playerController.SetDisplayNameStyleOverride(nameColor, TMPro.FontStyles.Bold);
                _playerController.SetOccupantPresenceState(presenceState);
                _playerController.SetMiningAnimationState(occupant.Activity == OccupantActivity.DoorJob);
                _playerController.SetBossJobAnimationState(occupant.Activity == OccupantActivity.BossFight);
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
                var sortingOffset = BaseSortingOrder + stackIndex;
                for (var index = 0; index < _spriteRenderers.Length; index += 1)
                {
                    var spriteRenderer = _spriteRenderers[index];
                    if (spriteRenderer == null)
                    {
                        continue;
                    }

                    var baseSortingOrder =
                        _baseSortingOrders != null && index < _baseSortingOrders.Length
                            ? _baseSortingOrders[index]
                            : spriteRenderer.sortingOrder;
                    spriteRenderer.sortingOrder = baseSortingOrder + sortingOffset;
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
            BoundWalletKey = string.Empty;
            BoundDisplayName = string.Empty;
            BoundPresenceState = OccupantPresenceState.Active;
            if (_playerController != null)
            {
                _playerController.ClearDisplayNameStyleOverride();
                _playerController.SetOccupantPresenceState(OccupantPresenceState.Active);
            }
        }

        private static Color ResolveNameColor(OccupantPresenceState state)
        {
            return state switch
            {
                OccupantPresenceState.Active => ActiveNameColor,
                OccupantPresenceState.Idle => IdleNameColor,
                OccupantPresenceState.Afk => AfkNameColor,
                _ => ActiveNameColor
            };
        }

        private static string ResolveDisplayNameForState(DungeonOccupantVisual occupant, OccupantPresenceState state)
        {
            var displayName = occupant?.DisplayName?.Trim() ?? string.Empty;
            if (state != OccupantPresenceState.Afk)
            {
                return displayName;
            }

            var shortWallet = ShortWallet(occupant?.WalletKey);
            return string.IsNullOrWhiteSpace(shortWallet)
                ? $"(AFK) {displayName}"
                : $"(AFK) {shortWallet}";
        }

        private static string ShortWallet(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return string.Empty;
            }

            var trimmed = walletKey.Trim();
            if (trimmed.Length <= 10)
            {
                return trimmed;
            }

            return $"{trimmed.Substring(0, 4)}...{trimmed.Substring(trimmed.Length - 4)}";
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
