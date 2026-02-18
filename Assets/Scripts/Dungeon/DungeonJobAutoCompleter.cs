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
        [SerializeField] private DungeonManager dungeonManager;

        [Header("Scheduler")]
        [SerializeField] private bool autoRun = true;
        [SerializeField] private float idlePollSeconds = 6f;
        [SerializeField] private float minRecheckSeconds = 0.75f;
        [SerializeField] private float maxRecheckSeconds = 8f;
        [SerializeField] private float playerStateRefreshSeconds = 20f;
        [SerializeField] private float slotSecondsEstimate = 0.4f;
        [SerializeField] private ulong readyBufferSlots = 1UL;
        [SerializeField] private float txAttemptCooldownSeconds = 2f;
        [Header("Boss Auto Tick")]
        [SerializeField] private bool autoTickBossFight = true;
        [SerializeField] private float bossTickIntervalSeconds = 2.2f;
        [SerializeField] private float bossTickRetryCooldownSeconds = 1.2f;
        [SerializeField] private float bossTickRateLimitBaseCooldownSeconds = 3f;
        [SerializeField] private float bossTickRateLimitMaxCooldownSeconds = 18f;
        [SerializeField] private int bossPropagationMaxAttempts = 4;
        [SerializeField] private int bossPropagationDelayMs = 220;

        [SerializeField] private float completeJobFailCooldownSeconds = 30f;
        [SerializeField] private int maxCompleteJobRetries = 3;
        [SerializeField] private float confirmTxPollSeconds = 1.5f;
        [SerializeField] private int confirmTxMaxPolls = 10;
        [SerializeField] private int maxClaimRetries = 3;
        [SerializeField] private float claimRetryDelaySeconds = 2f;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        private readonly Dictionary<byte, float> _nextAttemptAtByDirection = new();
        private readonly Dictionary<byte, int> _completeJobFailCount = new();
        private CancellationTokenSource _loopCancellationTokenSource;
        private float _nextPlayerRefreshAt;
        private float _nextBossTickAt;
        private float _nextBossFighterCheckAt;
        private bool _cachedIsLocalBossFighter;
        private int _bossTickRateLimitFailureCount;

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

            if (dungeonManager == null)
            {
                dungeonManager = UnityEngine.Object.FindFirstObjectByType<DungeonManager>();
            }
        }

        private void OnEnable()
        {
            // Startup is now coordinated by DungeonManager.InitializeAsync
            // to avoid racing with the initial room fetch and job finalization.
            // DungeonManager calls StartLoop() after init completes.
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

            // Boss auto-tick loop for smoother HP/death updates without repeated clicks.
            var bossTickDelay = await ProcessBossAutoTickAsync(room, cancellationToken);
            if (bossTickDelay >= 0f)
            {
                return bossTickDelay;
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

        private async UniTask<float> ProcessBossAutoTickAsync(
            Chaindepth.Accounts.RoomAccount room,
            CancellationToken cancellationToken)
        {
            if (!autoTickBossFight || lgManager == null || room == null)
            {
                return -1f;
            }

            if (room.CenterType != LGConfig.CENTER_BOSS || room.BossDefeated)
            {
                _cachedIsLocalBossFighter = false;
                return -1f;
            }

            var isLocalFighter = dungeonManager != null && dungeonManager.IsLocalPlayerFightingBoss;
            if (!isLocalFighter)
            {
                var nowForFighterCheck = Time.unscaledTime;
                if (nowForFighterCheck >= _nextBossFighterCheckAt)
                {
                    _cachedIsLocalBossFighter = await ResolveLocalBossFighterFromPdaAsync(room);
                    _nextBossFighterCheckAt = nowForFighterCheck + 2f;
                }

                if (!_cachedIsLocalBossFighter)
                {
                    return -1f;
                }
            }

            var now = Time.unscaledTime;
            if (now < _nextBossTickAt)
            {
                return Mathf.Max(minRecheckSeconds, _nextBossTickAt - now);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var hpBeforeTick = room.BossCurrentHp;
            var tickResult = await lgManager.TickBossFight();
            if (!tickResult.Success)
            {
                if (IsBossAlreadyDefeatedError(tickResult.Error))
                {
                    _cachedIsLocalBossFighter = false;
                    if (dungeonManager != null)
                    {
                        dungeonManager.ClearOptimisticBossFight();
                        await dungeonManager.RefreshCurrentRoomSnapshotAsync();
                    }

                    return Mathf.Max(minRecheckSeconds, 0.25f);
                }

                if (IsRpcRateLimitError(tickResult.Error))
                {
                    _bossTickRateLimitFailureCount = Mathf.Clamp(_bossTickRateLimitFailureCount + 1, 1, 8);
                    var backoffSeconds = Mathf.Min(
                        bossTickRateLimitMaxCooldownSeconds,
                        bossTickRateLimitBaseCooldownSeconds * Mathf.Pow(1.8f, _bossTickRateLimitFailureCount - 1));
                    var jitter = UnityEngine.Random.Range(0.05f, 0.45f);
                    _nextBossTickAt = now + backoffSeconds + jitter;
                    GameplayActionLog.AutoComplete("CenterBoss", "TickBossFight", false, tickResult.Error);
                    Log(
                        $"Boss tick rate-limited. failureCount={_bossTickRateLimitFailureCount} " +
                        $"cooldown={(_nextBossTickAt - now):F2}s");
                    return Mathf.Max(minRecheckSeconds, _nextBossTickAt - now);
                }

                _nextBossTickAt = now + Mathf.Max(0.2f, bossTickRetryCooldownSeconds);
                GameplayActionLog.AutoComplete("CenterBoss", "TickBossFight", false, tickResult.Error);
                return Mathf.Max(minRecheckSeconds, bossTickRetryCooldownSeconds);
            }

            _bossTickRateLimitFailureCount = 0;
            GameplayActionLog.AutoComplete("CenterBoss", "TickBossFight", true, tickResult.Signature);
            _nextBossTickAt = now + Mathf.Max(0.2f, bossTickIntervalSeconds);
            await WaitForBossStatePropagationAsync(room, hpBeforeTick, cancellationToken);

            if (dungeonManager != null)
            {
                await dungeonManager.RefreshCurrentRoomSnapshotAsync();
            }

            return Mathf.Max(minRecheckSeconds, bossTickIntervalSeconds);
        }

        private async UniTask WaitForBossStatePropagationAsync(
            Chaindepth.Accounts.RoomAccount room,
            ulong hpBeforeTick,
            CancellationToken cancellationToken)
        {
            if (lgManager == null || room == null || room.CenterType != LGConfig.CENTER_BOSS || room.BossDefeated)
            {
                return;
            }

            var maxAttempts = Mathf.Max(1, bossPropagationMaxAttempts);
            var delayMs = Mathf.Max(80, bossPropagationDelayMs);

            for (var attempt = 0; attempt < maxAttempts; attempt += 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var refreshedRoom = await lgManager.FetchRoomState(room.X, room.Y, fireEvent: false);
                if (refreshedRoom != null)
                {
                    if (refreshedRoom.CenterType != LGConfig.CENTER_BOSS ||
                        refreshedRoom.BossDefeated ||
                        refreshedRoom.BossCurrentHp < hpBeforeTick)
                    {
                        return;
                    }
                }

                await UniTask.Delay(delayMs, cancellationToken: cancellationToken);
            }
        }

        private static bool IsRpcRateLimitError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return
                error.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("Too Many Requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBossAlreadyDefeatedError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return error.IndexOf("BossAlreadyDefeated", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async UniTask<bool> ResolveLocalBossFighterFromPdaAsync(Chaindepth.Accounts.RoomAccount room)
        {
            if (lgManager?.CurrentGlobalState == null || room == null)
            {
                return false;
            }

            var playerPubkey = Web3.Wallet?.Account?.PublicKey;
            if (playerPubkey == null)
            {
                return false;
            }

            try
            {
                var roomPda = lgManager.DeriveRoomPda(
                    lgManager.CurrentGlobalState.SeasonSeed,
                    room.X,
                    room.Y);
                if (roomPda == null)
                {
                    return false;
                }

                return await lgManager.HasBossFightInCurrentRoom(roomPda, playerPubkey);
            }
            catch (Exception error)
            {
                Log($"Boss fighter PDA check failed: {error.Message}");
                return false;
            }
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

        /// <summary>
        /// Polls the RPC until the given transaction signature reaches Confirmed
        /// commitment, or until the poll limit is exceeded.
        /// </summary>
        private async UniTask<bool> WaitForTxConfirmationAsync(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            var rpc = Web3.Wallet?.ActiveRpcClient;
            if (rpc == null)
            {
                return false;
            }

            for (var poll = 1; poll <= confirmTxMaxPolls; poll++)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(confirmTxPollSeconds));

                try
                {
                    var confirmed = await rpc.ConfirmTransaction(signature, Commitment.Confirmed);
                    if (confirmed)
                    {
                        Log($"TX confirmed after {poll} poll(s): {signature.Substring(0, 16)}...");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Log($"ConfirmTransaction poll {poll} error: {e.Message}");
                }
            }

            Log($"TX confirmation timed out after {confirmTxMaxPolls} polls: {signature.Substring(0, 16)}...");
            return false;
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

            // ── Step 1: Complete (auto-ticks + frees job slot on-chain) ──
            Log($"Calling CompleteJob for {directionName}...");
            var completeResult = await lgManager.CompleteJob(direction);
            GameplayActionLog.AutoComplete(directionName, "CompleteJob", completeResult.Success,
                completeResult.Success ? completeResult.Signature : completeResult.Error);
            if (!completeResult.Success)
            {
                failCount = (_completeJobFailCount.TryGetValue(direction, out var cc) ? cc : 0) + 1;
                _completeJobFailCount[direction] = failCount;
                SetNextAttemptAt(direction, Time.unscaledTime + completeJobFailCooldownSeconds);
                Log($"CompleteJob TX failed for {directionName} (attempt {failCount}/{maxCompleteJobRetries})");
                return;
            }

            _completeJobFailCount.Remove(direction);
            Log($"CompleteJob succeeded for {directionName}");

            // Wait for CompleteJob TX to be confirmed on-chain before
            // proceeding. Without this, ClaimJobReward's preflight reads
            // stale state and fails with JobNotCompleted, and the snapshot
            // refresh still shows rubble.
            var confirmed = await WaitForTxConfirmationAsync(completeResult.Signature);
            if (!confirmed)
            {
                Log($"CompleteJob TX confirmation timed out for {directionName}. " +
                    "Will retry claim next cycle.");
            }

            // ── Step 2: Claim reward (token payout + HelperStake closure) ──
            // Retry a few times because the wall is no longer Rubble after
            // CompleteJob, so the normal polling loop will skip this direction.
            var claimSucceeded = false;
            for (var attempt = 1; attempt <= maxClaimRetries; attempt++)
            {
                Log($"Calling ClaimJobReward for {directionName} (attempt {attempt}/{maxClaimRetries})...");
                var claimResult = await lgManager.ClaimJobReward(direction);
                GameplayActionLog.AutoComplete(directionName, "ClaimJobReward", claimResult.Success,
                    claimResult.Success ? claimResult.Signature : claimResult.Error);
                if (claimResult.Success)
                {
                    Log($"ClaimJobReward succeeded for {directionName}");
                    claimSucceeded = true;
                    break;
                }

                Log($"ClaimJobReward TX failed for {directionName} (attempt {attempt}/{maxClaimRetries})");
                if (attempt < maxClaimRetries)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(claimRetryDelaySeconds));
                }
            }

            if (!claimSucceeded)
            {
                Log($"ClaimJobReward exhausted retries for {directionName}. " +
                    "Player may need to claim manually.");
            }

            // Push one clean snapshot that reflects the actual post-TX state.
            if (dungeonManager != null)
            {
                await dungeonManager.RefreshCurrentRoomSnapshotAsync();
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
