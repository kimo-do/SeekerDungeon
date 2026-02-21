using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
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
        [SerializeField] private float duelReplayHitIntervalSeconds = 1f;
        [SerializeField] private float duelReplayPostKillSeconds = 2f;
        [SerializeField] private float duelReplaySpacingUnits = 0.8f;
        [SerializeField] private float duelReplayResolveTimeoutSeconds = 3f;
        [SerializeField] private float duelReplayResolvePollSeconds = 0.2f;
        [Header("Duel Camera")]
        [SerializeField] private CinemachineCamera duelReplayCamera;
        [SerializeField] private float duelReplayOrthoSize = 3.44f;
        [Header("Duel Hit Splat")]
        [SerializeField] private GameObject duelHitSplatPrefab;
        [SerializeField] private List<Sprite> duelHitSplatSprites = new();
        [SerializeField] private float duelHitSplatYOffset = 0.4f;

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

            if (duelReplayCamera == null)
            {
                duelReplayCamera = FindFirstObjectByType<CinemachineCamera>();
            }
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

            try
            {
                await RunDuelReplayAsync(localWallet, challenge);
            }
            catch (Exception replayError)
            {
                Debug.LogWarning($"[DuelCoordinator] Duel replay failed: {replayError.Message}");
            }

            ShowDuelResultModal(localWallet, challenge);
            Debug.Log(manager.FormatDuelTranscriptForLog(challenge));
        }

        private async UniTask RunDuelReplayAsync(PublicKey localWallet, DuelChallengeView challenge)
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
                return;
            }

            var hasPreviousCameraSize = TryGetCurrentOrthoSize(out var previousCameraSize);
            TrySetOrthoSize(duelReplayOrthoSize);
            try
            {
                var challengerIsLocal = string.Equals(challenge.Challenger?.Key, localWallet.Key, StringComparison.Ordinal);
                var localStartPosition = localPlayer.transform.position;
                var remoteStartPosition = remotePlayer.transform.position;
                var midpoint = (localStartPosition + remoteStartPosition) * 0.5f;
                var leftX = midpoint.x - duelReplaySpacingUnits * 0.5f;
                var rightX = midpoint.x + duelReplaySpacingUnits * 0.5f;

                localPlayer.transform.position = challengerIsLocal
                    ? new Vector3(leftX, midpoint.y, localStartPosition.z)
                    : new Vector3(rightX, midpoint.y, localStartPosition.z);
                remotePlayer.transform.position = challengerIsLocal
                    ? new Vector3(rightX, midpoint.y, remoteStartPosition.z)
                    : new Vector3(leftX, midpoint.y, remoteStartPosition.z);
                localPlayer.SetFacingDirection(challengerIsLocal ? OccupantFacingDirection.Right : OccupantFacingDirection.Left);
                remotePlayer.SetFacingDirection(challengerIsLocal ? OccupantFacingDirection.Left : OccupantFacingDirection.Right);

                const ushort maxHp = 100;
                var challengerHp = (int)maxHp;
                var opponentHp = (int)maxHp;
                localPlayer.SetCombatHealth(maxHp, maxHp, true);
                remotePlayer.SetCombatHealth(maxHp, maxHp, true);
                localPlayer.SetBossJobAnimationState(true);
                remotePlayer.SetBossJobAnimationState(true);

                var totalTurns = Math.Max(
                    challenge.TurnsPlayed,
                    (byte)Math.Max(challenge.ChallengerHits?.Count ?? 0, challenge.OpponentHits?.Count ?? 0));
                for (var turnIndex = 0; turnIndex < totalTurns; turnIndex += 1)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.2f, duelReplayHitIntervalSeconds)));

                    var challengerDamage = turnIndex < (challenge.ChallengerHits?.Count ?? 0)
                        ? challenge.ChallengerHits[turnIndex]
                        : (byte)0;
                    var opponentDamage = turnIndex < (challenge.OpponentHits?.Count ?? 0)
                        ? challenge.OpponentHits[turnIndex]
                        : (byte)0;

                    SpawnHitSplat(remotePlayer, challengerDamage, spawnedHitSplats);
                    SpawnHitSplat(localPlayer, opponentDamage, spawnedHitSplats);

                    opponentHp = Math.Max(0, opponentHp - challengerDamage);
                    challengerHp = Math.Max(0, challengerHp - opponentDamage);

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

                await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, duelReplayPostKillSeconds)));

                localPlayer.SetBossJobAnimationState(false);
                remotePlayer.SetBossJobAnimationState(false);
                localPlayer.SetCombatHealth(0, 0, false);
                remotePlayer.SetCombatHealth(0, 0, false);
                localPlayer.transform.position = localStartPosition;
                remotePlayer.transform.position = remoteStartPosition;
            }
            finally
            {
                CleanupHitSplats(spawnedHitSplats);
                if (hasPreviousCameraSize)
                {
                    TrySetOrthoSize(previousCameraSize);
                }
            }
        }

        private async UniTask<(LGPlayerController LocalPlayer, LGPlayerController RemotePlayer)> ResolveReplayActorsAsync(
            PublicKey remoteWallet)
        {
            var elapsed = 0f;
            var timeout = Mathf.Max(0.5f, duelReplayResolveTimeoutSeconds);
            var poll = Mathf.Max(0.05f, duelReplayResolvePollSeconds);

            while (elapsed <= timeout)
            {
                var localPlayer = ResolveLocalPlayerController();
                var remotePlayer = ResolveRemotePlayerController(remoteWallet);
                if (localPlayer != null && remotePlayer != null)
                {
                    return (localPlayer, remotePlayer);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(poll));
                elapsed += poll;
            }

            return (ResolveLocalPlayerController(), ResolveRemotePlayerController(remoteWallet));
        }

        private void SpawnHitSplat(LGPlayerController targetPlayer, byte damage, List<GameObject> spawnedHitSplats)
        {
            if (duelHitSplatPrefab == null || targetPlayer == null)
            {
                return;
            }

            var spawnPosition = targetPlayer.transform.position + new Vector3(0f, duelHitSplatYOffset, 0f);
            var instance = Instantiate(duelHitSplatPrefab, spawnPosition, Quaternion.identity);
            if (instance == null)
            {
                return;
            }
            spawnedHitSplats?.Add(instance);

            var hitSplat = instance.GetComponent<HitSplatView>();
            if (hitSplat == null)
            {
                return;
            }

            Sprite chosenSprite = null;
            if (duelHitSplatSprites != null && duelHitSplatSprites.Count > 0)
            {
                chosenSprite = duelHitSplatSprites[Random.Range(0, duelHitSplatSprites.Count)];
            }

            hitSplat.SetDamage(damage, chosenSprite);
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

        private bool TryGetCurrentOrthoSize(out float size)
        {
            size = 0f;
            if (duelReplayCamera != null)
            {
                var lens = duelReplayCamera.Lens;
                size = lens.OrthographicSize;
                return true;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.orthographic)
            {
                size = mainCamera.orthographicSize;
                return true;
            }

            return false;
        }

        private void TrySetOrthoSize(float size)
        {
            if (duelReplayCamera != null)
            {
                var lens = duelReplayCamera.Lens;
                lens.OrthographicSize = size;
                duelReplayCamera.Lens = lens;
                return;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.orthographic)
            {
                mainCamera.orthographicSize = size;
            }
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
                return;
            }

            hud.ShowDuelResultModal("YOU LOST", "Defeat. Better luck next duel.");
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
                HandleTxResult(result, "Duel accepted. Rolling...", "Failed to accept duel");
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
