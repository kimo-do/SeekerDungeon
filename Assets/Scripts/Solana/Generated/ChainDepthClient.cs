using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Solana.Unity;
using Solana.Unity.Programs.Abstract;
using Solana.Unity.Programs.Utilities;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Rpc.Core.Sockets;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet;
using Chaindepth;
using Chaindepth.Program;
using Chaindepth.Errors;
using Chaindepth.Accounts;
using Chaindepth.Events;
using Chaindepth.Types;

namespace Chaindepth
{
    namespace Accounts
    {
        public partial class GlobalAccount
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 5002420280216021377UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{129, 105, 124, 171, 189, 42, 108, 69};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "NeTdLJMKWDA";
            public ulong SeasonSeed { get; set; }

            public uint Depth { get; set; }

            public PublicKey SkrMint { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey Admin { get; set; }

            public ulong EndSlot { get; set; }

            public ulong JobsCompleted { get; set; }

            public byte Bump { get; set; }

            public static GlobalAccount Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                GlobalAccount result = new GlobalAccount();
                result.SeasonSeed = _data.GetU64(offset);
                offset += 8;
                result.Depth = _data.GetU32(offset);
                offset += 4;
                result.SkrMint = _data.GetPubKey(offset);
                offset += 32;
                result.PrizePool = _data.GetPubKey(offset);
                offset += 32;
                result.Admin = _data.GetPubKey(offset);
                offset += 32;
                result.EndSlot = _data.GetU64(offset);
                offset += 8;
                result.JobsCompleted = _data.GetU64(offset);
                offset += 8;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class PlayerAccount
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 17019182578430687456UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{224, 184, 224, 50, 98, 72, 48, 236};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "eb62BHK8YZR";
            public PublicKey Owner { get; set; }

            public sbyte CurrentRoomX { get; set; }

            public sbyte CurrentRoomY { get; set; }

            public ActiveJob[] ActiveJobs { get; set; }

            public ulong JobsCompleted { get; set; }

            public ulong ChestsLooted { get; set; }

            public ulong SeasonSeed { get; set; }

            public byte Bump { get; set; }

            public static PlayerAccount Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                PlayerAccount result = new PlayerAccount();
                result.Owner = _data.GetPubKey(offset);
                offset += 32;
                result.CurrentRoomX = _data.GetS8(offset);
                offset += 1;
                result.CurrentRoomY = _data.GetS8(offset);
                offset += 1;
                int resultActiveJobsLength = (int)_data.GetU32(offset);
                offset += 4;
                result.ActiveJobs = new ActiveJob[resultActiveJobsLength];
                for (uint resultActiveJobsIdx = 0; resultActiveJobsIdx < resultActiveJobsLength; resultActiveJobsIdx++)
                {
                    offset += ActiveJob.Deserialize(_data, offset, out var resultActiveJobsresultActiveJobsIdx);
                    result.ActiveJobs[resultActiveJobsIdx] = resultActiveJobsresultActiveJobsIdx;
                }

                result.JobsCompleted = _data.GetU64(offset);
                offset += 8;
                result.ChestsLooted = _data.GetU64(offset);
                offset += 8;
                result.SeasonSeed = _data.GetU64(offset);
                offset += 8;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class RoomAccount
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 3518101838093712240UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{112, 123, 57, 103, 251, 206, 210, 48};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "KpDBFV4LiZ5";
            public sbyte X { get; set; }

            public sbyte Y { get; set; }

            public ulong SeasonSeed { get; set; }

            public byte[] Walls { get; set; }

            public PublicKey[][] Helpers { get; set; }

            public byte[] HelperCounts { get; set; }

            public ulong[] Progress { get; set; }

            public ulong[] StartSlot { get; set; }

            public ulong[] BaseSlots { get; set; }

            public ulong[] StakedAmount { get; set; }

            public bool HasChest { get; set; }

            public PublicKey[] LootedBy { get; set; }

            public byte Bump { get; set; }

            public static RoomAccount Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                RoomAccount result = new RoomAccount();
                result.X = _data.GetS8(offset);
                offset += 1;
                result.Y = _data.GetS8(offset);
                offset += 1;
                result.SeasonSeed = _data.GetU64(offset);
                offset += 8;
                result.Walls = _data.GetBytes(offset, 4);
                offset += 4;
                result.Helpers = new PublicKey[4][];
                for (uint resultHelpersIdx = 0; resultHelpersIdx < 4; resultHelpersIdx++)
                {
                    result.Helpers[resultHelpersIdx] = new PublicKey[4];
                    for (uint resultHelpersresultHelpersIdxIdx = 0; resultHelpersresultHelpersIdxIdx < 4; resultHelpersresultHelpersIdxIdx++)
                    {
                        result.Helpers[resultHelpersIdx][resultHelpersresultHelpersIdxIdx] = _data.GetPubKey(offset);
                        offset += 32;
                    }
                }

                result.HelperCounts = _data.GetBytes(offset, 4);
                offset += 4;
                result.Progress = new ulong[4];
                for (uint resultProgressIdx = 0; resultProgressIdx < 4; resultProgressIdx++)
                {
                    result.Progress[resultProgressIdx] = _data.GetU64(offset);
                    offset += 8;
                }

                result.StartSlot = new ulong[4];
                for (uint resultStartSlotIdx = 0; resultStartSlotIdx < 4; resultStartSlotIdx++)
                {
                    result.StartSlot[resultStartSlotIdx] = _data.GetU64(offset);
                    offset += 8;
                }

                result.BaseSlots = new ulong[4];
                for (uint resultBaseSlotsIdx = 0; resultBaseSlotsIdx < 4; resultBaseSlotsIdx++)
                {
                    result.BaseSlots[resultBaseSlotsIdx] = _data.GetU64(offset);
                    offset += 8;
                }

                result.StakedAmount = new ulong[4];
                for (uint resultStakedAmountIdx = 0; resultStakedAmountIdx < 4; resultStakedAmountIdx++)
                {
                    result.StakedAmount[resultStakedAmountIdx] = _data.GetU64(offset);
                    offset += 8;
                }

                result.HasChest = _data.GetBool(offset);
                offset += 1;
                int resultLootedByLength = (int)_data.GetU32(offset);
                offset += 4;
                result.LootedBy = new PublicKey[resultLootedByLength];
                for (uint resultLootedByIdx = 0; resultLootedByIdx < resultLootedByLength; resultLootedByIdx++)
                {
                    result.LootedBy[resultLootedByIdx] = _data.GetPubKey(offset);
                    offset += 32;
                }

                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }
    }

