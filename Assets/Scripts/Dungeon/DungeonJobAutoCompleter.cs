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

        [SerializeField] private float completeJobFailCooldownSeconds = 30f;
        [SerializeField] private int maxCompleteJobRetries = 3;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        private readonly Dictionary<byte, float> _nextAttemptAtByDirection = new();
        private readonly Dictionary<byte, int> _completeJobFailCount = new();
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

            // fireEvent: false avoids triggering snapshot rebuilds / pop-in / camera snaps
            var room = await lgManager.FetchRoomState(player.CurrentRoomX, player.CurrentRoomY, fireEvent: false);
            if (room == null)
            {
                return idlePollSeconds;
            }

            // ── Find active jobs by checking helper stakes (more reliable than player.ActiveJobs) ──
            // player.ActiveJobs can be empty due to deserialization/realloc issues, but helper stakes
            // are the on-chain source of truth.
            var activeDirections = new List<byte>();
            for (byte dir = 0; dir <= LGConfig.DIRECTION_WEST; dir++)
            {
                var directionIndex = (int)dir;
                if (directionIndex >= room.Walls.Length)
                {
                    continue;
                }

                var wallState = room.Walls[directionIndex];
                if (wallState != LGConfig.WALL_RUBBLE)
                {
                    continue;
                }

                var helperCount = directionIndex < room.HelperCounts.Length ? room.HelperCounts[directionIndex] : 0U;
                if (helperCount == 0)
                {
                    continue;
                }

                // Check if this player has a helper stake for this direction
                var hasStake = await lgManager.HasHelperStakeInCurrentRoom(dir);
                if (hasStake)
                {
                    activeDirections.Add(dir);
                }
            }

            if (activeDirections.Count == 0)
            {
                return idlePollSeconds;
            }

            var currentSlot = await FetchCurrentSlotAsync();
            var minDelaySeconds = maxRecheckSeconds;
            var selectedDirection = (byte)255;
            var selectedRemainingProgress = ulong.MaxValue;

            foreach (var direction in activeDirections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directionIndex = (int)direction;

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

            // Check if we've exceeded max retries for CompleteJob on this direction
            var failCount = _completeJobFailCount.TryGetValue(direction, out var fc) ? fc : 0;
            if (failCount >= maxCompleteJobRetries)
            {
                Log($"CompleteJob for {directionName} has failed {failCount} times, giving up until room changes");
                return;
            }

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
                // Wall is no longer rubble, reset fail count
                _completeJobFailCount.Remove(direction);
                return;
            }

            if (progress < required)
            {
                Log($"Auto-complete not ready yet: {directionName} progress={progress}/{required}");
                return;
            }

            // Use helper stake check (more reliable than player.ActiveJobs)
            var hasHelperStake = await lgManager.HasHelperStakeInCurrentRoom(direction);
            if (!hasHelperStake)
            {
                Log($"No helper stake found for {directionName}, skipping CompleteJob");
                return;
            }

            try
            {
                Log($"Calling CompleteJob for {directionName}...");
                await lgManager.CompleteJob(direction);
                // Success: reset fail count
                _completeJobFailCount.Remove(direction);
                Log($"CompleteJob succeeded for {directionName}");
            }
            catch (Exception ex)
            {
                failCount = (_completeJobFailCount.TryGetValue(direction, out var c) ? c : 0) + 1;
                _completeJobFailCount[direction] = failCount;
                SetNextAttemptAt(direction, Time.unscaledTime + completeJobFailCooldownSeconds);
                Log($"CompleteJob failed for {directionName} (attempt {failCount}/{maxCompleteJobRetries}): {ex.Message}");
            }
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
