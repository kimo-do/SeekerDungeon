using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class CenterInteractable : MonoBehaviour
    {
        [SerializeField] private Transform interactPoint;

        public Vector3 InteractWorldPosition => interactPoint != null ? interactPoint.position : transform.position;
    }
}
