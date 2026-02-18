using SeekerDungeon.Solana;
using UnityEngine;
using System.Collections.Generic;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Spawns ambient rats with per-visit random chance.
    /// Dead rat positions are tracked in-session per room and replayed on revisit.
    /// </summary>
    public sealed class RoomAmbientRatSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject ratPrefab;
        [SerializeField] private Transform spawnAnchor;
        [SerializeField] private Transform spawnedParent;
        [SerializeField] private Vector2 spawnAreaSize = new(2.7f, 2.7f);
        [Range(0f, 1f)]
        [SerializeField] private float firstRatSpawnChance = 0.25f;
        [Range(0f, 1f)]
        [SerializeField] private float secondRatSpawnChance = 0.08f;
        [SerializeField] private int randomSalt = 731;

        private int _lastRoomX = int.MinValue;
        private int _lastRoomY = int.MinValue;
        private readonly List<GameObject> _spawnedRats = new();
        private readonly Dictionary<(int x, int y), List<Vector3>> _deadRatPositionsByRoom = new();

        private void OnDisable()
        {
            DespawnRats();
            _lastRoomX = int.MinValue;
            _lastRoomY = int.MinValue;
        }

        public void ApplyRoom(RoomView room)
        {
            if (room == null)
            {
                return;
            }

            var roomX = room.X;
            var roomY = room.Y;
            if (_lastRoomX == roomX && _lastRoomY == roomY)
            {
                return;
            }

            _lastRoomX = roomX;
            _lastRoomY = roomY;

            DespawnRats();
            if (ratPrefab == null)
            {
                return;
            }

            if (firstRatSpawnChance > 0f)
            {
                var firstRoll = UnityEngine.Random.value;
                if (firstRoll <= firstRatSpawnChance)
                {
                    SpawnRat(roomX, roomY, randomSalt + 101);

                    if (secondRatSpawnChance > 0f)
                    {
                        var secondRoll = UnityEngine.Random.value;
                        if (secondRoll <= secondRatSpawnChance)
                        {
                            SpawnRat(roomX, roomY, randomSalt + 211);
                        }
                    }
                }
            }

            SpawnPersistedDeadRats(roomX, roomY);
        }

        private Vector3 BuildSpawnPosition(int roomX, int roomY, uint hash)
        {
            var anchor = spawnAnchor != null ? spawnAnchor.position : transform.position;
            var halfX = Mathf.Max(0.01f, spawnAreaSize.x * 0.5f);
            var halfY = Mathf.Max(0.01f, spawnAreaSize.y * 0.5f);

            var xHash = ComputeRoomHash(roomX, roomY, randomSalt + 17);
            var yHash = ComputeRoomHash(roomX, roomY, randomSalt + 29);
            var offsetX = Mathf.Lerp(-halfX, halfX, (xHash % 10000U) / 10000f);
            var offsetY = Mathf.Lerp(-halfY, halfY, (yHash % 10000U) / 10000f);

            return new Vector3(anchor.x + offsetX, anchor.y + offsetY, anchor.z);
        }

        private void SpawnRat(int roomX, int roomY, int salt)
        {
            var spawnPos = BuildSpawnPosition(roomX, roomY, ComputeRoomHash(roomX, roomY, salt));
            SpawnRatAtPosition(spawnPos, spawnDead: false);
        }

        private void SpawnPersistedDeadRats(int roomX, int roomY)
        {
            var roomKey = (roomX, roomY);
            if (!_deadRatPositionsByRoom.TryGetValue(roomKey, out var positions) || positions == null || positions.Count == 0)
            {
                return;
            }

            for (var index = 0; index < positions.Count; index += 1)
            {
                SpawnRatAtPosition(positions[index], spawnDead: true);
            }
        }

        private void SpawnRatAtPosition(Vector3 spawnPos, bool spawnDead)
        {
            var rat = Instantiate(
                ratPrefab,
                spawnPos,
                ratPrefab.transform.rotation,
                spawnedParent);
            _spawnedRats.Add(rat);

            var wander = rat.GetComponent<RatWander2D>();
            if (wander != null)
            {
                wander.SetRoamArea(spawnPos, spawnAreaSize);
                if (spawnDead)
                {
                    wander.SetDead(false);
                }
                else
                {
                    wander.Killed += HandleRatKilled;
                }
            }
        }

        private void HandleRatKilled(RatWander2D rat, Vector3 worldPosition)
        {
            var roomKey = (_lastRoomX, _lastRoomY);
            if (!_deadRatPositionsByRoom.TryGetValue(roomKey, out var positions) || positions == null)
            {
                positions = new List<Vector3>();
                _deadRatPositionsByRoom[roomKey] = positions;
            }

            if (!ContainsPosition(positions, worldPosition))
            {
                positions.Add(worldPosition);
            }

            if (rat != null)
            {
                rat.Killed -= HandleRatKilled;
            }
        }

        private void DespawnRats()
        {
            if (_spawnedRats.Count == 0)
            {
                return;
            }

            for (var index = 0; index < _spawnedRats.Count; index += 1)
            {
                var rat = _spawnedRats[index];
                if (rat != null)
                {
                    var wander = rat.GetComponent<RatWander2D>();
                    if (wander != null)
                    {
                        wander.Killed -= HandleRatKilled;
                    }
                    Destroy(rat);
                }
            }

            _spawnedRats.Clear();
        }

        private static uint ComputeRoomHash(int roomX, int roomY, int salt)
        {
            unchecked
            {
                uint hash = 2166136261U;
                hash = (hash ^ (uint)(roomX + 256)) * 16777619U;
                hash = (hash ^ (uint)(roomY + 256)) * 16777619U;
                hash = (hash ^ (uint)salt) * 16777619U;
                return hash;
            }
        }

        private static bool ContainsPosition(List<Vector3> positions, Vector3 candidate)
        {
            const float epsilonSq = 0.0004f;
            for (var index = 0; index < positions.Count; index += 1)
            {
                if ((positions[index] - candidate).sqrMagnitude <= epsilonSq)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
