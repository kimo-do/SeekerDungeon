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

        private bool _isOpen;
        private bool _hasRoomKey;
        private int _roomX;
        private int _roomY;
        private bool _optimisticOpenedForRoom;

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
                target.localScale = Vector3.zero;
                target.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack);
            }
        }
    }
}