    namespace Errors
    {
        public enum ChaindepthErrorKind : uint
        {
            NotAdjacent = 6000U,
            WallNotOpen = 6001U,
            OutOfBounds = 6002U,
            InvalidDirection = 6003U,
            NotRubble = 6004U,
            AlreadyJoined = 6005U,
            JobFull = 6006U,
            NotHelper = 6007U,
            JobNotReady = 6008U,
            NoActiveJob = 6009U,
            TooManyActiveJobs = 6010U,
            NoChest = 6011U,
            AlreadyLooted = 6012U,
            NotInRoom = 6013U,
            SeasonNotEnded = 6014U,
            Unauthorized = 6015U,
            InsufficientBalance = 6016U,
            TransferFailed = 6017U,
            Overflow = 6018U
        }
    }

    namespace Events
    {
    }

    namespace Types
    {
        public partial class ActiveJob
        {
            public sbyte RoomX { get; set; }

            public sbyte RoomY { get; set; }

            public byte Direction { get; set; }

            public int Serialize(byte[] _data, int initialOffset)
            {
                int offset = initialOffset;
                _data.WriteS8(RoomX, offset);
                offset += 1;
                _data.WriteS8(RoomY, offset);
                offset += 1;
                _data.WriteU8(Direction, offset);
                offset += 1;
                return offset - initialOffset;
            }

            public static int Deserialize(ReadOnlySpan<byte> _data, int initialOffset, out ActiveJob result)
            {
                int offset = initialOffset;
                result = new ActiveJob();
                result.RoomX = _data.GetS8(offset);
                offset += 1;
                result.RoomY = _data.GetS8(offset);
                offset += 1;
                result.Direction = _data.GetU8(offset);
                offset += 1;
                return offset - initialOffset;
            }
        }
    }

    public partial class ChaindepthClient : TransactionalBaseClient<ChaindepthErrorKind>
    {
        public ChaindepthClient(IRpcClient rpcClient, IStreamingRpcClient streamingRpcClient, PublicKey programId = null) : base(rpcClient, streamingRpcClient, programId ?? new PublicKey(ChaindepthProgram.ID))
        {
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<GlobalAccount>>> GetGlobalAccountsAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = GlobalAccount.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<GlobalAccount>>(res);
            List<GlobalAccount> resultingAccounts = new List<GlobalAccount>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => GlobalAccount.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<GlobalAccount>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<PlayerAccount>>> GetPlayerAccountsAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = PlayerAccount.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<PlayerAccount>>(res);
            List<PlayerAccount> resultingAccounts = new List<PlayerAccount>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => PlayerAccount.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<PlayerAccount>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<RoomAccount>>> GetRoomAccountsAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = RoomAccount.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<RoomAccount>>(res);
            List<RoomAccount> resultingAccounts = new List<RoomAccount>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => RoomAccount.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<RoomAccount>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<GlobalAccount>> GetGlobalAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<GlobalAccount>(res);
            var resultingAccount = GlobalAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<GlobalAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<PlayerAccount>> GetPlayerAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<PlayerAccount>(res);
            var resultingAccount = PlayerAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<PlayerAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<RoomAccount>> GetRoomAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<RoomAccount>(res);
            var resultingAccount = RoomAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<RoomAccount>(res, resultingAccount);
        }

