using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class DoorInteractable : MonoBehaviour
    {
        [SerializeField] private RoomDirection direction = RoomDirection.North;

        public RoomDirection Direction => direction;
    }
}
