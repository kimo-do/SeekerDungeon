using DG.Tweening;
using SeekerDungeon.Solana;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonChestVisualController : MonoBehaviour
    {
        [SerializeField] private bool logDebugMessages;
        [SerializeField] private GameObject closedVisual;
        [SerializeField] private GameObject openVisual;
        [SerializeField] private float openPopScaleMultiplier = 1.08f;
        [SerializeField] private float openPopDurationSeconds = 0.2f;

        private bool _isOpen;
        private bool _hasRoomKey;
        private int _roomX;
        private int _roomY;
        private bool _optimisticOpenedForRoom;
        private Vector3 _openVisualBaseScale = Vector3.one;

        private void Awake()
        {
            if (openVisual != null)
            {
                _openVisualBaseScale = openVisual.transform.localScale;
            }
        }

        public void Apply(RoomView room)
        {
            if (room == null)
            {
                return;
            }

            var roomChanged = !_hasRoomKey || _roomX != room.X || _roomY != room.Y;
            if (roomChanged)
            {
                _hasRoomKey = true;
                _roomX = room.X;
                _roomY = room.Y;
                _optimisticOpenedForRoom = false;
            }

            if (logDebugMessages)
            {
                Debug.Log($"[DungeonChestVisual] LootedCount={room.LootedCount} HasLocalPlayerLooted={room.HasLocalPlayerLooted}");
            }

            var shouldBeOpen = room.HasLocalPlayerLooted || _optimisticOpenedForRoom;
            SetOpen(shouldBeOpen, animate: false);
        }

        /// <summary>
        /// Immediately flip to the open state with a punch animation.
        /// Called after a successful loot transaction.
        /// </summary>
        public void PlayOpenAnimation()
        {
            _optimisticOpenedForRoom = true;

            if (_isOpen)
            {
                return;
            }

            SetOpen(true, animate: true);
        }

        private void SetOpen(bool open, bool animate)
        {
            _isOpen = open;

            if (closedVisual != null)
            {
                closedVisual.SetActive(!open);
            }

            if (openVisual != null)
            {
                openVisual.SetActive(open);
            }

            if (animate && open && openVisual != null)
            {
                var target = openVisual.transform;
                target.DOKill();
                target.localScale = _openVisualBaseScale;
                target
                    .DOPunchScale(_openVisualBaseScale * (openPopScaleMultiplier - 1f), openPopDurationSeconds, 1, 0f)
                    .SetEase(Ease.OutQuad);
            }
            else if (open && openVisual != null)
            {
                openVisual.transform.localScale = _openVisualBaseScale;
            }
        }
    }
}
