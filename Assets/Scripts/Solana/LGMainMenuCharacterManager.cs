using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using SeekerDungeon;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeekerDungeon.Solana
{
    public sealed class MainMenuCharacterState
    {
        public bool IsReady { get; init; }
        public bool HasProfile { get; init; }
        public bool HasUnsavedProfileChanges { get; init; }
        public bool IsBusy { get; init; }
        public PlayerSkinId SelectedSkin { get; init; }
        public string SelectedSkinLabel { get; init; }
        public string WalletShortAddress { get; init; }
        public string DisplayName { get; init; }
        public string PlayerDisplayName { get; init; }
        public string SolBalanceText { get; init; }
        public string SkrBalanceText { get; init; }
        public bool IsSessionReady { get; init; }
        public string SessionStatusText { get; init; }
        public string StatusMessage { get; init; }
        public bool IsLowBalanceModalVisible { get; init; }
        public string LowBalanceModalMessage { get; init; }
        public bool IsLowBalanceBlocking { get; init; }
        public bool IsDevnetRuntime { get; init; }
        public bool IsRequestingDevnetTopUp { get; init; }
    }

    /// <summary>
    /// Handles MainMenu character creation/profile flow and skin placeholder switching.
    /// </summary>
    public sealed class LGMainMenuCharacterManager : MonoBehaviour
    {
        private const int DefaultMaxDisplayNameLength = 24;
        private const int PlayerInitFetchMaxAttempts = 12;
        private const int PlayerInitFetchDelayMs = 500;
        private const int ProfileFetchMaxAttempts = 12;
        private const int ProfileFetchDelayMs = 500;
        private const string LocalSeekerIdentityConfigResourcePath = "LocalSecrets/LocalSeekerIdentityConfig";
        private const int DevnetTopUpRetryAttempts = 3;
        private const int DevnetTopUpRetryDelayMs = 1200;
        private const ulong DevnetTopUpLamports = 1_000_000_000UL;

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

        [Header("Seeker ID")]
        [SerializeField] private bool autoResolveSeekerIdAsDefaultName = true;
        [SerializeField] private bool allowSeekerIdentityLookupInEditor = false;
        [SerializeField] private bool preferEnhancedSeekerIdentityLookup = true;
        [SerializeField] private string seekerIdentityBackendResolveUrlTemplate = string.Empty;
        [SerializeField] private List<string> seekerIdentityBackendFallbackUrlTemplates = new();
        [SerializeField] private string seekerIdentityEnhancedHistoryUrlTemplate = string.Empty;
        [SerializeField] private List<string> seekerIdentityEnhancedFallbackUrlTemplates = new();
        [SerializeField] private string seekerIdentityRpcUrl = "https://api.mainnet-beta.solana.com";
        [SerializeField] private List<string> seekerIdentityFallbackRpcUrls = new();
        [SerializeField] private int seekerIdentitySignatureScanLimit = 20;

        [Header("Character Pop In")]
        [SerializeField] private bool animateInitialCharacterPop = true;
        [SerializeField] private float initialCharacterPopStartScale = 0.82f;
        [SerializeField] private float initialCharacterPopOvershootScale = 1.08f;
        [SerializeField] private float initialCharacterPopGrowDuration = 0.14f;
        [SerializeField] private float initialCharacterPopSettleDuration = 0.12f;

        [Header("Low Balance UX")]
        [SerializeField] private bool showLowBalanceModalOnMenuLoad = true;
        [SerializeField] private double minimumSolForCharacterCreate = 0.001d;
        [SerializeField] private double minimumSkrForCharacterCreate = 0.001d;

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        public event Action<MainMenuCharacterState> OnStateChanged;
        public event Action<string> OnError;

        public bool IsReady { get; private set; }
        public bool HasExistingProfile { get; private set; }
        public bool IsBusy { get; private set; }
        public Transform CharacterNameAnchorTransform =>
            playerController != null ? playerController.CharacterNameAnchorTransform : null;
        public LGPlayerController PreviewPlayerController => playerController;
        public PlayerSkinId SelectedSkin { get; private set; } = PlayerSkinId.Goblin;
        public string PendingDisplayName { get; private set; } = string.Empty;
        public bool HasUnsavedProfileChanges { get; private set; }
        private readonly List<PlayerSkinId> _selectableSkins = new();
        private PlayerSkinId _savedProfileSkin = PlayerSkinId.Goblin;
        private string _savedDisplayName = string.Empty;
        private string _solBalanceText = "--";
        private string _skrBalanceText = "--";
        private bool _isSessionReady;
        private bool _isRefreshingWalletPanel;
        private bool _isEnsuringSessionFromMenu;
        private bool _hasUserEditedDisplayName;
        private bool _isResolvingSeekerId;
        private bool _hasPlayedInitialCharacterPop;
        private Vector3 _playerBaseScale = Vector3.one;
        private Tween _playerPopTween;
        private double _solBalanceValue;
        private double _skrBalanceValue;
        private bool _hasWalletBalanceSnapshot;
        private bool _showLowBalanceModal;
        private string _lowBalanceModalMessage = string.Empty;
        private LocalSeekerIdentityConfig _localSeekerIdentityConfig;
        private bool _isRequestingDevnetTopUp;

        private bool IsSessionAutoPreparationEnabled
        {
            get
            {
                if (!prepareGameplaySessionInMenu)
                {
                    return false;
                }

#if UNITY_EDITOR
                if (walletSessionManager != null && walletSessionManager.SimulateMobileFlowInEditor)
                {
                    return false;
                }
#endif
                return true;
            }
        }

        private bool ShouldResolveSeekerIdentityDefaultName
        {
            get
            {
                if (!autoResolveSeekerIdAsDefaultName)
                {
                    return false;
                }

#if UNITY_EDITOR
                if (!allowSeekerIdentityLookupInEditor)
                {
                    return false;
                }
#endif

                return true;
            }
        }

        private void Awake()
        {
            if (lgManager == null)
            {
                lgManager = LGManager.EnsureInstance();
            }

            if (playerController == null)
            {
                playerController = FindObjectOfType<LGPlayerController>();
            }

            if (walletSessionManager == null)
            {
                walletSessionManager = LGWalletSessionManager.EnsureInstance();
            }

            _localSeekerIdentityConfig =
                Resources.Load<LocalSeekerIdentityConfig>(LocalSeekerIdentityConfigResourcePath);
            if (_localSeekerIdentityConfig != null && logDebugMessages)
            {
                Debug.Log(
                    $"[MainMenuCharacter] Loaded local Seeker identity config from Resources/{LocalSeekerIdentityConfigResourcePath}");
            }

            RebuildSelectableSkins();

            if (_selectableSkins.Count > 0)
            {
                SelectedSkin = _selectableSkins[0];
            }

            CapturePlayerBaseScale();
            SetPlayerVisible(false);
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        private void OnDestroy()
        {
            _playerPopTween?.Kill();
            _playerPopTween = null;
        }

        public MainMenuCharacterState GetCurrentState()
        {
            return BuildState(string.Empty);
        }

        public void SelectNextSkin()
        {
            if (IsBusy)
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
            RefreshUnsavedProfileChanges();
            EmitState("Choose your character");
        }

        public void SelectPreviousSkin()
        {
            if (IsBusy)
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
            RefreshUnsavedProfileChanges();
            EmitState("Choose your character");
        }

        public void SetPendingDisplayName(string nameInput)
        {
            if (IsBusy)
            {
                return;
            }

            _hasUserEditedDisplayName = true;
            PendingDisplayName = SanitizeDisplayName(nameInput);
            RefreshUnsavedProfileChanges();
            EmitState("Choose your character");
        }

        public async UniTask CreateCharacterAsync()
        {
            if (IsBusy)
            {
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

            if (IsLowBalanceForCharacterCreate())
            {
                ShowLowBalanceModal();
                EmitState("Wallet balance is too low.");
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(PendingDisplayName)
                ? GetShortWalletAddress()
                : PendingDisplayName;
            var requestedSkin = SelectedSkin;
            var isUpdatingExistingProfile = HasExistingProfile;

            IsBusy = true;
            EmitState(isUpdatingExistingProfile
                ? "Saving character onchain..."
                : "Creating character onchain...");

            try
            {
                await EnsurePlayerInitializedAsync();

                var signature = await lgManager.CreatePlayerProfile((ushort)SelectedSkin, displayName);
                if (string.IsNullOrWhiteSpace(signature))
                {
                    EmitError("Create profile transaction failed.");
                    return;
                }

                var profileReady = await WaitForProfileAfterCreateAsync();
                HasExistingProfile = profileReady && lgManager.CurrentProfileState != null;

                if (!HasExistingProfile)
                {
                    EmitError("Profile was not found after creation yet. Please wait and try again.");
                    return;
                }

                var onchainProfile = lgManager.CurrentProfileState;
                // Keep the locally selected skin after create/save so UI does not snap back
                // to a stale default profile value while onchain reads catch up.
                SelectedSkin = requestedSkin;
                PendingDisplayName = string.IsNullOrWhiteSpace(onchainProfile.DisplayName)
                    ? displayName
                    : onchainProfile.DisplayName;
                SetSavedProfileSnapshot(SelectedSkin, PendingDisplayName);
                RefreshUnsavedProfileChanges();

                ApplySelectedSkinVisual();
                await RefreshWalletPanelAsync();
                if (IsLowBalanceForCharacterCreate())
                {
                    ShowLowBalanceModal();
                }
                else if (IsSessionAutoPreparationEnabled)
                {
                    try
                    {
                        await PrepareGameplaySessionAsync();
                        await RefreshWalletPanelAsync();
                    }
                    catch (Exception exception)
                    {
                        if (logDebugMessages)
                        {
                            Debug.Log($"[MainMenuCharacter] Session prep after create skipped: {exception.Message}");
                        }
                    }
                }
                EmitState(isUpdatingExistingProfile ? "Character saved" : "Character created");
            }
            catch (Exception exception)
            {
                EmitError($"Save character failed: {exception.Message}");
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

            if (IsLowBalanceForCharacterCreate())
            {
                ShowLowBalanceModal();
                EmitState("Wallet balance is too low.");
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

        public void EnsureSessionReadyFromMenu()
        {
            if (!HasExistingProfile)
            {
                EmitState("Create your character before activating session.");
                return;
            }

            if (IsLowBalanceForCharacterCreate())
            {
                ShowLowBalanceModal();
                EmitState("Wallet balance is too low.");
                return;
            }

            EnsureSessionReadyFromMenuAsync().Forget();
        }

        public void RequestDevnetTopUpFromMenu()
        {
            RequestDevnetTopUpFromMenuAsync().Forget();
        }

        public void DismissLowBalanceModal()
        {
            DismissLowBalanceModalAsync().Forget();
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
            var startupStatusMessage = string.Empty;

            try
            {
                await lgManager.FetchGlobalState();
                await lgManager.FetchPlayerState();
                await lgManager.FetchPlayerProfile();

                var profile = lgManager.CurrentProfileState;
                HasExistingProfile = profile != null;

                if (HasExistingProfile)
                {
                    SelectedSkin = (PlayerSkinId)profile.SkinId;
                    PendingDisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? GetShortWalletAddress()
                        : profile.DisplayName;
                    _hasUserEditedDisplayName = false;
                    SetSavedProfileSnapshot(SelectedSkin, PendingDisplayName);
                    ApplySelectedSkinVisual();
                }
                else
                {
                    if (_selectableSkins.Count > 0)
                    {
                        SelectedSkin = _selectableSkins[0];
                    }

                    PendingDisplayName = GetShortWalletAddress();
                    _hasUserEditedDisplayName = false;
                    HasUnsavedProfileChanges = false;
                    ApplySelectedSkinVisual();
                }

                IsReady = true;
                await RefreshWalletPanelAsync();
                if (showLowBalanceModalOnMenuLoad && IsLowBalanceForCharacterCreate())
                {
                    ShowLowBalanceModal();
                }
                else if (IsSessionAutoPreparationEnabled && HasExistingProfile)
                {
                    await PrepareGameplaySessionAsync();
                    await RefreshWalletPanelAsync();
                }
                if (IsSessionAutoPreparationEnabled &&
                    walletSessionManager != null &&
                    HasExistingProfile &&
                    !walletSessionManager.CanUseLocalSessionSigning)
                {
                    startupStatusMessage = "Session unavailable. Gameplay will require wallet approval.";
                }

                if (!HasExistingProfile && ShouldResolveSeekerIdentityDefaultName)
                {
                    ResolveSeekerIdDefaultNameAsync().Forget();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MainMenuCharacter] Failed to load menu profile: {exception.Message}");
                HasExistingProfile = false;
                if (_selectableSkins.Count > 0)
                {
                    SelectedSkin = _selectableSkins[0];
                }

                PendingDisplayName = GetShortWalletAddress();
                _hasUserEditedDisplayName = false;
                HasUnsavedProfileChanges = false;
                ApplySelectedSkinVisual();
                IsReady = true;
                await RefreshWalletPanelAsync();
                if (showLowBalanceModalOnMenuLoad && IsLowBalanceForCharacterCreate())
                {
                    ShowLowBalanceModal();
                }
                if (ShouldResolveSeekerIdentityDefaultName)
                {
                    ResolveSeekerIdDefaultNameAsync().Forget();
                }
                startupStatusMessage = "Wallet connected. Character sync unavailable.";
            }
            finally
            {
                IsBusy = false;
                ShowPlayerAfterInitialize();
                EmitState(startupStatusMessage);
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

            if (!HasExistingProfile)
            {
                EmitState("Create your character before activating session.");
                return;
            }

            if (IsLowBalanceForCharacterCreate())
            {
                ShowLowBalanceModal();
                EmitState("Wallet balance is too low.");
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

        private async UniTaskVoid EnsureSessionReadyFromMenuAsync()
        {
            if (_isEnsuringSessionFromMenu || walletSessionManager == null)
            {
                return;
            }

            if (!HasExistingProfile)
            {
                EmitState("Create your character before activating session.");
                return;
            }

            if (IsLowBalanceForCharacterCreate())
            {
                ShowLowBalanceModal();
                EmitState("Wallet balance is too low.");
                return;
            }

            _isEnsuringSessionFromMenu = true;
            try
            {
                EmitState("Preparing gameplay session...");
                await walletSessionManager.EnsureGameplaySessionAsync(emitPromptStatus: true);
                await RefreshWalletPanelAsync();
                if (_isSessionReady)
                {
                    EmitState("Session ready");
                }
                else
                {
                    EmitState("Session unavailable. Gameplay may prompt wallet approvals.");
                }
            }
            finally
            {
                _isEnsuringSessionFromMenu = false;
            }
        }

        private async UniTaskVoid RequestDevnetTopUpFromMenuAsync()
        {
            if (_isRequestingDevnetTopUp)
            {
                return;
            }

            if (LGConfig.IsMainnetRuntime)
            {
                EmitState("Devnet top-up is unavailable on mainnet.");
                return;
            }

            var wallet = Web3.Wallet;
            if (wallet?.Account == null || wallet.ActiveRpcClient == null)
            {
                EmitError("Wallet is not connected.");
                return;
            }

            _isRequestingDevnetTopUp = true;
            try
            {
                for (var attempt = 1; attempt <= DevnetTopUpRetryAttempts; attempt += 1)
                {
                    EmitState($"Requesting devnet airdrop ({attempt}/{DevnetTopUpRetryAttempts})...");
                    var result = await wallet.RequestAirdrop(DevnetTopUpLamports, Commitment.Confirmed);
                    if (result.WasSuccessful && !string.IsNullOrWhiteSpace(result.Result))
                    {
                        await UniTask.Delay(DevnetTopUpRetryDelayMs);
                        await RefreshWalletPanelAsync();
                        if (!IsLowBalanceForCharacterCreate())
                        {
                            _showLowBalanceModal = false;
                            EmitState("Airdrop received. Balance updated.");
                            return;
                        }
                    }

                    if (attempt < DevnetTopUpRetryAttempts)
                    {
                        await UniTask.Delay(DevnetTopUpRetryDelayMs);
                    }
                }

                await RefreshWalletPanelAsync();
                ShowLowBalanceModal();
                EmitState("Airdrop did not clear low balance. Please try again.");
            }
            catch (Exception exception)
            {
                ShowLowBalanceModal();
                EmitError($"Devnet top-up failed: {exception.Message}");
            }
            finally
            {
                _isRequestingDevnetTopUp = false;
                EmitState(string.Empty);
            }
        }

        private async UniTaskVoid DismissLowBalanceModalAsync()
        {
            if (!_showLowBalanceModal || _isRequestingDevnetTopUp)
            {
                return;
            }

            _showLowBalanceModal = false;
            EmitState("Refreshing wallet balances...");
            try
            {
                await RefreshWalletPanelAsync();
            }
            catch (Exception exception)
            {
                if (logDebugMessages)
                {
                    Debug.Log($"[MainMenuCharacter] Balance refresh after dismiss skipped: {exception.Message}");
                }
            }

            EmitState(string.Empty);
        }

        private async UniTask RefreshWalletPanelAsync()
        {
            if (_isRefreshingWalletPanel)
            {
                return;
            }

            _isRefreshingWalletPanel = true;
            try
            {
                var wallet = Web3.Wallet;
                var account = wallet?.Account;
                if (wallet?.ActiveRpcClient == null || account == null)
                {
                    _solBalanceText = "--";
                    _skrBalanceText = "--";
                    _solBalanceValue = 0d;
                    _skrBalanceValue = 0d;
                    _hasWalletBalanceSnapshot = false;
                    _isSessionReady = false;
                    return;
                }

                _isSessionReady = walletSessionManager != null && walletSessionManager.CanUseLocalSessionSigning;
                var hasSolSnapshot = false;
                var hasSkrSnapshot = false;

                try
                {
                    var solResult = await wallet.ActiveRpcClient.GetBalanceAsync(account.PublicKey, Commitment.Confirmed);
                    if (solResult.WasSuccessful && solResult.Result != null)
                    {
                        _solBalanceValue = solResult.Result.Value / 1_000_000_000d;
                        _solBalanceText = $"{_solBalanceValue:F2}";
                        hasSolSnapshot = true;
                    }
                    else
                    {
                        _solBalanceText = "--";
                        _solBalanceValue = 0d;
                    }

                    var skrMint = new PublicKey(LGConfig.ActiveSkrMint);
                    var playerAta = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(account.PublicKey, skrMint);
                    var tokenResult = await wallet.ActiveRpcClient.GetTokenAccountBalanceAsync(playerAta, Commitment.Confirmed);
                    if (tokenResult.WasSuccessful && tokenResult.Result?.Value != null)
                    {
                        var rawAmount = tokenResult.Result.Value.Amount ?? "0";
                        var amountLamports = ulong.TryParse(rawAmount, out var parsedAmount) ? parsedAmount : 0UL;
                        _skrBalanceValue = amountLamports / (double)LGConfig.SKR_MULTIPLIER;
                        _skrBalanceText = $"{_skrBalanceValue:F2}";
                        hasSkrSnapshot = true;
                    }
                    else
                    {
                        _skrBalanceValue = 0d;
                        if (IsMissingTokenAccountReason(tokenResult?.Reason))
                        {
                            _skrBalanceText = "0";
                            hasSkrSnapshot = true;
                        }
                        else
                        {
                            _skrBalanceText = "--";
                        }
                    }
                }
                catch (Exception exception)
                {
                    _solBalanceText = "--";
                    _skrBalanceText = "--";
                    _solBalanceValue = 0d;
                    _skrBalanceValue = 0d;
                    hasSolSnapshot = false;
                    hasSkrSnapshot = false;
                    if (logDebugMessages)
                    {
                        Debug.Log($"[MainMenuCharacter] Balance refresh skipped: {exception.Message}");
                    }
                }

                _hasWalletBalanceSnapshot = hasSolSnapshot && hasSkrSnapshot;
            }
            finally
            {
                _isRefreshingWalletPanel = false;
            }
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

        private async UniTask<bool> WaitForProfileAfterCreateAsync()
        {
            for (var attempt = 0; attempt < ProfileFetchMaxAttempts; attempt += 1)
            {
                await lgManager.FetchPlayerProfile();
                if (lgManager.CurrentProfileState != null)
                {
                    return true;
                }

                if (attempt < ProfileFetchMaxAttempts - 1)
                {
                    await UniTask.Delay(ProfileFetchDelayMs);
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
                PlayerSkinId.CheekyGoblin => "Cheeky Goblin",
                PlayerSkinId.ScrappyDwarfCharacter => "Scrappy Dwarf",
                PlayerSkinId.DrunkDwarfCharacter => "Drunk Dwarf",
                PlayerSkinId.FatDwarfCharacter => "Fat Dwarf",
                PlayerSkinId.FriendlyGoblin => "Friendly Goblin",
                PlayerSkinId.GingerBearDwarfVariant => "Ginger Dwarf",
                PlayerSkinId.HappyDrunkDwarf => "Happy Dwarf",
                PlayerSkinId.IdleGoblin => "Idle Goblin",
                PlayerSkinId.IdleHumanCharacter => "Idle Human",
                PlayerSkinId.JollyDwarfCharacter => "Jolly Dwarf",
                PlayerSkinId.JollyDwarfVariant => "Jolly Dwarf II",
                PlayerSkinId.OldDwarfCharacter => "Old Dwarf",
                PlayerSkinId.ScrappyDwarfGingerBeard => "Ginger Beard",
                PlayerSkinId.ScrappyDwarfVariant => "Scrappy Dwarf II",
                PlayerSkinId.ScrappyHumanAssassin => "Human Assassin",
                PlayerSkinId.ScrappySkeleton => "Scrappy Skeleton",
                PlayerSkinId.SinisterHoodedFigure => "Hooded Figure",
                _ => SelectedSkin.ToString()
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

        private bool GetPreferEnhancedSeekerLookup()
        {
            if (_localSeekerIdentityConfig != null)
            {
                return _localSeekerIdentityConfig.PreferEnhancedLookup;
            }

            return preferEnhancedSeekerIdentityLookup;
        }

        private string GetSeekerIdentityRpcUrl()
        {
            var localValue = _localSeekerIdentityConfig != null
                ? _localSeekerIdentityConfig.MainnetRpcUrl
                : null;
            if (!string.IsNullOrWhiteSpace(localValue))
            {
                return localValue.Trim();
            }

            if (!string.IsNullOrWhiteSpace(seekerIdentityRpcUrl))
            {
                return seekerIdentityRpcUrl.Trim();
            }

            return "https://api.mainnet-beta.solana.com";
        }

        private IReadOnlyList<string> GetSeekerIdentityFallbackRpcUrls()
        {
            if (_localSeekerIdentityConfig != null &&
                _localSeekerIdentityConfig.FallbackMainnetRpcUrls != null &&
                _localSeekerIdentityConfig.FallbackMainnetRpcUrls.Count > 0)
            {
                return _localSeekerIdentityConfig.FallbackMainnetRpcUrls;
            }

            return seekerIdentityFallbackRpcUrls;
        }

        private string GetSeekerIdentityEnhancedUrlTemplate()
        {
            var localValue = _localSeekerIdentityConfig != null
                ? _localSeekerIdentityConfig.EnhancedHistoryUrlTemplate
                : null;
            if (!string.IsNullOrWhiteSpace(localValue))
            {
                return localValue.Trim();
            }

            if (!string.IsNullOrWhiteSpace(seekerIdentityEnhancedHistoryUrlTemplate))
            {
                return seekerIdentityEnhancedHistoryUrlTemplate.Trim();
            }

            return string.Empty;
        }

        private string GetSeekerIdentityBackendUrlTemplate()
        {
            var localValue = _localSeekerIdentityConfig != null
                ? _localSeekerIdentityConfig.BackendResolveUrlTemplate
                : null;
            if (!string.IsNullOrWhiteSpace(localValue))
            {
                return localValue.Trim();
            }

            if (!string.IsNullOrWhiteSpace(seekerIdentityBackendResolveUrlTemplate))
            {
                return seekerIdentityBackendResolveUrlTemplate.Trim();
            }

            return string.Empty;
        }

        private IReadOnlyList<string> GetSeekerIdentityBackendFallbackTemplates()
        {
            if (_localSeekerIdentityConfig != null &&
                _localSeekerIdentityConfig.FallbackBackendResolveUrlTemplates != null &&
                _localSeekerIdentityConfig.FallbackBackendResolveUrlTemplates.Count > 0)
            {
                return _localSeekerIdentityConfig.FallbackBackendResolveUrlTemplates;
            }

            return seekerIdentityBackendFallbackUrlTemplates;
        }

        private IReadOnlyList<string> GetSeekerIdentityEnhancedFallbackTemplates()
        {
            if (_localSeekerIdentityConfig != null &&
                _localSeekerIdentityConfig.FallbackEnhancedHistoryUrlTemplates != null &&
                _localSeekerIdentityConfig.FallbackEnhancedHistoryUrlTemplates.Count > 0)
            {
                return _localSeekerIdentityConfig.FallbackEnhancedHistoryUrlTemplates;
            }

            return seekerIdentityEnhancedFallbackUrlTemplates;
        }

        private async UniTaskVoid ResolveSeekerIdDefaultNameAsync()
        {
            if (!ShouldResolveSeekerIdentityDefaultName)
            {
                return;
            }

            if (_isResolvingSeekerId || HasExistingProfile || _hasUserEditedDisplayName)
            {
                return;
            }

            var walletPublicKey = Web3.Wallet?.Account?.PublicKey;
            if (walletPublicKey == null)
            {
                return;
            }

            _isResolvingSeekerId = true;
            try
            {
                var seekerId = await SeekerIdentityResolver.TryResolveSkrForWalletAsync(
                    walletPublicKey,
                    GetSeekerIdentityRpcUrl(),
                    seekerIdentitySignatureScanLimit,
                    GetSeekerIdentityFallbackRpcUrls(),
                    GetPreferEnhancedSeekerLookup(),
                    GetSeekerIdentityEnhancedUrlTemplate(),
                    GetSeekerIdentityEnhancedFallbackTemplates(),
                    GetSeekerIdentityBackendUrlTemplate(),
                    GetSeekerIdentityBackendFallbackTemplates());
                if (string.IsNullOrWhiteSpace(seekerId))
                {
                    return;
                }

                if (HasExistingProfile || _hasUserEditedDisplayName)
                {
                    return;
                }

                var currentDefault = GetShortWalletAddress();
                if (!string.Equals(PendingDisplayName, currentDefault, StringComparison.Ordinal))
                {
                    return;
                }

                PendingDisplayName = SanitizeDisplayName(seekerId);
                RefreshUnsavedProfileChanges();
                EmitState(string.Empty);

                if (logDebugMessages)
                {
                    Debug.Log($"[MainMenuCharacter] Resolved Seeker ID default name: {PendingDisplayName}");
                }
            }
            catch (Exception exception)
            {
                if (logDebugMessages)
                {
                    Debug.Log($"[MainMenuCharacter] Seeker ID resolution skipped: {exception.Message}");
                }
            }
            finally
            {
                _isResolvingSeekerId = false;
            }
        }

        private void SetSavedProfileSnapshot(PlayerSkinId skin, string displayName)
        {
            _savedProfileSkin = skin;
            _savedDisplayName = SanitizeDisplayName(displayName);
        }

        private void RefreshUnsavedProfileChanges()
        {
            if (!HasExistingProfile)
            {
                HasUnsavedProfileChanges = false;
                return;
            }

            HasUnsavedProfileChanges =
                SelectedSkin != _savedProfileSkin ||
                !string.Equals(
                    SanitizeDisplayName(PendingDisplayName),
                    _savedDisplayName,
                    StringComparison.Ordinal);
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
            var playerName = HasExistingProfile && !string.IsNullOrWhiteSpace(PendingDisplayName)
                ? PendingDisplayName
                : GetShortWalletAddress();
            var lowBalanceBlocking = IsLowBalanceForCharacterCreate();
            return new MainMenuCharacterState
            {
                IsReady = IsReady,
                HasProfile = HasExistingProfile,
                HasUnsavedProfileChanges = HasUnsavedProfileChanges,
                IsBusy = IsBusy,
                SelectedSkin = SelectedSkin,
                SelectedSkinLabel = GetSelectedSkinLabel(),
                WalletShortAddress = GetShortWalletAddress(),
                DisplayName = PendingDisplayName,
                PlayerDisplayName = playerName,
                SolBalanceText = _solBalanceText,
                SkrBalanceText = _skrBalanceText,
                IsSessionReady = _isSessionReady,
                SessionStatusText = _isSessionReady ? "Ready" : "Not Ready",
                StatusMessage = statusMessage,
                IsLowBalanceModalVisible = _showLowBalanceModal,
                LowBalanceModalMessage = _lowBalanceModalMessage,
                IsLowBalanceBlocking = lowBalanceBlocking,
                IsDevnetRuntime = !LGConfig.IsMainnetRuntime,
                IsRequestingDevnetTopUp = _isRequestingDevnetTopUp
            };
        }

        private void EmitError(string message)
        {
            Debug.LogError($"[MainMenuCharacter] {message}");
            OnError?.Invoke(message);
            EmitState(message);
        }

        private bool IsLowBalanceForCharacterCreate()
        {
            if (!_hasWalletBalanceSnapshot)
            {
                return false;
            }

            return
                _solBalanceValue < minimumSolForCharacterCreate ||
                _skrBalanceValue < minimumSkrForCharacterCreate;
        }

        private void ShowLowBalanceModal()
        {
            _showLowBalanceModal = true;
            _lowBalanceModalMessage =
                $"Your SOL and SKR balances are too low for character setup.\n\n" +
                $"SOL: {_solBalanceText} / min {minimumSolForCharacterCreate:F3}\n" +
                $"SKR: {_skrBalanceText} / min {minimumSkrForCharacterCreate:F3}\n\n" +
                "Please send more SOL and SKR to your connected wallet, then try again.";
        }

        private static bool IsMissingTokenAccountReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            return
                reason.IndexOf("could not find account", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("account not found", StringComparison.OrdinalIgnoreCase) >= 0;
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

            if (!isVisible)
            {
                _playerPopTween?.Kill();
                _playerPopTween = null;
            }

            playerController.gameObject.SetActive(isVisible);
        }

        private void ShowPlayerAfterInitialize()
        {
            if (playerController == null)
            {
                return;
            }

            if (_hasPlayedInitialCharacterPop || !animateInitialCharacterPop)
            {
                SetPlayerVisible(true);
                ResetPlayerScaleImmediate();
                return;
            }

            SetPlayerVisible(true);
            var playerTransform = playerController.transform;
            if (playerTransform == null)
            {
                return;
            }

            _hasPlayedInitialCharacterPop = true;
            _playerPopTween?.Kill();
            playerTransform.localScale = _playerBaseScale * Mathf.Max(0.01f, initialCharacterPopStartScale);
            _playerPopTween = DOTween.Sequence()
                .Append(playerTransform
                    .DOScale(_playerBaseScale * Mathf.Max(0.01f, initialCharacterPopOvershootScale), initialCharacterPopGrowDuration)
                    .SetEase(Ease.OutQuad))
                .Append(playerTransform
                    .DOScale(_playerBaseScale, initialCharacterPopSettleDuration)
                    .SetEase(Ease.OutBack))
                .SetUpdate(true)
                .OnKill(() =>
                {
                    if (playerTransform != null)
                    {
                        playerTransform.localScale = _playerBaseScale;
                    }
                });
        }

        private void CapturePlayerBaseScale()
        {
            if (playerController == null || playerController.transform == null)
            {
                return;
            }

            _playerBaseScale = playerController.transform.localScale;
        }

        private void ResetPlayerScaleImmediate()
        {
            if (playerController == null || playerController.transform == null)
            {
                return;
            }

            playerController.transform.localScale = _playerBaseScale;
        }
    }
}
