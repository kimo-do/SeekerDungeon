using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using SeekerDungeon.Solana;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonJobAutoCompleter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LGManager lgManager;

        [Header("Scheduler")]
        [SerializeField] private bool autoRun = true;
        [SerializeField] private float idlePollSeconds = 6f;
        [SerializeField] private float minRecheckSeconds = 0.75f;
        [SerializeField] private float maxRecheckSeconds = 8f;
        [SerializeField] private float playerStateRefreshSeconds = 20f;
        [SerializeField] private float slotSecondsEstimate = 0.4f;
        [SerializeField] private ulong readyBufferSlots = 1UL;
        [SerializeField] private float txAttemptCooldownSeconds = 2f;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        private readonly Dictionary<byte, float> _nextAttemptAtByDirection = new();
        private CancellationTokenSource _loopCancellationTokenSource;
        private float _nextPlayerRefreshAt;

        private void Awake()
        {
            if (lgManager == null)
            {
                lgManager = LGManager.Instance;
            }

            if (lgManager == null)
            {
                lgManager = UnityEngine.Object.FindFirstObjectByType<LGManager>();
            }
        }

        private void OnEnable()
        {
            if (!autoRun)
            {
                return;
            }

            StartLoop();
        }

        private void OnDisable()
        {
            StopLoop();
        }

        public void StartLoop()
        {
            if (_loopCancellationTokenSource != null)
            {
                return;
            }

            _loopCancellationTokenSource = new CancellationTokenSource();
            RunLoopAsync(_loopCancellationTokenSource.Token).Forget();
        }

        public void StopLoop()
        {
            if (_loopCancellationTokenSource == null)
            {
                return;
            }

            _loopCancellationTokenSource.Cancel();
            _loopCancellationTokenSource.Dispose();
            _loopCancellationTokenSource = null;
        }

        private async UniTaskVoid RunLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var delaySeconds = await ProcessStepAsync(cancellationToken);
                    var clampedDelay = Mathf.Clamp(delaySeconds, minRecheckSeconds, maxRecheckSeconds);
                    await UniTask.Delay(TimeSpan.FromSeconds(clampedDelay), cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception error)
                {
                    Log($"Auto-complete step failed: {error.Message}");
                    await UniTask.Delay(TimeSpan.FromSeconds(idlePollSeconds), cancellationToken: cancellationToken);
                }
            }
        }

        private async UniTask<float> ProcessStepAsync(CancellationToken cancellationToken)
        {
            if (lgManager == null || Web3.Wallet?.Account == null)
            {
                return idlePollSeconds;
            }

            if (lgManager.CurrentGlobalState == null)
            {
                await lgManager.FetchGlobalState();
            }

            var player = await GetPlayerStateAsync();
            if (player == null)
            {
                return idlePollSeconds;
            }

            var activeJobsInCurrentRoom = (player.ActiveJobs ?? Array.Empty<Chaindepth.Types.ActiveJob>())
                .Where(job =>
                    job != null &&
                    job.RoomX == player.CurrentRoomX &&
                    job.RoomY == player.CurrentRoomY &&
                    job.Direction <= LGConfig.DIRECTION_WEST)
                .ToArray();

            if (activeJobsInCurrentRoom.Length == 0)
            {
                return idlePollSeconds;
            }

            var room = await lgManager.FetchRoomState(player.CurrentRoomX, player.CurrentRoomY);
            if (room == null)
            {
                return idlePollSeconds;
            }

            var currentSlot = await FetchCurrentSlotAsync();
            var minDelaySeconds = maxRecheckSeconds;
            var selectedDirection = (byte)255;
            var selectedRemainingProgress = ulong.MaxValue;

            foreach (var job in activeJobsInCurrentRoom)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var direction = job.Direction;
                var directionIndex = (int)direction;
                if (directionIndex < 0 || directionIndex >= room.Walls.Length)
                {
                    continue;
                }

                var wallState = room.Walls[directionIndex];
                if (wallState != LGConfig.WALL_RUBBLE)
                {
                    continue;
                }

                var helperCount = directionIndex < room.HelperCounts.Length ? room.HelperCounts[directionIndex] : 0U;
                var startSlot = directionIndex < room.StartSlot.Length ? room.StartSlot[directionIndex] : 0UL;
                var requiredProgress = directionIndex < room.BaseSlots.Length ? room.BaseSlots[directionIndex] : 0UL;
                if (helperCount == 0 || requiredProgress == 0 || startSlot == 0)
                {
                    continue;
                }

                var elapsedSlots = currentSlot > startSlot ? currentSlot - startSlot : 0UL;
                var effectiveProgress = elapsedSlots * helperCount;
                var remainingProgress = requiredProgress > effectiveProgress ? requiredProgress - effectiveProgress : 0UL;
                var isReadySoon = remainingProgress <= readyBufferSlots;

                if (!isReadySoon)
                {
                    var estimatedSeconds = remainingProgress * slotSecondsEstimate;
                    minDelaySeconds = Mathf.Min(minDelaySeconds, Mathf.Max(minRecheckSeconds, estimatedSeconds));
                    continue;
                }

                if (Time.unscaledTime < GetNextAttemptAt(direction))
                {
                    minDelaySeconds = Mathf.Min(minDelaySeconds, txAttemptCooldownSeconds);
                    continue;
                }

                if (remainingProgress < selectedRemainingProgress)
                {
                    selectedRemainingProgress = remainingProgress;
                    selectedDirection = direction;
                }
            }

            if (selectedDirection <= LGConfig.DIRECTION_WEST)
            {
                SetNextAttemptAt(selectedDirection, Time.unscaledTime + txAttemptCooldownSeconds);
                await TryTickAndCompleteJobAsync(selectedDirection);
                minDelaySeconds = Mathf.Min(minDelaySeconds, minRecheckSeconds);
            }

            return minDelaySeconds;
        }

        private async UniTask<Chaindepth.Accounts.PlayerAccount> GetPlayerStateAsync()
        {
            if (lgManager.CurrentPlayerState != null && Time.unscaledTime < _nextPlayerRefreshAt)
            {
                return lgManager.CurrentPlayerState;
            }

            var refreshedPlayer = await lgManager.FetchPlayerState();
            _nextPlayerRefreshAt = Time.unscaledTime + playerStateRefreshSeconds;
            return refreshedPlayer;
        }

        private async UniTask<ulong> FetchCurrentSlotAsync()
        {
            var rpc = Web3.Wallet?.ActiveRpcClient;
            if (rpc == null)
            {
                return 0UL;
            }

            var slotResult = await rpc.GetSlotAsync(Commitment.Confirmed);
            if (!slotResult.WasSuccessful || slotResult.Result == null)
            {
                return 0UL;
            }

            return slotResult.Result;
        }

        private async UniTask TryTickAndCompleteJobAsync(byte direction)
        {
            var directionName = LGConfig.GetDirectionName(direction);
            Log($"Auto-complete candidate: {directionName}");

            await lgManager.TickJob(direction);

            var room = lgManager.CurrentRoomState;
            var player = lgManager.CurrentPlayerState;
            if (room == null || player == null)
            {
                return;
            }

            var directionIndex = (int)direction;
            if (directionIndex < 0 || directionIndex >= room.Walls.Length)
            {
                return;
            }

            var wallState = room.Walls[directionIndex];
            var progress = directionIndex < room.Progress.Length ? room.Progress[directionIndex] : 0UL;
            var required = directionIndex < room.BaseSlots.Length ? room.BaseSlots[directionIndex] : 0UL;
            if (wallState != LGConfig.WALL_RUBBLE)
            {
                return;
            }

            if (progress < required)
            {
                Log($"Auto-complete not ready yet: {directionName} progress={progress}/{required}");
                return;
            }

            if (!lgManager.HasActiveJobInCurrentRoom(direction))
            {
                return;
            }

            await lgManager.CompleteJob(direction);
        }

        private float GetNextAttemptAt(byte direction)
        {
            return _nextAttemptAtByDirection.TryGetValue(direction, out var nextAttemptAt)
                ? nextAttemptAt
                : 0f;
        }

        private void SetNextAttemptAt(byte direction, float nextAttemptAt)
        {
            _nextAttemptAtByDirection[direction] = nextAttemptAt;
        }

        private void Log(string message)
        {
            if (!logDebugMessages)
            {
                return;
            }

            Debug.Log($"[JobAutoCompleter] {message}");
        }
    }
}
