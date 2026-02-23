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
        private static DuelCoordinator _activeInstance;

        [SerializeField] private LGManager manager;
        [SerializeField] private LGGameHudUI hud;
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
        private static float _lastCreateRequestRealtime;
        private static string _lastCreateRequestFingerprint = string.Empty;
        private bool _hasBaselineSnapshot;
        private bool _isActionInFlight;
        private bool _isRefreshInFlight;
        private float _lastSlotRefreshRealtime;
        private ulong? _latestKnownSlot;
        private int _localSwingEventsObserved;
        private int _remoteSwingEventsObserved;
        private LGPlayerController _activeReplayLocalPlayer;
        private LGPlayerController _activeReplayRemotePlayer;

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

            gameplayCamera ??= ResolveGameplayCamera();
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
                if (Time.realtimeSinceStartup - _lastSlotRefreshRealtime >= 10f)
                {
                    _latestKnownSlot = await manager.GetCurrentSlotAsync();
                    _lastSlotRefreshRealtime = Time.realtimeSinceStartup;
                }

                await manager.FetchRelevantDuelChallengesAsync();
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

                PlaySettledDuelReplayAsync(localWallet, challenge).Forget();
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

        private async UniTaskVoid PlaySettledDuelReplayAsync(PublicKey localWallet, DuelChallengeView challenge)
        {
            hud?.PrepareForDuelReplay();
            var audioManager = GameAudioManager.Instance;
            audioManager?.PlayLoopOnce(AudioLoopId.DuelBattle, Vector3.zero);

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
                    replayPlayed = await RunDuelReplayAsync(localWallet, challenge);
                }
                catch (Exception replayError)
                {
                    Debug.LogWarning($"[DuelCoordinator] Duel replay failed: {replayError.Message}");
                }
            }

            if (!replayPlayed)
            {
                Debug.LogWarning("[DuelCoordinator] Duel replay fallback: could not resolve both actors in time.");
            }

            if (audioManager != null && audioManager.IsLoopPlaying(AudioLoopId.DuelBattle))
            {
                await audioManager.FadeOutLoopAndStopAsync(
                    AudioLoopId.DuelBattle,
                    Mathf.Max(0.01f, duelBattleMusicFadeOutSeconds));
            }

            ShowDuelResultModal(localWallet, challenge);
            Debug.Log(manager.FormatDuelTranscriptForLog(challenge));
        }

        private async UniTask<bool> RunDuelReplayAsync(PublicKey localWallet, DuelChallengeView challenge)
        {
            var spawnedHitSplats = new List<GameObject>();
            var remoteWallet = ResolveOpponentWalletForLocal(localWallet, challenge);
            var replayActors = await ResolveReplayActorsAsync(remoteWallet);
            var localPlayer = replayActors.LocalPlayer;
            var remotePlayer = replayActors.RemotePlayer;
            if (localPlayer == null || remotePlayer == null)
            {
                Debug.LogWarning(
                    $"[DuelCoordinator] Replay skipped: local={localPlayer != null} remote={remotePlayer != null}");
                return false;
            }

            Transform duelCenterAnchor = null;
            ActivateDuelReplayCamera(localPlayer.transform);
            var localWalletKey = localWallet?.Key;
            var remoteWalletKey = remoteWallet?.Key;
            DuelVisualLockRegistry.Lock(localWalletKey);
            DuelVisualLockRegistry.Lock(remoteWalletKey);
            try
            {
                var challengerIsLocal = string.Equals(challenge.Challenger?.Key, localWallet.Key, StringComparison.Ordinal);
                var localStartPosition = localPlayer.transform.position;
                var remoteStartPosition = remotePlayer.transform.position;
                var localToRemoteOffsetX = challengerIsLocal ? duelReplaySpacingUnits : -duelReplaySpacingUnits;
                var localXMin = localToRemoteOffsetX >= 0f
                    ? duelReplayBoundsX.x
                    : duelReplayBoundsX.x - localToRemoteOffsetX;
                var localXMax = localToRemoteOffsetX >= 0f
                    ? duelReplayBoundsX.y - localToRemoteOffsetX
                    : duelReplayBoundsX.y;

                var duelLocalX = Mathf.Clamp(localStartPosition.x, localXMin, localXMax);
                var duelLocalY = Mathf.Clamp(localStartPosition.y, duelReplayBoundsY.x, duelReplayBoundsY.y);
                var duelRemoteX = Mathf.Clamp(duelLocalX + localToRemoteOffsetX, duelReplayBoundsX.x, duelReplayBoundsX.y);
                var duelRemoteY = duelLocalY;
                var localUsedFallbackWeapon = localPlayer.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);
                var remoteUsedFallbackWeapon = remotePlayer.EnsureFallbackWieldedItem(ItemId.BronzePickaxe);

                localPlayer.transform.position = new Vector3(duelLocalX, duelLocalY, localStartPosition.z);
                remotePlayer.transform.position = new Vector3(duelRemoteX, duelRemoteY, remoteStartPosition.z);
                localPlayer.SetFacingDirection(challengerIsLocal ? OccupantFacingDirection.Right : OccupantFacingDirection.Left);
                remotePlayer.SetFacingDirection(challengerIsLocal ? OccupantFacingDirection.Left : OccupantFacingDirection.Right);
                duelCenterAnchor = new GameObject("DuelCameraCenterAnchor").transform;
                duelCenterAnchor.position = ComputeDuelCenterPosition(localPlayer.transform.position, remotePlayer.transform.position);
                if (duelReplayCamera != null)
                {
                    duelReplayCamera.Follow = duelCenterAnchor;
                    duelReplayCamera.LookAt = duelCenterAnchor;
                }

                const ushort maxHp = 100;
                var challengerHp = (int)maxHp;
                var opponentHp = (int)maxHp;
                var combatAnimationStopped = false;
                localPlayer.SetCombatHealth(maxHp, maxHp, true);
                remotePlayer.SetCombatHealth(maxHp, maxHp, true);
                _localSwingEventsObserved = 0;
                _remoteSwingEventsObserved = 0;
                _activeReplayLocalPlayer = localPlayer;
                _activeReplayRemotePlayer = remotePlayer;
                DuelSwingEventRelay.OnSwing += HandleDuelSwingEvent;
                localPlayer.SetBossJobAnimationState(false);
                remotePlayer.SetBossJobAnimationState(false);
                await UniTask.Yield();

                var totalTurns = Math.Max(
                    challenge.TurnsPlayed,
                    (byte)Math.Max(challenge.ChallengerHits?.Count ?? 0, challenge.OpponentHits?.Count ?? 0));
                var activeAttackHitSplats = new List<HitSplatView>(2);
                var challengerPlayer = challengerIsLocal ? localPlayer : remotePlayer;
                var opponentPlayer = challengerIsLocal ? remotePlayer : localPlayer;
                var challengerStartsRound = challenge.Starter != DuelStarter.Opponent;
                for (var turnIndex = 0; turnIndex < totalTurns; turnIndex += 1)
                {
                    var turnIntervalSeconds = Mathf.Max(0.2f, duelReplayHitIntervalSeconds);
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

                    if (challengerIsLocal)
                    {
                        localPlayer.SetCombatHealth((ushort)challengerHp, maxHp, true);
                        remotePlayer.SetCombatHealth((ushort)opponentHp, maxHp, true);
                    }
                    else
                    {
                        localPlayer.SetCombatHealth((ushort)opponentHp, maxHp, true);
                        remotePlayer.SetCombatHealth((ushort)challengerHp, maxHp, true);
                    }

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

                    if (challengerIsLocal)
                    {
                        localPlayer.SetCombatHealth((ushort)challengerHp, maxHp, true);
                        remotePlayer.SetCombatHealth((ushort)opponentHp, maxHp, true);
                    }
                    else
                    {
                        localPlayer.SetCombatHealth((ushort)opponentHp, maxHp, true);
                        remotePlayer.SetCombatHealth((ushort)challengerHp, maxHp, true);
                    }

                    if (challengerHp == 0 || opponentHp == 0)
                    {
                        localPlayer.SetBossJobAnimationState(false);
                        remotePlayer.SetBossJobAnimationState(false);
                        combatAnimationStopped = true;
                        break;
                    }
                }

                if (challengerIsLocal)
                {
                    localPlayer.SetCombatHealth(challenge.ChallengerFinalHp, maxHp, true);
                    remotePlayer.SetCombatHealth(challenge.OpponentFinalHp, maxHp, true);
                }
                else
                {
                    localPlayer.SetCombatHealth(challenge.OpponentFinalHp, maxHp, true);
                    remotePlayer.SetCombatHealth(challenge.ChallengerFinalHp, maxHp, true);
                }

                if (challenge.ChallengerFinalHp == 0 || challenge.OpponentFinalHp == 0)
                {
                    localPlayer.SetBossJobAnimationState(false);
                    remotePlayer.SetBossJobAnimationState(false);
                    combatAnimationStopped = true;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, duelReplayPostKillSeconds)));

                if (!combatAnimationStopped)
                {
                    localPlayer.SetBossJobAnimationState(false);
                    remotePlayer.SetBossJobAnimationState(false);
                }
                localPlayer.SetCombatHealth(0, 0, false);
                remotePlayer.SetCombatHealth(0, 0, false);
                localPlayer.transform.position = localStartPosition;
                remotePlayer.transform.position = remoteStartPosition;
                if (localUsedFallbackWeapon)
                {
                    localPlayer.HideAllWieldedItems();
                }
                if (remoteUsedFallbackWeapon)
                {
                    remotePlayer.HideAllWieldedItems();
                }
            }
            finally
            {
                DuelSwingEventRelay.OnSwing -= HandleDuelSwingEvent;
                _activeReplayLocalPlayer = null;
                _activeReplayRemotePlayer = null;
                DuelVisualLockRegistry.Unlock(localWalletKey);
                DuelVisualLockRegistry.Unlock(remoteWalletKey);
                if (duelCenterAnchor != null)
                {
                    Destroy(duelCenterAnchor.gameObject);
                }
                CleanupHitSplats(spawnedHitSplats);
                RestoreDuelReplayCameraState();
            }

            return true;
        }

        private async UniTask<(LGPlayerController LocalPlayer, LGPlayerController RemotePlayer)> ResolveReplayActorsAsync(
            PublicKey remoteWallet)
        {
            var elapsed = 0f;
            var timeout = Mathf.Max(0.5f, duelReplayResolveTimeoutSeconds);
            var poll = Mathf.Max(0.05f, duelReplayResolvePollSeconds);
            var nextRefreshAt = 0f;

            while (elapsed <= timeout)
            {
                var localPlayer = ResolveLocalPlayerController();
                var remotePlayer = ResolveRemotePlayerController(remoteWallet);
                if (localPlayer != null && remotePlayer != null)
                {
                    return (localPlayer, remotePlayer);
                }

                if (elapsed >= nextRefreshAt)
                {
                    await ForceReplayContextRefreshAsync();
                    nextRefreshAt += 0.8f;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(poll));
                elapsed += poll;
            }

            return (ResolveLocalPlayerController(), ResolveRemotePlayerController(remoteWallet));
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
                return;
            }

            hud.ShowDuelResultModal("YOU LOST", "Defeat. Better luck next duel.");
            GameAudioManager.Instance?.PlayStinger(StingerSfxId.DuelDefeat);
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

        private static PublicKey ResolveOpponentWalletForLocal(PublicKey localWallet, DuelChallengeView challenge)
        {
            if (localWallet == null || challenge == null)
            {
                return null;
            }

            return string.Equals(challenge.Challenger?.Key, localWallet.Key, StringComparison.Ordinal)
                ? challenge.Opponent
                : challenge.Challenger;
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