        public async Task<SubscriptionState> SubscribeGlobalAccountAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, GlobalAccount> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                GlobalAccount parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = GlobalAccount.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        public async Task<SubscriptionState> SubscribePlayerAccountAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, PlayerAccount> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                PlayerAccount parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = PlayerAccount.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        public async Task<SubscriptionState> SubscribeRoomAccountAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, RoomAccount> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                RoomAccount parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = RoomAccount.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        protected override Dictionary<uint, ProgramError<ChaindepthErrorKind>> BuildErrorsDictionary()
        {
            return new Dictionary<uint, ProgramError<ChaindepthErrorKind>>{{6000U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotAdjacent, "Invalid move: target room is not adjacent")}, {6001U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.WallNotOpen, "Invalid move: wall is not open")}, {6002U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.OutOfBounds, "Invalid move: coordinates out of bounds")}, {6003U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidDirection, "Invalid direction: must be 0-3 (N/S/E/W)")}, {6004U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotRubble, "Wall is not rubble: cannot start job")}, {6005U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.AlreadyJoined, "Already joined this job")}, {6006U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.JobFull, "Job is full: maximum helpers reached")}, {6007U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotHelper, "Not a helper on this job")}, {6008U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.JobNotReady, "Job not ready: progress insufficient")}, {6009U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NoActiveJob, "No active job at this location")}, {6010U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.TooManyActiveJobs, "Too many active jobs: abandon one first")}, {6011U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NoChest, "Room has no chest")}, {6012U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.AlreadyLooted, "Already looted this chest")}, {6013U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotInRoom, "Player not in this room")}, {6014U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.SeasonNotEnded, "Season has not ended yet")}, {6015U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.Unauthorized, "Unauthorized: only admin can perform this action")}, {6016U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InsufficientBalance, "Insufficient balance for stake")}, {6017U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.TransferFailed, "Token transfer failed")}, {6018U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.Overflow, "Arithmetic overflow")}, };
        }
    }

    namespace Program
    {
        public class AbandonJobAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        }

        public class BoostJobAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        }

        public class CompleteJobAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey AdjacentRoom { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class InitGlobalAccounts
        {
            public PublicKey Admin { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey SkrMint { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey AdminTokenAccount { get; set; }

            public PublicKey StartRoom { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class InitPlayerAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class JoinJobAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SkrMint { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class LootChestAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }
        }

        public class MovePlayerAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey CurrentRoom { get; set; }

            public PublicKey TargetRoom { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class ResetSeasonAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Global { get; set; }
        }

        public class TickJobAccounts
        {
            public PublicKey Caller { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey Room { get; set; }
        }

        public static class ChaindepthProgram
        {
            public const string ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
            public static Solana.Unity.Rpc.Models.TransactionInstruction AbandonJob(AbandonJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(18178073425521205758UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction BoostJob(BoostJobAccounts accounts, byte direction, ulong boost_amount, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(14583492075229093809UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                _data.WriteU64(boost_amount, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction CompleteJob(CompleteJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.AdjacentRoom, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(793753272268740829UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction InitGlobal(InitGlobalAccounts accounts, ulong initial_prize_pool_amount, ulong season_seed, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Admin, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SkrMint, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.AdminTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.StartRoom, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(11727573871456284204UL, offset);
                offset += 8;
                _data.WriteU64(initial_prize_pool_amount, offset);
                offset += 8;
                _data.WriteU64(season_seed, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction InitPlayer(InitPlayerAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(4819994211046333298UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction JoinJob(JoinJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SkrMint, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(5937278740201911420UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction LootChest(LootChestAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(4166659101437723766UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction MovePlayer(MovePlayerAccounts accounts, sbyte new_x, sbyte new_y, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.CurrentRoom, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TargetRoom, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(16684840164937447953UL, offset);
                offset += 8;
                _data.WriteS8(new_x, offset);
                offset += 1;
                _data.WriteS8(new_y, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction ResetSeason(ResetSeasonAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(1230071605279309681UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction TickJob(TickJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Caller, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(8672572876003750988UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }
        }
    }
}