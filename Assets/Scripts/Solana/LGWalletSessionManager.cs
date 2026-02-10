using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Solana.Unity.Programs;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Wallet.Bip39;
using UnityEngine;
using UnityEngine.Networking;
using Chaindepth.Program;
using Chaindepth.Errors;

namespace SeekerDungeon.Solana
{
    [Flags]
    public enum SessionInstructionAllowlist : int
    {
        None = 0,
        BoostJob = 1 << 0,
        AbandonJob = 1 << 1,
        ClaimJobReward = 1 << 2,
        EquipItem = 1 << 3,
        SetPlayerSkin = 1 << 4,
        RemoveInventoryItem = 1 << 5,
        MovePlayer = 1 << 6,
        JoinJob = 1 << 7,
        CompleteJob = 1 << 8,
        CreatePlayerProfile = 1 << 9,
        JoinBossFight = 1 << 10,
        LootChest = 1 << 11,
        LootBoss = 1 << 12
    }

    public enum WalletLoginMode
    {
        Auto = 0,
        EditorDevWallet = 1,
        WalletAdapter = 2
    }

    /// <summary>
    /// Wallet/session manager for LG.
    /// - Handles wallet connect/disconnect without SDK template prefabs.
    /// - Supports editor test login with an in-game wallet.
    /// - Supports Solana Wallet Adapter login for device builds.
    /// - Handles onchain begin_session/end_session for gameplay sessions.
    /// </summary>
    public sealed class LGWalletSessionManager : MonoBehaviour
    {
        private const string WalletConnectIntentPrefKey = "LG_WALLET_CONNECT_INTENT_V1";
        private const string EditorWalletSlotPrefKey = "LG_EDITOR_DEV_WALLET_SLOT_V1";
        private const string EditorWalletSlotMnemonicPrefPrefix = "LG_EDITOR_DEV_WALLET_SLOT_MNEMONIC_V1_";

        public static LGWalletSessionManager Instance { get; private set; }

        public static LGWalletSessionManager EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindExistingInstance();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var bootstrapObject = new GameObject(nameof(LGWalletSessionManager));
            return bootstrapObject.AddComponent<LGWalletSessionManager>();
        }

