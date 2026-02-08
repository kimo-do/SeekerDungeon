using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class DoorInteractable : MonoBehaviour
    {
        [SerializeField] private RoomDirection direction = RoomDirection.North;
        [SerializeField] private Transform interactPoint;

        public RoomDirection Direction => direction;
        public Vector3 InteractWorldPosition => interactPoint != null ? interactPoint.position : transform.position;
    }
}
