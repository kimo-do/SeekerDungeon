using System;
using Cysharp.Threading.Tasks;
using SeekerDungeon;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SeekerDungeon.Solana
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LGGameHudUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LGManager manager;
        [SerializeField] private LGWalletSessionManager walletSessionManager;

        [Header("Scene Flow")]
        [SerializeField] private string backSceneName = "MenuScene";
        [SerializeField] private string loadingSceneName = "Loading";

        [Header("Refresh")]
        [SerializeField] private float hudRefreshSeconds = 3f;
        [SerializeField] private bool logDebugMessages = false;

        private UIDocument _document;
        private Label _solBalanceLabel;
        private Label _skrBalanceLabel;
        private Label _jobInfoLabel;
        private Label _statusLabel;
        private Button _backButton;
        private Button _disconnectButton;

        private bool _isLoadingScene;

        private void Awake()
        {
            LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi();
            _document = GetComponent<UIDocument>();

            if (manager == null)
            {
                manager = LGManager.Instance;
            }

            if (manager == null)
            {
                manager = UnityEngine.Object.FindFirstObjectByType<LGManager>();
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = LGWalletSessionManager.Instance;
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = UnityEngine.Object.FindFirstObjectByType<LGWalletSessionManager>();
            }
        }

        private void OnEnable()
        {
            var root = _document?.rootVisualElement;
            if (root == null)
            {
                return;
            }

            _solBalanceLabel = root.Q<Label>("hud-sol-balance");
            _skrBalanceLabel = root.Q<Label>("hud-skr-balance");
            _jobInfoLabel = root.Q<Label>("hud-job-info");
            _statusLabel = root.Q<Label>("hud-status");
            _backButton = root.Q<Button>("hud-btn-back");
            _disconnectButton = root.Q<Button>("hud-btn-disconnect");

            if (_backButton != null)
            {
                _backButton.clicked += HandleBackClicked;
            }

            if (_disconnectButton != null)
            {
                _disconnectButton.clicked += HandleDisconnectClicked;
            }

            if (manager != null)
            {
                manager.OnPlayerStateUpdated += HandlePlayerStateUpdated;
                manager.OnRoomStateUpdated += HandleRoomStateUpdated;
            }

            RefreshHudAsync().Forget();
            RefreshLoopAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void OnDisable()
        {
            if (_backButton != null)
            {
                _backButton.clicked -= HandleBackClicked;
            }

            if (_disconnectButton != null)
            {
                _disconnectButton.clicked -= HandleDisconnectClicked;
            }

            if (manager != null)
            {
                manager.OnPlayerStateUpdated -= HandlePlayerStateUpdated;
                manager.OnRoomStateUpdated -= HandleRoomStateUpdated;
            }
        }

        private async UniTaskVoid RefreshLoopAsync(System.Threading.CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.5f, hudRefreshSeconds)), cancellationToken: cancellationToken);
                    await RefreshHudAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    SetStatus($"HUD refresh failed: {exception.Message}");
                }
            }
        }

        private void HandlePlayerStateUpdated(Chaindepth.Accounts.PlayerAccount _)
        {
            RefreshJobInfo();
        }

        private void HandleRoomStateUpdated(Chaindepth.Accounts.RoomAccount _)
        {
            RefreshJobInfo();
        }

        private void HandleBackClicked()
        {
            if (_isLoadingScene)
            {
                return;
            }

            LoadSceneWithFadeAsync(backSceneName).Forget();
        }

        private void HandleDisconnectClicked()
        {
            if (_isLoadingScene)
            {
                return;
            }

            walletSessionManager?.Disconnect();
            LoadSceneWithFadeAsync(loadingSceneName).Forget();
        }

        private async UniTaskVoid LoadSceneWithFadeAsync(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            _isLoadingScene = true;
            try
            {
                await SceneLoadController.GetOrCreate().LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            finally
            {
                _isLoadingScene = false;
            }
        }

        private async UniTask RefreshHudAsync()
        {
            await RefreshBalancesAsync();
            RefreshJobInfo();
        }

        private async UniTask RefreshBalancesAsync()
        {
            var wallet = Web3.Wallet;
            var account = wallet?.Account;
            if (wallet?.ActiveRpcClient == null || account == null)
            {
                SetLabel(_solBalanceLabel, "SOL: --");
                SetLabel(_skrBalanceLabel, "SKR: --");
                return;
            }

            var solResult = await wallet.ActiveRpcClient.GetBalanceAsync(account.PublicKey, Commitment.Confirmed);
            if (solResult.WasSuccessful && solResult.Result != null)
            {
                var sol = solResult.Result.Value / 1_000_000_000d;
                SetLabel(_solBalanceLabel, $"SOL: {sol:F3}");
            }
            else
            {
                SetLabel(_solBalanceLabel, "SOL: --");
            }

            var skrMint = new PublicKey(LGConfig.SKR_MINT);
            var tokenAccounts = await wallet.GetTokenAccounts(skrMint, TokenProgram.ProgramIdKey);
            if (tokenAccounts != null && tokenAccounts.Length > 0)
            {
                ulong amountLamports = 0UL;
                foreach (var tokenAccount in tokenAccounts)
                {
                    if (tokenAccount?.Account?.Data?.Parsed?.Info?.TokenAmount == null)
                    {
                        continue;
                    }

                    amountLamports += tokenAccount.Account.Data.Parsed.Info.TokenAmount.AmountUlong;
                }

                var skrUi = amountLamports / (double)LGConfig.SKR_MULTIPLIER;
                SetLabel(_skrBalanceLabel, $"SKR: {skrUi:F3}");
            }
            else
            {
                SetLabel(_skrBalanceLabel, "SKR: 0");
            }
        }

        private void RefreshJobInfo()
        {
            if (manager?.CurrentPlayerState == null)
            {
                SetLabel(_jobInfoLabel, "Job: None");
                return;
            }

            var player = manager.CurrentPlayerState;
            var activeJobs = player.ActiveJobs;
            if (activeJobs == null || activeJobs.Length == 0)
            {
                SetLabel(_jobInfoLabel, "Job: None");
                return;
            }

            for (var i = activeJobs.Length - 1; i >= 0; i -= 1)
            {
                var job = activeJobs[i];
                if (job == null || job.RoomX != player.CurrentRoomX || job.RoomY != player.CurrentRoomY)
                {
                    continue;
                }

                var direction = job.Direction;
                var directionName = LGConfig.GetDirectionName(direction);

                if (manager.CurrentRoomState == null || direction >= manager.CurrentRoomState.Walls.Length)
                {
                    SetLabel(_jobInfoLabel, $"Job: {directionName}");
                    return;
                }

                var room = manager.CurrentRoomState;
                var progress = room.Progress[direction];
                var required = room.BaseSlots[direction];
                var helpers = room.HelperCounts[direction];
                SetLabel(_jobInfoLabel, $"Job: {directionName} {progress}/{required} ({helpers} helpers)");
                return;
            }

            SetLabel(_jobInfoLabel, "Job: None");
        }

        private void SetStatus(string message)
        {
            if (logDebugMessages)
            {
                Debug.Log($"[GameHUD] {message}");
            }

            SetLabel(_statusLabel, message);
        }

        private static void SetLabel(Label label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
