using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Spawns an ambient rat in some rooms. Chance is deterministic by room coords
    /// so repeated snapshot refreshes in the same room do not reroll.
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
        private readonly System.Collections.Generic.List<GameObject> _spawnedRats = new();

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
            if (ratPrefab == null || firstRatSpawnChance <= 0f)
            {
                return;
            }

            var firstRoll = (ComputeRoomHash(roomX, roomY, randomSalt) % 10000U) / 10000f;
            if (firstRoll > firstRatSpawnChance)
            {
                return;
            }

            SpawnRat(roomX, roomY, randomSalt + 101);

            if (secondRatSpawnChance <= 0f)
            {
                return;
            }

            var secondRoll = (ComputeRoomHash(roomX, roomY, randomSalt + 53) % 10000U) / 10000f;
            if (secondRoll <= secondRatSpawnChance)
            {
                SpawnRat(roomX, roomY, randomSalt + 211);
            }
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
    }
}