        private static LGWalletSessionManager FindExistingInstance()
        {
            var allInstances = Resources.FindObjectsOfTypeAll<LGWalletSessionManager>();
            for (var index = 0; index < allInstances.Length; index += 1)
            {
                var candidate = allInstances[index];
                if (candidate == null || candidate.gameObject == null)
                {
                    continue;
                }

                if (!candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        [Header("Network")]
        [SerializeField] private string rpcUrl = LGConfig.RPC_URL;
        [SerializeField] private string fallbackRpcUrl = LGConfig.RPC_FALLBACK_URL;
        [SerializeField] private Commitment commitment = Commitment.Confirmed;

        [Header("Startup Login")]
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool allowAutoConnectOnDeviceBuilds = false;
        [SerializeField] private bool requireManualConnectOnDevice = true;
        [SerializeField] private WalletLoginMode startupLoginMode = WalletLoginMode.Auto;
        [SerializeField] private bool autoUseEditorDevWallet = true;
        [SerializeField] private string editorDevWalletPassword = "seeker-dev-wallet";
        [SerializeField] private bool useEditorWalletSlots = true;
        [SerializeField] private string editorWalletPasswordPrefix = "seeker-dev-wallet-slot-";
        [SerializeField] private int editorWalletSlot = 0;
        [SerializeField] private bool persistEditorWalletSlot = true;
        [SerializeField] private bool createEditorDevWalletIfMissing = true;
        [SerializeField] private bool requestAirdropIfLowSolInEditor = true;
        [SerializeField] private double editorLowSolThreshold = 0.2d;
        [SerializeField] private ulong editorAirdropLamports = 1_000_000_000UL;

        [Header("Editor Simulation")]
        [SerializeField] private bool simulateMobileFlowInEditor = false;

        [Header("Session Defaults")]
        [SerializeField] private bool autoBeginSessionAfterConnect;
        [SerializeField] private int sessionDurationMinutes = 60;
        [SerializeField] private ulong defaultSessionMaxTokenSpend = 200_000_000UL;
        [SerializeField] private bool autoFundSessionSigner = true;
        [SerializeField] private ulong sessionSignerMinLamports = 5_000_000UL;
        [SerializeField] private ulong sessionSignerTopUpLamports = 10_000_000UL;
        [SerializeField] private int defaultAllowlistMask =
            (int)(
                SessionInstructionAllowlist.MovePlayer |
                SessionInstructionAllowlist.JoinJob |
                SessionInstructionAllowlist.CompleteJob |
                SessionInstructionAllowlist.BoostJob |
                SessionInstructionAllowlist.AbandonJob |
                SessionInstructionAllowlist.ClaimJobReward |
                SessionInstructionAllowlist.EquipItem |
                SessionInstructionAllowlist.SetPlayerSkin |
                SessionInstructionAllowlist.CreatePlayerProfile |
                SessionInstructionAllowlist.JoinBossFight |
                SessionInstructionAllowlist.LootChest |
                SessionInstructionAllowlist.LootBoss
            );

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        public event Action<bool> OnWalletConnectionChanged;
        public event Action<bool> OnSessionStateChanged;
        public event Action<string> OnStatus;
        public event Action<string> OnError;

        public bool IsWalletConnected => Web3.Wallet?.Account != null;
        public PublicKey ConnectedWalletPublicKey => Web3.Wallet?.Account?.PublicKey;
        public WalletLoginMode ActiveWalletMode { get; private set; } = WalletLoginMode.Auto;
        public bool HasWalletConnectIntent => PlayerPrefs.GetInt(WalletConnectIntentPrefKey, 0) == 1;
        public int EditorWalletSlot => editorWalletSlot;
        public bool SimulateMobileFlowInEditor
        {
            get
            {
#if UNITY_EDITOR
                return simulateMobileFlowInEditor;
#else
                return false;
#endif
            }
        }

        public bool HasActiveOnchainSession => _hasActiveOnchainSession && _sessionSignerAccount != null;
        public bool CanUseLocalSessionSigning =>
            _hasActiveOnchainSession &&
            _sessionSignerAccount != null &&
            _sessionAuthorityPda != null &&
            _isSessionSignerFunded;
        public PublicKey ActiveSessionSignerPublicKey => _sessionSignerAccount?.PublicKey;
        public Account ActiveSessionSignerAccount => _sessionSignerAccount;
        public PublicKey ActiveSessionAuthorityPda => _sessionAuthorityPda;

        private PublicKey _programId;
        private PublicKey _globalPda;
        private IRpcClient _fallbackRpcClient;
        private IRpcClient _secondaryRpcClient;

        private Account _sessionSignerAccount;
        private PublicKey _sessionAuthorityPda;
        private bool _hasActiveOnchainSession;
        private bool _isSessionSignerFunded;
        private bool _isEnsuringGameplaySession;
        private const int MaxTransientSendAttemptsPerRpc = 2;
        private const int BaseTransientRetryDelayMs = 300;
        private const int RawHttpProbeTimeoutSeconds = 20;
        private const int RawHttpBodyLogLimit = 400;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _programId = new PublicKey(LGConfig.PROGRAM_ID);
            _globalPda = new PublicKey(LGConfig.GLOBAL_PDA);
            InitializeEditorWalletSlot();
            rpcUrl = LGConfig.GetRuntimeRpcUrl(rpcUrl);
            fallbackRpcUrl = LGConfig.GetRuntimeFallbackRpcUrl(fallbackRpcUrl, rpcUrl);
            _fallbackRpcClient = ClientFactory.GetClient(rpcUrl);
            _secondaryRpcClient = string.Equals(rpcUrl, fallbackRpcUrl, StringComparison.OrdinalIgnoreCase)
                ? null
                : ClientFactory.GetClient(fallbackRpcUrl);

            EnsureWeb3ExistsAndConfigured();
            EmitStatus($"RPC primary={rpcUrl} fallback={fallbackRpcUrl}");
            EmitStatus($"Runtime network={LGConfig.ActiveRuntimeNetwork}");
        }

        private void OnEnable()
        {
            Web3.OnWalletChangeState += HandleWalletStateChanged;
        }

        private void OnDisable()
        {
            Web3.OnWalletChangeState -= HandleWalletStateChanged;
        }

        private void Start()
        {
            if (!connectOnStart)
            {
                return;
            }

#if UNITY_EDITOR
            if (simulateMobileFlowInEditor)
            {
                EmitStatus("Simulated mobile flow enabled in editor. Waiting for manual connect.");
                return;
            }
#endif

            if (!Application.isEditor && Application.isMobilePlatform && requireManualConnectOnDevice)
            {
                if (!HasWalletConnectIntent)
                {
                    EmitStatus("Manual connect required on device. Waiting for user action.");
                    return;
                }

                EmitStatus("Auto-connect allowed on device from remembered wallet intent.");
            }

            if (!Application.isEditor && Application.isMobilePlatform && !allowAutoConnectOnDeviceBuilds)
            {
                EmitStatus("Auto-connect disabled on device builds. Waiting for user action.");
                return;
            }

            ConnectAsync(startupLoginMode).Forget();
        }

        public async UniTask<bool> ConnectAsync(WalletLoginMode mode = WalletLoginMode.Auto)
        {
            EnsureWeb3ExistsAndConfigured();

            if (IsWalletConnected)
            {
                EmitStatus($"Wallet already connected: {ConnectedWalletPublicKey}");
                return true;
            }

            var resolvedMode = ResolveLoginMode(mode);
            try
            {
                switch (resolvedMode)
                {
                    case WalletLoginMode.EditorDevWallet:
                        await ConnectEditorDevWalletAsync();
                        break;
                    case WalletLoginMode.WalletAdapter:
                        await ConnectWalletAdapterAsync();
                        break;
                    default:
                        throw new InvalidOperationException("Invalid wallet login mode.");
                }

                if (!IsWalletConnected)
                {
                    if (resolvedMode == WalletLoginMode.WalletAdapter)
                    {
                        ClearWalletConnectIntent();
                        ResetWalletAdapterConnectionState();
                    }

                    EmitError("Wallet connection failed.");
                    return false;
                }

                ActiveWalletMode = resolvedMode;
                if (resolvedMode == WalletLoginMode.WalletAdapter)
                {
                    MarkWalletConnectIntent();
                }
                var editorSlotSuffix =
                    resolvedMode == WalletLoginMode.EditorDevWallet &&
                    Application.isEditor &&
                    useEditorWalletSlots
                        ? $" slot=#{editorWalletSlot}"
                        : string.Empty;
                EmitStatus($"Wallet connected ({resolvedMode}{editorSlotSuffix}): {ConnectedWalletPublicKey}");

                if (requestAirdropIfLowSolInEditor && Application.isEditor)
                {
                    await TryAirdropIfNeeded();
                }

                if (autoBeginSessionAfterConnect)
                {
                    await BeginGameplaySessionAsync();
                }

                return true;
            }
            catch (Exception exception)
            {
                if (resolvedMode == WalletLoginMode.WalletAdapter)
                {
                    ClearWalletConnectIntent();
                    ResetWalletAdapterConnectionState();
                }

                Debug.LogException(exception);
                EmitError($"Connect failed: {exception.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            if (Web3.Instance != null)
            {
                Web3.Instance.Logout();
            }

            ClearWalletConnectIntent();
            ClearSessionState();
            ActiveWalletMode = WalletLoginMode.Auto;
            EmitStatus("Wallet disconnected.");
        }

        public void MarkWalletConnectIntent()
        {
            PlayerPrefs.SetInt(WalletConnectIntentPrefKey, 1);
            PlayerPrefs.Save();
        }

        public void ClearWalletConnectIntent()
        {
            if (!PlayerPrefs.HasKey(WalletConnectIntentPrefKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(WalletConnectIntentPrefKey);
            PlayerPrefs.Save();
        }

        public async UniTask<bool> SwitchEditorWalletSlotAsync(int slot, bool reconnect = true)
        {
            if (!Application.isEditor)
            {
                EmitError("Editor wallet slot switching is only available in Unity Editor.");
                return false;
            }

            SetEditorWalletSlot(slot);
            if (!reconnect)
            {
                return true;
            }

            if (IsWalletConnected)
            {
                Disconnect();
            }

            return await ConnectAsync(WalletLoginMode.EditorDevWallet);
        }

        public async UniTask<bool> UseNextEditorWalletAsync(bool reconnect = true)
        {
            return await SwitchEditorWalletSlotAsync(editorWalletSlot + 1, reconnect);
        }

        public async UniTask<bool> UsePreviousEditorWalletAsync(bool reconnect = true)
        {
            return await SwitchEditorWalletSlotAsync(Math.Max(0, editorWalletSlot - 1), reconnect);
        }

        public string GetEditorWalletSelectionLabel()
        {
            if (!Application.isEditor || !useEditorWalletSlots)
            {
                return string.Empty;
            }

            return $"Editor wallet slot #{editorWalletSlot}";
        }

        public async UniTask<bool> BeginGameplaySessionAsync(
            SessionInstructionAllowlist? allowlistOverride = null,
            ulong? maxTokenSpendOverride = null,
            int? durationMinutesOverride = null)
        {
            if (!IsWalletConnected)
            {
                EmitError("Cannot begin session: wallet not connected.");
                return false;
            }

            var player = ConnectedWalletPublicKey;
            var playerPda = DerivePlayerPda(player);
            var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                player,
                new PublicKey(LGConfig.ActiveSkrMint)
            );

            var playerTokenAccountReady = await EnsurePlayerTokenAccountExistsAsync(player, playerTokenAccount);
            if (!playerTokenAccountReady)
            {
                ClearSessionState();
                return false;
            }

            var durationMinutes = Math.Max(1, durationMinutesOverride ?? sessionDurationMinutes);
            var resolvedAllowlist = allowlistOverride ?? (SessionInstructionAllowlist)defaultAllowlistMask;
            var allowlist = (ulong)resolvedAllowlist;
            var maxTokenSpend = maxTokenSpendOverride ?? defaultSessionMaxTokenSpend;

            if (allowlist == 0)
            {
                EmitError("Cannot begin session: instruction allowlist is empty.");
                return false;
            }

            _sessionSignerAccount = new Account();
            _sessionAuthorityPda = DeriveSessionAuthorityPda(player, _sessionSignerAccount.PublicKey);

            var rpc = GetRpcClient();
            var slotResult = await rpc.GetSlotAsync(commitment);
            if (!slotResult.WasSuccessful || slotResult.Result == null)
            {
                EmitError($"Failed to fetch slot: {slotResult.Reason}");
                ClearSessionState();
                return false;
            }

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAtSlot = slotResult.Result + ((ulong)durationMinutes * 150UL);
            var expiresAtUnix = nowUnix + durationMinutes * 60L;

            var instruction = ChaindepthProgram.BeginSession(
                new BeginSessionAccounts
                {
                    Player = player,
                    SessionKey = _sessionSignerAccount.PublicKey,
                    PlayerAccount = playerPda,
                    Global = _globalPda,
                    PlayerTokenAccount = playerTokenAccount,
                    SessionAuthority = _sessionAuthorityPda,
                    TokenProgram = TokenProgram.ProgramIdKey,
                    SystemProgram = SystemProgram.ProgramIdKey
                },
                expiresAtSlot,
                expiresAtUnix,
                allowlist,
                maxTokenSpend,
                _programId
            );

            var instructions = new List<TransactionInstruction>();
            if (autoFundSessionSigner)
            {
                var sessionTopUpAmount = Math.Max(sessionSignerTopUpLamports, sessionSignerMinLamports);
                if (sessionTopUpAmount > 0)
                {
                    instructions.Add(SystemProgram.Transfer(
                        player,
                        _sessionSignerAccount.PublicKey,
                        sessionTopUpAmount));
                    EmitStatus(
                        $"Preparing gameplay session ({sessionTopUpAmount / 1_000_000_000d:F6} SOL top-up). Approve in wallet...");
                }
            }

            instructions.Add(instruction);

            var signature = await SendInstructionsSignedByLocalAccounts(
                instructions,
                new List<Account> { Web3.Wallet.Account, _sessionSignerAccount }
            );
            if (string.IsNullOrEmpty(signature))
            {
                ClearSessionState();
                return false;
            }

            _hasActiveOnchainSession = true;
            _isSessionSignerFunded = true;
            OnSessionStateChanged?.Invoke(true);
            EmitStatus($"Session started. Session key={_sessionSignerAccount.PublicKey} tx={signature}");

            return true;
        }

        private async UniTask<bool> EnsurePlayerTokenAccountExistsAsync(PublicKey player, PublicKey playerTokenAccount)
        {
            var rpc = GetRpcClient();
            if (rpc == null)
            {
                EmitError("RPC unavailable while validating player token account.");
                return false;
            }

            var accountInfo = await rpc.GetAccountInfoAsync(playerTokenAccount, commitment);
            if (accountInfo.WasSuccessful && accountInfo.Result?.Value != null)
            {
                return true;
            }

            EmitStatus("Creating player SKR token account...");
            var createAtaInstruction = AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(
                player,
                player,
                new PublicKey(LGConfig.ActiveSkrMint));
            var createAtaSignature = await SendInstructionSignedByLocalAccounts(
                createAtaInstruction,
                new List<Account> { Web3.Wallet.Account });
            if (string.IsNullOrWhiteSpace(createAtaSignature))
            {
                EmitError("Failed to create player SKR token account.");
                return false;
            }

            EmitStatus($"Player SKR token account ready. tx={createAtaSignature}");
            return true;
        }

        public bool IsSessionRecoverableProgramError(uint? errorCode)
        {
            if (!errorCode.HasValue)
            {
                return false;
            }

            var value = errorCode.Value;
            return
                value == (uint)ChaindepthErrorKind.SessionExpired ||
                value == (uint)ChaindepthErrorKind.SessionInactive ||
                value == (uint)ChaindepthErrorKind.SessionInstructionNotAllowed ||
                value == (uint)ChaindepthErrorKind.SessionSpendCapExceeded ||
                value == (uint)ChaindepthErrorKind.Unauthorized;
        }

        public async UniTask<bool> EnsureGameplaySessionAsync(bool emitPromptStatus = true)
        {
            if (_hasActiveOnchainSession && _sessionSignerAccount != null && _sessionAuthorityPda != null)
            {
                var funded = await EnsureSessionSignerFundedAsync(emitPromptStatus: false);
                if (funded)
                {
                    return true;
                }
            }

            while (_isEnsuringGameplaySession)
            {
                await UniTask.Yield();
                if (CanUseLocalSessionSigning)
                {
                    return true;
                }
            }

            _isEnsuringGameplaySession = true;
            try
            {
                if (_hasActiveOnchainSession &&
                    _sessionSignerAccount != null &&
                    _sessionAuthorityPda != null)
                {
                    var funded = await EnsureSessionSignerFundedAsync(emitPromptStatus);
                    if (funded)
                    {
                        if (emitPromptStatus)
                        {
                            EmitStatus("Session restored. Retrying action...");
                        }

                        return true;
                    }
                }

                if (emitPromptStatus)
                {
                    EmitStatus("Session expired. Re-starting session...");
                }

                var started = await BeginGameplaySessionAsync();
                if (started && emitPromptStatus)
                {
                    EmitStatus("Session restored. Retrying action...");
                }
                else if (!started && emitPromptStatus)
                {
                    EmitError("Session restart failed.");
                }

                return started;
            }
            finally
            {
                _isEnsuringGameplaySession = false;
            }
        }

        public async UniTask<bool> EndGameplaySessionAsync()
        {
            if (!IsWalletConnected)
            {
                EmitError("Cannot end session: wallet not connected.");
                return false;
            }

            if (!_hasActiveOnchainSession || _sessionSignerAccount == null)
            {
                EmitStatus("No active onchain session to end.");
                return true;
            }

            var player = ConnectedWalletPublicKey;
            var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                player,
                new PublicKey(LGConfig.ActiveSkrMint)
            );

            var instruction = ChaindepthProgram.EndSession(
                new EndSessionAccounts
                {
                    Player = player,
                    SessionKey = _sessionSignerAccount.PublicKey,
                    SessionAuthority = _sessionAuthorityPda,
                    Global = _globalPda,
                    PlayerTokenAccount = playerTokenAccount,
                    TokenProgram = TokenProgram.ProgramIdKey
                },
                _programId
            );

            var signature = await SendInstructionSignedByLocalAccounts(
                instruction,
                new List<Account> { Web3.Wallet.Account }
            );
            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }

            ClearSessionState();
            OnSessionStateChanged?.Invoke(false);
            EmitStatus($"Session ended. tx={signature}");
            return true;
        }

        public async UniTask<bool> EnsureSessionSignerFundedAsync(bool emitPromptStatus = true)
        {
            if (_sessionSignerAccount == null)
            {
                EmitError("Session signer is unavailable for funding.");
                _isSessionSignerFunded = false;
                return false;
            }

            var rpc = GetRpcClient();
            if (rpc == null)
            {
                EmitError("RPC unavailable for session signer funding.");
                _isSessionSignerFunded = false;
                return false;
            }

            var sessionBalanceResult = await rpc.GetBalanceAsync(_sessionSignerAccount.PublicKey, commitment);
            if (!sessionBalanceResult.WasSuccessful || sessionBalanceResult.Result == null)
            {
                EmitError($"Failed to fetch session signer balance: {sessionBalanceResult.Reason}");
                _isSessionSignerFunded = false;
                return false;
            }

            var currentLamports = sessionBalanceResult.Result.Value;
            if (currentLamports >= sessionSignerMinLamports)
            {
                _isSessionSignerFunded = true;
                return true;
            }

            var wallet = Web3.Wallet;
            if (wallet?.Account == null)
            {
                EmitError("Wallet disconnected while funding session signer.");
                _isSessionSignerFunded = false;
                return false;
            }

            if (emitPromptStatus)
            {
                var needed = Math.Max(sessionSignerTopUpLamports, sessionSignerMinLamports - currentLamports);
                EmitStatus(
                    $"Funding session wallet ({needed / 1_000_000_000d:F6} SOL). Approve in wallet...");
            }

            var topUpAmount = Math.Max(sessionSignerTopUpLamports, sessionSignerMinLamports - currentLamports);
            var transferResult = await wallet.Transfer(
                _sessionSignerAccount.PublicKey,
                topUpAmount,
                commitment);
            if (!transferResult.WasSuccessful || string.IsNullOrWhiteSpace(transferResult.Result))
            {
                var reason = string.IsNullOrWhiteSpace(transferResult.Reason)
                    ? "<unknown transfer failure>"
                    : transferResult.Reason;
                EmitError($"Session signer top-up failed: {reason}");
                _isSessionSignerFunded = false;
                return false;
            }

            _isSessionSignerFunded = true;
            if (emitPromptStatus)
            {
                EmitStatus($"Session wallet funded. tx={transferResult.Result}");
            }

            return true;
        }

        private async UniTask ConnectEditorDevWalletAsync()
        {
            var editorWalletPassword = ResolveEditorWalletPassword();
            if (Application.isEditor && useEditorWalletSlots)
            {
                var mnemonic = GetOrCreateEditorWalletSlotMnemonic();
                var slotAccount = await Web3.Instance.CreateAccount(mnemonic, editorWalletPassword);
                if (slotAccount == null)
                {
                    throw new InvalidOperationException(
                        $"Editor wallet slot #{editorWalletSlot} failed to load from deterministic mnemonic.");
                }

                return;
            }

            var account = await Web3.Instance.LoginInGameWallet(editorWalletPassword);
            if (account == null && createEditorDevWalletIfMissing)
            {
                EmitStatus("No existing editor in-game wallet found. Creating one now.");
                account = await Web3.Instance.CreateAccount(null, editorWalletPassword);
            }

            if (account == null)
            {
                throw new InvalidOperationException(
                    "Editor in-game wallet login returned null. " +
                    "If this is first run, enable createEditorDevWalletIfMissing.");
            }
        }

        private void InitializeEditorWalletSlot()
        {
            editorWalletSlot = Math.Max(0, editorWalletSlot);
            if (!Application.isEditor || !persistEditorWalletSlot)
            {
                return;
            }

            var storedSlot = PlayerPrefs.GetInt(EditorWalletSlotPrefKey, editorWalletSlot);
            editorWalletSlot = Math.Max(0, storedSlot);
        }

        private void SetEditorWalletSlot(int slot)
        {
            var sanitizedSlot = Math.Max(0, slot);
            if (editorWalletSlot == sanitizedSlot)
            {
                return;
            }

            editorWalletSlot = sanitizedSlot;
            if (Application.isEditor && persistEditorWalletSlot)
            {
                PlayerPrefs.SetInt(EditorWalletSlotPrefKey, editorWalletSlot);
                PlayerPrefs.Save();
            }

            EmitStatus($"Using editor wallet slot #{editorWalletSlot}");
        }

        private bool TryGetEditorWalletSlotMnemonic(out string mnemonic)
        {
            mnemonic = null;
            if (!Application.isEditor || !useEditorWalletSlots)
            {
                return false;
            }

            var key = GetEditorWalletSlotMnemonicPrefKey(editorWalletSlot);
            if (!PlayerPrefs.HasKey(key))
            {
                return false;
            }

            var value = PlayerPrefs.GetString(key, string.Empty)?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            mnemonic = value;
            return true;
        }

        private string GetOrCreateEditorWalletSlotMnemonic()
        {
            if (TryGetEditorWalletSlotMnemonic(out var existingMnemonic))
            {
                return existingMnemonic;
            }

            var generatedMnemonic = new Mnemonic(WordList.English, WordCount.TwentyFour).ToString();
            var key = GetEditorWalletSlotMnemonicPrefKey(editorWalletSlot);
            PlayerPrefs.SetString(key, generatedMnemonic);
            PlayerPrefs.Save();
            EmitStatus($"Created deterministic editor wallet slot #{editorWalletSlot}");
            return generatedMnemonic;
        }

        private static string GetEditorWalletSlotMnemonicPrefKey(int slot)
        {
            var safeSlot = Math.Max(0, slot);
            return $"{EditorWalletSlotMnemonicPrefPrefix}{safeSlot}";
        }

        private string ResolveEditorWalletPassword()
        {
            if (!useEditorWalletSlots)
            {
                return editorDevWalletPassword;
            }

            var safePrefix = string.IsNullOrWhiteSpace(editorWalletPasswordPrefix)
                ? "seeker-dev-wallet-slot-"
                : editorWalletPasswordPrefix.Trim();
            return $"{safePrefix}{editorWalletSlot}";
        }

        private async UniTask ConnectWalletAdapterAsync()
        {
            // Defensive reset: after manual disconnect some devices keep a stale MWA state
            // that can prevent the next prompt from opening.
            ResetWalletAdapterConnectionState();
            var account = await Web3.Instance.LoginWalletAdapter();
            if (account == null)
            {
                throw new InvalidOperationException("Wallet adapter login returned null.");
            }
        }

        private void ResetWalletAdapterConnectionState()
        {
            if (Web3.Instance == null)
            {
                return;
            }

            try
            {
                Web3.Instance.Logout();
            }
            catch (Exception exception)
            {
                EmitStatus($"Wallet adapter state reset skipped: {exception.Message}");
            }
        }

        private async UniTask TryAirdropIfNeeded()
        {
            var wallet = Web3.Wallet;
            if (wallet?.Account == null)
            {
                return;
            }

            var balanceResult = await wallet.ActiveRpcClient.GetBalanceAsync(wallet.Account.PublicKey, commitment);
            if (!balanceResult.WasSuccessful || balanceResult.Result == null)
            {
                return;
            }

            var currentSol = balanceResult.Result.Value / 1_000_000_000d;
            if (currentSol >= editorLowSolThreshold)
            {
                return;
            }

            var airdropResult = await wallet.RequestAirdrop(editorAirdropLamports, commitment);
            if (airdropResult.WasSuccessful)
            {
                EmitStatus($"Airdrop requested for editor wallet: {airdropResult.Result}");
            }
        }

        private WalletLoginMode ResolveLoginMode(WalletLoginMode mode)
        {
            if (mode != WalletLoginMode.Auto)
            {
                return mode;
            }

            if (Application.isEditor && autoUseEditorDevWallet)
            {
                return WalletLoginMode.EditorDevWallet;
            }

            return WalletLoginMode.WalletAdapter;
        }

        private IRpcClient GetRpcClient()
        {
            if (Web3.Wallet?.ActiveRpcClient != null)
            {
                return _fallbackRpcClient ?? Web3.Wallet.ActiveRpcClient;
            }

            return _fallbackRpcClient;
        }

        private async UniTask<string> SendInstructionSignedByLocalAccounts(
            TransactionInstruction instruction,
            IList<Account> signers)
        {
            if (instruction == null)
            {
                EmitError("Cannot send null instruction.");
                return null;
            }

            return await SendInstructionsSignedByLocalAccounts(
                new List<TransactionInstruction> { instruction },
                signers);
        }

        private async UniTask<string> SendInstructionsSignedByLocalAccounts(
            IList<TransactionInstruction> instructions,
            IList<Account> signers)
        {
            if (signers == null || signers.Count == 0)
            {
                EmitError("Cannot send transaction without signers.");
                return null;
            }

            if (instructions == null || instructions.Count == 0)
            {
                EmitError("Cannot send transaction without instructions.");
                return null;
            }

            var rpcCandidates = GetRpcCandidates();
            if (rpcCandidates.Count == 0)
            {
                EmitError("RPC client not available.");
                return null;
            }

            if (ShouldUseWalletAdapterSigning(signers))
            {
                var walletSignature = await SendTransactionViaWalletAdapterAsync(instructions, signers);
                if (!string.IsNullOrWhiteSpace(walletSignature))
                {
                    return walletSignature;
                }
            }

            string lastFailure = null;
            for (var candidateIndex = 0; candidateIndex < rpcCandidates.Count; candidateIndex += 1)
            {
                var rpcCandidate = rpcCandidates[candidateIndex];
                var rpcLabel = candidateIndex == 0 ? "primary" : "fallback";
                var endpoint = DescribeRpcEndpoint(rpcCandidate);
                var rawProbeAttempted = false;
                for (var attempt = 1; attempt <= MaxTransientSendAttemptsPerRpc; attempt += 1)
                {
                    var latestBlockHash = await rpcCandidate.GetLatestBlockHashAsync(commitment);
                    if (!latestBlockHash.WasSuccessful || latestBlockHash.Result?.Value == null)
                    {
                        EmitError(
                            $"[{rpcLabel}] Failed to get latest blockhash (attempt {attempt}/{MaxTransientSendAttemptsPerRpc}) endpoint={endpoint}: {latestBlockHash.Reason}");
                        break;
                    }

                    var transactionBytes = new TransactionBuilder()
                        .SetRecentBlockHash(latestBlockHash.Result.Value.Blockhash)
                        .SetFeePayer(signers[0]);
                    for (var instructionIndex = 0; instructionIndex < instructions.Count; instructionIndex += 1)
                    {
                        transactionBytes.AddInstruction(instructions[instructionIndex]);
                    }

                    var builtTransactionBytes = transactionBytes.Build(new List<Account>(signers));

                    var transactionBase64 = Convert.ToBase64String(builtTransactionBytes);
                    var sendResult = await rpcCandidate.SendTransactionAsync(
                        transactionBase64,
                        skipPreflight: false,
                        preFlightCommitment: commitment);

                    if (sendResult.WasSuccessful)
                    {
                        return sendResult.Result;
                    }

                    var reason = string.IsNullOrWhiteSpace(sendResult.Reason)
                        ? "<empty reason>"
                        : sendResult.Reason;
                    lastFailure = reason;
                    if (IsTransientRpcFailure(reason))
                    {
                        EmitStatus(
                            $"[{rpcLabel}] Transaction failed (attempt {attempt}/{MaxTransientSendAttemptsPerRpc}) endpoint={endpoint}: {reason}");
                    }
                    else
                    {
                        EmitError(
                            $"[{rpcLabel}] Transaction failed (attempt {attempt}/{MaxTransientSendAttemptsPerRpc}) endpoint={endpoint}: {reason}");
                    }
                    if (sendResult.ServerErrorCode != 0)
                    {
                        EmitStatus($"[{rpcLabel}] Server error code: {sendResult.ServerErrorCode}");
                    }

                    if (!rawProbeAttempted && IsJsonParseFailure(reason))
                    {
                        rawProbeAttempted = true;
                        var rawProbe = await TrySendTransactionViaRawHttpAsync(endpoint, transactionBase64);
                        if (rawProbe.Attempted)
                        {
                            var rawProbeMessage =
                                $"[{rpcLabel}] Raw HTTP probe status={rawProbe.HttpStatusCode} " +
                                $"networkError={rawProbe.NetworkError ?? "<none>"} " +
                                $"rpcError={rawProbe.RpcError ?? "<none>"} " +
                                $"body={rawProbe.BodySnippet}";
                            if (IsNonFatalProbeError(rawProbe.RpcError))
                            {
                                EmitStatus(rawProbeMessage);
                            }
                            else
                            {
                                EmitError(rawProbeMessage);
                            }
                        }

                        if (rawProbe.WasSuccessful)
                        {
                            return rawProbe.Signature;
                        }
                    }

                    if (!IsTransientRpcFailure(reason))
                    {
                        break;
                    }

                    if (attempt < MaxTransientSendAttemptsPerRpc)
                    {
                        var retryDelayMs = BaseTransientRetryDelayMs * attempt;
                        EmitStatus(
                            $"[{rpcLabel}] Transient RPC failure detected. Retrying in {retryDelayMs}ms.");
                        await UniTask.Delay(retryDelayMs);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(lastFailure))
            {
                EmitError($"Transaction failed after retry attempts: {lastFailure}");
            }

            return null;
        }

        private async UniTask<string> SendTransactionViaWalletAdapterAsync(
            IList<TransactionInstruction> instructions,
            IList<Account> signers)
        {
            var wallet = Web3.Wallet;
            var walletAccount = wallet?.Account;
            if (wallet == null || walletAccount == null || instructions == null || instructions.Count == 0)
            {
                return null;
            }

            var blockhash = await wallet.GetBlockHash(commitment, useCache: false);
            if (string.IsNullOrWhiteSpace(blockhash))
            {
                EmitError("Wallet adapter signing failed: missing recent blockhash.");
                return null;
            }

            var transaction = new Transaction
            {
                RecentBlockHash = blockhash,
                FeePayer = walletAccount.PublicKey,
                Instructions = new List<TransactionInstruction>(instructions),
                Signatures = new List<SignaturePubKeyPair>()
            };

            for (var signerIndex = 0; signerIndex < signers.Count; signerIndex += 1)
            {
                var signer = signers[signerIndex];
                if (signer == null || signer.PublicKey == null)
                {
                    continue;
                }

                if (string.Equals(
                        signer.PublicKey.Key,
                        walletAccount.PublicKey.Key,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                transaction.PartialSign(signer);
            }

            var sendResult = await wallet.SignAndSendTransaction(
                transaction,
                skipPreflight: false,
                commitment: commitment);
            if (sendResult.WasSuccessful && !string.IsNullOrWhiteSpace(sendResult.Result))
            {
                return sendResult.Result;
            }

            var reason = string.IsNullOrWhiteSpace(sendResult.Reason)
                ? "<empty reason>"
                : sendResult.Reason;
            if (IsNonFatalWalletAdapterSendReason(reason))
            {
                EmitStatus($"Wallet adapter send fallback: {reason}");
            }
            else
            {
                EmitError($"Wallet adapter send failed: {reason}");
            }
            return null;
        }

        private static bool IsNonFatalWalletAdapterSendReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            return
                reason.IndexOf("sanitize accounts offsets", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("Unable to parse json", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsNonFatalProbeError(string rpcError)
        {
            if (string.IsNullOrWhiteSpace(rpcError))
            {
                return false;
            }

            return rpcError.IndexOf("SignatureFailure", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldUseWalletAdapterSigning(IList<Account> signers)
        {
            var walletAccount = Web3.Wallet?.Account;
            if (walletAccount == null || signers == null || signers.Count == 0)
            {
                return false;
            }

            for (var signerIndex = 0; signerIndex < signers.Count; signerIndex += 1)
            {
                var signer = signers[signerIndex];
                if (signer?.PublicKey == null)
                {
                    continue;
                }

                if (string.Equals(
                        signer.PublicKey.Key,
                        walletAccount.PublicKey.Key,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private List<IRpcClient> GetRpcCandidates()
        {
            var candidates = new List<IRpcClient>();
            if (_fallbackRpcClient != null)
            {
                candidates.Add(_fallbackRpcClient);
            }

            if (_secondaryRpcClient != null && !candidates.Contains(_secondaryRpcClient))
            {
                candidates.Add(_secondaryRpcClient);
            }

            var walletRpc = Web3.Wallet?.ActiveRpcClient;
            if (walletRpc != null && !candidates.Contains(walletRpc))
            {
                candidates.Add(walletRpc);
            }

            return candidates;
        }

        private static bool IsTransientRpcFailure(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return true;
            }

            return
                reason.IndexOf("Unable to parse json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("header part of a frame could not be read", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("Too Many Requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("temporarily unavailable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsJsonParseFailure(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason) &&
                   reason.IndexOf("Unable to parse json", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private struct RawHttpProbeResult
        {
            public bool Attempted;
            public bool WasSuccessful;
            public string Signature;
            public long HttpStatusCode;
            public string NetworkError;
            public string RpcError;
            public string BodySnippet;
        }

        private async UniTask<RawHttpProbeResult> TrySendTransactionViaRawHttpAsync(string endpoint, string txBase64)
        {
            if (string.IsNullOrWhiteSpace(endpoint) ||
                !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
            {
                return default;
            }

            var payload =
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"sendTransaction\",\"params\":[\"" +
                txBase64 +
                "\",{\"encoding\":\"base64\",\"skipPreflight\":false,\"preflightCommitment\":\"confirmed\"}]}";

            using var request = new UnityWebRequest(endpointUri.AbsoluteUri, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RawHttpProbeTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            try
            {
                await request.SendWebRequest().ToUniTask();
            }
            catch (Exception exception)
            {
                return new RawHttpProbeResult
                {
                    Attempted = true,
                    WasSuccessful = false,
                    HttpStatusCode = request.responseCode,
                    NetworkError = exception.Message,
                    RpcError = null,
                    BodySnippet = "<request exception>"
                };
            }

            var body = request.downloadHandler?.text ?? string.Empty;
            var signature = ExtractJsonStringField(body, "result");
            var rpcError = ExtractJsonStringField(body, "message");

            return new RawHttpProbeResult
            {
                Attempted = true,
                WasSuccessful = !string.IsNullOrWhiteSpace(signature),
                Signature = signature,
                HttpStatusCode = request.responseCode,
                NetworkError = request.error,
                RpcError = rpcError,
                BodySnippet = TruncateForLog(body, RawHttpBodyLogLimit)
            };
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            var pattern = $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"([^\"]+)\"";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string TruncateForLog(string text, int limit)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "<empty>";
            }

            if (text.Length <= limit)
            {
                return text;
            }

            return text.Substring(0, limit) + "...";
        }

        private void EnsureWeb3ExistsAndConfigured()
        {
            if (Web3.Instance != null)
            {
                ApplyWeb3RpcOverrides(Web3.Instance);
                EnsureWalletAdapterOptions(Web3.Instance);
                return;
            }

            var web3GameObject = new GameObject("Web3");
            DontDestroyOnLoad(web3GameObject);
            var web3 = web3GameObject.AddComponent<Web3>();
            ApplyWeb3RpcOverrides(web3);
            EnsureWalletAdapterOptions(web3);
        }

        private void ApplyWeb3RpcOverrides(Web3 web3)
        {
            var cluster = RpcCluster.DevNet;
            if (LGConfig.IsMainnetRuntime &&
                Enum.TryParse<RpcCluster>("MainNet", true, out var mainnetCluster))
            {
                cluster = mainnetCluster;
            }

            web3.rpcCluster = cluster;
            web3.customRpc = rpcUrl;
            web3.webSocketsRpc = ToWebSocketUrl(rpcUrl);
            web3.autoConnectOnStartup = false;
        }

        private static void EnsureWalletAdapterOptions(Web3 web3)
        {
            if (web3 == null)
            {
                return;
            }

            web3.solanaWalletAdapterOptions ??= new SolanaWalletAdapterOptions();
            web3.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions ??=
                new SolanaMobileWalletAdapterOptions();
            web3.solanaWalletAdapterOptions.solanaWalletAdapterWebGLOptions ??=
                new SolanaWalletAdapterWebGLOptions();
            web3.solanaWalletAdapterOptions.phantomWalletOptions ??= new PhantomWalletOptions();
        }

        private static string NormalizeRpcUrl(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim();
        }

        private static string ToWebSocketUrl(string httpUrl)
        {
            if (httpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + httpUrl.Substring("https://".Length);
            }

            if (httpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + httpUrl.Substring("http://".Length);
            }

            return httpUrl;
        }

        private static string DescribeRpcEndpoint(IRpcClient rpcClient)
        {
            if (rpcClient == null)
            {
                return "<null>";
            }

            try
            {
                var property = rpcClient.GetType().GetProperty("NodeAddress");
                var value = property?.GetValue(rpcClient) as Uri;
                if (value != null)
                {
                    return value.AbsoluteUri;
                }
            }
            catch
            {
                // Best effort diagnostics only.
            }

            return rpcClient.GetType().Name;
        }

        private PublicKey DerivePlayerPda(PublicKey player)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.PLAYER_SEED),
                    player.KeyBytes
                },
                _programId,
                out var pda,
                out _);
            return success ? pda : null;
        }

        private PublicKey DeriveSessionAuthorityPda(PublicKey player, PublicKey sessionKey)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes("session"),
                    player.KeyBytes,
                    sessionKey.KeyBytes
                },
                _programId,
                out var pda,
                out _);
            return success ? pda : null;
        }

        private void HandleWalletStateChanged()
        {
            OnWalletConnectionChanged?.Invoke(IsWalletConnected);
        }

        private void ClearSessionState()
        {
            _sessionSignerAccount = null;
            _sessionAuthorityPda = null;
            _hasActiveOnchainSession = false;
            _isSessionSignerFunded = false;
        }

        private void EmitStatus(string message)
        {
            if (logDebugMessages)
            {
                Debug.Log($"[WalletSession] {message}");
            }

            OnStatus?.Invoke(message);
        }

        private void EmitError(string message)
        {
            Debug.LogError($"[WalletSession] {message}");
            OnError?.Invoke(message);
        }
    }
}

