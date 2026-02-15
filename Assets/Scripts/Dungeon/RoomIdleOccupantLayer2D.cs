using System;
using System.Collections.Generic;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class RoomIdleOccupantLayer2D : MonoBehaviour
    {
        [SerializeField] private List<BoxCollider2D> spawnZones = new();
        [SerializeField] private GameObject occupantVisualPrefab;
        [SerializeField] private Transform visualSpawnRoot;
        [SerializeField] private float minDistanceBetweenOccupants = 0.1f;
        [SerializeField] private int maxSpawnAttemptsPerOccupant = 18;
        [SerializeField] private float spawnPopDuration = 0.1f;
        [SerializeField] private float spawnPopStartScaleMultiplier = 0.82f;
        [SerializeField] private float spawnStaggerSeconds = 0.05f;

        private readonly Dictionary<string, DoorOccupantVisual2D> _activeByOccupantKey = new();
        private readonly Queue<DoorOccupantVisual2D> _pooledVisuals = new();
        private readonly List<string> _releaseBuffer = new();
        private readonly HashSet<string> _usedKeys = new(StringComparer.Ordinal);
        private readonly List<Vector2> _reservedPositions = new();
        private bool _suppressSpawnPop;

        public void SetVisualSpawnRoot(Transform spawnRoot)
        {
            visualSpawnRoot = spawnRoot;
        }

        public void SetSuppressSpawnPop(bool suppress)
        {
            _suppressSpawnPop = suppress;
        }

        public bool TryGetLocalPlayerSpawnPosition(out Vector3 worldPosition)
        {
            worldPosition = default;

            if (spawnZones == null || spawnZones.Count == 0)
            {
                return false;
            }

            var reservedPositions = new List<Vector2>();
            foreach (var visual in _activeByOccupantKey.Values)
            {
                if (visual == null)
                {
                    continue;
                }

                reservedPositions.Add(new Vector2(visual.transform.position.x, visual.transform.position.y));
            }

            if (!TryFindSpawnPosition(reservedPositions, out var spawnPosition))
            {
                return false;
            }

            worldPosition = new Vector3(spawnPosition.x, spawnPosition.y, 0f);
            return true;
        }

        public void SetOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            if (occupantVisualPrefab == null || spawnZones == null || spawnZones.Count == 0 || occupants == null)
            {
                ReleaseAllActiveVisuals();
                return;
            }

            _usedKeys.Clear();
            _reservedPositions.Clear();
            var newVisualIndex = 0;
            foreach (var visual in _activeByOccupantKey.Values)
            {
                if (visual == null)
                {
                    continue;
                }

                _reservedPositions.Add(new Vector2(visual.transform.position.x, visual.transform.position.y));
            }

            for (var index = 0; index < occupants.Count; index += 1)
            {
                var occupant = occupants[index];
                var key = ResolveOccupantKey(occupant, index);
                if (!_usedKeys.Add(key))
                {
                    key = $"{key}#{index}";
                    _usedKeys.Add(key);
                }

                var isNewVisual = !_activeByOccupantKey.TryGetValue(key, out var visual) || visual == null;
                if (isNewVisual)
                {
                    if (!TryFindSpawnPosition(_reservedPositions, out var spawnPosition))
                    {
                        continue;
                    }

                    visual = GetOrCreateVisual();
                    if (visual == null)
                    {
                        continue;
                    }

                    _activeByOccupantKey[key] = visual;
                    var visualTransform = visual.transform;
                    var parent = visualSpawnRoot != null ? visualSpawnRoot : null;
                    if (visualTransform.parent != parent)
                    {
                        visualTransform.SetParent(parent, true);
                    }

                    visualTransform.SetPositionAndRotation(
                        new Vector3(spawnPosition.x, spawnPosition.y, 0f),
                        Quaternion.identity);
                    _reservedPositions.Add(spawnPosition);

                    var shouldPlaySpawnPop = !_suppressSpawnPop &&
                                             !OccupantSpawnPopTracker.HasSeen(key);
                    if (shouldPlaySpawnPop)
                    {
                        var spawnDelay = newVisualIndex * spawnStaggerSeconds;
                        visual.PlaySpawnPop(spawnPopDuration, spawnPopStartScaleMultiplier, spawnDelay);
                    }
                    newVisualIndex += 1;
                }

                visual.transform.rotation = Quaternion.identity;
                visual.Bind(occupant, 0, OccupantFacingDirection.Right);
                OccupantSpawnPopTracker.MarkSeen(key);
            }

            ReleaseUnusedVisuals(_usedKeys);
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

            return visualObject.AddComponent<DoorOccupantVisual2D>();
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
            _usedKeys.Clear();
            _reservedPositions.Clear();
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

        private bool TryFindSpawnPosition(IReadOnlyList<Vector2> reservedPositions, out Vector2 spawnPosition)
        {
            spawnPosition = default;

            var availableZones = GetAvailableZones();
            if (availableZones.Count == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < maxSpawnAttemptsPerOccupant; attempt += 1)
            {
                var zone = availableZones[UnityEngine.Random.Range(0, availableZones.Count)];
                var bounds = zone.bounds;
                var candidate = new Vector2(
                    UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                    UnityEngine.Random.Range(bounds.min.y, bounds.max.y));

                if (!zone.OverlapPoint(candidate))
                {
                    continue;
                }

                if (!HasEnoughDistance(candidate, reservedPositions))
                {
                    continue;
                }

                spawnPosition = candidate;
                return true;
            }

            return false;
        }

        private List<BoxCollider2D> GetAvailableZones()
        {
            var zones = new List<BoxCollider2D>(spawnZones.Count);
            for (var i = 0; i < spawnZones.Count; i += 1)
            {
                var zone = spawnZones[i];
                if (zone == null || !zone.gameObject.activeInHierarchy || !zone.enabled)
                {
                    continue;
                }

                zones.Add(zone);
            }

            return zones;
        }

        private bool HasEnoughDistance(Vector2 candidate, IReadOnlyList<Vector2> reservedPositions)
        {
            for (var i = 0; i < reservedPositions.Count; i += 1)
            {
                if (Vector2.Distance(candidate, reservedPositions[i]) < minDistanceBetweenOccupants)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ResolveOccupantKey(DungeonOccupantVisual occupant, int index)
        {
            if (!string.IsNullOrWhiteSpace(occupant.WalletKey))
            {
                return occupant.WalletKey;
            }

            return $"{occupant.DisplayName}_{index}";
        }
    }
}
