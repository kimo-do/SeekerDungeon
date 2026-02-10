using System;
using Cysharp.Threading.Tasks;
using SeekerDungeon;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Loading scene controller:
    /// - Waits for wallet connection.
    /// - Supports auto-connect on startup.
    /// - Exposes a button hook for "Connect Seeker".
    /// - Loads the next scene once connected.
    /// </summary>
    public sealed class LGLoadingController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LGWalletSessionManager walletSessionManager;

        [Header("Scene Flow")]
        [SerializeField] private string connectedSceneName = "MenuScene";
        [SerializeField] private bool autoConnectOnStart = true;
        [SerializeField] private bool allowAutoConnectOnDeviceBuilds = false;
        [SerializeField] private bool requireManualConnectOnDevice = true;
        [SerializeField] private WalletLoginMode autoConnectMode = WalletLoginMode.Auto;
        [SerializeField] private float walletAdapterConnectTimeoutSeconds = 20f;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        private bool _isConnecting;
        private bool _isSceneLoading;

        private void Awake()
        {
            if (walletSessionManager == null)
            {
                walletSessionManager = LGWalletSessionManager.EnsureInstance();
            }
        }

        private void OnEnable()
        {
            if (walletSessionManager != null)
            {
                walletSessionManager.OnWalletConnectionChanged += HandleWalletConnectionChanged;
            }
        }

        private void OnDisable()
        {
            if (walletSessionManager != null)
            {
                walletSessionManager.OnWalletConnectionChanged -= HandleWalletConnectionChanged;
            }
        }

        private void Start()
        {
            RunStartupFlow().Forget();
        }

        /// <summary>
        /// Hook this to your "Connect Seeker" UI button.
        /// </summary>
        public void OnConnectSeekerClicked()
        {
            walletSessionManager?.MarkWalletConnectIntent();
            var connectMode = WalletLoginMode.WalletAdapter;
#if UNITY_EDITOR
            if (walletSessionManager != null && walletSessionManager.SimulateMobileFlowInEditor)
            {
                // Keep editor wallet auto-signing behavior while requiring a manual connect click.
                connectMode = WalletLoginMode.Auto;
            }
#endif
            ConnectAsync(connectMode).Forget();
        }

        /// <summary>
        /// Optional alternate hook for "Connect (Auto)" buttons.
        /// </summary>
        public void OnConnectAutoClicked()
        {
            ConnectAsync(autoConnectMode).Forget();
        }

        private async UniTaskVoid RunStartupFlow()
        {
            if (walletSessionManager == null)
            {
                LogError("LGWalletSessionManager not found in scene.");
                return;
            }

#if UNITY_EDITOR
            if (walletSessionManager.SimulateMobileFlowInEditor)
            {
                Log("Simulated mobile flow enabled in editor. Waiting for manual connect.");
                return;
            }
#endif

            if (walletSessionManager.IsWalletConnected)
            {
                LoadConnectedScene();
                return;
            }

            if (!Application.isEditor && Application.isMobilePlatform && requireManualConnectOnDevice)
            {
                if (!walletSessionManager.HasWalletConnectIntent)
                {
                    Log("Manual connect required on device. Waiting for button press.");
                    return;
                }

                Log("Auto-connect allowed on device from remembered wallet intent.");
            }

            if (!autoConnectOnStart)
            {
                return;
            }

            if (!Application.isEditor && Application.isMobilePlatform && !allowAutoConnectOnDeviceBuilds)
            {
                if (walletSessionManager.HasWalletConnectIntent)
                {
                    Log("Auto-connect allowed from prior user connect intent.");
                }
                else
                {
                    Log("Auto-connect disabled on device builds. Waiting for button press.");
                    return;
                }
            }

            await ConnectAsync(autoConnectMode);
        }

        private async UniTask ConnectAsync(WalletLoginMode mode)
        {
            if (_isConnecting || _isSceneLoading)
            {
                return;
            }

            if (walletSessionManager == null)
            {
                LogError("Cannot connect: LGWalletSessionManager is missing.");
                return;
            }

            _isConnecting = true;
            try
            {
                Log($"Connecting wallet ({mode})...");
                var connected = await ConnectWithTimeoutAsync(mode);
                if (connected || walletSessionManager.IsWalletConnected)
                {
                    LoadConnectedScene();
                }
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private async UniTask<bool> ConnectWithTimeoutAsync(WalletLoginMode mode)
        {
            if (walletSessionManager == null)
            {
                return false;
            }

            if (mode != WalletLoginMode.WalletAdapter || walletAdapterConnectTimeoutSeconds <= 0f)
            {
                return await walletSessionManager.ConnectAsync(mode);
            }

            try
            {
                return await walletSessionManager
                    .ConnectAsync(mode)
                    .Timeout(TimeSpan.FromSeconds(walletAdapterConnectTimeoutSeconds));
            }
            catch (TimeoutException)
            {
                walletSessionManager.ClearWalletConnectIntent();
                walletSessionManager.Disconnect();
                LogError("Wallet adapter connect timed out. User can retry.");
                return false;
            }
        }

        private void HandleWalletConnectionChanged(bool isConnected)
        {
            if (isConnected)
            {
                LoadConnectedScene();
            }
        }

        private void LoadConnectedScene()
        {
            if (_isSceneLoading)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(connectedSceneName))
            {
                LogError("Connected scene name is empty.");
                return;
            }

            _isSceneLoading = true;
            Log($"Wallet connected. Loading scene '{connectedSceneName}'.");
            LoadConnectedSceneWithFadeAsync().Forget();
        }

        private async UniTaskVoid LoadConnectedSceneWithFadeAsync()
        {
            try
            {
                await SceneLoadController.GetOrCreate().LoadSceneAsync(connectedSceneName, LoadSceneMode.Single);
            }
            finally
            {
                _isSceneLoading = false;
            }
        }

        private void Log(string message)
        {
            if (logDebugMessages)
            {
                Debug.Log($"[LGLoading] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LGLoading] {message}");
        }
    }
}
