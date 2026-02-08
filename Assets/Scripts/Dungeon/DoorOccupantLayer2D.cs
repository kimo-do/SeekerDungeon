using System.Collections.Generic;
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
        private const int MaxVisibleOccupants = 5;

        private readonly List<GameObject> _spawnedVisuals = new();

        public void SetVisualSpawnRoot(Transform spawnRoot)
        {
            visualSpawnRoot = spawnRoot;
        }

        public void SetOccupants(IReadOnlyList<DungeonOccupantVisual> occupants)
        {
            ClearVisuals();

            if (occupantVisualPrefab == null || visualSlots == null || visualSlots.Length == 0 || occupants == null)
            {
                return;
            }

            var visibleCount = Mathf.Min(MaxVisibleOccupants, visualSlots.Length, occupants.Count);
            for (var index = 0; index < visibleCount; index += 1)
            {
                var slot = visualSlots[index];
                if (slot == null || slot.Anchor == null)
                {
                    continue;
                }

                var visualObject = Instantiate(
                    occupantVisualPrefab,
                    slot.Anchor.position,
                    slot.Anchor.rotation,
                    visualSpawnRoot);
                visualObject.name = $"DoorOccupant_{occupants[index].DisplayName}_{index}";

                var visual = visualObject.GetComponent<DoorOccupantVisual2D>();
                if (visual != null)
                {
                    visual.Bind(occupants[index], index, slot.FacingDirection);
                }

                _spawnedVisuals.Add(visualObject);
            }

            // Hook for overflow visuals (e.g. +95 near door) can be added here later.
        }

        private void ClearVisuals()
        {
            for (var index = 0; index < _spawnedVisuals.Count; index += 1)
            {
                var visual = _spawnedVisuals[index];
                if (visual != null)
                {
                    Destroy(visual);
                }
            }

            _spawnedVisuals.Clear();
        }
    }

    public sealed class DoorOccupantVisual2D : MonoBehaviour
    {
        private const int BaseSortingOrder = 10;
        private LGPlayerController _playerController;
        private SpriteRenderer[] _spriteRenderers;

        private void Awake()
        {
            _playerController = GetComponent<LGPlayerController>();
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        public void Bind(DungeonOccupantVisual occupant, int stackIndex, OccupantFacingDirection facingDirection)
        {
            if (_playerController != null)
            {
                _playerController.ApplySkin(occupant.SkinId);
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

        private void ApplyFacing(OccupantFacingDirection facingDirection)
        {
            var localScale = transform.localScale;
            var absoluteX = Mathf.Abs(localScale.x);
            localScale.x = facingDirection == OccupantFacingDirection.Right
                ? absoluteX
                : -absoluteX;
            transform.localScale = localScale;
        }
    }
}
