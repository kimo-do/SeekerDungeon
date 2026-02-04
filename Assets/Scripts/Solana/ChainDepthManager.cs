using System;
using System.Collections.Generic;
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
        public RoomAccount CurrentRoomState { get; private set; }

        // Events
        public event Action<GlobalAccount> OnGlobalStateUpdated;
        public event Action<PlayerAccount> OnPlayerStateUpdated;
        public event Action<RoomAccount> OnRoomStateUpdated;
        public event Action<string> OnTransactionSent;
        public event Action<string> OnError;

        private PublicKey _programId;
        private PublicKey _globalPda;
        private IRpcClient _rpcClient;
        private ChaindepthClient _client;

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
                    Encoding.UTF8.GetBytes("escrow"),
                    roomPda.KeyBytes,
                    new[] { direction }
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
            await FetchCurrentRoom();
            Log("State refresh complete");
        }

        #endregion

        #region Instructions (Using Generated Client)

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

            Log("Initializing player account...");

            try
            {
                var playerPda = DerivePlayerPda(Web3.Wallet.Account.PublicKey);

                // Use generated instruction builder
                var instruction = ChaindepthProgram.InitPlayer(
                    new InitPlayerAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
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
                var currentRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, 
                    CurrentPlayerState?.CurrentRoomX ?? ChainDepthConfig.START_X,
                    CurrentPlayerState?.CurrentRoomY ?? ChainDepthConfig.START_Y);
                var targetRoomPda = DeriveRoomPda(CurrentGlobalState.SeasonSeed, newX, newY);

                // Use generated instruction builder
                var instruction = ChaindepthProgram.MovePlayer(
                    new MovePlayerAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        CurrentRoom = currentRoomPda,
                        TargetRoom = targetRoomPda,
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
                        Escrow = escrowPda,
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
                var playerTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    Web3.Wallet.Account.PublicKey,
                    CurrentGlobalState.SkrMint
                );

                // Use generated instruction builder
                var instruction = ChaindepthProgram.CompleteJob(
                    new CompleteJobAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda,
                        AdjacentRoom = adjacentRoomPda,
                        Escrow = escrowPda,
                        PrizePool = CurrentGlobalState.PrizePool,
                        PlayerTokenAccount = playerTokenAccount,
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
                        Escrow = escrowPda,
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

                // Use generated instruction builder
                var instruction = ChaindepthProgram.LootChest(
                    new LootChestAccounts
                    {
                        Player = Web3.Wallet.Account.PublicKey,
                        Global = _globalPda,
                        PlayerAccount = playerPda,
                        Room = roomPda
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
