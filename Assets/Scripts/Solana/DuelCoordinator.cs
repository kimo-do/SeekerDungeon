using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using SeekerDungeon.Audio;
using SeekerDungeon.Dungeon;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;
using Unity.Cinemachine;
using Random = UnityEngine.Random;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Orchestrates duel UX flow between occupant taps, HUD actions, and LGManager duel APIs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuelCoordinator : MonoBehaviour
    {
        private const float EstimatedSecondsPerSlot = 0.4f;
        private const string SpectatorReplaySeenPrefsKey = "LG_DUEL_SPECTATOR_SEEN_V1";

        private static DuelCoordinator _activeInstance;

        [SerializeField] private LGManager manager;
        [SerializeField] private LGGameHudUI hud;
        [SerializeField] private ServerFeedClient serverFeedClient;
        [SerializeField] private DungeonInputController dungeonInputController;
        [SerializeField] private DungeonManager dungeonManager;
        [SerializeField] private float normalPollSeconds = 8f;
        [SerializeField] private float pendingPollSeconds = 3f;
        [SerializeField] private float duelReplayHitIntervalSeconds = 1.35f;
        [SerializeField] private float duelReplayPostKillSeconds = 2f;
        [SerializeField] private bool useDuelSwingEventSync = true;
        [SerializeField] private float duelSwingSyncTimeoutSeconds = 1.35f;
        [SerializeField] private float duelSwingSyncMinimumImpactDelaySeconds = 0.35f;
        [SerializeField] private float duelReplaySpacingUnits = 0.8f;
        [SerializeField] private Vector2 duelReplayBoundsX = new(-2.2f, 2.2f);
        [SerializeField] private Vector2 duelReplayBoundsY = new(-2.2f, 2.2f);
        [SerializeField] private float duelReplayResolveTimeoutSeconds = 3f;
        [SerializeField] private float duelReplayResolvePollSeconds = 0.2f;
        [SerializeField] private float duelBattleMusicFadeOutSeconds = 0.65f;
        [Header("Spectator Duel Replay")]
        [SerializeField] private bool spectatorReplayEnabled = true;
        [SerializeField] private float spectatorReplaySpeedMultiplier = 1.2f;
        [SerializeField] private bool spectatorReplayShowToast = true;
        [SerializeField] private float spectatorReplayCooldownSeconds = 7f;
        [SerializeField] private float spectatorReplayMaxAgeSeconds = 300f;
        [SerializeField] private float spectatorReplaySeenRetentionSeconds = 86400f;
        [SerializeField] private bool duelReplayDebugLogs;
        [Header("Duel Camera")]
        [SerializeField] private CinemachineCamera gameplayCamera;
        [SerializeField] private CinemachineCamera duelReplayCamera;
        [SerializeField] private int duelReplayCameraPriority = 100;
        [SerializeField] private float duelReplayOrthoSize = 3.44f;
        [Header("Duel Hit Splat")]
        [SerializeField] private GameObject duelHitSplatPrefab;
        [SerializeField] private List<Sprite> duelHitSplatSprites = new();
        [SerializeField] private float duelHitSplatYOffset = 0.4f;
        [SerializeField] private float duelHitSplatRemoveLeadSeconds = 0.4f;
        [Header("Duel Crit Visuals")]
        [SerializeField] private bool inferCriticalHitsFromDamage = true;
        [SerializeField] private byte inferredCriticalDamageThreshold = 16;
        [SerializeField] private float criticalHitSplatScaleMultiplier = 1.3f;

        private readonly List<DuelChallengeView> _cachedChallenges = new();
        private readonly Dictionary<string, DuelChallengeStatus> _knownStatuses = new();
        private readonly HashSet<string> _settledReplayStartedByPda = new(StringComparer.Ordinal);
        private readonly HashSet<string> _spectatorReplayTransitionsSeen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _spectatorReplayCooldownUntilByPda = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _spectatorReplaySessionExpiryByPda = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _spectatorReplaySeenAtUnixByTransition = new(StringComparer.Ordinal);
        private readonly Queue<SpectatorReplayRequest> _spectatorReplayQueue = new();
        private static float _lastCreateRequestRealtime;
        private static string _lastCreateRequestFingerprint = string.Empty;
        private bool _hasBaselineSnapshot;
        private bool _isActionInFlight;
        private bool _isRefreshInFlight;
        private bool _isReplayInProgress;
        private bool _isSpectatorReplayLoopRunning;
        private float _lastSlotRefreshRealtime;
        private ulong? _latestKnownSlot;
        private int _localSwingEventsObserved;
        private int _remoteSwingEventsObserved;
        private LGPlayerController _activeReplayLocalPlayer;
        private LGPlayerController _activeReplayRemotePlayer;

        private readonly struct SpectatorReplayRequest
        {
            public SpectatorReplayRequest(DuelChallengeView challenge, sbyte roomX, sbyte roomY)
            {
                Challenge = challenge;
                RoomX = roomX;
                RoomY = roomY;
            }

            public DuelChallengeView Challenge { get; }
            public sbyte RoomX { get; }
            public sbyte RoomY { get; }
        }

        [Serializable]
        private sealed class SpectatorReplaySeenStore
        {
            public List<SpectatorReplaySeenEntry> Entries = new();
        }

        [Serializable]
        private sealed class SpectatorReplaySeenEntry
        {
            public string TransitionKey;
            public long SeenAtUnix;
        }

        private void Awake()
        {
            if (_activeInstance != null && _activeInstance != this)
            {
                Debug.LogWarning("[DuelCoordinator] Duplicate coordinator detected. Destroying duplicate instance.");
                Destroy(this);
                return;
            }

            _activeInstance = this;

            if (manager == null)
            {
                manager = LGManager.Instance ?? FindFirstObjectByType<LGManager>();
            }

            if (hud == null)
            {
                hud = GetComponent<LGGameHudUI>() ?? FindFirstObjectByType<LGGameHudUI>();
            }

            if (dungeonInputController == null)
            {
                dungeonInputController = FindFirstObjectByType<DungeonInputController>();
            }

            if (dungeonManager == null)
            {
                dungeonManager = FindFirstObjectByType<DungeonManager>();
            }

            if (serverFeedClient == null)
            {
                serverFeedClient = ServerFeedClient.Instance ?? FindFirstObjectByType<ServerFeedClient>();
            }

            gameplayCamera ??= ResolveGameplayCamera();
            LoadSpectatorReplaySeenStore();
        }

        private void OnDestroy()
        {
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
        }

        private void OnEnable()
        {
            if (manager == null || hud == null)
            {
                return;
            }

            manager.OnDuelChallengesUpdated += HandleDuelChallengesUpdated;
            hud.OnDuelChallengeRequested += HandleDuelChallengeRequested;
            hud.OnDuelAcceptRequested += HandleDuelAcceptRequested;
            hud.OnDuelDeclineRequested += HandleDuelDeclineRequested;
            hud.OnDuelClaimExpiredRequested += HandleDuelClaimExpiredRequested;

            if (dungeonInputController == null)
            {
                dungeonInputController = FindFirstObjectByType<DungeonInputController>();
            }

            if (dungeonInputController != null)
            {
                dungeonInputController.OnRemoteOccupantTapped += HandleRemoteOccupantTapped;
            }

            PollLoopAsync(this.GetCancellationTokenOnDestroy()).Forget();
            ForceRefreshAsync().Forget();
        }

        private void OnDisable()
        {
            if (manager != null)
            {
                manager.OnDuelChallengesUpdated -= HandleDuelChallengesUpdated;
            }

            if (hud != null)
            {
                hud.OnDuelChallengeRequested -= HandleDuelChallengeRequested;
                hud.OnDuelAcceptRequested -= HandleDuelAcceptRequested;
                hud.OnDuelDeclineRequested -= HandleDuelDeclineRequested;
                hud.OnDuelClaimExpiredRequested -= HandleDuelClaimExpiredRequested;
            }

            if (dungeonInputController != null)
            {
                dungeonInputController.OnRemoteOccupantTapped -= HandleRemoteOccupantTapped;
            }
        }

        private async UniTaskVoid PollLoopAsync(System.Threading.CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (dungeonInputController == null)
                    {
                        dungeonInputController = FindFirstObjectByType<DungeonInputController>();
                        if (dungeonInputController != null)
                        {
                            dungeonInputController.OnRemoteOccupantTapped += HandleRemoteOccupantTapped;
                        }
                    }

                    var delay = HasPendingRandomnessForLocal() ? pendingPollSeconds : normalPollSeconds;
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.5f, delay)), cancellationToken: cancellationToken);
                    await ForceRefreshAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    hud?.ShowCenterToast($"Duel refresh failed: {exception.Message}", 1.5f);
                }
            }
        }

        private bool HasPendingRandomnessForLocal()
        {
            var localWallet = Web3.Wallet?.Account?.PublicKey;
            if (localWallet == null)
            {
                return false;
            }

            for (var i = 0; i < _cachedChallenges.Count; i += 1)
            {
                var challenge = _cachedChallenges[i];
                if (challenge == null)
                {
                    continue;
                }

                var isRelevant = challenge.IsIncomingFor(localWallet) || challenge.IsOutgoingFrom(localWallet);
                if (isRelevant && challenge.Status == DuelChallengeStatus.PendingRandomness)
                {
                    return true;
                }
            }

            return false;
        }

        private async UniTask ForceRefreshAsync()
        {
            if (manager == null)
            {
                return;
            }

            if (_isRefreshInFlight)
            {
                return;
            }

            _isRefreshInFlight = true;
            try
            {
                if (!_latestKnownSlot.HasValue || Time.realtimeSinceStartup - _lastSlotRefreshRealtime >= 10f)
                {
                    _latestKnownSlot = await manager.GetCurrentSlotAsync();
                    _lastSlotRefreshRealtime = Time.realtimeSinceStartup;
                }

                await manager.FetchRelevantDuelChallengesAsync();
                await RefreshSpectatorReplayCandidatesAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DuelCoordinator] Duel refresh failed: {exception.Message}");
            }
            finally
            {
                _isRefreshInFlight = false;
            }
        }

        private void HandleRemoteOccupantTapped(string walletKey, string displayName)
        {
            if (hud == null || string.IsNullOrWhiteSpace(walletKey))
            {
                return;
            }

            try
            {
                var wallet = new PublicKey(walletKey.Trim());
                hud.OpenDuelChallengeForTarget(walletKey, displayName, wallet);
            }
            catch (Exception)
            {
                hud.ShowCenterToast("Invalid target wallet for duel.", 1.5f);
            }
        }

        private void HandleDuelChallengesUpdated(IReadOnlyList<DuelChallengeView> challenges)
        {
            _cachedChallenges.Clear();
            if (challenges != null)
            {
                _cachedChallenges.AddRange(challenges.Where(item => item != null));
            }

            var localWallet = Web3.Wallet?.Account?.PublicKey;
            if (hud != null)
            {
                hud.UpdateDuelInbox(_cachedChallenges, localWallet, _latestKnownSlot);
            }

            if (localWallet == null)
            {
                return;
            }

            for (var i = 0; i < _cachedChallenges.Count; i += 1)
            {
                var challenge = _cachedChallenges[i];
                var pdaKey = challenge.Pda?.Key;
                if (string.IsNullOrWhiteSpace(pdaKey))
                {
                    continue;
                }

                var hadPreviousStatus = _knownStatuses.TryGetValue(pdaKey, out var previousStatus);
                _knownStatuses[pdaKey] = challenge.Status;

                if (!_hasBaselineSnapshot || !hadPreviousStatus)
                {
                    continue;
                }

                if (previousStatus == challenge.Status)
                {
                    continue;
                }

                if (!challenge.IsTerminal)
                {
                    continue;
                }

                EmitTerminalFeedback(localWallet, challenge);
            }

            if (!_hasBaselineSnapshot)
            {
                _hasBaselineSnapshot = true;
            }
        }

        private async UniTask RefreshSpectatorReplayCandidatesAsync()
        {
            if (!spectatorReplayEnabled || manager == null)
            {
                return;
            }

            var localWallet = Web3.Wallet?.Account?.PublicKey;
            var localPlayer = manager.CurrentPlayerState;
            if (localPlayer == null)
            {
                return;
            }

            PruneSpectatorCaches();
            var roomChallenges = await manager.FetchRoomRelevantDuelChallengesAsync(localPlayer.CurrentRoomX, localPlayer.CurrentRoomY);
            if (roomChallenges == null || roomChallenges.Count == 0)
            {
                return;
            }

            for (var index = 0; index < roomChallenges.Count; index += 1)
            {
                var challenge = roomChallenges[index];
                if (!ShouldQueueSpectatorReplay(
                        localWallet,
                        challenge,
                        localPlayer.CurrentRoomX,
                        localPlayer.CurrentRoomY,
                        _latestKnownSlot))
                {
                    continue;
                }

                _spectatorReplayQueue.Enqueue(new SpectatorReplayRequest(challenge, localPlayer.CurrentRoomX, localPlayer.CurrentRoomY));
                LogReplayDebug(
                    $"spectator replay queued pda={challenge.Pda?.Key} status={challenge.Status} room=({challenge.RoomX},{challenge.RoomY})");
            }

            if (_spectatorReplayQueue.Count > 0)
            {
                StartSpectatorReplayLoop();
            }
        }

        private bool ShouldQueueSpectatorReplay(
            PublicKey localWallet,
            DuelChallengeView challenge,
            sbyte roomX,
            sbyte roomY,
            ulong? currentSlot)
        {
            if (challenge == null || challenge.Pda == null)
            {
                return false;
            }

            if (challenge.RoomX != roomX || challenge.RoomY != roomY)
            {
                return false;
            }

            if (challenge.Status != DuelChallengeStatus.PendingRandomness &&
                challenge.Status != DuelChallengeStatus.Settled)
            {
                return false;
            }

            if (!IsChallengeRecentEnoughForSpectatorReplay(challenge, currentSlot))
            {
                return false;
            }

            if (localWallet != null &&
                (string.Equals(challenge.Challenger?.Key, localWallet.Key, StringComparison.Ordinal) ||
                 string.Equals(challenge.Opponent?.Key, localWallet.Key, StringComparison.Ordinal)))
            {
                return false;
            }

            var challengePdaKey = challenge.Pda.Key;
            var transitionKey = BuildReplayTransitionKey(challenge);
            if (_spectatorReplayTransitionsSeen.Contains(transitionKey))
            {
                return false;
            }

            if (_spectatorReplaySeenAtUnixByTransition.ContainsKey(transitionKey))
            {
                return false;
            }

            if (_spectatorReplaySessionExpiryByPda.TryGetValue(challengePdaKey, out var activeUntil) &&
                Time.realtimeSinceStartup < activeUntil)
            {
                return false;
            }

            if (_spectatorReplayCooldownUntilByPda.TryGetValue(challengePdaKey, out var cooldownUntil) &&
                Time.realtimeSinceStartup < cooldownUntil)
            {
                return false;
            }

            if (!CanReplayTranscript(challenge))
            {
                return false;
            }

            if (!AreReplayActorsCurrentlyAvailable(challenge.Challenger, challenge.Opponent))
            {
                return false;
            }

            _spectatorReplayTransitionsSeen.Add(transitionKey);
            _spectatorReplaySessionExpiryByPda[challengePdaKey] = Time.realtimeSinceStartup + 20f;
            return true;
        }

        private static bool CanReplayTranscript(DuelChallengeView challenge)
        {
            if (challenge == null)
            {
                return false;
            }

            if ((challenge.ChallengerHits?.Count ?? 0) > 0 || (challenge.OpponentHits?.Count ?? 0) > 0)
            {
                return true;
            }

            return challenge.TurnsPlayed > 0;
        }

        private bool AreReplayActorsCurrentlyAvailable(PublicKey firstWallet, PublicKey secondWallet)
        {
            return ResolveRemotePlayerController(firstWallet) != null &&
                   ResolveRemotePlayerController(secondWallet) != null;
        }

        private void StartSpectatorReplayLoop()
        {
            if (_isSpectatorReplayLoopRunning)
            {
                return;
            }

            ProcessSpectatorReplayQueueAsync().Forget();
        }

        private async UniTaskVoid ProcessSpectatorReplayQueueAsync()
        {
            _isSpectatorReplayLoopRunning = true;
            try
            {
                while (_spectatorReplayQueue.Count > 0)
                {
                    if (_isReplayInProgress)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(0.15f));
                        continue;
                    }

                    var request = _spectatorReplayQueue.Dequeue();
                    var challenge = request.Challenge;
                    if (challenge?.Pda == null)
                    {
                        continue;
                    }

                    var localPlayer = manager?.CurrentPlayerState;
                    if (localPlayer == null ||
                        localPlayer.CurrentRoomX != request.RoomX ||
                        localPlayer.CurrentRoomY != request.RoomY)
                    {
                        LogReplayDebug($"spectator replay skipped due to room change pda={challenge.Pda.Key}");
                        continue;
                    }

                    if (!AreReplayActorsCurrentlyAvailable(challenge.Challenger, challenge.Opponent))
                    {
                        LogReplayDebug($"spectator replay skipped due to missing actors pda={challenge.Pda.Key}");
                        continue;
                    }

                    var localWallet = Web3.Wallet?.Account?.PublicKey;
                    if (spectatorReplayShowToast && hud != null)
                    {
                        var left = GetReplayDisplayName(challenge.ChallengerDisplayNameSnapshot, challenge.Challenger);
                        var right = GetReplayDisplayName(challenge.OpponentDisplayNameSnapshot, challenge.Opponent);
                        hud.ShowCenterToast($"Duel: {left} vs {right}", 1.4f);
                    }

                    var replayPlayed = false;
                    try
                    {
                        replayPlayed = await PlaySettledDuelReplayAsync(localWallet, challenge, spectatorMode: true);
                    }
                    catch (Exception replayError)
                    {
                        Debug.LogWarning($"[DuelCoordinator] Spectator duel replay failed: {replayError.Message}");
                    }

                    var cooldownSeconds = Mathf.Max(1f, spectatorReplayCooldownSeconds);
                    _spectatorReplayCooldownUntilByPda[challenge.Pda.Key] = Time.realtimeSinceStartup + cooldownSeconds;
                    _spectatorReplaySessionExpiryByPda.Remove(challenge.Pda.Key);
                    if (replayPlayed)
                    {
                        MarkSpectatorReplaySeen(BuildReplayTransitionKey(challenge));
                    }

                    LogReplayDebug(
                        $"spectator replay {(replayPlayed ? "completed" : "skipped")} pda={challenge.Pda.Key} cooldown={cooldownSeconds:0.0}s");
                }
            }
            finally
            {
                _isSpectatorReplayLoopRunning = false;
            }
        }

        private static string BuildReplayTransitionKey(DuelChallengeView challenge)
        {
            var pda = challenge?.Pda?.Key ?? string.Empty;
            var status = challenge?.Status ?? DuelChallengeStatus.Unknown;
            var settledSlot = challenge?.SettledSlot ?? 0UL;
            return $"{pda}:{(byte)status}:{settledSlot}";
        }

        private bool IsChallengeRecentEnoughForSpectatorReplay(DuelChallengeView challenge, ulong? currentSlot)
        {
            var maxAgeSeconds = Mathf.Max(0f, spectatorReplayMaxAgeSeconds);
            if (maxAgeSeconds <= 0f || !currentSlot.HasValue)
            {
                return true;
            }

            var referenceSlot = challenge.Status == DuelChallengeStatus.Settled && challenge.SettledSlot > 0UL
                ? challenge.SettledSlot
                : challenge.RequestedSlot;
            if (referenceSlot == 0UL || currentSlot.Value <= referenceSlot)
            {
                return true;
            }

            var maxAgeSlotsFloat = maxAgeSeconds / EstimatedSecondsPerSlot;
            var maxAgeSlots = maxAgeSlotsFloat <= 0f ? 0UL : (ulong)Mathf.CeilToInt(maxAgeSlotsFloat);
            return currentSlot.Value - referenceSlot <= maxAgeSlots;
        }

        private static string GetReplayDisplayName(string snapshotDisplayName, PublicKey wallet)
        {
            if (!string.IsNullOrWhiteSpace(snapshotDisplayName))
            {
                return snapshotDisplayName.Trim();
            }

            if (string.IsNullOrWhiteSpace(wallet?.Key) || wallet.Key.Length < 10)
            {
                return "Unknown";
            }

            return $"{wallet.Key.Substring(0, 4)}...{wallet.Key.Substring(wallet.Key.Length - 4)}";
        }

        private void PruneSpectatorCaches()
        {
            if (_spectatorReplaySessionExpiryByPda.Count > 0)
            {
                var now = Time.realtimeSinceStartup;
                var expiredKeys = new List<string>();
                foreach (var kvp in _spectatorReplaySessionExpiryByPda)
                {
                    if (now >= kvp.Value)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                for (var i = 0; i < expiredKeys.Count; i += 1)
                {
                    _spectatorReplaySessionExpiryByPda.Remove(expiredKeys[i]);
                }
            }

            if (_spectatorReplayCooldownUntilByPda.Count > 0)
            {
                var now = Time.realtimeSinceStartup;
                var expiredKeys = new List<string>();
                foreach (var kvp in _spectatorReplayCooldownUntilByPda)
                {
                    if (now >= kvp.Value)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                for (var i = 0; i < expiredKeys.Count; i += 1)
                {
                    _spectatorReplayCooldownUntilByPda.Remove(expiredKeys[i]);
                }
            }

            PrunePersistedSpectatorReplaySeenStore();
        }

        private void LoadSpectatorReplaySeenStore()
        {
            _spectatorReplaySeenAtUnixByTransition.Clear();
            if (!PlayerPrefs.HasKey(SpectatorReplaySeenPrefsKey))
            {
                return;
            }

            var raw = PlayerPrefs.GetString(SpectatorReplaySeenPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            try
            {
                var store = JsonUtility.FromJson<SpectatorReplaySeenStore>(raw);
                var entries = store?.Entries;
                if (entries == null || entries.Count == 0)
                {
                    return;
                }

                for (var i = 0; i < entries.Count; i += 1)
                {
                    var entry = entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.TransitionKey) || entry.SeenAtUnix <= 0L)
                    {
                        continue;
                    }

                    _spectatorReplaySeenAtUnixByTransition[entry.TransitionKey] = entry.SeenAtUnix;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DuelCoordinator] Failed to load spectator replay seen store: {exception.Message}");
            }

            PrunePersistedSpectatorReplaySeenStore();
        }

        private void MarkSpectatorReplaySeen(string transitionKey)
        {
            if (string.IsNullOrWhiteSpace(transitionKey))
            {
                return;
            }

            _spectatorReplaySeenAtUnixByTransition[transitionKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SaveSpectatorReplaySeenStore();
        }

        private void PrunePersistedSpectatorReplaySeenStore()
        {
            if (_spectatorReplaySeenAtUnixByTransition.Count == 0)
            {
                return;
            }

            var retentionSeconds = Mathf.Max(
                Mathf.Max(60f, spectatorReplaySeenRetentionSeconds),
                Mathf.Max(60f, spectatorReplayMaxAgeSeconds) * 2f);
            var cutoffUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)Mathf.CeilToInt(retentionSeconds);
            var keysToRemove = new List<string>();
            foreach (var kvp in _spectatorReplaySeenAtUnixByTransition)
            {
                if (kvp.Value < cutoffUnix)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            if (keysToRemove.Count == 0)
            {
                return;
            }

            for (var i = 0; i < keysToRemove.Count; i += 1)
            {
                _spectatorReplaySeenAtUnixByTransition.Remove(keysToRemove[i]);
            }

            SaveSpectatorReplaySeenStore();
        }

        private void SaveSpectatorReplaySeenStore()
        {
            try
            {
                var store = new SpectatorReplaySeenStore();
                foreach (var kvp in _spectatorReplaySeenAtUnixByTransition)
                {
                    store.Entries.Add(new SpectatorReplaySeenEntry
                    {
                        TransitionKey = kvp.Key,
                        SeenAtUnix = kvp.Value
                    });
                }

                var raw = JsonUtility.ToJson(store);
                PlayerPrefs.SetString(SpectatorReplaySeenPrefsKey, raw);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DuelCoordinator] Failed to save spectator replay seen store: {exception.Message}");
            }
        }

        private void LogReplayDebug(string message)
        {
            if (!duelReplayDebugLogs)
            {
                return;
            }

            Debug.Log($"[DuelCoordinator] {message}");
        }

        private void EmitTerminalFeedback(PublicKey localWallet, DuelChallengeView challenge)
        {
            if (hud == null || manager == null)
            {
                return;
            }

            if (challenge.Status == DuelChallengeStatus.Settled)
            {
                var duelPda = challenge.Pda?.Key;
                if (!string.IsNullOrWhiteSpace(duelPda) && _settledReplayStartedByPda.Contains(duelPda))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(duelPda))
                {
                    _settledReplayStartedByPda.Add(duelPda);
                }

                PlaySettledDuelReplayAsync(localWallet, challenge, spectatorMode: false).Forget();
                return;
            }

            string message;
            if (challenge.Status == DuelChallengeStatus.Declined)
            {
                message = "Duel request was declined.";
            }
            else if (challenge.Status == DuelChallengeStatus.Expired)
            {
                message = "Duel request expired.";
            }
            else if (challenge.IsDraw)
            {
                message = "Duel ended in a draw. Stakes refunded.";
            }
            else if (string.Equals(challenge.Winner?.Key, localWallet.Key, StringComparison.Ordinal))
            {
                message = "You won the duel.";
            }
            else
            {
                message = "You lost the duel.";
            }

            hud.ShowCenterToast(message, 1.8f);
            Debug.Log(manager.FormatDuelTranscriptForLog(challenge));
        }

        private async UniTask<bool> PlaySettledDuelReplayAsync(PublicKey localWallet, DuelChallengeView challenge, bool spectatorMode)
        {
            _isReplayInProgress = true;
            try
            {
                LogReplayDebug(
                    $"replay started spectator={spectatorMode} pda={challenge?.Pda?.Key} status={challenge?.Status}");
                if (!spectatorMode)
                {
                    hud?.PrepareForDuelReplay();
                }

                var audioManager = spectatorMode ? null : GameAudioManager.Instance;
                if (audioManager != null)
                {
                    audioManager.PlayLoopOnce(AudioLoopId.DuelBattle, Vector3.zero);
                }

                var replayPlayed = false;
                const int replayAttempts = 2;

                for (var attempt = 0; attempt < replayAttempts && !replayPlayed; attempt += 1)
                {
                    if (attempt > 0)
                    {
                        await ForceReplayContextRefreshAsync();
                        await UniTask.Delay(TimeSpan.FromSeconds(0.35f));
                    }

                    try
                    {
                        replayPlayed = await RunDuelReplayAsync(localWallet, challenge, spectatorMode);
                    }
                    catch (Exception replayError)
                    {
                        Debug.LogWarning($"[DuelCoordinator] Duel replay failed: {replayError.Message}");
                    }
                }

                if (!replayPlayed)
                {
                    LogReplayDebug($"replay fallback spectator={spectatorMode} pda={challenge?.Pda?.Key}");
                }

                if (audioManager != null && audioManager.IsLoopPlaying(AudioLoopId.DuelBattle))
                {
                    await audioManager.FadeOutLoopAndStopAsync(
                        AudioLoopId.DuelBattle,
                        Mathf.Max(0.01f, duelBattleMusicFadeOutSeconds));
                }

                if (!spectatorMode)
                {
                    ShowDuelResultModal(localWallet, challenge);
                }

                if (manager != null)
                {
                    Debug.Log(manager.FormatDuelTranscriptForLog(challenge));
                }

                return replayPlayed;
            }
            finally
            {
                _isReplayInProgress = false;
            }
        }

        private async UniTask<bool> RunDuelReplayAsync(PublicKey localWallet, DuelChallengeView challenge, bool spectatorMode)
        {
            var spawnedHitSplats = new List<GameObject>();
            var replayActors = await ResolveReplayActorsAsync(localWallet, challenge, spectatorMode);
            var challengerPlayer = replayActors.ChallengerPlayer;
            var opponentPlayer = replayActors.OpponentPlayer;
            if (challengerPlayer == null || opponentPlayer == null)
            {
                Debug.LogWarning(
                    $"[DuelCoordinator] Replay skipped: challenger={challengerPlayer != null} opponent={opponentPlayer != null}");
                return false;
            }

            var challengerWalletKey = challenge.Challenger?.Key;
            var opponentWalletKey = challenge.Opponent?.Key;
            var replayAborted = false;

            Transform duelCenterAnchor = null;
            var cameraActivated = false;
            if (!spectatorMode)
            {
                ActivateDuelReplayCamera(challengerPlayer.transform);
                cameraActivated = true;
            }

            DuelVisualLockRegistry.Lock(challengerWalletKey);
            DuelVisualLockRegistry.Lock(opponentWalletKey);
            LogReplayDebug($"lock challengers pda={challenge.Pda?.Key} challenger={challengerWalletKey} opponent={opponentWalletKey}");
            try
            {
                var challengerStartPosition = challengerPlayer.transform.position;
                var opponentStartPosition = opponentPlayer.transform.position;
                var duelChallengerX = Mathf.Clamp(challengerStartPosition.x, duelReplayBoundsX.x, duelReplayBoundsX.y - duelReplaySpacingUnits);
                var duelChallengerY = Mathf.Clamp(challengerStartPosition.y, duelReplayBoundsY.x, duelReplayBoundsY.y);
                var duelOpponentX = Mathf.Clamp(duelChallengerX + duelReplaySpacingUnits, duelReplayBoundsX.x, duelReplayBoundsX.y);
                var duelOpponentY = duelChallengerY;
                var challengerUsedFallbackWeapon = challengerPlayer.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);
                var opponentUsedFallbackWeapon = opponentPlayer.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);

                challengerPlayer.transform.position = new Vector3(duelChallengerX, duelChallengerY, challengerStartPosition.z);
                opponentPlayer.transform.position = new Vector3(duelOpponentX, duelOpponentY, opponentStartPosition.z);
                challengerPlayer.SetFacingDirection(OccupantFacingDirection.Right);
                opponentPlayer.SetFacingDirection(OccupantFacingDirection.Left);
                if (!spectatorMode)
                {
                    duelCenterAnchor = new GameObject("DuelCameraCenterAnchor").transform;
                    duelCenterAnchor.position = ComputeDuelCenterPosition(challengerPlayer.transform.position, opponentPlayer.transform.position);
                    if (duelReplayCamera != null)
                    {
                        duelReplayCamera.Follow = duelCenterAnchor;
                        duelReplayCamera.LookAt = duelCenterAnchor;
                    }
                }

                const ushort maxHp = 100;
                var challengerHp = (int)maxHp;
                var opponentHp = (int)maxHp;
                var combatAnimationStopped = false;
                challengerPlayer.SetCombatHealth(maxHp, maxHp, true);
                opponentPlayer.SetCombatHealth(maxHp, maxHp, true);
                _localSwingEventsObserved = 0;
                _remoteSwingEventsObserved = 0;
                _activeReplayLocalPlayer = challengerPlayer;
                _activeReplayRemotePlayer = opponentPlayer;
                DuelSwingEventRelay.OnSwing += HandleDuelSwingEvent;
                challengerPlayer.SetBossJobAnimationState(false);
                opponentPlayer.SetBossJobAnimationState(false);
                await UniTask.Yield();

                var totalTurns = Math.Max(
                    challenge.TurnsPlayed,
                    (byte)Math.Max(challenge.ChallengerHits?.Count ?? 0, challenge.OpponentHits?.Count ?? 0));
                var activeAttackHitSplats = new List<HitSplatView>(2);
                var challengerStartsRound = challenge.Starter != DuelStarter.Opponent;
                for (var turnIndex = 0; turnIndex < totalTurns; turnIndex += 1)
                {
                    if (spectatorMode &&
                        (!AreReplayActorsCurrentlyAvailable(challenge.Challenger, challenge.Opponent) ||
                         manager?.CurrentPlayerState == null ||
                         manager.CurrentPlayerState.CurrentRoomX != challenge.RoomX ||
                         manager.CurrentPlayerState.CurrentRoomY != challenge.RoomY))
                    {
                        replayAborted = true;
                        break;
                    }

                    var turnIntervalSeconds = ScaleReplayDuration(Mathf.Max(0.2f, duelReplayHitIntervalSeconds), spectatorMode);
                    var attackStepSeconds = Mathf.Max(0.12f, turnIntervalSeconds * 0.5f);

                    var challengerDamage = turnIndex < (challenge.ChallengerHits?.Count ?? 0)
                        ? challenge.ChallengerHits[turnIndex]
                        : (byte)0;
                    var opponentDamage = turnIndex < (challenge.OpponentHits?.Count ?? 0)
                        ? challenge.OpponentHits[turnIndex]
                        : (byte)0;

                    var firstAttacker = challengerStartsRound ? challengerPlayer : opponentPlayer;
                    var secondAttacker = challengerStartsRound ? opponentPlayer : challengerPlayer;
                    var firstIsChallenger = challengerStartsRound;
                    var firstDamage = firstIsChallenger ? challengerDamage : opponentDamage;
                    var secondDamage = firstIsChallenger ? opponentDamage : challengerDamage;
                    var firstTarget = firstIsChallenger ? opponentPlayer : challengerPlayer;
                    var secondTarget = firstIsChallenger ? challengerPlayer : opponentPlayer;

                    firstAttacker.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);
                    firstTarget.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);
                    var firstSwingBaseline = GetObservedSwingCountForActor(firstAttacker);
                    firstAttacker.TriggerBossAttackOnce();
                    await WaitForAttackImpactWindowAsync(
                        activeAttackHitSplats,
                        attackStepSeconds,
                        firstAttacker,
                        firstSwingBaseline);

                    activeAttackHitSplats.Clear();
                    var firstHitSplat = SpawnHitSplat(
                        firstTarget,
                        firstDamage,
                        spawnedHitSplats,
                        IsCriticalHit(firstDamage));
                    if (firstHitSplat != null)
                    {
                        activeAttackHitSplats.Add(firstHitSplat);
                    }

                    if (firstIsChallenger)
                    {
                        opponentHp = Math.Max(0, opponentHp - firstDamage);
                    }
                    else
                    {
                        challengerHp = Math.Max(0, challengerHp - firstDamage);
                    }

                    challengerPlayer.SetCombatHealth((ushort)challengerHp, maxHp, true);
                    opponentPlayer.SetCombatHealth((ushort)opponentHp, maxHp, true);

                    secondAttacker.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);
                    secondTarget.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);
                    var secondSwingBaseline = GetObservedSwingCountForActor(secondAttacker);
                    secondAttacker.TriggerBossAttackOnce();
                    await WaitForAttackImpactWindowAsync(
                        activeAttackHitSplats,
                        attackStepSeconds,
                        secondAttacker,
                        secondSwingBaseline);

                    activeAttackHitSplats.Clear();
                    var secondHitSplat = SpawnHitSplat(
                        secondTarget,
                        secondDamage,
                        spawnedHitSplats,
                        IsCriticalHit(secondDamage));
                    if (secondHitSplat != null)
                    {
                        activeAttackHitSplats.Add(secondHitSplat);
                    }

                    if (firstIsChallenger)
                    {
                        challengerHp = Math.Max(0, challengerHp - secondDamage);
                    }
                    else
                    {
                        opponentHp = Math.Max(0, opponentHp - secondDamage);
                    }

                    challengerPlayer.SetCombatHealth((ushort)challengerHp, maxHp, true);
                    opponentPlayer.SetCombatHealth((ushort)opponentHp, maxHp, true);

                    if (challengerHp == 0 || opponentHp == 0)
                    {
                        challengerPlayer.SetBossJobAnimationState(false);
                        opponentPlayer.SetBossJobAnimationState(false);
                        combatAnimationStopped = true;
                        break;
                    }
                }

                if (!replayAborted)
                {
                    challengerPlayer.SetCombatHealth(challenge.ChallengerFinalHp, maxHp, true);
                    opponentPlayer.SetCombatHealth(challenge.OpponentFinalHp, maxHp, true);

                    if (challenge.ChallengerFinalHp == 0 || challenge.OpponentFinalHp == 0)
                    {
                        challengerPlayer.SetBossJobAnimationState(false);
                        opponentPlayer.SetBossJobAnimationState(false);
                        combatAnimationStopped = true;
                    }

                    await UniTask.Delay(TimeSpan.FromSeconds(ScaleReplayDuration(Mathf.Max(0f, duelReplayPostKillSeconds), spectatorMode)));
                }

                if (!combatAnimationStopped)
                {
                    challengerPlayer.SetBossJobAnimationState(false);
                    opponentPlayer.SetBossJobAnimationState(false);
                }
                challengerPlayer.SetCombatHealth(0, 0, false);
                opponentPlayer.SetCombatHealth(0, 0, false);
                challengerPlayer.transform.position = challengerStartPosition;
                opponentPlayer.transform.position = opponentStartPosition;
                if (challengerUsedFallbackWeapon)
                {
                    challengerPlayer.HideAllWieldedItems();
                }
                if (opponentUsedFallbackWeapon)
                {
                    opponentPlayer.HideAllWieldedItems();
                }
            }
            finally
            {
                DuelSwingEventRelay.OnSwing -= HandleDuelSwingEvent;
                _activeReplayLocalPlayer = null;
                _activeReplayRemotePlayer = null;
                DuelVisualLockRegistry.Unlock(challengerWalletKey);
                DuelVisualLockRegistry.Unlock(opponentWalletKey);
                LogReplayDebug($"unlock challengers pda={challenge.Pda?.Key}");
                if (duelCenterAnchor != null)
                {
                    Destroy(duelCenterAnchor.gameObject);
                }
                CleanupHitSplats(spawnedHitSplats);
                if (cameraActivated)
                {
                    RestoreDuelReplayCameraState();
                }
            }

            return !replayAborted;
        }

        private async UniTask<(LGPlayerController ChallengerPlayer, LGPlayerController OpponentPlayer)> ResolveReplayActorsAsync(
            PublicKey localWallet,
            DuelChallengeView challenge,
            bool spectatorMode)
        {
            var elapsed = 0f;
            var timeout = Mathf.Max(0.5f, duelReplayResolveTimeoutSeconds);
            var poll = Mathf.Max(0.05f, duelReplayResolvePollSeconds);
            var nextRefreshAt = 0f;

            while (elapsed <= timeout)
            {
                var challengerPlayer = ResolveReplayPlayerController(challenge?.Challenger, localWallet, spectatorMode);
                var opponentPlayer = ResolveReplayPlayerController(challenge?.Opponent, localWallet, spectatorMode);
                if (challengerPlayer != null && opponentPlayer != null)
                {
                    return (challengerPlayer, opponentPlayer);
                }

                if (elapsed >= nextRefreshAt)
                {
                    await ForceReplayContextRefreshAsync();
                    nextRefreshAt += 0.8f;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(poll));
                elapsed += poll;
            }

            return (
                ResolveReplayPlayerController(challenge?.Challenger, localWallet, spectatorMode),
                ResolveReplayPlayerController(challenge?.Opponent, localWallet, spectatorMode));
        }

        private async UniTask ForceReplayContextRefreshAsync()
        {
            try
            {
                if (dungeonManager == null)
                {
                    dungeonManager = FindFirstObjectByType<DungeonManager>();
                }

                if (dungeonManager != null)
                {
                    await dungeonManager.RefreshCurrentRoomSnapshotAsync();
                }
                else if (manager != null)
                {
                    await manager.RefreshAllState();
                }
            }
            catch (Exception refreshError)
            {
                Debug.LogWarning($"[DuelCoordinator] Replay context refresh failed: {refreshError.Message}");
            }
        }

        private HitSplatView SpawnHitSplat(
            LGPlayerController targetPlayer,
            byte damage,
            List<GameObject> spawnedHitSplats,
            bool isCritical)
        {
            if (duelHitSplatPrefab == null || targetPlayer == null)
            {
                return null;
            }
            if (damage > 0)
            {
                HapticsFeedback.DuelDamageTick();
            }

            var spawnPosition = targetPlayer.transform.position + new Vector3(0f, duelHitSplatYOffset, 0f);
            var instance = Instantiate(duelHitSplatPrefab, spawnPosition, Quaternion.identity);
            if (instance == null)
            {
                return null;
            }
            spawnedHitSplats?.Add(instance);

            var hitSplat = instance.GetComponent<HitSplatView>();
            if (hitSplat == null)
            {
                return null;
            }

            Sprite chosenSprite = null;
            if (duelHitSplatSprites != null && duelHitSplatSprites.Count > 0)
            {
                chosenSprite = duelHitSplatSprites[Random.Range(0, duelHitSplatSprites.Count)];
            }

            hitSplat.SetDamage(damage, chosenSprite);
            hitSplat.SetScaleMultiplier(isCritical ? criticalHitSplatScaleMultiplier : 1f);
            return hitSplat;
        }

        private static void TriggerRemoveOnHitSplats(List<HitSplatView> hitSplats)
        {
            if (hitSplats == null || hitSplats.Count == 0)
            {
                return;
            }

            for (var i = 0; i < hitSplats.Count; i += 1)
            {
                var hitSplat = hitSplats[i];
                if (hitSplat != null)
                {
                    hitSplat.TriggerRemove();
                }
            }
        }

        private async UniTask WaitForAttackImpactWindowAsync(
            List<HitSplatView> activeAttackHitSplats,
            float attackStepSeconds,
            LGPlayerController attacker,
            int attackerSwingBaseline)
        {
            var removeLeadSeconds = Mathf.Clamp(duelHitSplatRemoveLeadSeconds, 0f, attackStepSeconds);
            var removeAtSeconds = Mathf.Max(0f, attackStepSeconds - removeLeadSeconds);
            var minImpactDelay = Mathf.Clamp(duelSwingSyncMinimumImpactDelaySeconds, 0f, attackStepSeconds);
            var elapsed = 0f;
            var removeTriggered = false;
            var syncedBySwing = false;
            var attackerSwingSeen = false;
            var useSwingSync = useDuelSwingEventSync && attacker != null;
            var swingTimeout = Mathf.Clamp(duelSwingSyncTimeoutSeconds, 0.2f, attackStepSeconds);
            const float pollStep = 0.02f;

            while (elapsed < attackStepSeconds)
            {
                if (!removeTriggered && elapsed >= removeAtSeconds)
                {
                    TriggerRemoveOnHitSplats(activeAttackHitSplats);
                    removeTriggered = true;
                }

                if (useSwingSync && elapsed <= swingTimeout)
                {
                    if (GetObservedSwingCountForActor(attacker) > attackerSwingBaseline)
                    {
                        attackerSwingSeen = true;
                    }

                    if (attackerSwingSeen && elapsed >= minImpactDelay)
                    {
                        syncedBySwing = true;
                        break;
                    }
                }

                var remaining = attackStepSeconds - elapsed;
                if (remaining <= 0f)
                {
                    break;
                }

                var step = Mathf.Min(pollStep, remaining);
                await UniTask.Delay(TimeSpan.FromSeconds(step));
                elapsed += step;
            }

            if (!removeTriggered && removeLeadSeconds > 0f)
            {
                TriggerRemoveOnHitSplats(activeAttackHitSplats);
                await UniTask.Delay(TimeSpan.FromSeconds(removeLeadSeconds));
                return;
            }

            if (!syncedBySwing)
            {
                var remainingToInterval = attackStepSeconds - elapsed;
                if (remainingToInterval > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(remainingToInterval));
                }
            }
        }

        private int GetObservedSwingCountForActor(LGPlayerController actor)
        {
            if (actor == null)
            {
                return 0;
            }

            if (actor == _activeReplayLocalPlayer)
            {
                return _localSwingEventsObserved;
            }

            if (actor == _activeReplayRemotePlayer)
            {
                return _remoteSwingEventsObserved;
            }

            return 0;
        }

        private float ScaleReplayDuration(float seconds, bool spectatorMode)
        {
            if (!spectatorMode)
            {
                return seconds;
            }

            var speed = Mathf.Max(0.5f, spectatorReplaySpeedMultiplier);
            return seconds / speed;
        }

        private bool IsCriticalHit(byte damage)
        {
            return inferCriticalHitsFromDamage && damage >= inferredCriticalDamageThreshold;
        }

        private void HandleDuelSwingEvent(LGPlayerController owner)
        {
            if (owner == null)
            {
                return;
            }

            if (owner == _activeReplayLocalPlayer)
            {
                _localSwingEventsObserved += 1;
                return;
            }

            if (owner == _activeReplayRemotePlayer)
            {
                _remoteSwingEventsObserved += 1;
            }
        }

        private static Vector3 ComputeDuelCenterPosition(Vector3 localPosition, Vector3 remotePosition)
        {
            return (localPosition + remotePosition) * 0.5f;
        }

        private static void CleanupHitSplats(List<GameObject> spawnedHitSplats)
        {
            if (spawnedHitSplats == null || spawnedHitSplats.Count == 0)
            {
                return;
            }

            for (var i = 0; i < spawnedHitSplats.Count; i += 1)
            {
                var instance = spawnedHitSplats[i];
                if (instance != null)
                {
                    Destroy(instance);
                }
            }

            spawnedHitSplats.Clear();
        }

        private int _duelReplayCameraPreviousPriority;
        private int _gameplayCameraPreviousPriority;
        private bool _duelReplayCameraPriorityCaptured;
        private bool _gameplayCameraPriorityCaptured;
        private Transform _duelReplayCameraPreviousFollow;
        private Transform _duelReplayCameraPreviousLookAt;
        private bool _duelReplayCameraFollowCaptured;
        private bool _duelReplayCameraLookAtCaptured;
        private float _duelReplayCameraPreviousOrthoSize;
        private bool _duelReplayCameraOrthoCaptured;

        private void ActivateDuelReplayCamera(Transform followTarget)
        {
            if (duelReplayCamera == null)
            {
                return;
            }

            if (!_duelReplayCameraPriorityCaptured)
            {
                _duelReplayCameraPreviousPriority = duelReplayCamera.Priority.Value;
                _duelReplayCameraPriorityCaptured = true;
            }
            if (!_duelReplayCameraFollowCaptured)
            {
                _duelReplayCameraPreviousFollow = duelReplayCamera.Follow;
                _duelReplayCameraFollowCaptured = true;
            }
            if (!_duelReplayCameraLookAtCaptured)
            {
                _duelReplayCameraPreviousLookAt = duelReplayCamera.LookAt;
                _duelReplayCameraLookAtCaptured = true;
            }
            if (!_duelReplayCameraOrthoCaptured)
            {
                _duelReplayCameraPreviousOrthoSize = duelReplayCamera.Lens.OrthographicSize;
                _duelReplayCameraOrthoCaptured = true;
            }

            gameplayCamera ??= ResolveGameplayCamera();
            if (gameplayCamera != null &&
                gameplayCamera != duelReplayCamera &&
                !_gameplayCameraPriorityCaptured)
            {
                _gameplayCameraPreviousPriority = gameplayCamera.Priority.Value;
                _gameplayCameraPriorityCaptured = true;
            }

            duelReplayCamera.Priority = duelReplayCameraPriority;
            duelReplayCamera.Follow = followTarget;
            duelReplayCamera.LookAt = followTarget;

            if (gameplayCamera != null && gameplayCamera != duelReplayCamera)
            {
                var loweredPriority = Math.Min(gameplayCamera.Priority.Value, duelReplayCameraPriority - 1);
                gameplayCamera.Priority = loweredPriority;
            }

            if (duelReplayOrthoSize > 0f)
            {
                var lens = duelReplayCamera.Lens;
                lens.OrthographicSize = duelReplayOrthoSize;
                duelReplayCamera.Lens = lens;
            }
        }

        private void RestoreDuelReplayCameraState()
        {
            if (duelReplayCamera != null && _duelReplayCameraPriorityCaptured)
            {
                duelReplayCamera.Priority = _duelReplayCameraPreviousPriority;
            }
            if (duelReplayCamera != null && _duelReplayCameraFollowCaptured)
            {
                duelReplayCamera.Follow = _duelReplayCameraPreviousFollow;
            }
            if (duelReplayCamera != null && _duelReplayCameraLookAtCaptured)
            {
                duelReplayCamera.LookAt = _duelReplayCameraPreviousLookAt;
            }
            if (duelReplayCamera != null && _duelReplayCameraOrthoCaptured)
            {
                var lens = duelReplayCamera.Lens;
                lens.OrthographicSize = _duelReplayCameraPreviousOrthoSize;
                duelReplayCamera.Lens = lens;
            }

            if (gameplayCamera != null && _gameplayCameraPriorityCaptured)
            {
                gameplayCamera.Priority = _gameplayCameraPreviousPriority;
            }

            _duelReplayCameraPriorityCaptured = false;
            _gameplayCameraPriorityCaptured = false;
            _duelReplayCameraFollowCaptured = false;
            _duelReplayCameraLookAtCaptured = false;
            _duelReplayCameraOrthoCaptured = false;
        }

        private CinemachineCamera ResolveGameplayCamera()
        {
            if (gameplayCamera != null)
            {
                return gameplayCamera;
            }

            var cameras = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            for (var index = 0; index < cameras.Length; index += 1)
            {
                var candidate = cameras[index];
                if (candidate == null || candidate == duelReplayCamera)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private void ShowDuelResultModal(PublicKey localWallet, DuelChallengeView challenge)
        {
            if (hud == null)
            {
                return;
            }

            if (challenge.IsDraw)
            {
                hud.ShowDuelResultModal("DRAW", "Both players were knocked out. Stakes refunded.");
                return;
            }

            var localWon = string.Equals(challenge.Winner?.Key, localWallet.Key, StringComparison.Ordinal);
            if (localWon)
            {
                hud.ShowDuelResultModal("YOU WON", "Victory. The duel payout has been awarded.");
                GameAudioManager.Instance?.PlayStinger(StingerSfxId.DuelVictory);
                TryPublishDuelWin(localWallet, challenge);
                return;
            }

            hud.ShowDuelResultModal("YOU LOST", "Defeat. Better luck next duel.");
            GameAudioManager.Instance?.PlayStinger(StingerSfxId.DuelDefeat);
        }

        private void TryPublishDuelWin(PublicKey localWallet, DuelChallengeView challenge)
        {
            if (challenge == null || localWallet == null)
            {
                return;
            }

            if (serverFeedClient == null)
            {
                serverFeedClient = ServerFeedClient.Instance ?? FindFirstObjectByType<ServerFeedClient>();
            }

            if (serverFeedClient == null)
            {
                return;
            }

            var challengerIsLocal = string.Equals(challenge.Challenger?.Key, localWallet.Key, StringComparison.Ordinal);
            var opponentWallet = challengerIsLocal ? challenge.Opponent : challenge.Challenger;
            var opponentNameSnapshot = challengerIsLocal
                ? challenge.OpponentDisplayNameSnapshot
                : challenge.ChallengerDisplayNameSnapshot;
            var opponentName = GetReplayDisplayName(opponentNameSnapshot, opponentWallet);
            serverFeedClient.PublishDuelWon(opponentName);
        }

        private LGPlayerController ResolveLocalPlayerController()
        {
            if (dungeonManager == null)
            {
                dungeonManager = FindFirstObjectByType<DungeonManager>();
            }

            if (dungeonManager == null)
            {
                return null;
            }

            var controllers = FindObjectsByType<LGPlayerController>(FindObjectsSortMode.None);
            for (var index = 0; index < controllers.Length; index += 1)
            {
                var candidate = controllers[index];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.GetComponentInParent<DoorOccupantVisual2D>() != null)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private LGPlayerController ResolveReplayPlayerController(PublicKey wallet, PublicKey localWallet, bool spectatorMode)
        {
            if (wallet == null)
            {
                return null;
            }

            var isLocalWallet = !spectatorMode &&
                                localWallet != null &&
                                string.Equals(wallet.Key, localWallet.Key, StringComparison.Ordinal);
            return isLocalWallet
                ? ResolveLocalPlayerController()
                : ResolveRemotePlayerController(wallet);
        }

        private DoorOccupantVisual2D ResolveRemoteOccupantVisual(PublicKey wallet)
        {
            if (wallet == null || string.IsNullOrWhiteSpace(wallet.Key))
            {
                return null;
            }

            var visuals = FindObjectsByType<DoorOccupantVisual2D>(FindObjectsSortMode.None);
            for (var index = 0; index < visuals.Length; index += 1)
            {
                var visual = visuals[index];
                if (visual == null)
                {
                    continue;
                }

                if (string.Equals(visual.BoundWalletKey, wallet.Key, StringComparison.Ordinal))
                {
                    return visual;
                }
            }

            return null;
        }

        private LGPlayerController ResolveRemotePlayerController(PublicKey wallet)
        {
            var visual = ResolveRemoteOccupantVisual(wallet);
            return visual != null ? visual.GetComponent<LGPlayerController>() : null;
        }

        private async void HandleDuelChallengeRequested(PublicKey opponentWallet, ulong stakeRaw)
        {
            if (_isActionInFlight || manager == null || hud == null)
            {
                return;
            }

            if (ShouldSuppressDuplicateCreateRequest(opponentWallet, stakeRaw))
            {
                Debug.Log("[DuelCoordinator] Suppressed duplicate duel create request.");
                return;
            }

            _isActionInFlight = true;
            hud.SetDuelActionBusy(true);
            try
            {
                var currentSlot = await manager.GetCurrentSlotAsync();
                var expiryOffset = manager.GetDefaultDuelExpirySlotOffset();
                var expiresAtSlot = currentSlot.HasValue ? currentSlot.Value + expiryOffset : expiryOffset;

                var result = await manager.CreateDuelChallengeAsync(opponentWallet, stakeRaw, expiresAtSlot);
                HandleTxResult(result, "Duel request sent.", "Failed to send duel request");
            }
            finally
            {
                hud.SetDuelActionBusy(false);
                _isActionInFlight = false;
            }
        }

        private static bool ShouldSuppressDuplicateCreateRequest(PublicKey opponentWallet, ulong stakeRaw)
        {
            var opponentKey = opponentWallet?.Key ?? string.Empty;
            var fingerprint = $"{opponentKey}:{stakeRaw}";
            var now = Time.realtimeSinceStartup;
            if (string.Equals(fingerprint, _lastCreateRequestFingerprint, StringComparison.Ordinal) &&
                now - _lastCreateRequestRealtime <= 1.5f)
            {
                return true;
            }

            _lastCreateRequestFingerprint = fingerprint;
            _lastCreateRequestRealtime = now;
            return false;
        }

        private async void HandleDuelAcceptRequested(PublicKey duelPda)
        {
            var challenge = FindChallengeByPda(duelPda);
            if (challenge == null)
            {
                hud?.ShowCenterToast("Duel request no longer available.", 1.5f);
                await ForceRefreshAsync();
                return;
            }

            if (_isActionInFlight || manager == null || hud == null)
            {
                return;
            }

            hud.PrepareForDuelReplay();
            _isActionInFlight = true;
            hud.SetDuelActionBusy(true);
            try
            {
                var result = await manager.AcceptDuelChallengeAsync(challenge);
                HandleTxResult(result, "Duel accepted. Preparing fight...", "Failed to accept duel");
            }
            finally
            {
                hud.SetDuelActionBusy(false);
                _isActionInFlight = false;
            }
        }

        private async void HandleDuelDeclineRequested(PublicKey duelPda)
        {
            var challenge = FindChallengeByPda(duelPda);
            if (challenge == null)
            {
                hud?.ShowCenterToast("Duel request no longer available.", 1.5f);
                await ForceRefreshAsync();
                return;
            }

            if (_isActionInFlight || manager == null || hud == null)
            {
                return;
            }

            _isActionInFlight = true;
            hud.SetDuelActionBusy(true);
            try
            {
                var result = await manager.DeclineDuelChallengeAsync(challenge);
                HandleTxResult(result, "Duel declined.", "Failed to decline duel");
            }
            finally
            {
                hud.SetDuelActionBusy(false);
                _isActionInFlight = false;
            }
        }

        private async void HandleDuelClaimExpiredRequested(PublicKey duelPda)
        {
            var challenge = FindChallengeByPda(duelPda);
            if (challenge == null)
            {
                hud?.ShowCenterToast("Duel request no longer available.", 1.5f);
                await ForceRefreshAsync();
                return;
            }

            if (_isActionInFlight || manager == null || hud == null)
            {
                return;
            }

            _isActionInFlight = true;
            hud.SetDuelActionBusy(true);
            try
            {
                var result = await manager.ExpireDuelChallengeAsync(challenge);
                HandleTxResult(result, "Expired duel claimed.", "Failed to claim expired duel");
            }
            finally
            {
                hud.SetDuelActionBusy(false);
                _isActionInFlight = false;
            }
        }

        private DuelChallengeView FindChallengeByPda(PublicKey duelPda)
        {
            if (duelPda == null)
            {
                return null;
            }

            for (var i = 0; i < _cachedChallenges.Count; i += 1)
            {
                var challenge = _cachedChallenges[i];
                if (challenge?.Pda == null)
                {
                    continue;
                }

                if (string.Equals(challenge.Pda.Key, duelPda.Key, StringComparison.Ordinal))
                {
                    return challenge;
                }
            }

            return null;
        }

        private void HandleTxResult(TxResult result, string successMessage, string failurePrefix)
        {
            if (hud == null)
            {
                return;
            }

            if (result.Success)
            {
                hud.ShowCenterToast(successMessage, 1.5f);
                ForceRefreshAsync().Forget();
                return;
            }

            var error = string.IsNullOrWhiteSpace(result.Error) ? "Unknown error" : result.Error;
            hud.ShowCenterToast($"{failurePrefix}: {error}", 2.2f);
            ForceRefreshAsync().Forget();
        }
    }
}
