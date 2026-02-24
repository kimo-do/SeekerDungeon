using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeekerDungeon.Solana
{
    public sealed class ServerFeedHudQueue : MonoBehaviour
    {
        [SerializeField] private LGGameHudUI gameHudUi;
        [SerializeField] private float messageHoldSeconds = 2.5f;
        [SerializeField] private float messageGapSeconds = 0.35f;

        private readonly Queue<string> _messageQueue = new();
        private Coroutine _showRoutine;

        private void Awake()
        {
            if (gameHudUi == null)
            {
                gameHudUi = FindFirstObjectByType<LGGameHudUI>();
            }
        }

        public void EnqueueMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _messageQueue.Enqueue(message.Trim());
            if (_showRoutine == null)
            {
                _showRoutine = StartCoroutine(ShowQueueRoutine());
            }
        }

        private IEnumerator ShowQueueRoutine()
        {
            while (_messageQueue.Count > 0)
            {
                if (gameHudUi == null)
                {
                    gameHudUi = FindFirstObjectByType<LGGameHudUI>();
                }

                var nextMessage = _messageQueue.Dequeue();
                if (gameHudUi != null)
                {
                    gameHudUi.ShowServerFeedMessage(nextMessage, messageHoldSeconds);
                }

                yield return new WaitForSecondsRealtime(messageHoldSeconds + messageGapSeconds);
            }

            _showRoutine = null;
        }
    }
}
