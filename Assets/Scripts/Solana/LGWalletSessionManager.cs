using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Solana.Unity.Programs;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;
using Chaindepth.Program;

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
        public static LGWalletSessionManager Instance { get; private set; }

        [Header("Network")]
        [SerializeField] private string rpcUrl = LGConfig.RPC_URL;
        [SerializeField] private Commitment commitment = Commitment.Confirmed;

        [Header("Startup Login")]
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private WalletLoginMode startupLoginMode = WalletLoginMode.Auto;
        [SerializeField] private bool autoUseEditorDevWallet = true;
        [SerializeField] private string editorDevWalletPassword = "seeker-dev-wallet";
        [SerializeField] private bool createEditorDevWalletIfMissing = true;
        [SerializeField] private bool requestAirdropIfLowSolInEditor = true;
        [SerializeField] private double editorLowSolThreshold = 0.2d;
        [SerializeField] private ulong editorAirdropLamports = 1_000_000_000UL;

        [Header("Session Defaults")]
        [SerializeField] private bool autoBeginSessionAfterConnect;
        [SerializeField] private int sessionDurationMinutes = 60;
        [SerializeField] private ulong defaultSessionMaxTokenSpend = 200_000_000UL;
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

        public bool HasActiveOnchainSession => _hasActiveOnchainSession && _sessionSignerAccount != null;
        public PublicKey ActiveSessionSignerPublicKey => _sessionSignerAccount?.PublicKey;
        public PublicKey ActiveSessionAuthorityPda => _sessionAuthorityPda;

        private PublicKey _programId;
        private PublicKey _globalPda;
        private IRpcClient _fallbackRpcClient;

        private Account _sessionSignerAccount;
        private PublicKey _sessionAuthorityPda;
        private bool _hasActiveOnchainSession;

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
            _fallbackRpcClient = ClientFactory.GetClient(rpcUrl);
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
                    EmitError("Wallet connection failed.");
                    return false;
                }

                ActiveWalletMode = resolvedMode;
                EmitStatus($"Wallet connected ({resolvedMode}): {ConnectedWalletPublicKey}");

                if (autoBeginSessionAfterConnect)
                {
                    await BeginGameplaySessionAsync();
                }

                return true;
            }
            catch (Exception exception)
            {
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

            ClearSessionState();
            ActiveWalletMode = WalletLoginMode.Auto;
            EmitStatus("Wallet disconnected.");
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

            if (ActiveWalletMode == WalletLoginMode.WalletAdapter)
            {
                EmitError("Session creation currently supports editor dev wallet flow only.");
                return false;
            }

            var player = ConnectedWalletPublicKey;
            var playerPda = DerivePlayerPda(player);
            var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                player,
                new PublicKey(LGConfig.SKR_MINT)
            );

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

            var signature = await SendInstructionSignedByLocalAccounts(
                instruction,
                new List<Account> { Web3.Wallet.Account, _sessionSignerAccount }
            );
            if (string.IsNullOrEmpty(signature))
            {
                ClearSessionState();
                return false;
            }

            _hasActiveOnchainSession = true;
            OnSessionStateChanged?.Invoke(true);
            EmitStatus($"Session started. Session key={_sessionSignerAccount.PublicKey} tx={signature}");
            return true;
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
                new PublicKey(LGConfig.SKR_MINT)
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

        private async UniTask ConnectEditorDevWalletAsync()
        {
            var account = await Web3.Instance.LoginInGameWallet(editorDevWalletPassword);
            if (account == null && createEditorDevWalletIfMissing)
            {
                EmitStatus("No existing editor in-game wallet found. Creating one now.");
                account = await Web3.Instance.CreateAccount(null, editorDevWalletPassword);
            }

            if (account == null)
            {
                throw new InvalidOperationException(
                    "Editor in-game wallet login returned null. " +
                    "If this is first run, enable createEditorDevWalletIfMissing.");
            }

            if (requestAirdropIfLowSolInEditor && Application.isEditor)
            {
                await TryAirdropIfNeeded();
            }
        }

        private async UniTask ConnectWalletAdapterAsync()
        {
            var account = await Web3.Instance.LoginWalletAdapter();
            if (account == null)
            {
                throw new InvalidOperationException("Wallet adapter login returned null.");
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
                return Web3.Wallet.ActiveRpcClient;
            }

            return _fallbackRpcClient;
        }

        private async UniTask<string> SendInstructionSignedByLocalAccounts(
            TransactionInstruction instruction,
            IList<Account> signers)
        {
            if (signers == null || signers.Count == 0)
            {
                EmitError("Cannot send transaction without signers.");
                return null;
            }

            var rpc = GetRpcClient();
            var latestBlockHash = await rpc.GetLatestBlockHashAsync(commitment);
            if (!latestBlockHash.WasSuccessful || latestBlockHash.Result?.Value == null)
            {
                EmitError($"Failed to get latest blockhash: {latestBlockHash.Reason}");
                return null;
            }

            var transactionBytes = new TransactionBuilder()
                .SetRecentBlockHash(latestBlockHash.Result.Value.Blockhash)
                .SetFeePayer(signers[0])
                .AddInstruction(instruction)
                .Build(new List<Account>(signers));

            var transactionBase64 = Convert.ToBase64String(transactionBytes);
            var sendResult = await rpc.SendTransactionAsync(
                transactionBase64,
                skipPreflight: false,
                preFlightCommitment: commitment);

            if (!sendResult.WasSuccessful)
            {
                EmitError($"Transaction failed: {sendResult.Reason}");
                return null;
            }

            return sendResult.Result;
        }

        private void EnsureWeb3ExistsAndConfigured()
        {
            if (Web3.Instance != null)
            {
                ApplyWeb3RpcOverrides(Web3.Instance);
                return;
            }

            var web3GameObject = new GameObject("Web3");
            DontDestroyOnLoad(web3GameObject);
            var web3 = web3GameObject.AddComponent<Web3>();
            ApplyWeb3RpcOverrides(web3);
        }

        private void ApplyWeb3RpcOverrides(Web3 web3)
        {
            web3.rpcCluster = RpcCluster.DevNet;
            web3.customRpc = rpcUrl;
            web3.webSocketsRpc = rpcUrl.Replace("https://", "wss://");
            web3.autoConnectOnStartup = false;
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

