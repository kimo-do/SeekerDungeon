using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Solana.Unity.Programs;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;

// Generated client from IDL
using Chaindepth;
using Chaindepth.Accounts;
using Chaindepth.Program;
using Chaindepth.Types;
using UnityEngine.Networking;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Manager for LG Solana program interactions.
    /// Uses generated client from anchor IDL for type-safe operations.
    /// </summary>
    public class LGManager : MonoBehaviour
    {
        public static LGManager Instance { get; private set; }

        public static LGManager EnsureInstance()
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

            var bootstrapObject = new GameObject(nameof(LGManager));
            return bootstrapObject.AddComponent<LGManager>();
        }

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        [Header("RPC Settings")]
        [SerializeField] private string rpcUrl = LGConfig.RPC_URL;
        [SerializeField] private string fallbackRpcUrl = LGConfig.RPC_FALLBACK_URL;
        private bool enableStreamingRpc = false;

        // Cached state (using generated account types)
        public GlobalAccount CurrentGlobalState { get; private set; }
        public PlayerAccount CurrentPlayerState { get; private set; }
        public PlayerProfile CurrentProfileState { get; private set; }
        public RoomAccount CurrentRoomState { get; private set; }
        public InventoryAccount CurrentInventoryState { get; private set; }

        // Events
        public event Action<GlobalAccount> OnGlobalStateUpdated;
        public event Action<PlayerAccount> OnPlayerStateUpdated;
        public event Action<PlayerProfile> OnProfileStateUpdated;
        public event Action<RoomAccount> OnRoomStateUpdated;
        public event Action<IReadOnlyList<RoomOccupantView>> OnRoomOccupantsUpdated;
        public event Action<InventoryAccount> OnInventoryUpdated;
        public event Action<LootResult> OnChestLootResult;
        public event Action<string> OnTransactionSent;
        public event Action<string> OnError;

        private PublicKey _programId;
        private PublicKey _globalPda;
        private IRpcClient _rpcClient;
        private IRpcClient _fallbackRpcClient;
        private IStreamingRpcClient _streamingRpcClient;
        private ChaindepthClient _client;
        private readonly HashSet<string> _roomPresenceSubscriptionKeys = new();
        private uint? _lastProgramErrorCode;

        private struct GameplaySigningContext
        {
            public Account SignerAccount;
            public PublicKey Authority;
            public PublicKey Player;
            public PublicKey SessionAuthority;
            public bool UsesSessionSigner;
        }

        private const int MaxTransientSendAttemptsPerRpc = 2;
        private const int BaseTransientRetryDelayMs = 300;
        private const int RawHttpProbeTimeoutSeconds = 20;
        private const int RawHttpBodyLogLimit = 400;

        private static LGManager FindExistingInstance()
        {
            var allInstances = Resources.FindObjectsOfTypeAll<LGManager>();
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

            rpcUrl = LGConfig.GetRuntimeRpcUrl(rpcUrl);
            fallbackRpcUrl = LGConfig.GetRuntimeFallbackRpcUrl(fallbackRpcUrl, rpcUrl);

            // Initialize RPC client
            _rpcClient = ClientFactory.GetClient(rpcUrl);
            _fallbackRpcClient = string.Equals(rpcUrl, fallbackRpcUrl, StringComparison.OrdinalIgnoreCase)
                ? null
                : ClientFactory.GetClient(fallbackRpcUrl);

            // Initialize streaming RPC client for account subscriptions.
            // Disabled by default to avoid WebSocket reconnect flood on Android.
            if (enableStreamingRpc)
            {
                try
                {
                    var websocketUrl = rpcUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? rpcUrl.Replace("https://", "wss://")
                        : rpcUrl.Replace("http://", "ws://");
                    _streamingRpcClient = ClientFactory.GetStreamingClient(websocketUrl);
                    Log("Streaming RPC client initialized.");
                }
                catch (Exception streamingInitError)
                {
                    _streamingRpcClient = null;
                    Log($"Streaming RPC unavailable. Falling back to polling-only mode. Reason: {streamingInitError.Message}");
                }
            }
            else
            {
                _streamingRpcClient = null;
                Log("Streaming RPC disabled (enableStreamingRpc=false). Using polling-only mode.");
            }

            _client = new ChaindepthClient(_rpcClient, _streamingRpcClient, _programId);
            
            Log($"LG Manager initialized. Program: {LGConfig.PROGRAM_ID}");
            Log($"RPC primary={rpcUrl} fallback={fallbackRpcUrl}");
            Log($"Runtime network={LGConfig.ActiveRuntimeNetwork}");
            Log($"SKR mint={LGConfig.ActiveSkrMint}");
            if (LGConfig.IsUsingMainnetSkrMint &&
                rpcUrl.IndexOf("devnet", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                LogError("Mainnet SKR mint selected while RPC is devnet. Switch RPC/program config before release.");
            }
        }

        /// <summary>
        /// Get the active RPC client - prefers wallet's client, falls back to standalone
        /// </summary>
        private IRpcClient GetRpcClient()
        {
            return _rpcClient ?? Web3.Wallet?.ActiveRpcClient;
        }

        private void Log(string message)
        {
            if (logDebugMessages)
                Debug.Log($"[LG] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LG] {message}");
            OnError?.Invoke(message);
        }

        private async UniTask<bool> AccountHasData(PublicKey accountPda)
        {
            var rpc = GetRpcClient();
            if (rpc == null || accountPda == null)
            {
                return false;
            }

            try
            {
                var accountInfo = await rpc.GetAccountInfoAsync(accountPda, Commitment.Confirmed);
                if (!accountInfo.WasSuccessful || accountInfo.Result?.Value == null)
                {
                    return false;
                }

                var data = accountInfo.Result.Value.Data;
                return data != null && data.Count > 0 && !string.IsNullOrEmpty(data[0]);
            }
            catch
            {
                return false;
            }
        }

        #region PDA Derivation

        /// <summary>
        /// Derive player PDA from wallet public key
        /// </summary>
        public PublicKey DerivePlayerPda(PublicKey walletPubkey)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.PLAYER_SEED),
                    walletPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive room PDA from season seed and coordinates
        /// </summary>
        public PublicKey DeriveRoomPda(ulong seasonSeed, int x, int y)
        {
            var seasonBytes = BitConverter.GetBytes(seasonSeed);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(seasonBytes);

            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.ROOM_SEED),
                    seasonBytes,
                    new[] { (byte)(sbyte)x },
                    new[] { (byte)(sbyte)y }
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive escrow PDA for a room/direction job
        /// </summary>
        public PublicKey DeriveEscrowPda(PublicKey roomPda, byte direction)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.ESCROW_SEED),
                    roomPda.KeyBytes,
                    new[] { direction }
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive helper stake PDA for a room/direction/player
        /// </summary>
        public PublicKey DeriveHelperStakePda(PublicKey roomPda, byte direction, PublicKey playerPubkey)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.STAKE_SEED),
                    roomPda.KeyBytes,
                    new[] { direction },
                    playerPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive inventory PDA for a player
        /// </summary>
        public PublicKey DeriveInventoryPda(PublicKey playerPubkey)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.INVENTORY_SEED),
                    playerPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive player profile PDA
        /// </summary>
        public PublicKey DeriveProfilePda(PublicKey playerPubkey)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.PROFILE_SEED),
                    playerPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive room presence PDA for a player in a specific room
        /// </summary>
        public PublicKey DeriveRoomPresencePda(ulong seasonSeed, int roomX, int roomY, PublicKey playerPubkey)
        {
            var seasonBytes = BitConverter.GetBytes(seasonSeed);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(seasonBytes);
            }

            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.PRESENCE_SEED),
                    seasonBytes,
                    new[] { (byte)(sbyte)roomX },
                    new[] { (byte)(sbyte)roomY },
                    playerPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive loot receipt PDA for player+room (existence = already looted)
        /// </summary>
        public PublicKey DeriveLootReceiptPda(ulong seasonSeed, int roomX, int roomY, PublicKey playerPubkey)
        {
            var seasonBytes = BitConverter.GetBytes(seasonSeed);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(seasonBytes);
            }

            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.LOOT_RECEIPT_SEED),
                    seasonBytes,
                    new[] { (byte)(sbyte)roomX },
                    new[] { (byte)(sbyte)roomY },
                    playerPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        /// <summary>
        /// Derive boss fight PDA for room/player
        /// </summary>
        public PublicKey DeriveBossFightPda(PublicKey roomPda, PublicKey playerPubkey)
        {
            var success = PublicKey.TryFindProgramAddress(
                new List<byte[]>
                {
                    Encoding.UTF8.GetBytes(LGConfig.BOSS_FIGHT_SEED),
                    roomPda.KeyBytes,
                    playerPubkey.KeyBytes
                },
                _programId,
                out var pda,
                out _
            );
            return success ? pda : null;
        }

        #endregion

        #region Fetch Account Data (Using Generated Client)

        /// <summary>
        /// Fetch global game state using generated client
        /// </summary>
        public async UniTask<GlobalAccount> FetchGlobalState()
        {
            Log("Fetching global state...");

            try
            {
                var result = await _client.GetGlobalAccountAsync(_globalPda.Key, Commitment.Confirmed);
                
                if (!result.WasSuccessful || result.ParsedResult == null)
                {
                    LogError("Global account not found");
                    return null;
                }

                CurrentGlobalState = result.ParsedResult;
                Log($"Global State: SeasonSeed={CurrentGlobalState.SeasonSeed}, Depth={CurrentGlobalState.Depth}");
                OnGlobalStateUpdated?.Invoke(CurrentGlobalState);

                return CurrentGlobalState;
            }
            catch (Exception e)
            {
                LogError($"Failed to fetch global state: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetch player state for current wallet using generated client
        /// </summary>
        public async UniTask<PlayerAccount> FetchPlayerState()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            Log("Fetching player state...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                if (playerPda == null)
                {
                    LogError("Failed to derive player PDA");
                    return null;
                }

                if (!await AccountHasData(playerPda))
                {
                    Log("Player account not found (not initialized yet)");
                    CurrentPlayerState = null;
                    OnPlayerStateUpdated?.Invoke(null);
                    return null;
                }

                var result = await _client.GetPlayerAccountAsync(playerPda.Key, Commitment.Confirmed);
                
                if (!result.WasSuccessful || result.ParsedResult == null)
                {
                    Log("Player account not found (not initialized yet)");
                    CurrentPlayerState = null;
                    OnPlayerStateUpdated?.Invoke(null);
                    return null;
                }

                CurrentPlayerState = result.ParsedResult;
                Log($"Player State: Position=({CurrentPlayerState.CurrentRoomX}, {CurrentPlayerState.CurrentRoomY}), Jobs={CurrentPlayerState.JobsCompleted}");
                OnPlayerStateUpdated?.Invoke(CurrentPlayerState);

                return CurrentPlayerState;
            }
            catch (Exception e)
            {
                LogError($"Failed to fetch player state: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetch room state at coordinates using generated client.
        /// Set fireEvent to false when polling silently (e.g. auto-completer) to
        /// avoid triggering snapshot rebuilds that cause pop-in / camera snaps.
        /// </summary>
        public async UniTask<RoomAccount> FetchRoomState(int x, int y, bool fireEvent = true)
        {
            if (CurrentGlobalState == null)
            {
                await FetchGlobalState();
                if (CurrentGlobalState == null)
                {
                    LogError("Cannot fetch room without global state");
                    return null;
                }
            }

            Log($"Fetching room state at ({x}, {y})...");

            try
            {
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, x, y);
                if (roomPda == null)
                {
                    LogError("Failed to derive room PDA");
                    return null;
                }

                if (!await AccountHasData(roomPda))
                {
                    Log($"Room at ({x}, {y}) not initialized");
                    return null;
                }

                var result = await _client.GetRoomAccountAsync(roomPda.Key, Commitment.Confirmed);
                
                if (!result.WasSuccessful || result.ParsedResult == null)
                {
                    Log($"Room at ({x}, {y}) not initialized");
                    return null;
                }

                CurrentRoomState = result.ParsedResult;
                Log($"Room State: Walls=[{string.Join(",", CurrentRoomState.Walls)}], HasChest={CurrentRoomState.HasChest}");
                if (fireEvent)
                {
                    OnRoomStateUpdated?.Invoke(CurrentRoomState);
                }

                return CurrentRoomState;
            }
            catch (Exception e)
            {
                LogError($"Failed to fetch room state: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetch current player's room
        /// </summary>
        public async UniTask<RoomAccount> FetchCurrentRoom()
        {
            if (CurrentPlayerState == null)
            {
                await FetchPlayerState();
            }

            if (CurrentPlayerState == null)
            {
                // Player not initialized, fetch starting room
                return await FetchRoomState(LGConfig.START_X, LGConfig.START_Y);
            }

            return await FetchRoomState(CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
        }

        /// <summary>
        /// Fetch inventory for the current wallet
        /// </summary>
        public async UniTask<InventoryAccount> FetchInventory()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            Log("Fetching inventory...");

            try
            {
                var inventoryPda = DeriveInventoryPda(Web3.Wallet.Account.PublicKey);
                if (inventoryPda == null)
                {
                    LogError("Failed to derive inventory PDA");
                    return null;
                }

                if (!await AccountHasData(inventoryPda))
                {
                    Log("Inventory account not found (not initialized yet)");
                    CurrentInventoryState = null;
                    OnInventoryUpdated?.Invoke(null);
                    return null;
                }

                var result = await _client.GetInventoryAccountAsync(inventoryPda.Key, Commitment.Confirmed);

                if (!result.WasSuccessful || result.ParsedResult == null)
                {
                    Log("Inventory account not found");
                    CurrentInventoryState = null;
                    OnInventoryUpdated?.Invoke(null);
                    return null;
                }

                CurrentInventoryState = result.ParsedResult;
                var itemCount = CurrentInventoryState.Items?.Length ?? 0;
                Log($"Inventory fetched: {itemCount} item stacks");
                OnInventoryUpdated?.Invoke(CurrentInventoryState);

                return CurrentInventoryState;
            }
            catch (Exception e)
            {
                LogError($"Failed to fetch inventory: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get current room as a typed domain view
        /// </summary>
        public RoomView GetCurrentRoomView()
        {
            var wallet = Web3.Wallet?.Account?.PublicKey;
            return CurrentRoomState.ToRoomView(wallet);
        }

        /// <summary>
        /// Check if the local player has already looted the current room via LootReceipt PDA
        /// </summary>
        public async UniTask<bool> CheckHasLocalPlayerLooted()
        {
            if (Web3.Wallet == null || CurrentGlobalState == null || CurrentPlayerState == null)
                return false;

            var playerPubkey = Web3.Wallet.Account.PublicKey;
            var receiptPda = DeriveLootReceiptPda(
                CurrentGlobalState.SeasonSeed,
                CurrentPlayerState.CurrentRoomX,
                CurrentPlayerState.CurrentRoomY,
                playerPubkey);

            if (receiptPda == null) return false;

            try
            {
                var accountInfo = await Web3.Rpc.GetAccountInfoAsync(receiptPda.Key);
                return accountInfo?.Result?.Value != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get current player as a typed domain view
        /// </summary>
        public PlayerStateView GetCurrentPlayerView(int defaultSkinId = 0)
        {
            return CurrentPlayerState.ToPlayerView(CurrentProfileState, defaultSkinId);
        }

        #endregion

        #region Refresh All Data

        /// <summary>
        /// Refresh all relevant game state
        /// </summary>
        public async UniTask RefreshAllState()
        {
            Log("Refreshing all state...");
            await FetchGlobalState();
            await FetchPlayerState();
            await FetchPlayerProfile();
            await FetchCurrentRoom();
            await FetchInventory();
            Log("State refresh complete");
        }

        /// <summary>
        /// Fetch current player's profile state
        /// </summary>
        public async UniTask<PlayerProfile> FetchPlayerProfile()
        {
            if (Web3.Wallet == null)
            {
                return null;
            }

            try
            {
                var profilePda = DeriveProfilePda(Web3.Wallet.Account.PublicKey);
                if (!await AccountHasData(profilePda))
                {
                    CurrentProfileState = null;
                    OnProfileStateUpdated?.Invoke(null);
                    return null;
                }

                var result = await _client.GetPlayerProfileAsync(profilePda.Key, Commitment.Confirmed);
                if (!result.WasSuccessful || result.ParsedResult == null)
                {
                    CurrentProfileState = null;
                    OnProfileStateUpdated?.Invoke(null);
                    return null;
                }

                CurrentProfileState = result.ParsedResult;
                OnProfileStateUpdated?.Invoke(CurrentProfileState);
                return CurrentProfileState;
            }
            catch (Exception error)
            {
                LogError($"FetchPlayerProfile failed: {error.Message}");
                return null;
            }
        }

        #endregion

        #region Instructions (Using Generated Client)

        /// <summary>
        /// Returns true if the current player has an active job on the current room wall direction.
        /// </summary>
        public bool HasActiveJobInCurrentRoom(byte direction)
        {
            if (CurrentPlayerState == null || CurrentPlayerState.ActiveJobs == null)
            {
                return false;
            }

            var roomX = CurrentPlayerState.CurrentRoomX;
            var roomY = CurrentPlayerState.CurrentRoomY;

            foreach (var job in CurrentPlayerState.ActiveJobs)
            {
                if (job == null)
                {
                    continue;
                }

                if (job.RoomX == roomX && job.RoomY == roomY && job.Direction == direction)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Fetch all players currently in a room and decorate with boss-fight status.
        /// This is useful for rendering room occupants in Unity.
        /// </summary>
        public async UniTask<IReadOnlyList<RoomOccupantView>> FetchRoomOccupants(int roomX, int roomY)
        {
            if (CurrentGlobalState == null)
            {
                await FetchGlobalState();
            }

            if (CurrentGlobalState == null)
            {
                return Array.Empty<RoomOccupantView>();
            }

            try
            {
                var roomPresenceResult = await _client.GetRoomPresencesAsync(_programId.Key, Commitment.Confirmed);
                var allPresences = roomPresenceResult?.ParsedResult ?? new List<RoomPresence>();

                var roomPresences = allPresences
                    .Where(presence =>
                        presence != null &&
                        presence.SeasonSeed == CurrentGlobalState.SeasonSeed &&
                        presence.RoomX == roomX &&
                        presence.RoomY == roomY &&
                        presence.IsCurrent)
                    .ToList();

                var occupants = roomPresences
                    .Select(presence => new RoomOccupantView
                    {
                        Wallet = presence.Player,
                        EquippedItemId = LGDomainMapper.ToItemId(presence.EquippedItemId),
                        SkinId = presence.SkinId,
                        Activity = LGDomainMapper.ToOccupantActivity(presence.Activity),
                        ActivityDirection = presence.Activity == 1 &&
                                            LGDomainMapper.TryToDirection(presence.ActivityDirection, out var mappedDirection)
                            ? mappedDirection
                            : null,
                        IsFightingBoss = presence.Activity == 2
                    })
                    .ToArray();

                if (logDebugMessages)
                {
                    var rawDirections = string.Join(",",
                        roomPresences
                            .Where(p => p != null && p.Activity == 1)
                            .Select(p => p.ActivityDirection.ToString()));

                    var mappedNorth = occupants.Count(o => o.ActivityDirection == RoomDirection.North);
                    var mappedSouth = occupants.Count(o => o.ActivityDirection == RoomDirection.South);
                    var mappedEast = occupants.Count(o => o.ActivityDirection == RoomDirection.East);
                    var mappedWest = occupants.Count(o => o.ActivityDirection == RoomDirection.West);

                    Log(
                        $"RoomOccupants ({roomX},{roomY}) total={occupants.Length} doorJobRawDirs=[{rawDirections}] mapped N={mappedNorth} S={mappedSouth} E={mappedEast} W={mappedWest}");
                }

                OnRoomOccupantsUpdated?.Invoke(occupants);
                return occupants;
            }
            catch (Exception error)
            {
                LogError($"FetchRoomOccupants failed: {error.Message}");
                return Array.Empty<RoomOccupantView>();
            }
        }

        /// <summary>
        /// Subscribe to room presence account updates for occupants currently in room.
        /// </summary>
        public async UniTask StartRoomOccupantSubscriptions(int roomX, int roomY)
        {
            if (_streamingRpcClient == null)
            {
                Log("Streaming RPC not configured; skipping room occupant subscriptions.");
                return;
            }

            var occupants = await FetchRoomOccupants(roomX, roomY);
            foreach (var occupant in occupants)
            {
                if (occupant?.Wallet == null || CurrentGlobalState == null)
                {
                    continue;
                }

                var presencePda = DeriveRoomPresencePda(CurrentGlobalState.SeasonSeed, roomX, roomY, occupant.Wallet);
                if (presencePda == null)
                {
                    continue;
                }

                if (!_roomPresenceSubscriptionKeys.Add(presencePda.Key))
                {
                    continue;
                }

                try
                {
                    await _client.SubscribeRoomPresenceAsync(
                        presencePda.Key,
                        (_, _, _) => { FetchRoomOccupants(roomX, roomY).Forget(); },
                        Commitment.Confirmed
                    );
                }
                catch (Exception subscriptionError)
                {
                    LogError($"Room presence subscribe failed for {presencePda.Key}: {subscriptionError.Message}");
                }
            }
        }

        /// <summary>
        /// Performs the next sensible action for a blocked rubble door:
        /// Join -> Tick -> Complete -> Claim.
        /// </summary>
        public async UniTask<TxResult> InteractWithDoor(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (direction > LGConfig.DIRECTION_WEST)
            {
                LogError($"Invalid direction: {direction}");
                return TxResult.Fail("Invalid direction");
            }

            if (CurrentGlobalState == null || CurrentPlayerState == null)
            {
                await RefreshAllState();
            }

            if (CurrentPlayerState == null)
            {
                LogError("Player not initialized");
                return TxResult.Fail("Player not initialized");
            }

            var room = await FetchCurrentRoom();
            if (room == null)
            {
                LogError("Current room not loaded");
                return TxResult.Fail("Current room not loaded");
            }

            var dir = direction;
            var wallState = room.Walls[dir];
            if (wallState != LGConfig.WALL_RUBBLE)
            {
                if (wallState == LGConfig.WALL_OPEN)
                {
                    Log($"Door {LGConfig.GetDirectionName(direction)} is open. Moving player through door.");
                    return await MoveThroughDoor(direction);
                }
                else if (wallState == LGConfig.WALL_LOCKED)
                {
                    Log($"Door {LGConfig.GetDirectionName(direction)} is locked. Attempting unlock.");
                    var unlockResult = await UnlockDoor(direction);
                    if (!unlockResult.Success && IsMissingRequiredKeyError())
                    {
                        return TxResult.Fail("Missing required key: SkeletonKey");
                    }
                    if (unlockResult.Success)
                    {
                        await RefreshAllState();
                    }
                    return unlockResult;
                }
                else
                {
                    Log($"Door {LGConfig.GetDirectionName(direction)} is solid and cannot be worked.");
                }
                return TxResult.Fail("Door cannot be worked");
            }

            var hasActiveJob = HasActiveJobInCurrentRoom(direction);
            if (!hasActiveJob)
            {
                // Onchain helper stake is the source of truth; player ActiveJobs can lag briefly.
                hasActiveJob = await HasHelperStakeInCurrentRoom(direction);
            }
            var jobCompleted = room.JobCompleted != null && room.JobCompleted.Length > dir && room.JobCompleted[dir];

            if (jobCompleted)
            {
                if (!hasActiveJob)
                {
                    LogError("Job is completed, but this player is not an active helper for claiming.");
                    return TxResult.Fail("Not an active helper");
                }
                return await ClaimJobReward(direction);
            }

            if (!hasActiveJob)
            {
                var joinResult = await JoinJob(direction);
                if (joinResult.Success)
                {
                    return joinResult;
                }

                if (IsAlreadyJoinedError())
                {
                    Log("JoinJob returned AlreadyJoined. Refreshing and continuing as active helper.");
                    await RefreshAllState();
                    hasActiveJob = await HasHelperStakeInCurrentRoom(direction);
                    if (hasActiveJob)
                    {
                        room = await FetchCurrentRoom();
                        if (room == null)
                        {
                            return TxResult.Fail("Room not loaded after refresh");
                        }
                    }
                }
                else if (IsFrameworkAccountNotInitializedError())
                {
                    Log("JoinJob hit AccountNotInitialized. Refreshing and retrying once with latest room/player state.");
                    await RefreshAllState();
                    room = await FetchCurrentRoom();
                    if (room == null)
                    {
                        return TxResult.Fail("Room not loaded after refresh");
                    }

                    if (room.Walls[dir] == LGConfig.WALL_OPEN)
                    {
                        Log("Door became open during retry. Moving through door.");
                        return await MoveThroughDoor(direction);
                    }

                    hasActiveJob = await HasHelperStakeInCurrentRoom(direction);
                    if (!hasActiveJob)
                    {
                        var retryJoinResult = await JoinJob(direction);
                        if (retryJoinResult.Success)
                        {
                            return retryJoinResult;
                        }
                        hasActiveJob = await HasHelperStakeInCurrentRoom(direction);
                    }
                }

                if (!hasActiveJob)
                {
                    return TxResult.Fail("Could not join job");
                }
            }

            var progress = room.Progress[dir];
            var required = room.BaseSlots[dir];
            if (progress >= required)
            {
                var completeResult = await CompleteJob(direction);
                if (completeResult.Success)
                {
                    return completeResult;
                }

                if (IsMissingActiveJobError())
                {
                    Log("CompleteJob failed with NoActiveJob. Refreshing state and trying JoinJob if needed.");
                    await RefreshAllState();
                    var hasHelperStakeAfterRefresh = await HasHelperStakeInCurrentRoom(direction);
                    if (!hasHelperStakeAfterRefresh)
                    {
                        return await JoinJob(direction);
                    }
                }

                return TxResult.Fail("CompleteJob failed");
            }

            var tickResult = await TickJob(direction);
            if (tickResult.Success)
            {
                return tickResult;
            }

            if (IsMissingActiveJobError())
            {
                Log("TickJob failed with NoActiveJob. Refreshing state and trying JoinJob if needed.");
                await RefreshAllState();
                var hasHelperStakeAfterRefresh = await HasHelperStakeInCurrentRoom(direction);
                if (!hasHelperStakeAfterRefresh)
                {
                    return await JoinJob(direction);
                }
            }

            return TxResult.Fail("TickJob failed");
        }

        /// <summary>
        /// Performs the next sensible center action:
        /// Chest: Loot
        /// Boss alive: Join or Tick
        /// Boss defeated: Loot
        /// </summary>
        public async UniTask<TxResult> InteractWithCenter()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentGlobalState == null || CurrentPlayerState == null)
            {
                await RefreshAllState();
            }

            if (CurrentPlayerState == null)
            {
                LogError("Player not initialized");
                return TxResult.Fail("Player not initialized");
            }

            var room = await FetchCurrentRoom();
            if (room == null)
            {
                LogError("Current room not loaded");
                return TxResult.Fail("Current room not loaded");
            }

            if (room.CenterType == LGConfig.CENTER_EMPTY)
            {
                Log("Center is empty. No center action available.");
                return TxResult.Fail("Center empty");
            }

            if (room.CenterType == LGConfig.CENTER_CHEST)
            {
                Log("Center action: chest loot.");
                return await LootChest();
            }

            if (room.CenterType != LGConfig.CENTER_BOSS)
            {
                LogError($"Unknown center type: {room.CenterType}");
                return TxResult.Fail("Unknown center type");
            }

            if (room.BossDefeated)
            {
                Log("Center action: loot defeated boss.");
                return await LootBoss();
            }

            var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, room.X, room.Y);
            var isFighter = await HasBossFightInCurrentRoom(roomPda, Web3.Wallet.Account.PublicKey);
            if (!isFighter)
            {
                Log("Center action: join boss fight.");
                return await JoinBossFight();
            }

            Log("Center action: tick boss fight.");
            return await TickBossFight();
        }

        /// <summary>
        /// Check if current player has a boss fight PDA in current room
        /// </summary>
        public async UniTask<bool> HasBossFightInCurrentRoom(PublicKey roomPda, PublicKey playerPubkey)
        {
            try
            {
                var bossFightPda = DeriveBossFightPda(roomPda, playerPubkey);
                if (bossFightPda == null)
                {
                    return false;
                }

                var result = await _client.GetBossFightAccountAsync(bossFightPda.Key, Commitment.Confirmed);
                return result.WasSuccessful && result.ParsedResult != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if helper stake PDA exists for current room/direction/player.
        /// This is the onchain source of truth for whether player is an active helper.
        /// </summary>
        public async UniTask<bool> HasHelperStakeInCurrentRoom(byte direction)
        {
            if (Web3.Wallet == null)
            {
                return false;
            }

            if (CurrentGlobalState == null || CurrentPlayerState == null)
            {
                await RefreshAllState();
            }

            if (CurrentGlobalState == null || CurrentPlayerState == null)
            {
                return false;
            }

            var roomPda = DeriveRoomPda(
                CurrentGlobalState.SeasonSeed,
                CurrentPlayerState.CurrentRoomX,
                CurrentPlayerState.CurrentRoomY);
            if (roomPda == null)
            {
                return false;
            }

            var helperStakePda = DeriveHelperStakePda(roomPda, direction, Web3.Wallet.Account.PublicKey);
            if (helperStakePda == null)
            {
                return false;
            }

            return await AccountHasData(helperStakePda);
        }

        // ── Cross-room job helpers ────────────────────────────────────────
        // These variants accept explicit room coordinates so the caller can
        // clean up jobs that are NOT in the player's current room.

        /// <summary>
        /// Check if the player has a helper stake for a specific room and direction.
        /// Unlike <see cref="HasHelperStakeInCurrentRoom"/>, this accepts explicit room coordinates.
        /// </summary>
        public async UniTask<bool> HasHelperStakeForRoom(byte direction, int roomX, int roomY)
        {
            if (Web3.Wallet == null || CurrentGlobalState == null)
            {
                return false;
            }

            var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, roomX, roomY);
            if (roomPda == null)
            {
                return false;
            }

            var helperStakePda = DeriveHelperStakePda(roomPda, direction, Web3.Wallet.Account.PublicKey);
            if (helperStakePda == null)
            {
                return false;
            }

            return await AccountHasData(helperStakePda);
        }

        /// <summary>
        /// Tick a job for a specific room (not necessarily the player's current room).
        /// TickJob is permissionless and only needs the room PDA.
        /// </summary>
        public async UniTask<TxResult> TickJobForRoom(byte direction, int roomX, int roomY)
        {
            if (Web3.Wallet == null || CurrentGlobalState == null)
            {
                LogError("Wallet or global state not available");
                return TxResult.Fail("Wallet or global state not available");
            }

            Log($"Ticking job at ({roomX},{roomY}) direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "TickJob",
                    (context) =>
                    {
                        var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, roomX, roomY);

                        return ChaindepthProgram.TickJob(
                            new TickJobAccounts
                            {
                                Caller = context.Authority,
                                Global = _globalPda,
                                Room = roomPda
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job ticked at ({roomX},{roomY})! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"TickJobForRoom failed at ({roomX},{roomY}): {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Complete a job for a specific room (not necessarily the player's current room).
        /// </summary>
        public async UniTask<TxResult> CompleteJobForRoom(byte direction, int roomX, int roomY)
        {
            if (Web3.Wallet == null || CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Wallet, player, or global state not available");
                return TxResult.Fail("Wallet, player, or global state not available");
            }

            Log($"Completing job at ({roomX},{roomY}) direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "CompleteJob",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, roomX, roomY);
                        var (adjX, adjY) = LGConfig.GetAdjacentCoords(roomX, roomY, direction);
                        var adjacentRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, adjX, adjY);
                        var escrowPda = DeriveEscrowPda(roomPda, direction);
                        var helperStakePda = DeriveHelperStakePda(roomPda, direction, context.Player);

                        return ChaindepthProgram.CompleteJob(
                            new CompleteJobAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                HelperStake = helperStakePda,
                                AdjacentRoom = adjacentRoomPda,
                                Escrow = escrowPda,
                                PrizePool = CurrentGlobalState.PrizePool,
                                SessionAuthority = context.SessionAuthority,
                                TokenProgram = TokenProgram.ProgramIdKey,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job completed at ({roomX},{roomY})! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"CompleteJobForRoom failed at ({roomX},{roomY}): {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Claim a job reward for a specific room (not necessarily the player's current room).
        /// The room_presence PDA is always derived from the player's CURRENT room.
        /// </summary>
        public async UniTask<TxResult> ClaimJobRewardForRoom(byte direction, int roomX, int roomY)
        {
            if (Web3.Wallet == null || CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Wallet, player, or global state not available");
                return TxResult.Fail("Wallet, player, or global state not available");
            }

            Log($"Claiming reward at ({roomX},{roomY}) direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "ClaimJobReward",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        // Room PDA for the room where the job was.
                        var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, roomX, roomY);
                        var escrowPda = DeriveEscrowPda(roomPda, direction);
                        var helperStakePda = DeriveHelperStakePda(roomPda, direction, context.Player);
                        // Room presence is always the player's CURRENT room.
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );
                        var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                            context.Player,
                            CurrentGlobalState.SkrMint
                        );

                        return ChaindepthProgram.ClaimJobReward(
                            new ClaimJobRewardAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                RoomPresence = roomPresencePda,
                                Escrow = escrowPda,
                                HelperStake = helperStakePda,
                                PlayerTokenAccount = playerTokenAccount,
                                SessionAuthority = context.SessionAuthority,
                                TokenProgram = TokenProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job reward claimed at ({roomX},{roomY})! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"ClaimJobRewardForRoom failed at ({roomX},{roomY}): {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Initialize player account at spawn point
        /// </summary>
        public async UniTask<string> InitPlayer()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentGlobalState == null)
            {
                await FetchGlobalState();
                if (CurrentGlobalState == null)
                {
                    LogError("Global state not loaded");
                    return null;
                }
            }

            Log("Initializing player account...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var profilePda = DeriveProfilePda(Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    LGConfig.START_X,
                    LGConfig.START_Y,
                    Web3.Wallet.Account.PublicKey
                );

                // Use generated instruction builder
                var instruction = ChaindepthProgram.InitPlayer(
                    new InitPlayerAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Profile = profilePda,
                        RoomPresence = roomPresencePda,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Player initialized! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"InitPlayer failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Move player to new coordinates
        /// </summary>
        public async UniTask<TxResult> MovePlayer(int newX, int newY)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            Log($"Moving player to ({newX}, {newY})...");
            if (CurrentPlayerState != null &&
                TryGetMoveDirection(
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    newX,
                    newY,
                    out var moveDirection))
            {
                var currentWallState = CurrentRoomState != null &&
                    CurrentRoomState.Walls != null &&
                    moveDirection < CurrentRoomState.Walls.Length
                    ? CurrentRoomState.Walls[moveDirection]
                    : byte.MaxValue;

                Log(
                    $"Move validation: from=({CurrentPlayerState.CurrentRoomX},{CurrentPlayerState.CurrentRoomY}) " +
                    $"to=({newX},{newY}) direction={LGConfig.GetDirectionName(moveDirection)} " +
                    $"currentWall={LGConfig.GetWallStateName(currentWallState)}");
            }

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "MovePlayer",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var profilePda = DeriveProfilePda(context.Player);
                        var currentRoomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState?.CurrentRoomX ?? LGConfig.START_X,
                            CurrentPlayerState?.CurrentRoomY ?? LGConfig.START_Y);
                        var targetRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, newX, newY);
                        var currentPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState?.CurrentRoomX ?? LGConfig.START_X,
                            CurrentPlayerState?.CurrentRoomY ?? LGConfig.START_Y,
                            context.Player
                        );
                        var targetPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            newX,
                            newY,
                            context.Player
                        );

                        return ChaindepthProgram.MovePlayer(
                            new MovePlayerAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Profile = profilePda,
                                CurrentRoom = currentRoomPda,
                                TargetRoom = targetRoomPda,
                                CurrentPresence = currentPresencePda,
                                TargetPresence = targetPresencePda,
                                SessionAuthority = context.SessionAuthority,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            (sbyte)newX,
                            (sbyte)newY,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Move transaction sent: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"Move failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Unlock a locked door in the current room by consuming the required key.
        /// </summary>
        public async UniTask<TxResult> UnlockDoor(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Unlocking door in direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "UnlockDoor",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);
                        var (adjacentX, adjacentY) = LGConfig.GetAdjacentCoords(
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            direction);
                        var adjacentRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, adjacentX, adjacentY);
                        var inventoryPda = DeriveInventoryPda(context.Player);

                        return ChaindepthProgram.UnlockDoor(
                            new UnlockDoorAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                AdjacentRoom = adjacentRoomPda,
                                Inventory = inventoryPda,
                                SessionAuthority = context.SessionAuthority,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Door unlocked! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"UnlockDoor failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Join a job in the specified direction
        /// </summary>
        public async UniTask<TxResult> JoinJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Joining job in direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    Web3.Wallet.Account.PublicKey,
                    CurrentGlobalState.SkrMint
                );

                // ── Diagnostic: token account + delegation state ──
                var walletSessionManager = GetWalletSessionManager();
                var sessionActive = walletSessionManager != null && walletSessionManager.CanUseLocalSessionSigning;
                var sessionSignerKey = walletSessionManager?.ActiveSessionSignerPublicKey?.Key ?? "<none>";
                Log($"JoinJob diag: playerWallet={Web3.Wallet.Account.PublicKey.Key.Substring(0, 8)}.. playerATA={playerTokenAccount.Key.Substring(0, 8)}.. sessionActive={sessionActive} sessionSigner={sessionSignerKey.Substring(0, Math.Min(8, sessionSignerKey.Length))}.. skrMint={CurrentGlobalState.SkrMint.Key.Substring(0, 8)}..");

                try
                {
                    var rpc = Web3.Wallet.ActiveRpcClient;
                    if (rpc != null)
                    {
                        var tokenBalResult = await rpc.GetTokenAccountBalanceAsync(playerTokenAccount);
                        if (tokenBalResult.WasSuccessful && tokenBalResult.Result?.Value != null)
                        {
                            var rawAmount = tokenBalResult.Result.Value.Amount ?? "0";
                            var delegateStr = tokenBalResult.Result.Value.UiAmountString ?? "?";
                            Log($"JoinJob diag: playerATA balance={rawAmount} raw, uiAmount={delegateStr}");
                        }
                        else
                        {
                            Log($"JoinJob diag: failed to fetch playerATA balance: {tokenBalResult.Reason}");
                        }

                        // Check delegation on the token account
                        var accountInfoResult = await rpc.GetAccountInfoAsync(playerTokenAccount);
                        if (accountInfoResult.WasSuccessful && accountInfoResult.Result?.Value?.Data != null)
                        {
                            var data = accountInfoResult.Result.Value.Data;
                            Log($"JoinJob diag: playerATA account owner={accountInfoResult.Result.Value.Owner} dataLen={data.Count}");
                        }
                    }
                }
                catch (System.Exception diagEx)
                {
                    Log($"JoinJob diag: error fetching token info: {diagEx.Message}");
                }
                // ── End diagnostic ──

                if (!await AccountHasData(playerTokenAccount))
                {
                    LogError("JoinJob blocked: your SKR token account (ATA) is not initialized for this wallet.");
                    LogError("Mint/fund SKR first, then retry JoinJob.");
                    return TxResult.Fail("SKR token account not initialized");
                }

                var result = await ExecuteGameplayActionAsync(
                    "JoinJob",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);
                        var escrowPda = DeriveEscrowPda(roomPda, direction);
                        var helperStakePda = DeriveHelperStakePda(roomPda, direction, context.Player);
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );

                        if (context.UsesSessionSigner)
                        {
                            return ChaindepthProgram.JoinJobWithSession(
                                new JoinJobWithSessionAccounts
                                {
                                    Authority = context.Authority,
                                    Player = context.Player,
                                    Global = _globalPda,
                                    PlayerAccount = playerPda,
                                    Room = roomPda,
                                    RoomPresence = roomPresencePda,
                                    Escrow = escrowPda,
                                    HelperStake = helperStakePda,
                                    PlayerTokenAccount = playerTokenAccount,
                                    SkrMint = CurrentGlobalState.SkrMint,
                                    SessionAuthority = context.SessionAuthority,
                                    TokenProgram = TokenProgram.ProgramIdKey,
                                    SystemProgram = SystemProgram.ProgramIdKey
                                },
                                direction,
                                _programId
                            );
                        }

                        return ChaindepthProgram.JoinJob(
                            new JoinJobAccounts
                            {
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                RoomPresence = roomPresencePda,
                                Escrow = escrowPda,
                                HelperStake = helperStakePda,
                                PlayerTokenAccount = playerTokenAccount,
                                SkrMint = CurrentGlobalState.SkrMint,
                                TokenProgram = TokenProgram.ProgramIdKey,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (!result.Success)
                {
                    LogError($"JoinJob diag: TX failed. lastProgramErrorCode={_lastProgramErrorCode?.ToString() ?? "<null>"} (0x{_lastProgramErrorCode:X})");
                }
                else
                {
                    Log($"Joined job! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"JoinJob failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Complete a job in the specified direction
        /// </summary>
        public async UniTask<TxResult> CompleteJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Completing job in direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "CompleteJob",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);
                        var (adjX, adjY) = LGConfig.GetAdjacentCoords(
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            direction);
                        var adjacentRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, adjX, adjY);
                        var escrowPda = DeriveEscrowPda(roomPda, direction);
                        var helperStakePda = DeriveHelperStakePda(roomPda, direction, context.Player);

                        return ChaindepthProgram.CompleteJob(
                            new CompleteJobAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                HelperStake = helperStakePda,
                                AdjacentRoom = adjacentRoomPda,
                                Escrow = escrowPda,
                                PrizePool = CurrentGlobalState.PrizePool,
                                SessionAuthority = context.SessionAuthority,
                                TokenProgram = TokenProgram.ProgramIdKey,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job completed! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"CompleteJob failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Abandon a job in the specified direction
        /// </summary>
        public async UniTask<TxResult> AbandonJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Abandoning job in direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "AbandonJob",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);
                        var escrowPda = DeriveEscrowPda(roomPda, direction);
                        var helperStakePda = DeriveHelperStakePda(roomPda, direction, context.Player);
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );
                        var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                            context.Player,
                            CurrentGlobalState.SkrMint
                        );

                        return ChaindepthProgram.AbandonJob(
                            new AbandonJobAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                RoomPresence = roomPresencePda,
                                Escrow = escrowPda,
                                HelperStake = helperStakePda,
                                PrizePool = CurrentGlobalState.PrizePool,
                                PlayerTokenAccount = playerTokenAccount,
                                SessionAuthority = context.SessionAuthority,
                                TokenProgram = TokenProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job abandoned! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"AbandonJob failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        private static bool TryGetMoveDirection(
            int fromX,
            int fromY,
            int toX,
            int toY,
            out byte direction)
        {
            direction = 0;
            var dx = toX - fromX;
            var dy = toY - fromY;
            if (dx == 0 && dy == 1)
            {
                direction = LGConfig.DIRECTION_NORTH;
                return true;
            }
            if (dx == 0 && dy == -1)
            {
                direction = LGConfig.DIRECTION_SOUTH;
                return true;
            }
            if (dx == 1 && dy == 0)
            {
                direction = LGConfig.DIRECTION_EAST;
                return true;
            }
            if (dx == -1 && dy == 0)
            {
                direction = LGConfig.DIRECTION_WEST;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Claim reward for a completed job in the specified direction
        /// </summary>
        public async UniTask<TxResult> ClaimJobReward(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Claiming reward for direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "ClaimJobReward",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY
                        );
                        var escrowPda = DeriveEscrowPda(roomPda, direction);
                        var helperStakePda = DeriveHelperStakePda(roomPda, direction, context.Player);
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );
                        var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                            context.Player,
                            CurrentGlobalState.SkrMint
                        );

                        return ChaindepthProgram.ClaimJobReward(
                            new ClaimJobRewardAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                RoomPresence = roomPresencePda,
                                Escrow = escrowPda,
                                HelperStake = helperStakePda,
                                PlayerTokenAccount = playerTokenAccount,
                                SessionAuthority = context.SessionAuthority,
                                TokenProgram = TokenProgram.ProgramIdKey
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job reward claimed! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"ClaimJobReward failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Loot a chest in the current room
        /// </summary>
        public async UniTask<TxResult> LootChest()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log("Looting chest...");

            // Snapshot inventory before loot so we can diff afterwards
            var inventoryBefore = CurrentInventoryState;

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "LootChest",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);
                        var inventoryPda = DeriveInventoryPda(context.Player);
                        var lootReceiptPda = DeriveLootReceiptPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player);

                        return ChaindepthProgram.LootChest(
                            new LootChestAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                Inventory = inventoryPda,
                                LootReceipt = lootReceiptPda,
                                SessionAuthority = context.SessionAuthority,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Chest looted! TX: {result.Signature}");
                    await RefreshAllState();

                    // Compute loot diff and fire event
                    var lootResult = LGDomainMapper.ComputeLootDiff(inventoryBefore, CurrentInventoryState);
                    if (lootResult.Items.Count > 0)
                    {
                        Log($"Loot result: {lootResult.Items.Count} item(s) gained");
                        OnChestLootResult?.Invoke(lootResult);
                    }
                    else
                    {
                        Log("Loot result: no new items detected (diff empty)");
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"LootChest failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Equip an inventory item for combat (0 to unequip)
        /// </summary>
        public async UniTask<TxResult> EquipItem(ushort itemId)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null)
            {
                LogError("Player state not loaded");
                return TxResult.Fail("Player state not loaded");
            }

            Log($"Equipping item id {itemId}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "EquipItem",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var inventoryPda = DeriveInventoryPda(context.Player);
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );

                        return ChaindepthProgram.EquipItem(
                            new EquipItemAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Inventory = inventoryPda,
                                RoomPresence = roomPresencePda,
                                SessionAuthority = context.SessionAuthority
                            },
                            itemId,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Equipped item. TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"EquipItem failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Set player skin id in profile and current room presence.
        /// </summary>
        public async UniTask<TxResult> SetPlayerSkin(ushort skinId)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "SetPlayerSkin",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var profilePda = DeriveProfilePda(context.Player);
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );

                        return ChaindepthProgram.SetPlayerSkin(
                            new SetPlayerSkinAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Profile = profilePda,
                                RoomPresence = roomPresencePda,
                                SessionAuthority = context.SessionAuthority
                            },
                            skinId,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Skin set. TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception error)
            {
                LogError($"SetPlayerSkin failed: {error.Message}");
                return TxResult.Fail(error.Message);
            }
        }

        /// <summary>
        /// Create/update profile (skin + optional display name) and grant starter pickaxe once.
        /// </summary>
        public async UniTask<TxResult> CreatePlayerProfile(ushort skinId, string displayName)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "CreatePlayerProfile",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var profilePda = DeriveProfilePda(context.Player);
                        var inventoryPda = DeriveInventoryPda(context.Player);
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );

                        return ChaindepthProgram.CreatePlayerProfile(
                            new CreatePlayerProfileAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Profile = profilePda,
                                Inventory = inventoryPda,
                                RoomPresence = roomPresencePda,
                                SessionAuthority = context.SessionAuthority,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            skinId,
                            displayName ?? string.Empty,
                            _programId
                        );
                    },
                    ensureSessionIfPossible: false);

                if (result.Success)
                {
                    Log($"Profile created. TX: {result.Signature}");
                    await RefreshAllState();
                }

                return result;
            }
            catch (Exception error)
            {
                LogError($"CreatePlayerProfile failed: {error.Message}");
                return TxResult.Fail(error.Message);
            }
        }

        /// <summary>
        /// Join the boss fight in the current room
        /// </summary>
        public async UniTask<TxResult> JoinBossFight()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log("Joining boss fight...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "JoinBossFight",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var profilePda = DeriveProfilePda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY
                        );
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );
                        var bossFightPda = DeriveBossFightPda(roomPda, context.Player);

                        return ChaindepthProgram.JoinBossFight(
                            new JoinBossFightAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Profile = profilePda,
                                Room = roomPda,
                                RoomPresence = roomPresencePda,
                                BossFight = bossFightPda,
                                SessionAuthority = context.SessionAuthority,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Joined boss fight! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"JoinBossFight failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Tick boss HP in the current room
        /// </summary>
        public async UniTask<TxResult> TickBossFight()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log("Ticking boss fight...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "TickBossFight",
                    (context) =>
                    {
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY
                        );

                        return ChaindepthProgram.TickBossFight(
                            new TickBossFightAccounts
                            {
                                Caller = context.Authority,
                                Global = _globalPda,
                                Room = roomPda
                            },
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Boss ticked! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"TickBossFight failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Loot defeated boss in current room (fighters only)
        /// </summary>
        public async UniTask<TxResult> LootBoss()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log("Looting boss...");

            // Snapshot inventory before loot so we can diff afterwards
            var inventoryBefore = CurrentInventoryState;

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "LootBoss",
                    (context) =>
                    {
                        var playerPda = DerivePlayerPda(context.Player);
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY
                        );
                        var roomPresencePda = DeriveRoomPresencePda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player
                        );
                        var bossFightPda = DeriveBossFightPda(roomPda, context.Player);
                        var inventoryPda = DeriveInventoryPda(context.Player);
                        var lootReceiptPda = DeriveLootReceiptPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY,
                            context.Player);

                        return ChaindepthProgram.LootBoss(
                            new LootBossAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                PlayerAccount = playerPda,
                                Room = roomPda,
                                RoomPresence = roomPresencePda,
                                BossFight = bossFightPda,
                                Inventory = inventoryPda,
                                LootReceipt = lootReceiptPda,
                                SessionAuthority = context.SessionAuthority,
                                SystemProgram = SystemProgram.ProgramIdKey
                            },
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Boss looted! TX: {result.Signature}");
                    await RefreshAllState();

                    // Compute loot diff and fire event
                    var lootResult = LGDomainMapper.ComputeLootDiff(inventoryBefore, CurrentInventoryState);
                    if (lootResult.Items.Count > 0)
                    {
                        Log($"Boss loot result: {lootResult.Items.Count} item(s) gained");
                        OnChestLootResult?.Invoke(lootResult);
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"LootBoss failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Tick/update a job's progress
        /// </summary>
        public async UniTask<TxResult> TickJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Ticking job in direction {LGConfig.GetDirectionName(direction)}...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "TickJob",
                    (context) =>
                    {
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);

                        return ChaindepthProgram.TickJob(
                            new TickJobAccounts
                            {
                                Caller = context.Authority,
                                Global = _globalPda,
                                Room = roomPda
                            },
                            direction,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job ticked! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"TickJob failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        /// <summary>
        /// Boost a job with additional tokens
        /// </summary>
        public async UniTask<TxResult> BoostJob(byte direction, ulong boostAmount)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return TxResult.Fail("Wallet not connected");
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return TxResult.Fail("Player or global state not loaded");
            }

            Log($"Boosting job in direction {LGConfig.GetDirectionName(direction)} with {boostAmount} tokens...");

            try
            {
                var result = await ExecuteGameplayActionAsync(
                    "BoostJob",
                    (context) =>
                    {
                        var roomPda = DeriveRoomPda(
                            CurrentGlobalState.SeasonSeed,
                            CurrentPlayerState.CurrentRoomX,
                            CurrentPlayerState.CurrentRoomY);
                        var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                            context.Player,
                            CurrentGlobalState.SkrMint
                        );

                        return ChaindepthProgram.BoostJob(
                            new BoostJobAccounts
                            {
                                Authority = context.Authority,
                                Player = context.Player,
                                Global = _globalPda,
                                Room = roomPda,
                                PrizePool = CurrentGlobalState.PrizePool,
                                PlayerTokenAccount = playerTokenAccount,
                                SessionAuthority = context.SessionAuthority,
                                TokenProgram = TokenProgram.ProgramIdKey
                            },
                            direction,
                            boostAmount,
                            _programId
                        );
                    });

                if (result.Success)
                {
                    Log($"Job boosted! TX: {result.Signature}");
                }

                return result;
            }
            catch (Exception e)
            {
                LogError($"BoostJob failed: {e.Message}");
                return TxResult.Fail(e.Message);
            }
        }

        #endregion

        #region Helper Methods

        private LGWalletSessionManager GetWalletSessionManager()
        {
            return LGWalletSessionManager.EnsureInstance();
        }

        private GameplaySigningContext BuildGameplaySigningContext(LGWalletSessionManager walletSessionManager)
        {
            var walletAccount = Web3.Wallet?.Account;
            var context = new GameplaySigningContext
            {
                SignerAccount = walletAccount,
                Authority = walletAccount?.PublicKey,
                Player = walletAccount?.PublicKey,
                SessionAuthority = null,
                UsesSessionSigner = false
            };

            if (walletSessionManager == null || !walletSessionManager.CanUseLocalSessionSigning)
            {
                return context;
            }

            var sessionSigner = walletSessionManager.ActiveSessionSignerAccount;
            var sessionAuthority = walletSessionManager.ActiveSessionAuthorityPda;
            if (sessionSigner == null || sessionAuthority == null)
            {
                return context;
            }

            context.SignerAccount = sessionSigner;
            context.Authority = sessionSigner.PublicKey;
            context.SessionAuthority = sessionAuthority;
            context.UsesSessionSigner = true;
            return context;
        }

        private async UniTask<TxResult> ExecuteGameplayActionAsync(
            string actionName,
            Func<GameplaySigningContext, TransactionInstruction> buildInstruction,
            bool ensureSessionIfPossible = true)
        {
            if (Web3.Wallet?.Account == null)
            {
                LogError($"{actionName} failed: wallet not connected.");
                return TxResult.Fail("Wallet not connected");
            }

            var walletSessionManager = GetWalletSessionManager();
            if (ensureSessionIfPossible &&
                walletSessionManager != null &&
                !walletSessionManager.CanUseLocalSessionSigning)
            {
                var ensured = await walletSessionManager.EnsureGameplaySessionAsync();
                if (!ensured)
                {
                    Log($"{actionName}: session unavailable. Falling back to wallet signing.");
                }
            }

            var signingContext = BuildGameplaySigningContext(walletSessionManager);
            Log($"{actionName}: signingContext usesSession={signingContext.UsesSessionSigner} authority={signingContext.Authority?.Key?.Substring(0, 8) ?? "<null>"} player={signingContext.Player?.Key?.Substring(0, 8) ?? "<null>"}");
            if (signingContext.SignerAccount == null || signingContext.Authority == null || signingContext.Player == null)
            {
                LogError($"{actionName} failed: signer context is missing.");
                return TxResult.Fail("Signer context missing");
            }

            var signature = await SendTransaction(
                buildInstruction(signingContext),
                signingContext.SignerAccount,
                new List<Account> { signingContext.SignerAccount });

            if (!string.IsNullOrWhiteSpace(signature))
            {
                return TxResult.Ok(signature);
            }

            var errorDetail = FormatProgramError(_lastProgramErrorCode);

            if (!signingContext.UsesSessionSigner || walletSessionManager == null)
            {
                return TxResult.Fail($"{actionName} TX failed{errorDetail}");
            }

            if (!walletSessionManager.IsSessionRecoverableProgramError(_lastProgramErrorCode))
            {
                return TxResult.Fail($"{actionName} TX failed (non-recoverable){errorDetail}");
            }

            Log($"{actionName} failed with recoverable session error. Attempting one session restart.");
            var restarted = await walletSessionManager.EnsureGameplaySessionAsync();
            if (!restarted)
            {
                return TxResult.Fail($"{actionName} session restart failed");
            }

            var retryContext = BuildGameplaySigningContext(walletSessionManager);
            if (!retryContext.UsesSessionSigner || retryContext.SignerAccount == null)
            {
                LogError($"{actionName} retry failed: session signer unavailable after restart.");
                return TxResult.Fail($"{actionName} retry signer unavailable");
            }

            var retrySignature = await SendTransaction(
                buildInstruction(retryContext),
                retryContext.SignerAccount,
                new List<Account> { retryContext.SignerAccount });

            if (!string.IsNullOrWhiteSpace(retrySignature))
            {
                return TxResult.Ok(retrySignature);
            }

            return TxResult.Fail($"{actionName} retry TX failed");
        }

        /// <summary>
        /// Send a transaction with a single instruction
        /// </summary>
        private async UniTask<string> SendTransaction(TransactionInstruction instruction)
        {
            if (Web3.Wallet?.Account == null)
            {
                LogError("Wallet not connected - cannot send transaction");
                return null;
            }

            return await SendTransaction(
                instruction,
                Web3.Wallet.Account,
                new List<Account> { Web3.Wallet.Account });
        }

        private async UniTask<string> SendTransaction(
            TransactionInstruction instruction,
            Account feePayer,
            IList<Account> signers)
        {
            if (feePayer == null || signers == null || signers.Count == 0)
            {
                LogError("Missing signer(s) for transaction.");
                return null;
            }

            try
            {
                var useWalletAdapter = ShouldUseWalletAdapterSigning(feePayer, signers);
                Log($"SendTransaction: feePayer={feePayer.PublicKey?.Key?.Substring(0, 8) ?? "<null>"} signerCount={signers.Count} useWalletAdapter={useWalletAdapter}");
                if (useWalletAdapter)
                {
                    var walletSignedSignature = await SendTransactionViaWalletAdapterAsync(
                        instruction,
                        feePayer,
                        signers);
                    if (!string.IsNullOrWhiteSpace(walletSignedSignature))
                    {
                        _lastProgramErrorCode = null;
                        OnTransactionSent?.Invoke(walletSignedSignature);
                        return walletSignedSignature;
                    }
                }

                var rpcCandidates = GetRpcCandidates();
                if (rpcCandidates.Count == 0)
                {
                    LogError("RPC client not available");
                    return null;
                }

                for (var candidateIndex = 0; candidateIndex < rpcCandidates.Count; candidateIndex += 1)
                {
                    var rpcCandidate = rpcCandidates[candidateIndex];
                    var rpcLabel = candidateIndex == 0 ? "primary" : "fallback";
                    var endpoint = DescribeRpcEndpoint(rpcCandidate);
                    var rawProbeAttempted = false;
                    for (var attempt = 1; attempt <= MaxTransientSendAttemptsPerRpc; attempt += 1)
                    {
                        var blockHashResult = await rpcCandidate.GetLatestBlockHashAsync();
                        if (!blockHashResult.WasSuccessful || blockHashResult.Result?.Value == null)
                        {
                            LogError(
                                $"[{rpcLabel}] Failed to get blockhash (attempt {attempt}/{MaxTransientSendAttemptsPerRpc}) endpoint={endpoint}: {blockHashResult.Reason}");
                            break;
                        }

                        var txBytes = new TransactionBuilder()
                            .SetRecentBlockHash(blockHashResult.Result.Value.Blockhash)
                            .SetFeePayer(feePayer)
                            .AddInstruction(instruction)
                            .Build(new List<Account>(signers));

                        Log(
                            $"Transaction built and signed via {rpcLabel} RPC, size={txBytes.Length} bytes, attempt={attempt}");

                        var txBase64 = Convert.ToBase64String(txBytes);
                        var result = await rpcCandidate.SendTransactionAsync(
                            txBase64,
                            skipPreflight: false,
                            preFlightCommitment: Commitment.Confirmed);

                        if (result.WasSuccessful)
                        {
                            _lastProgramErrorCode = null;
                            Log($"Transaction sent ({rpcLabel}): {result.Result}");
                            OnTransactionSent?.Invoke(result.Result);
                            return result.Result;
                        }

                        var failureReason = string.IsNullOrWhiteSpace(result.Reason)
                            ? "<empty reason>"
                            : result.Reason;
                        _lastProgramErrorCode = ExtractCustomProgramErrorCode(failureReason);
                        LogError(
                            $"[{rpcLabel}] Transaction failed (attempt {attempt}/{MaxTransientSendAttemptsPerRpc}). " +
                            $"Endpoint={endpoint}. Reason: {failureReason}");
                        if (result.ServerErrorCode != 0)
                        {
                            LogError($"[{rpcLabel}] Server error code: {result.ServerErrorCode}");
                        }

                        if (!rawProbeAttempted && IsJsonParseFailure(failureReason))
                        {
                            rawProbeAttempted = true;
                            var rawProbe = await TrySendTransactionViaRawHttpAsync(endpoint, txBase64);
                            if (rawProbe.Attempted)
                            {
                                LogError(
                                    $"[{rpcLabel}] Raw HTTP probe status={rawProbe.HttpStatusCode} " +
                                    $"networkError={rawProbe.NetworkError ?? "<none>"} " +
                                    $"rpcError={rawProbe.RpcError ?? "<none>"} " +
                                    $"body={rawProbe.BodySnippet}");
                            }

                            if (rawProbe.WasSuccessful)
                            {
                                _lastProgramErrorCode = null;
                                Log($"Transaction sent via raw HTTP ({rpcLabel}): {rawProbe.Signature}");
                                OnTransactionSent?.Invoke(rawProbe.Signature);
                                return rawProbe.Signature;
                            }
                        }

                        LogFrameworkErrorDetails(failureReason);

                        if (!IsTransientRpcFailure(failureReason))
                        {
                            break;
                        }

                        if (attempt < MaxTransientSendAttemptsPerRpc)
                        {
                            var retryDelayMs = BaseTransientRetryDelayMs * attempt;
                            Log(
                                $"[{rpcLabel}] Transient RPC failure detected. Retrying in {retryDelayMs}ms.");
                            await UniTask.Delay(retryDelayMs);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"Transaction exception: {ex.Message}");
                return null;
            }
        }

        private async UniTask<string> SendTransactionViaWalletAdapterAsync(
            TransactionInstruction instruction,
            Account feePayer,
            IList<Account> signers)
        {
            var wallet = Web3.Wallet;
            var walletAccount = wallet?.Account;
            if (wallet == null || walletAccount == null || instruction == null)
            {
                LogError($"LGManager WalletAdapter path aborted: wallet={wallet != null} account={walletAccount != null} ixNull={instruction == null}");
                return null;
            }

            Log("LGManager WalletAdapter: requesting blockhash...");
            var blockhash = await wallet.GetBlockHash(Commitment.Confirmed, useCache: false);
            if (string.IsNullOrWhiteSpace(blockhash))
            {
                LogError("Wallet adapter signing failed: missing recent blockhash.");
                return null;
            }

            var transaction = new Transaction
            {
                RecentBlockHash = blockhash,
                FeePayer = feePayer.PublicKey,
                Instructions = new List<TransactionInstruction> { instruction },
                Signatures = new List<SignaturePubKeyPair>()
            };

            var partialSignCount = 0;
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
                partialSignCount++;
            }

            Log($"LGManager WalletAdapter: calling SignAndSendTransaction (partialSigned={partialSignCount}, feePayer={feePayer.PublicKey})...");
            var sendResult = await wallet.SignAndSendTransaction(
                transaction,
                skipPreflight: false,
                commitment: Commitment.Confirmed);
            Log($"LGManager WalletAdapter: result success={sendResult.WasSuccessful} sig={sendResult.Result ?? "<null>"} reason={sendResult.Reason ?? "<null>"}");

            if (sendResult.WasSuccessful && !string.IsNullOrWhiteSpace(sendResult.Result))
            {
                Log($"Transaction sent via wallet adapter: {sendResult.Result}");
                return sendResult.Result;
            }

            var reason = string.IsNullOrWhiteSpace(sendResult.Reason)
                ? "<empty reason>"
                : sendResult.Reason;
            LogError($"Wallet adapter send failed: {reason} class={ClassifyFailureReason(reason)}");
            return null;
        }

        private static string ClassifyFailureReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "unknown";
            if (reason.IndexOf("could not predict balance changes", StringComparison.OrdinalIgnoreCase) >= 0) return "wallet_simulation_unpredictable_balance";
            if (reason.IndexOf("custom program error", StringComparison.OrdinalIgnoreCase) >= 0) return "program_error";
            if (reason.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0) return "rpc_connection_refused";
            if (reason.IndexOf("Unable to parse json", StringComparison.OrdinalIgnoreCase) >= 0) return "rpc_json_parse";
            if (reason.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0) return "rpc_timeout";
            return "other";
        }

        private static bool ShouldUseWalletAdapterSigning(Account feePayer, IList<Account> signers)
        {
            var walletAccount = Web3.Wallet?.Account;
            if (walletAccount == null || feePayer == null || signers == null)
            {
                return false;
            }

            return string.Equals(
                walletAccount.PublicKey.Key,
                feePayer.PublicKey.Key,
                StringComparison.Ordinal);
        }

        private List<IRpcClient> GetRpcCandidates()
        {
            var candidates = new List<IRpcClient>();
            if (_rpcClient != null)
            {
                candidates.Add(_rpcClient);
            }

            if (_fallbackRpcClient != null && !candidates.Contains(_fallbackRpcClient))
            {
                candidates.Add(_fallbackRpcClient);
            }

            var walletRpc = Web3.Wallet?.ActiveRpcClient;
            if (walletRpc != null && !candidates.Contains(walletRpc))
            {
                candidates.Add(walletRpc);
            }

            return candidates;
        }

        private static string NormalizeRpcUrl(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim();
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

        private void LogFrameworkErrorDetails(string reason)
        {
            var errorCode = ExtractCustomProgramErrorCode(reason);
            if (!errorCode.HasValue)
            {
                return;
            }

            var hexValue = errorCode.Value.ToString("x");
            if (TryMapProgramError(errorCode.Value, out var programErrorMessage))
            {
                LogError($"Program error {errorCode.Value} (0x{hexValue}): {programErrorMessage}");
                return;
            }

            if (TryMapAnchorFrameworkError(errorCode.Value, out var mappedMessage))
            {
                LogError($"Program framework error {errorCode.Value} (0x{hexValue}): {mappedMessage}");
            }
        }

        private static uint? ExtractCustomProgramErrorCode(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return null;
            }

            var markerIndex = reason.IndexOf("custom program error: 0x", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var hexStart = markerIndex + "custom program error: 0x".Length;
            var hexEnd = hexStart;
            while (hexEnd < reason.Length && Uri.IsHexDigit(reason[hexEnd]))
            {
                hexEnd += 1;
            }

            if (hexEnd <= hexStart)
            {
                return null;
            }

            var hexValue = reason.Substring(hexStart, hexEnd - hexStart);
            if (!uint.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out var errorCode))
            {
                return null;
            }

            return errorCode;
        }

        private bool IsMissingActiveJobError()
        {
            return _lastProgramErrorCode.HasValue &&
                   _lastProgramErrorCode.Value == (uint)Chaindepth.Errors.ChaindepthErrorKind.NoActiveJob;
        }

        private bool IsAlreadyJoinedError()
        {
            return _lastProgramErrorCode.HasValue &&
                   _lastProgramErrorCode.Value == (uint)Chaindepth.Errors.ChaindepthErrorKind.AlreadyJoined;
        }

        private bool IsFrameworkAccountNotInitializedError()
        {
            return _lastProgramErrorCode.HasValue && _lastProgramErrorCode.Value == 3012;
        }

        private bool IsMissingRequiredKeyError()
        {
            return _lastProgramErrorCode.HasValue &&
                   _lastProgramErrorCode.Value == (uint)Chaindepth.Errors.ChaindepthErrorKind.MissingRequiredKey;
        }

        /// <summary>
        /// Formats the last program error code into a human-readable suffix
        /// for inclusion in TxResult error messages.
        /// Returns an empty string when there is no error code.
        /// </summary>
        private string FormatProgramError(uint? errorCode)
        {
            if (!errorCode.HasValue)
            {
                return string.Empty;
            }

            if (TryMapProgramError(errorCode.Value, out var programMsg))
            {
                return $" [program: {programMsg} (0x{errorCode.Value:X})]";
            }

            if (TryMapAnchorFrameworkError(errorCode.Value, out var anchorMsg))
            {
                return $" [anchor: {anchorMsg} (0x{errorCode.Value:X})]";
            }

            return $" [error: 0x{errorCode.Value:X}]";
        }

        private async UniTask<TxResult> MoveThroughDoor(byte direction)
        {
            if (CurrentPlayerState == null)
            {
                await FetchPlayerState();
                if (CurrentPlayerState == null)
                {
                    return TxResult.Fail("Player state not loaded");
                }
            }

            var (targetX, targetY) = LGConfig.GetAdjacentCoords(
                CurrentPlayerState.CurrentRoomX,
                CurrentPlayerState.CurrentRoomY,
                direction);

            var moveResult = await MovePlayer(targetX, targetY);
            if (moveResult.Success)
            {
                return moveResult;
            }

            if (IsFrameworkAccountNotInitializedError())
            {
                Log("MovePlayer hit AccountNotInitialized. Refreshing state and retrying once.");
                await RefreshAllState();
                moveResult = await MovePlayer(targetX, targetY);
            }

            return moveResult;
        }

        private static bool TryMapProgramError(uint errorCode, out string message)
        {
            if (Enum.IsDefined(typeof(Chaindepth.Errors.ChaindepthErrorKind), errorCode))
            {
                var errorKind = (Chaindepth.Errors.ChaindepthErrorKind)errorCode;
                message = errorKind.ToString();
                return true;
            }

            message = null;
            return false;
        }

        private static bool TryMapAnchorFrameworkError(uint errorCode, out string message)
        {
            switch (errorCode)
            {
                case 2006:
                    message = "Constraint seeds mismatch (client/account context is stale vs expected PDA seeds).";
                    return true;
                case 3000:
                    message = "Account discriminator already set.";
                    return true;
                case 3001:
                    message = "Account discriminator not found.";
                    return true;
                case 3002:
                    message = "Account discriminator mismatch.";
                    return true;
                case 3003:
                    message = "Account did not deserialize (often stale account layout vs current program struct).";
                    return true;
                case 3004:
                    message = "Account did not serialize.";
                    return true;
                case 3012:
                    message = "Account not initialized.";
                    return true;
                default:
                    message = null;
                    return false;
            }
        }

        #endregion
    }
}

