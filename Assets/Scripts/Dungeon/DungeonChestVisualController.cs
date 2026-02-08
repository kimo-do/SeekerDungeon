using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonChestVisualController : MonoBehaviour
    {
        [SerializeField] private bool logDebugMessages;

        public void Apply(RoomView room)
        {
            if (!logDebugMessages || room == null)
            {
                return;
            }

            Debug.Log($"[DungeonChestVisual] LootedCount={room.LootedCount}");
        }
    }
}
