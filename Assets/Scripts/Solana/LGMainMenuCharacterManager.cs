using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SeekerDungeon;
using Solana.Unity.SDK;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeekerDungeon.Solana
{
    public sealed class MainMenuCharacterState
    {
        public bool IsReady { get; init; }
        public bool HasProfile { get; init; }
        public bool IsBusy { get; init; }
        public PlayerSkinId SelectedSkin { get; init; }
        public string SelectedSkinLabel { get; init; }
        public string WalletShortAddress { get; init; }
        public string DisplayName { get; init; }
        public string StatusMessage { get; init; }
    }

    /// <summary>
    /// Handles MainMenu character creation/profile flow and skin placeholder switching.
    /// </summary>
    public sealed class LGMainMenuCharacterManager : MonoBehaviour
    {
        private const int DefaultMaxDisplayNameLength = 24;
        private const int PlayerInitFetchMaxAttempts = 12;
        private const int PlayerInitFetchDelayMs = 500;

        [Header("References")]
        [SerializeField] private LGManager lgManager;
        [SerializeField] private LGPlayerController playerController;
        [SerializeField] private LGWalletSessionManager walletSessionManager;

        [Header("Scene Flow")]
        [SerializeField] private string gameplaySceneName = "GameScene";
        [SerializeField] private string loadingSceneName = "Loading";

        [Header("Display Name")]
        [SerializeField] private int maxDisplayNameLength = DefaultMaxDisplayNameLength;

        [Header("Session UX")]
        [SerializeField] private bool prepareGameplaySessionInMenu = true;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        public event Action<MainMenuCharacterState> OnStateChanged;
        public event Action<string> OnError;

        public bool IsReady { get; private set; }
        public bool HasExistingProfile { get; private set; }
        public bool IsBusy { get; private set; }
        public PlayerSkinId SelectedSkin { get; private set; } = PlayerSkinId.Goblin;
        public string PendingDisplayName { get; private set; } = string.Empty;
        private readonly List<PlayerSkinId> _selectableSkins = new();

        private void Awake()
        {
            if (lgManager == null)
            {
                lgManager = LGManager.Instance;
            }

            if (lgManager == null)
            {
                lgManager = FindObjectOfType<LGManager>();
            }

            if (playerController == null)
            {
                playerController = FindObjectOfType<LGPlayerController>();
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = LGWalletSessionManager.Instance;
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = FindObjectOfType<LGWalletSessionManager>();
            }

            RebuildSelectableSkins();

            if (_selectableSkins.Count > 0)
            {
                SelectedSkin = _selectableSkins[0];
            }

            SetPlayerVisible(false);
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        public MainMenuCharacterState GetCurrentState()
        {
            return BuildState(string.Empty);
        }

        public void SelectNextSkin()
        {
            if (IsBusy || HasExistingProfile)
            {
                return;
            }

            if (_selectableSkins.Count == 0)
            {
                return;
            }

            var currentIndex = FindSelectedSkinIndex();
            var nextIndex = (currentIndex + 1) % _selectableSkins.Count;
            SelectedSkin = _selectableSkins[nextIndex];
            ApplySelectedSkinVisual();
            EmitState("Choose your character");
        }

        public void SelectPreviousSkin()
        {
            if (IsBusy || HasExistingProfile)
            {
                return;
            }

            if (_selectableSkins.Count == 0)
            {
                return;
            }

            var currentIndex = FindSelectedSkinIndex();
            var previousIndex = currentIndex <= 0 ? _selectableSkins.Count - 1 : currentIndex - 1;
            SelectedSkin = _selectableSkins[previousIndex];
            ApplySelectedSkinVisual();
            EmitState("Choose your character");
        }

        public void SetPendingDisplayName(string nameInput)
        {
            if (HasExistingProfile)
            {
                return;
            }

            PendingDisplayName = SanitizeDisplayName(nameInput);
            EmitState("Choose your character");
        }

        public async UniTask CreateCharacterAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (HasExistingProfile)
            {
                EmitState("Character already exists");
                return;
            }

            if (lgManager == null)
            {
                EmitError("LGManager not found in scene.");
                return;
            }

            if (Web3.Wallet?.Account == null)
            {
                EmitError("Wallet is not connected.");
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(PendingDisplayName)
                ? GetShortWalletAddress()
                : PendingDisplayName;

            IsBusy = true;
            EmitState("Creating character onchain...");

            try
            {
                await EnsurePlayerInitializedAsync();

                var signature = await lgManager.CreatePlayerProfile((ushort)SelectedSkin, displayName);
                if (string.IsNullOrWhiteSpace(signature))
                {
                    EmitError("Create profile transaction failed.");
                    return;
                }

                await lgManager.FetchPlayerProfile();
                HasExistingProfile = lgManager.CurrentProfileState != null;

                if (!HasExistingProfile)
                {
                    EmitError("Profile was not found after creation.");
                    return;
                }

                var onchainProfile = lgManager.CurrentProfileState;
                SelectedSkin = (PlayerSkinId)onchainProfile.SkinId;
                PendingDisplayName = string.IsNullOrWhiteSpace(onchainProfile.DisplayName)
                    ? GetShortWalletAddress()
                    : onchainProfile.DisplayName;

                ApplySelectedSkinVisual();
                EmitState("Character created");
            }
            catch (Exception exception)
            {
                EmitError($"Create character failed: {exception.Message}");
            }
            finally
            {
                IsBusy = false;
                EmitState(string.Empty);
            }
        }

        public void EnterDungeon()
        {
            if (IsBusy)
            {
                return;
            }

            if (!HasExistingProfile)
            {
                EmitError("Create a character first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(gameplaySceneName))
            {
                EmitError("Gameplay scene name is empty.");
                return;
            }

            LoadSceneWithFadeAsync(gameplaySceneName).Forget();
        }

        public void DisconnectWallet()
        {
            if (IsBusy)
            {
                return;
            }

            var walletSessionManager = LGWalletSessionManager.Instance;
            if (walletSessionManager == null)
            {
                walletSessionManager = FindObjectOfType<LGWalletSessionManager>();
            }

            if (walletSessionManager != null)
            {
                walletSessionManager.Disconnect();
            }

            if (!string.IsNullOrWhiteSpace(loadingSceneName))
            {
                LoadSceneWithFadeAsync(loadingSceneName).Forget();
            }
        }

        private async UniTaskVoid InitializeAsync()
        {
            if (lgManager == null)
            {
                EmitError("LGManager not found in scene.");
                return;
            }

            if (Web3.Wallet?.Account == null)
            {
                EmitError("Wallet is not connected.");
                return;
            }

            IsBusy = true;
            SetPlayerVisible(false);
            EmitState("Loading profile...");

            try
            {
                await EnsurePlayerInitializedAsync();
                await lgManager.FetchPlayerProfile();

                if (prepareGameplaySessionInMenu)
                {
                    await PrepareGameplaySessionAsync();
                }

                var profile = lgManager.CurrentProfileState;
                HasExistingProfile = profile != null;

                if (HasExistingProfile)
                {
                    SelectedSkin = (PlayerSkinId)profile.SkinId;
                    PendingDisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? GetShortWalletAddress()
                        : profile.DisplayName;
                    ApplySelectedSkinVisual();
                }
                else
                {
                    if (_selectableSkins.Count > 0)
                    {
                        SelectedSkin = _selectableSkins[0];
                    }

                    PendingDisplayName = GetShortWalletAddress();
                    ApplySelectedSkinVisual();
                }

                IsReady = true;
                if (prepareGameplaySessionInMenu &&
                    walletSessionManager != null &&
                    !walletSessionManager.CanUseLocalSessionSigning)
                {
                    EmitState("Session unavailable. Gameplay will require wallet approval.");
                }
                else
                {
                    EmitState(string.Empty);
                }
            }
            catch (Exception exception)
            {
                EmitError($"Failed to load menu profile: {exception.Message}");
            }
            finally
            {
                IsBusy = false;
                SetPlayerVisible(true);
                EmitState(string.Empty);
            }
        }

        private async UniTask EnsurePlayerInitializedAsync()
        {
            await lgManager.FetchGlobalState();
            await lgManager.FetchPlayerState();

            if (lgManager.CurrentPlayerState != null)
            {
                return;
            }

            EmitState("Initializing player account...");
            var initSignature = await lgManager.InitPlayer();
            if (string.IsNullOrWhiteSpace(initSignature))
            {
                throw new InvalidOperationException("Failed to initialize player account.");
            }

            var initialized = await WaitForPlayerAccountAfterInitAsync();
            if (!initialized)
            {
                throw new InvalidOperationException(
                    $"Player account still missing after init. signature={initSignature}");
            }
        }

        private async UniTask PrepareGameplaySessionAsync()
        {
            if (walletSessionManager == null)
            {
                return;
            }

            if (walletSessionManager.CanUseLocalSessionSigning)
            {
                EmitState("Session ready");
                return;
            }

            EmitState("Preparing gameplay session...");
            var sessionReady = await walletSessionManager.EnsureGameplaySessionAsync(emitPromptStatus: true);
            if (sessionReady && walletSessionManager.CanUseLocalSessionSigning)
            {
                EmitState("Session ready");
                return;
            }

            EmitState("Session unavailable. Gameplay may prompt wallet approvals.");
        }

        private async UniTask<bool> WaitForPlayerAccountAfterInitAsync()
        {
            for (var attempt = 0; attempt < PlayerInitFetchMaxAttempts; attempt += 1)
            {
                await lgManager.FetchPlayerState();
                if (lgManager.CurrentPlayerState != null)
                {
                    return true;
                }

                if (attempt < PlayerInitFetchMaxAttempts - 1)
                {
                    await UniTask.Delay(PlayerInitFetchDelayMs);
                }
            }

            return false;
        }

        private int FindSelectedSkinIndex()
        {
            for (var index = 0; index < _selectableSkins.Count; index += 1)
            {
                if (_selectableSkins[index] == SelectedSkin)
                {
                    return index;
                }
            }

            return 0;
        }

        private void ApplySelectedSkinVisual()
        {
            if (playerController == null)
            {
                return;
            }

            playerController.ApplySkin(SelectedSkin);
        }

        private void RebuildSelectableSkins()
        {
            _selectableSkins.Clear();

            if (playerController != null)
            {
                var configuredSkins = playerController.GetConfiguredSkins();
                foreach (var configuredSkin in configuredSkins)
                {
                    _selectableSkins.Add(configuredSkin);
                }
            }

            if (_selectableSkins.Count > 0)
            {
                return;
            }

            foreach (PlayerSkinId skin in Enum.GetValues(typeof(PlayerSkinId)))
            {
                if (_selectableSkins.Contains(skin))
                {
                    continue;
                }

                _selectableSkins.Add(skin);
            }
        }

        private string GetSelectedSkinLabel()
        {
            return SelectedSkin switch
            {
                PlayerSkinId.Goblin => "GOBLIN",
                PlayerSkinId.Dwarf => "DWARF",
                _ => SelectedSkin.ToString().ToUpperInvariant()
            };
        }

        private string GetShortWalletAddress()
        {
            var key = Web3.Wallet?.Account?.PublicKey?.Key;
            if (string.IsNullOrWhiteSpace(key) || key.Length < 10)
            {
                return "Unknown";
            }

            return $"{key.Substring(0, 4)}...{key.Substring(key.Length - 4)}";
        }

        private string SanitizeDisplayName(string value)
        {
            var trimmedValue = (value ?? string.Empty).Trim();
            if (trimmedValue.Length <= maxDisplayNameLength)
            {
                return trimmedValue;
            }

            return trimmedValue.Substring(0, maxDisplayNameLength);
        }

        private void EmitState(string statusMessage)
        {
            var state = BuildState(statusMessage);
            OnStateChanged?.Invoke(state);

            if (logDebugMessages && !string.IsNullOrWhiteSpace(statusMessage))
            {
                Debug.Log($"[MainMenuCharacter] {statusMessage}");
            }
        }

        private MainMenuCharacterState BuildState(string statusMessage)
        {
            return new MainMenuCharacterState
            {
                IsReady = IsReady,
                HasProfile = HasExistingProfile,
                IsBusy = IsBusy,
                SelectedSkin = SelectedSkin,
                SelectedSkinLabel = GetSelectedSkinLabel(),
                WalletShortAddress = GetShortWalletAddress(),
                DisplayName = PendingDisplayName,
                StatusMessage = statusMessage
            };
        }

        private void EmitError(string message)
        {
            Debug.LogError($"[MainMenuCharacter] {message}");
            OnError?.Invoke(message);
            EmitState(message);
        }

        private async UniTaskVoid LoadSceneWithFadeAsync(string sceneName)
        {
            var sceneLoadController = SceneLoadController.GetOrCreate();
            if (!string.IsNullOrWhiteSpace(sceneName) &&
                string.Equals(sceneName, gameplaySceneName, StringComparison.Ordinal))
            {
                sceneLoadController.HoldBlackScreen("gameplay_doors_ready");
            }

            await sceneLoadController.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }

        private void SetPlayerVisible(bool isVisible)
        {
            if (playerController == null)
            {
                return;
            }

            playerController.gameObject.SetActive(isVisible);
        }
    }
}
