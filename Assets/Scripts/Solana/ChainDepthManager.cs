using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Manager for ChainDepth Solana program interactions.
    /// Uses generated client from anchor IDL for type-safe operations.
    /// </summary>
    public class ChainDepthManager : MonoBehaviour
    {
        public static ChainDepthManager Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages = true;

        [Header("RPC Settings")]
        [SerializeField] private string rpcUrl = "https://api.devnet.solana.com";

        // Cached state (using generated account types)
        public GlobalAccount CurrentGlobalState { get; private set; }
        public PlayerAccount CurrentPlayerState { get; private set; }
        public PlayerProfile CurrentProfileState { get; private set; }
        public RoomAccount CurrentRoomState { get; private set; }

        // Events
        public event Action<GlobalAccount> OnGlobalStateUpdated;
        public event Action<PlayerAccount> OnPlayerStateUpdated;
        public event Action<PlayerProfile> OnProfileStateUpdated;
        public event Action<RoomAccount> OnRoomStateUpdated;
        public event Action<IReadOnlyList<RoomOccupantView>> OnRoomOccupantsUpdated;
        public event Action<string> OnTransactionSent;
        public event Action<string> OnError;

        private PublicKey _programId;
        private PublicKey _globalPda;
        private IRpcClient _rpcClient;
        private ChaindepthClient _client;
        private readonly HashSet<string> _roomPresenceSubscriptionKeys = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _programId = new PublicKey(ChainDepthConfig.PROGRAM_ID);
            _globalPda = new PublicKey(ChainDepthConfig.GLOBAL_PDA);
            
            // Initialize RPC client
            _rpcClient = ClientFactory.GetClient(rpcUrl);
            
            // Initialize generated client (no streaming for now)
            _client = new ChaindepthClient(_rpcClient, null, _programId);
            
            Log($"ChainDepth Manager initialized. Program: {ChainDepthConfig.PROGRAM_ID}");
        }

        /// <summary>
        /// Get the active RPC client - prefers wallet's client, falls back to standalone
        /// </summary>
        private IRpcClient GetRpcClient()
        {
            if (Web3.Wallet?.ActiveRpcClient != null)
                return Web3.Wallet.ActiveRpcClient;
            return _rpcClient;
        }

        private void Log(string message)
        {
            if (logDebugMessages)
                Debug.Log($"[ChainDepth] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[ChainDepth] {message}");
            OnError?.Invoke(message);
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.PLAYER_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.ROOM_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.ESCROW_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.STAKE_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.INVENTORY_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.PROFILE_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.PRESENCE_SEED),
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
                    Encoding.UTF8.GetBytes(ChainDepthConfig.BOSS_FIGHT_SEED),
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
        /// Fetch room state at coordinates using generated client
        /// </summary>
        public async UniTask<RoomAccount> FetchRoomState(int x, int y)
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

                var result = await _client.GetRoomAccountAsync(roomPda.Key, Commitment.Confirmed);
                
                if (!result.WasSuccessful || result.ParsedResult == null)
                {
                    Log($"Room at ({x}, {y}) not initialized");
                    return null;
                }

                CurrentRoomState = result.ParsedResult;
                Log($"Room State: Walls=[{string.Join(",", CurrentRoomState.Walls)}], HasChest={CurrentRoomState.HasChest}");
                OnRoomStateUpdated?.Invoke(CurrentRoomState);

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
                return await FetchRoomState(ChainDepthConfig.START_X, ChainDepthConfig.START_Y);
            }

            return await FetchRoomState(CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
        }

        /// <summary>
        /// Get current room as a typed domain view
        /// </summary>
        public RoomView GetCurrentRoomView()
        {
            return CurrentRoomState.ToRoomView();
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
                        EquippedItemId = ChainDepthDomainMapper.ToItemId(presence.EquippedItemId),
                        SkinId = presence.SkinId,
                        Activity = ChainDepthDomainMapper.ToOccupantActivity(presence.Activity),
                        ActivityDirection = presence.Activity == 1 && presence.ActivityDirection <= 3
                            ? ChainDepthDomainMapper.ToDirection(presence.ActivityDirection)
                            : null,
                        IsFightingBoss = presence.Activity == 2
                    })
                    .ToArray();

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

                await _client.SubscribeRoomPresenceAsync(
                    presencePda.Key,
                    (_, _, _) => { FetchRoomOccupants(roomX, roomY).Forget(); },
                    Commitment.Confirmed
                );
            }
        }

        /// <summary>
        /// Performs the next sensible action for a blocked rubble door:
        /// Join -> Tick -> Complete -> Claim.
        /// </summary>
        public async UniTask<string> InteractWithDoor(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (direction > ChainDepthConfig.DIRECTION_WEST)
            {
                LogError($"Invalid direction: {direction}");
                return null;
            }

            if (CurrentGlobalState == null || CurrentPlayerState == null)
            {
                await RefreshAllState();
            }

            if (CurrentPlayerState == null)
            {
                LogError("Player not initialized");
                return null;
            }

            var room = await FetchCurrentRoom();
            if (room == null)
            {
                LogError("Current room not loaded");
                return null;
            }

            var dir = direction;
            var wallState = room.Walls[dir];
            if (wallState != ChainDepthConfig.WALL_RUBBLE)
            {
                if (wallState == ChainDepthConfig.WALL_OPEN)
                {
                    Log($"Door {ChainDepthConfig.GetDirectionName(direction)} is already open.");
                }
                else
                {
                    Log($"Door {ChainDepthConfig.GetDirectionName(direction)} is solid and cannot be worked.");
                }
                return null;
            }

            var hasActiveJob = HasActiveJobInCurrentRoom(direction);
            var jobCompleted = room.JobCompleted != null && room.JobCompleted.Length > dir && room.JobCompleted[dir];

            if (jobCompleted)
            {
                if (!hasActiveJob)
                {
                    LogError("Job is completed, but this player is not an active helper for claiming.");
                    return null;
                }
                return await ClaimJobReward(direction);
            }

            if (!hasActiveJob)
            {
                return await JoinJob(direction);
            }

            var progress = room.Progress[dir];
            var required = room.BaseSlots[dir];
            if (progress >= required)
            {
                return await CompleteJob(direction);
            }

            return await TickJob(direction);
        }

        /// <summary>
        /// Performs the next sensible center action:
        /// Chest: Loot
        /// Boss alive: Join or Tick
        /// Boss defeated: Loot
        /// </summary>
        public async UniTask<string> InteractWithCenter()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentGlobalState == null || CurrentPlayerState == null)
            {
                await RefreshAllState();
            }

            if (CurrentPlayerState == null)
            {
                LogError("Player not initialized");
                return null;
            }

            var room = await FetchCurrentRoom();
            if (room == null)
            {
                LogError("Current room not loaded");
                return null;
            }

            if (room.CenterType == ChainDepthConfig.CENTER_EMPTY)
            {
                Log("Center is empty. No center action available.");
                return null;
            }

            if (room.CenterType == ChainDepthConfig.CENTER_CHEST)
            {
                Log("Center action: chest loot.");
                return await LootChest();
            }

            if (room.CenterType != ChainDepthConfig.CENTER_BOSS)
            {
                LogError($"Unknown center type: {room.CenterType}");
                return null;
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
                    ChainDepthConfig.START_X,
                    ChainDepthConfig.START_Y,
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
        public async UniTask<string> MovePlayer(int newX, int newY)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            Log($"Moving player to ({newX}, {newY})...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var profilePda = DeriveProfilePda(Web3.Wallet.Account.PublicKey);
                var currentRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState?.CurrentRoomX ?? ChainDepthConfig.START_X,
                    CurrentPlayerState?.CurrentRoomY ?? ChainDepthConfig.START_Y);
                var targetRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, newX, newY);
                var currentPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState?.CurrentRoomX ?? ChainDepthConfig.START_X,
                    CurrentPlayerState?.CurrentRoomY ?? ChainDepthConfig.START_Y,
                    Web3.Wallet.Account.PublicKey
                );
                var targetPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    newX,
                    newY,
                    Web3.Wallet.Account.PublicKey
                );

                // Use generated instruction builder
                var instruction = ChaindepthProgram.MovePlayer(
                    new MovePlayerAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Profile = profilePda,
                        CurrentRoom = currentRoomPda,
                        TargetRoom = targetRoomPda,
                        CurrentPresence = currentPresencePda,
                        TargetPresence = targetPresencePda,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    (sbyte)newX,
                    (sbyte)newY,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Move transaction sent: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"Move failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Join a job in the specified direction
        /// </summary>
        public async UniTask<string> JoinJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log($"Joining job in direction {ChainDepthConfig.GetDirectionName(direction)}...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                var escrowPda = DeriveEscrowPda(roomPda, direction);
                var helperStakePda = DeriveHelperStakePda(roomPda, direction, Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );

                // Get player's token account (ATA)
                var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    Web3.Wallet.Account.PublicKey,
                    CurrentGlobalState.SkrMint
                );

                // Use generated instruction builder
                var instruction = ChaindepthProgram.JoinJob(
                    new JoinJobAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
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

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Joined job! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"JoinJob failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Complete a job in the specified direction
        /// </summary>
        public async UniTask<string> CompleteJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log($"Completing job in direction {ChainDepthConfig.GetDirectionName(direction)}...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                
                // Calculate adjacent room coordinates
                var (adjX, adjY) = ChainDepthConfig.GetAdjacentCoords(
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY, direction);
                var adjacentRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, adjX, adjY);
                var escrowPda = DeriveEscrowPda(roomPda, direction);
                var helperStakePda = DeriveHelperStakePda(roomPda, direction, Web3.Wallet.Account.PublicKey);

                // Use generated instruction builder
                var instruction = ChaindepthProgram.CompleteJob(
                    new CompleteJobAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda,
                        HelperStake = helperStakePda,
                        AdjacentRoom = adjacentRoomPda,
                        Escrow = escrowPda,
                        PrizePool = CurrentGlobalState.PrizePool,
                        TokenProgram = TokenProgram.ProgramIdKey,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    direction,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Job completed! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"CompleteJob failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Abandon a job in the specified direction
        /// </summary>
        public async UniTask<string> AbandonJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log($"Abandoning job in direction {ChainDepthConfig.GetDirectionName(direction)}...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                var escrowPda = DeriveEscrowPda(roomPda, direction);
                var helperStakePda = DeriveHelperStakePda(roomPda, direction, Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );
                var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    Web3.Wallet.Account.PublicKey,
                    CurrentGlobalState.SkrMint
                );

                // Use generated instruction builder
                var instruction = ChaindepthProgram.AbandonJob(
                    new AbandonJobAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda,
                        RoomPresence = roomPresencePda,
                        Escrow = escrowPda,
                        HelperStake = helperStakePda,
                        PrizePool = CurrentGlobalState.PrizePool,
                        PlayerTokenAccount = playerTokenAccount,
                        TokenProgram = TokenProgram.ProgramIdKey
                    },
                    direction,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Job abandoned! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"AbandonJob failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Claim reward for a completed job in the specified direction
        /// </summary>
        public async UniTask<string> ClaimJobReward(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log($"Claiming reward for direction {ChainDepthConfig.GetDirectionName(direction)}...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY
                );
                var escrowPda = DeriveEscrowPda(roomPda, direction);
                var helperStakePda = DeriveHelperStakePda(roomPda, direction, Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );
                var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    Web3.Wallet.Account.PublicKey,
                    CurrentGlobalState.SkrMint
                );

                var instruction = ChaindepthProgram.ClaimJobReward(
                    new ClaimJobRewardAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda,
                        RoomPresence = roomPresencePda,
                        Escrow = escrowPda,
                        HelperStake = helperStakePda,
                        PlayerTokenAccount = playerTokenAccount,
                        TokenProgram = TokenProgram.ProgramIdKey
                    },
                    direction,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Job reward claimed! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"ClaimJobReward failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loot a chest in the current room
        /// </summary>
        public async UniTask<string> LootChest()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log("Looting chest...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                var inventoryPda = DeriveInventoryPda(Web3.Wallet.Account.PublicKey);

                // Use generated instruction builder
                var instruction = ChaindepthProgram.LootChest(
                    new LootChestAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda,
                        Inventory = inventoryPda,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Chest looted! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"LootChest failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Equip an inventory item for combat (0 to unequip)
        /// </summary>
        public async UniTask<string> EquipItem(ushort itemId)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null)
            {
                LogError("Player state not loaded");
                return null;
            }

            Log($"Equipping item id {itemId}...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var inventoryPda = DeriveInventoryPda(Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );

                var instruction = ChaindepthProgram.EquipItem(
                    new EquipItemAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Inventory = inventoryPda,
                        RoomPresence = roomPresencePda
                    },
                    itemId,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Equipped item. TX: {signature}");
                    await FetchPlayerState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"EquipItem failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Set player skin id in profile and current room presence.
        /// </summary>
        public async UniTask<string> SetPlayerSkin(ushort skinId)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var profilePda = DeriveProfilePda(Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );

                var instruction = ChaindepthProgram.SetPlayerSkin(
                    new SetPlayerSkinAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Profile = profilePda,
                        RoomPresence = roomPresencePda
                    },
                    skinId,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    await FetchPlayerProfile();
                }
                return signature;
            }
            catch (Exception error)
            {
                LogError($"SetPlayerSkin failed: {error.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create/update profile (skin + optional display name) and grant starter pickaxe once.
        /// </summary>
        public async UniTask<string> CreatePlayerProfile(ushort skinId, string displayName)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var profilePda = DeriveProfilePda(Web3.Wallet.Account.PublicKey);
                var inventoryPda = DeriveInventoryPda(Web3.Wallet.Account.PublicKey);
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );

                var instruction = ChaindepthProgram.CreatePlayerProfile(
                    new CreatePlayerProfileAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Profile = profilePda,
                        Inventory = inventoryPda,
                        RoomPresence = roomPresencePda,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    skinId,
                    displayName ?? string.Empty,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception error)
            {
                LogError($"CreatePlayerProfile failed: {error.Message}");
                return null;
            }
        }

        /// <summary>
        /// Join the boss fight in the current room
        /// </summary>
        public async UniTask<string> JoinBossFight()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log("Joining boss fight...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var profilePda = DeriveProfilePda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY
                );
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );
                var bossFightPda = DeriveBossFightPda(roomPda, Web3.Wallet.Account.PublicKey);

                var instruction = ChaindepthProgram.JoinBossFight(
                    new JoinBossFightAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Profile = profilePda,
                        Room = roomPda,
                        RoomPresence = roomPresencePda,
                        BossFight = bossFightPda,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Joined boss fight! TX: {signature}");
                    await FetchRoomState(CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"JoinBossFight failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tick boss HP in the current room
        /// </summary>
        public async UniTask<string> TickBossFight()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log("Ticking boss fight...");

            try
            {
                var roomPda = DeriveRoomPda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY
                );

                var instruction = ChaindepthProgram.TickBossFight(
                    new TickBossFightAccounts
                    {
                        Caller = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        Room = roomPda
                    },
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Boss ticked! TX: {signature}");
                    await FetchRoomState(CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"TickBossFight failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loot defeated boss in current room (fighters only)
        /// </summary>
        public async UniTask<string> LootBoss()
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log("Looting boss...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);
                var roomPda = DeriveRoomPda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY
                );
                var roomPresencePda = DeriveRoomPresencePda(
                    CurrentGlobalState.SeasonSeed,
                    CurrentPlayerState.CurrentRoomX,
                    CurrentPlayerState.CurrentRoomY,
                    Web3.Wallet.Account.PublicKey
                );
                var bossFightPda = DeriveBossFightPda(roomPda, Web3.Wallet.Account.PublicKey);
                var inventoryPda = DeriveInventoryPda(Web3.Wallet.Account.PublicKey);

                var instruction = ChaindepthProgram.LootBoss(
                    new LootBossAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda,
                        RoomPresence = roomPresencePda,
                        BossFight = bossFightPda,
                        Inventory = inventoryPda,
                        SystemProgram = SystemProgram.ProgramIdKey
                    },
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Boss looted! TX: {signature}");
                    await RefreshAllState();
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"LootBoss failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tick/update a job's progress
        /// </summary>
        public async UniTask<string> TickJob(byte direction)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log($"Ticking job in direction {ChainDepthConfig.GetDirectionName(direction)}...");

            try
            {
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);

                // Use generated instruction builder
                var instruction = ChaindepthProgram.TickJob(
                    new TickJobAccounts
                    {
                        Caller = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        Room = roomPda
                    },
                    direction,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Job ticked! TX: {signature}");
                    await FetchRoomState(CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"TickJob failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Boost a job with additional tokens
        /// </summary>
        public async UniTask<string> BoostJob(byte direction, ulong boostAmount)
        {
            if (Web3.Wallet == null)
            {
                LogError("Wallet not connected");
                return null;
            }

            if (CurrentPlayerState == null || CurrentGlobalState == null)
            {
                LogError("Player or global state not loaded");
                return null;
            }

            Log($"Boosting job in direction {ChainDepthConfig.GetDirectionName(direction)} with {boostAmount} tokens...");

            try
            {
                var roomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    Web3.Wallet.Account.PublicKey,
                    CurrentGlobalState.SkrMint
                );

                // Use generated instruction builder
                var instruction = ChaindepthProgram.BoostJob(
                    new BoostJobAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        Room = roomPda,
                        PrizePool = CurrentGlobalState.PrizePool,
                        PlayerTokenAccount = playerTokenAccount,
                        TokenProgram = TokenProgram.ProgramIdKey
                    },
                    direction,
                    boostAmount,
                    _programId
                );

                var signature = await SendTransaction(instruction);
                if (signature != null)
                {
                    Log($"Job boosted! TX: {signature}");
                    await FetchRoomState(CurrentPlayerState.CurrentRoomX, CurrentPlayerState.CurrentRoomY);
                }
                return signature;
            }
            catch (Exception e)
            {
                LogError($"BoostJob failed: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Helper Methods

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
            
            try
            {
                var rpc = GetRpcClient();
                if (rpc == null)
                {
                    LogError("RPC client not available");
                    return null;
                }
                
                // Get recent blockhash
                var blockHashResult = await rpc.GetLatestBlockHashAsync();
                if (!blockHashResult.WasSuccessful)
                {
                    LogError($"Failed to get blockhash: {blockHashResult.Reason}");
                    return null;
                }

                // Build and sign transaction
                var txBytes = new TransactionBuilder()
                    .SetRecentBlockHash(blockHashResult.Result.Value.Blockhash)
                    .SetFeePayer(Web3.Wallet.Account)
                    .AddInstruction(instruction)
                    .Build(Web3.Wallet.Account);

                Log($"Transaction built and signed, size={txBytes.Length} bytes");

                // Convert to base64 for RPC
                var txBase64 = Convert.ToBase64String(txBytes);

                // Send the signed transaction
                var result = await rpc.SendTransactionAsync(
                    txBase64,
                    skipPreflight: false,
                    preFlightCommitment: Commitment.Confirmed);

                if (result.WasSuccessful)
                {
                    Log($"Transaction sent: {result.Result}");
                    OnTransactionSent?.Invoke(result.Result);
                    return result.Result;
                }
                else
                {
                    LogError($"Transaction failed: {result.Reason}");
                    if (result.ServerErrorCode != 0)
                    {
                        LogError($"Server error code: {result.ServerErrorCode}");
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogError($"Transaction exception: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
