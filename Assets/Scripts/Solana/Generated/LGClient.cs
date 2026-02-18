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
        public partial class BossFightAccount
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 16452785744412067854UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{14, 200, 74, 197, 118, 9, 84, 228};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "3UQeXwPaPeb";
            public PublicKey Player { get; set; }

            public PublicKey Room { get; set; }

            public ulong Dps { get; set; }

            public ulong JoinedSlot { get; set; }

            public byte Bump { get; set; }

            public static BossFightAccount Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                BossFightAccount result = new BossFightAccount();
                result.Player = _data.GetPubKey(offset);
                offset += 32;
                result.Room = _data.GetPubKey(offset);
                offset += 32;
                result.Dps = _data.GetU64(offset);
                offset += 8;
                result.JoinedSlot = _data.GetU64(offset);
                offset += 8;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

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

        public partial class HelperStake
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 14607265679536494594UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{2, 24, 135, 48, 190, 110, 183, 202};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "MLFt7a7Hqj";
            public PublicKey Player { get; set; }

            public PublicKey Room { get; set; }

            public byte Direction { get; set; }

            public ulong Amount { get; set; }

            public ulong JoinedSlot { get; set; }

            public byte Bump { get; set; }

            public static HelperStake Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                HelperStake result = new HelperStake();
                result.Player = _data.GetPubKey(offset);
                offset += 32;
                result.Room = _data.GetPubKey(offset);
                offset += 32;
                result.Direction = _data.GetU8(offset);
                offset += 1;
                result.Amount = _data.GetU64(offset);
                offset += 8;
                result.JoinedSlot = _data.GetU64(offset);
                offset += 8;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class InventoryAccount
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 3100704154362213227UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{107, 115, 86, 10, 0, 234, 7, 43};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "JyQUbUC7fL6";
            public PublicKey Owner { get; set; }

            public InventoryItem[] Items { get; set; }

            public byte Bump { get; set; }

            public static InventoryAccount Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                InventoryAccount result = new InventoryAccount();
                result.Owner = _data.GetPubKey(offset);
                offset += 32;
                int resultItemsLength = (int)_data.GetU32(offset);
                offset += 4;
                result.Items = new InventoryItem[resultItemsLength];
                for (uint resultItemsIdx = 0; resultItemsIdx < resultItemsLength; resultItemsIdx++)
                {
                    offset += InventoryItem.Deserialize(_data, offset, out var resultItemsresultItemsIdx);
                    result.Items[resultItemsIdx] = resultItemsresultItemsIdx;
                }

                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class LootReceipt
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 10034683517864585537UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{65, 41, 201, 83, 126, 91, 66, 139};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "BuAZrvAwx2v";
            public PublicKey Player { get; set; }

            public ulong SeasonSeed { get; set; }

            public sbyte RoomX { get; set; }

            public sbyte RoomY { get; set; }

            public byte Bump { get; set; }

            public static LootReceipt Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                LootReceipt result = new LootReceipt();
                result.Player = _data.GetPubKey(offset);
                offset += 32;
                result.SeasonSeed = _data.GetU64(offset);
                offset += 8;
                result.RoomX = _data.GetS8(offset);
                offset += 1;
                result.RoomY = _data.GetS8(offset);
                offset += 1;
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

            public ushort EquippedItemId { get; set; }

            public ulong TotalScore { get; set; }

            public ulong CurrentRunStartSlot { get; set; }

            public ulong RunsExtracted { get; set; }

            public ulong LastExtractionSlot { get; set; }

            public bool InDungeon { get; set; }

            public ushort DataVersion { get; set; }

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
                result.EquippedItemId = _data.GetU16(offset);
                offset += 2;
                result.TotalScore = _data.GetU64(offset);
                offset += 8;
                result.CurrentRunStartSlot = _data.GetU64(offset);
                offset += 8;
                result.RunsExtracted = _data.GetU64(offset);
                offset += 8;
                result.LastExtractionSlot = _data.GetU64(offset);
                offset += 8;
                result.InDungeon = _data.GetBool(offset);
                offset += 1;
                result.DataVersion = _data.GetU16(offset);
                offset += 2;
                result.SeasonSeed = _data.GetU64(offset);
                offset += 8;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class PlayerProfile
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 5815698136171274834UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{82, 226, 99, 87, 164, 130, 181, 80};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "Es5kD8GxkuM";
            public PublicKey Owner { get; set; }

            public ushort SkinId { get; set; }

            public string DisplayName { get; set; }

            public bool StarterPickaxeGranted { get; set; }

            public byte Bump { get; set; }

            public static PlayerProfile Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                PlayerProfile result = new PlayerProfile();
                result.Owner = _data.GetPubKey(offset);
                offset += 32;
                result.SkinId = _data.GetU16(offset);
                offset += 2;
                offset += _data.GetBorshString(offset, out var resultDisplayName);
                result.DisplayName = resultDisplayName;
                result.StarterPickaxeGranted = _data.GetBool(offset);
                offset += 1;
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

            public byte[] DoorLockKinds { get; set; }

            public uint[] HelperCounts { get; set; }

            public ulong[] Progress { get; set; }

            public ulong[] StartSlot { get; set; }

            public ulong[] BaseSlots { get; set; }

            public ulong[] TotalStaked { get; set; }

            public bool[] JobCompleted { get; set; }

            public ulong[] BonusPerHelper { get; set; }

            public bool HasChest { get; set; }

            public bool ForcedKeyDrop { get; set; }

            public byte CenterType { get; set; }

            public ushort CenterId { get; set; }

            public ulong BossMaxHp { get; set; }

            public ulong BossCurrentHp { get; set; }

            public ulong BossLastUpdateSlot { get; set; }

            public ulong BossTotalDps { get; set; }

            public uint BossFighterCount { get; set; }

            public bool BossDefeated { get; set; }

            public uint LootedCount { get; set; }

            public PublicKey CreatedBy { get; set; }

            public ulong CreatedSlot { get; set; }

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
                result.DoorLockKinds = _data.GetBytes(offset, 4);
                offset += 4;
                result.HelperCounts = new uint[4];
                for (uint resultHelperCountsIdx = 0; resultHelperCountsIdx < 4; resultHelperCountsIdx++)
                {
                    result.HelperCounts[resultHelperCountsIdx] = _data.GetU32(offset);
                    offset += 4;
                }

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

                result.TotalStaked = new ulong[4];
                for (uint resultTotalStakedIdx = 0; resultTotalStakedIdx < 4; resultTotalStakedIdx++)
                {
                    result.TotalStaked[resultTotalStakedIdx] = _data.GetU64(offset);
                    offset += 8;
                }

                result.JobCompleted = new bool[4];
                for (uint resultJobCompletedIdx = 0; resultJobCompletedIdx < 4; resultJobCompletedIdx++)
                {
                    result.JobCompleted[resultJobCompletedIdx] = _data.GetBool(offset);
                    offset += 1;
                }

                result.BonusPerHelper = new ulong[4];
                for (uint resultBonusPerHelperIdx = 0; resultBonusPerHelperIdx < 4; resultBonusPerHelperIdx++)
                {
                    result.BonusPerHelper[resultBonusPerHelperIdx] = _data.GetU64(offset);
                    offset += 8;
                }

                result.HasChest = _data.GetBool(offset);
                offset += 1;
                result.ForcedKeyDrop = _data.GetBool(offset);
                offset += 1;
                result.CenterType = _data.GetU8(offset);
                offset += 1;
                result.CenterId = _data.GetU16(offset);
                offset += 2;
                result.BossMaxHp = _data.GetU64(offset);
                offset += 8;
                result.BossCurrentHp = _data.GetU64(offset);
                offset += 8;
                result.BossLastUpdateSlot = _data.GetU64(offset);
                offset += 8;
                result.BossTotalDps = _data.GetU64(offset);
                offset += 8;
                result.BossFighterCount = _data.GetU32(offset);
                offset += 4;
                result.BossDefeated = _data.GetBool(offset);
                offset += 1;
                result.LootedCount = _data.GetU32(offset);
                offset += 4;
                result.CreatedBy = _data.GetPubKey(offset);
                offset += 32;
                result.CreatedSlot = _data.GetU64(offset);
                offset += 8;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class RoomPresence
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 2547011151930592266UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{10, 12, 200, 229, 37, 205, 88, 35};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "2gVpwqA73dt";
            public PublicKey Player { get; set; }

            public ulong SeasonSeed { get; set; }

            public sbyte RoomX { get; set; }

            public sbyte RoomY { get; set; }

            public ushort SkinId { get; set; }

            public ushort EquippedItemId { get; set; }

            public byte Activity { get; set; }

            public byte ActivityDirection { get; set; }

            public bool IsCurrent { get; set; }

            public byte Bump { get; set; }

            public static RoomPresence Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                RoomPresence result = new RoomPresence();
                result.Player = _data.GetPubKey(offset);
                offset += 32;
                result.SeasonSeed = _data.GetU64(offset);
                offset += 8;
                result.RoomX = _data.GetS8(offset);
                offset += 1;
                result.RoomY = _data.GetS8(offset);
                offset += 1;
                result.SkinId = _data.GetU16(offset);
                offset += 2;
                result.EquippedItemId = _data.GetU16(offset);
                offset += 2;
                result.Activity = _data.GetU8(offset);
                offset += 1;
                result.ActivityDirection = _data.GetU8(offset);
                offset += 1;
                result.IsCurrent = _data.GetBool(offset);
                offset += 1;
                result.Bump = _data.GetU8(offset);
                offset += 1;
                return result;
            }
        }

        public partial class SessionAuthority
        {
            public static ulong ACCOUNT_DISCRIMINATOR => 12298243742889806128UL;
            public static ReadOnlySpan<byte> ACCOUNT_DISCRIMINATOR_BYTES => new byte[]{48, 9, 30, 120, 134, 35, 172, 170};
            public static string ACCOUNT_DISCRIMINATOR_B58 => "931LAeW67wX";
            public PublicKey Player { get; set; }

            public PublicKey SessionKey { get; set; }

            public ulong ExpiresAtSlot { get; set; }

            public long ExpiresAtUnixTimestamp { get; set; }

            public ulong InstructionAllowlist { get; set; }

            public ulong MaxTokenSpend { get; set; }

            public ulong SpentTokenAmount { get; set; }

            public bool IsActive { get; set; }

            public byte Bump { get; set; }

            public static SessionAuthority Deserialize(ReadOnlySpan<byte> _data)
            {
                int offset = 0;
                ulong accountHashValue = _data.GetU64(offset);
                offset += 8;
                if (accountHashValue != ACCOUNT_DISCRIMINATOR)
                {
                    return null;
                }

                SessionAuthority result = new SessionAuthority();
                result.Player = _data.GetPubKey(offset);
                offset += 32;
                result.SessionKey = _data.GetPubKey(offset);
                offset += 32;
                result.ExpiresAtSlot = _data.GetU64(offset);
                offset += 8;
                result.ExpiresAtUnixTimestamp = _data.GetS64(offset);
                offset += 8;
                result.InstructionAllowlist = _data.GetU64(offset);
                offset += 8;
                result.MaxTokenSpend = _data.GetU64(offset);
                offset += 8;
                result.SpentTokenAmount = _data.GetU64(offset);
                offset += 8;
                result.IsActive = _data.GetBool(offset);
                offset += 1;
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
            WallNotLocked = 6002U,
            OutOfBounds = 6003U,
            InvalidDirection = 6004U,
            NotRubble = 6005U,
            AlreadyJoined = 6006U,
            JobFull = 6007U,
            NotHelper = 6008U,
            JobNotReady = 6009U,
            NoActiveJob = 6010U,
            JobAlreadyCompleted = 6011U,
            JobNotCompleted = 6012U,
            TooManyActiveJobs = 6013U,
            InventoryFull = 6014U,
            InvalidItemId = 6015U,
            InvalidItemAmount = 6016U,
            InsufficientItemAmount = 6017U,
            MissingRequiredKey = 6018U,
            InvalidLockKind = 6019U,
            NoChest = 6020U,
            AlreadyLooted = 6021U,
            TreasuryInsufficientFunds = 6022U,
            NotInRoom = 6023U,
            NotAtEntranceRoom = 6024U,
            EntranceStairsRequired = 6025U,
            CannotExitWithActiveJobs = 6026U,
            NoBoss = 6027U,
            BossAlreadyDefeated = 6028U,
            BossNotDefeated = 6029U,
            AlreadyFightingBoss = 6030U,
            NotBossFighter = 6031U,
            InvalidCenterType = 6032U,
            DisplayNameTooLong = 6033U,
            InvalidSessionExpiry = 6034U,
            InvalidSessionAllowlist = 6035U,
            SessionExpired = 6036U,
            SessionInactive = 6037U,
            SessionInstructionNotAllowed = 6038U,
            SessionSpendCapExceeded = 6039U,
            SeasonNotEnded = 6040U,
            Unauthorized = 6041U,
            InsufficientBalance = 6042U,
            TransferFailed = 6043U,
            Overflow = 6044U
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

        public partial class InventoryItem
        {
            public ushort ItemId { get; set; }

            public uint Amount { get; set; }

            public ushort Durability { get; set; }

            public int Serialize(byte[] _data, int initialOffset)
            {
                int offset = initialOffset;
                _data.WriteU16(ItemId, offset);
                offset += 2;
                _data.WriteU32(Amount, offset);
                offset += 4;
                _data.WriteU16(Durability, offset);
                offset += 2;
                return offset - initialOffset;
            }

            public static int Deserialize(ReadOnlySpan<byte> _data, int initialOffset, out InventoryItem result)
            {
                int offset = initialOffset;
                result = new InventoryItem();
                result.ItemId = _data.GetU16(offset);
                offset += 2;
                result.Amount = _data.GetU32(offset);
                offset += 4;
                result.Durability = _data.GetU16(offset);
                offset += 2;
                return offset - initialOffset;
            }
        }
    }

    public partial class ChaindepthClient : TransactionalBaseClient<ChaindepthErrorKind>
    {
        public ChaindepthClient(IRpcClient rpcClient, IStreamingRpcClient streamingRpcClient, PublicKey programId = null) : base(rpcClient, streamingRpcClient, programId ?? new PublicKey(ChaindepthProgram.ID))
        {
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<BossFightAccount>>> GetBossFightAccountsAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = BossFightAccount.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<BossFightAccount>>(res);
            List<BossFightAccount> resultingAccounts = new List<BossFightAccount>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => BossFightAccount.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<BossFightAccount>>(res, resultingAccounts);
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

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<HelperStake>>> GetHelperStakesAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = HelperStake.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<HelperStake>>(res);
            List<HelperStake> resultingAccounts = new List<HelperStake>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => HelperStake.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<HelperStake>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<InventoryAccount>>> GetInventoryAccountsAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = InventoryAccount.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<InventoryAccount>>(res);
            List<InventoryAccount> resultingAccounts = new List<InventoryAccount>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => InventoryAccount.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<InventoryAccount>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<LootReceipt>>> GetLootReceiptsAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = LootReceipt.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<LootReceipt>>(res);
            List<LootReceipt> resultingAccounts = new List<LootReceipt>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => LootReceipt.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<LootReceipt>>(res, resultingAccounts);
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

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<PlayerProfile>>> GetPlayerProfilesAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = PlayerProfile.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<PlayerProfile>>(res);
            List<PlayerProfile> resultingAccounts = new List<PlayerProfile>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => PlayerProfile.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<PlayerProfile>>(res, resultingAccounts);
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

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<RoomPresence>>> GetRoomPresencesAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = RoomPresence.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<RoomPresence>>(res);
            List<RoomPresence> resultingAccounts = new List<RoomPresence>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => RoomPresence.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<RoomPresence>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<SessionAuthority>>> GetSessionAuthoritysAsync(string programAddress = ChaindepthProgram.ID, Commitment commitment = Commitment.Confirmed)
        {
            var list = new List<Solana.Unity.Rpc.Models.MemCmp>{new Solana.Unity.Rpc.Models.MemCmp{Bytes = SessionAuthority.ACCOUNT_DISCRIMINATOR_B58, Offset = 0}};
            var res = await RpcClient.GetProgramAccountsAsync(programAddress, commitment, memCmpList: list);
            if (!res.WasSuccessful || !(res.Result?.Count > 0))
                return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<SessionAuthority>>(res);
            List<SessionAuthority> resultingAccounts = new List<SessionAuthority>(res.Result.Count);
            resultingAccounts.AddRange(res.Result.Select(result => SessionAuthority.Deserialize(Convert.FromBase64String(result.Account.Data[0]))));
            return new Solana.Unity.Programs.Models.ProgramAccountsResultWrapper<List<SessionAuthority>>(res, resultingAccounts);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<BossFightAccount>> GetBossFightAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<BossFightAccount>(res);
            var resultingAccount = BossFightAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<BossFightAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<GlobalAccount>> GetGlobalAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<GlobalAccount>(res);
            var resultingAccount = GlobalAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<GlobalAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<HelperStake>> GetHelperStakeAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<HelperStake>(res);
            var resultingAccount = HelperStake.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<HelperStake>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<InventoryAccount>> GetInventoryAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<InventoryAccount>(res);
            var resultingAccount = InventoryAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<InventoryAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<LootReceipt>> GetLootReceiptAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<LootReceipt>(res);
            var resultingAccount = LootReceipt.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<LootReceipt>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<PlayerAccount>> GetPlayerAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<PlayerAccount>(res);
            var resultingAccount = PlayerAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<PlayerAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<PlayerProfile>> GetPlayerProfileAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<PlayerProfile>(res);
            var resultingAccount = PlayerProfile.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<PlayerProfile>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<RoomAccount>> GetRoomAccountAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<RoomAccount>(res);
            var resultingAccount = RoomAccount.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<RoomAccount>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<RoomPresence>> GetRoomPresenceAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<RoomPresence>(res);
            var resultingAccount = RoomPresence.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<RoomPresence>(res, resultingAccount);
        }

        public async Task<Solana.Unity.Programs.Models.AccountResultWrapper<SessionAuthority>> GetSessionAuthorityAsync(string accountAddress, Commitment commitment = Commitment.Finalized)
        {
            var res = await RpcClient.GetAccountInfoAsync(accountAddress, commitment);
            if (!res.WasSuccessful)
                return new Solana.Unity.Programs.Models.AccountResultWrapper<SessionAuthority>(res);
            var resultingAccount = SessionAuthority.Deserialize(Convert.FromBase64String(res.Result.Value.Data[0]));
            return new Solana.Unity.Programs.Models.AccountResultWrapper<SessionAuthority>(res, resultingAccount);
        }

        public async Task<SubscriptionState> SubscribeBossFightAccountAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, BossFightAccount> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                BossFightAccount parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = BossFightAccount.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
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

        public async Task<SubscriptionState> SubscribeHelperStakeAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, HelperStake> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                HelperStake parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = HelperStake.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        public async Task<SubscriptionState> SubscribeInventoryAccountAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, InventoryAccount> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                InventoryAccount parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = InventoryAccount.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        public async Task<SubscriptionState> SubscribeLootReceiptAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, LootReceipt> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                LootReceipt parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = LootReceipt.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
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

        public async Task<SubscriptionState> SubscribePlayerProfileAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, PlayerProfile> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                PlayerProfile parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = PlayerProfile.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
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

        public async Task<SubscriptionState> SubscribeRoomPresenceAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, RoomPresence> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                RoomPresence parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = RoomPresence.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        public async Task<SubscriptionState> SubscribeSessionAuthorityAsync(string accountAddress, Action<SubscriptionState, Solana.Unity.Rpc.Messages.ResponseValue<Solana.Unity.Rpc.Models.AccountInfo>, SessionAuthority> callback, Commitment commitment = Commitment.Finalized)
        {
            SubscriptionState res = await StreamingRpcClient.SubscribeAccountInfoAsync(accountAddress, (s, e) =>
            {
                SessionAuthority parsingResult = null;
                if (e.Value?.Data?.Count > 0)
                    parsingResult = SessionAuthority.Deserialize(Convert.FromBase64String(e.Value.Data[0]));
                callback(s, e, parsingResult);
            }, commitment);
            return res;
        }

        protected override Dictionary<uint, ProgramError<ChaindepthErrorKind>> BuildErrorsDictionary()
        {
            return new Dictionary<uint, ProgramError<ChaindepthErrorKind>>{{6000U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotAdjacent, "Invalid move: target room is not adjacent")}, {6001U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.WallNotOpen, "Invalid move: wall is not open")}, {6002U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.WallNotLocked, "Wall is not locked")}, {6003U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.OutOfBounds, "Invalid move: coordinates out of bounds")}, {6004U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidDirection, "Invalid direction: must be 0-3 (N/S/E/W)")}, {6005U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotRubble, "Wall is not rubble: cannot start job")}, {6006U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.AlreadyJoined, "Already joined this job")}, {6007U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.JobFull, "Job is full: maximum helpers reached")}, {6008U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotHelper, "Not a helper on this job")}, {6009U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.JobNotReady, "Job not ready: progress insufficient")}, {6010U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NoActiveJob, "No active job at this location")}, {6011U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.JobAlreadyCompleted, "Job has already been completed")}, {6012U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.JobNotCompleted, "Job has not been completed yet")}, {6013U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.TooManyActiveJobs, "Too many active jobs: abandon one first")}, {6014U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InventoryFull, "Inventory is full")}, {6015U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidItemId, "Invalid item id")}, {6016U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidItemAmount, "Invalid item amount")}, {6017U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InsufficientItemAmount, "Not enough items")}, {6018U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.MissingRequiredKey, "Missing required key item")}, {6019U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidLockKind, "Invalid lock kind")}, {6020U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NoChest, "Room has no chest")}, {6021U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.AlreadyLooted, "Already looted this chest")}, {6022U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.TreasuryInsufficientFunds, "Treasury has insufficient SOL to reimburse room rent")}, {6023U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotInRoom, "Player not in this room")}, {6024U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotAtEntranceRoom, "Player is not at the entrance room")}, {6025U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.EntranceStairsRequired, "Entrance stairs are required to exit")}, {6026U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.CannotExitWithActiveJobs, "Cannot exit while having active jobs")}, {6027U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NoBoss, "No boss in this room center")}, {6028U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.BossAlreadyDefeated, "Boss is already defeated")}, {6029U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.BossNotDefeated, "Boss has not been defeated yet")}, {6030U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.AlreadyFightingBoss, "Player is already fighting this boss")}, {6031U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.NotBossFighter, "Player is not a fighter for this boss")}, {6032U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidCenterType, "Invalid center type")}, {6033U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.DisplayNameTooLong, "Display name is too long")}, {6034U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidSessionExpiry, "Invalid session expiry values")}, {6035U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InvalidSessionAllowlist, "Session instruction allowlist cannot be empty")}, {6036U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.SessionExpired, "Session has expired")}, {6037U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.SessionInactive, "Session is inactive")}, {6038U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.SessionInstructionNotAllowed, "Instruction is not allowed by session policy")}, {6039U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.SessionSpendCapExceeded, "Session spend cap exceeded")}, {6040U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.SeasonNotEnded, "Season has not ended yet")}, {6041U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.Unauthorized, "Unauthorized: only admin can perform this action")}, {6042U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.InsufficientBalance, "Insufficient balance for stake")}, {6043U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.TransferFailed, "Token transfer failed")}, {6044U, new ProgramError<ChaindepthErrorKind>(ChaindepthErrorKind.Overflow, "Arithmetic overflow")}, };
        }
    }

    namespace Program
    {
        public class AbandonJobAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey HelperStake { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        }

        public class AddInventoryItemAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class BeginSessionAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey SessionKey { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class BoostJobAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        }

        public class ClaimJobRewardAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey HelperStake { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        }

        public class CompleteJobAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey HelperStake { get; set; }

            public PublicKey AdjacentRoom { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey PrizePool { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class CreatePlayerProfileAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Profile { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class EndSessionAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey SessionKey { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        }

        public class EnsureStartRoomAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey StartRoom { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class EquipItemAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey SessionAuthority { get; set; }
        }

        public class ExitDungeonAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class ForceResetSeasonAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Global { get; set; }
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

            public PublicKey Profile { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class JoinBossFightAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Profile { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey BossFight { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class JoinJobAccounts
        {
            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey HelperStake { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SkrMint { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class JoinJobWithSessionAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey Escrow { get; set; }

            public PublicKey HelperStake { get; set; }

            public PublicKey PlayerTokenAccount { get; set; }

            public PublicKey SkrMint { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey TokenProgram { get; set; } = new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class LootBossAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey BossFight { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey LootReceipt { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class LootChestAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey LootReceipt { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class MovePlayerAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Profile { get; set; }

            public PublicKey CurrentRoom { get; set; }

            public PublicKey TargetRoom { get; set; }

            public PublicKey CurrentPresence { get; set; }

            public PublicKey TargetPresence { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class RemoveInventoryItemAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey SessionAuthority { get; set; }
        }

        public class ResetMyPlayerAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public class ResetPlayerForTestingAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Profile { get; set; }
        }

        public class ResetSeasonAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Global { get; set; }
        }

        public class SetPlayerSkinAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Profile { get; set; }

            public PublicKey RoomPresence { get; set; }

            public PublicKey SessionAuthority { get; set; }
        }

        public class TickBossFightAccounts
        {
            public PublicKey Caller { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey Room { get; set; }
        }

        public class TickJobAccounts
        {
            public PublicKey Caller { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey Room { get; set; }
        }

        public class UnlockDoorAccounts
        {
            public PublicKey Authority { get; set; }

            public PublicKey Player { get; set; }

            public PublicKey Global { get; set; }

            public PublicKey PlayerAccount { get; set; }

            public PublicKey Room { get; set; }

            public PublicKey AdjacentRoom { get; set; }

            public PublicKey Inventory { get; set; }

            public PublicKey SessionAuthority { get; set; }

            public PublicKey SystemProgram { get; set; } = new PublicKey("11111111111111111111111111111111");
        }

        public static class ChaindepthProgram
        {
            public const string ID = "3Ctc2FgnNHQtGAcZftMS4ykLhJYjLzBD3hELKy55DnKo";
            public static Solana.Unity.Rpc.Models.TransactionInstruction AbandonJob(AbandonJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.HelperStake, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false)};
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction AddInventoryItem(AddInventoryItemAccounts accounts, ushort item_id, uint amount, ushort durability, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(16831650881450377564UL, offset);
                offset += 8;
                _data.WriteU16(item_id, offset);
                offset += 2;
                _data.WriteU32(amount, offset);
                offset += 4;
                _data.WriteU16(durability, offset);
                offset += 2;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction BeginSession(BeginSessionAccounts accounts, ulong expires_at_slot, long expires_at_unix_timestamp, ulong instruction_allowlist, ulong max_token_spend, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SessionKey, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(15610960564545719996UL, offset);
                offset += 8;
                _data.WriteU64(expires_at_slot, offset);
                offset += 8;
                _data.WriteS64(expires_at_unix_timestamp, offset);
                offset += 8;
                _data.WriteU64(instruction_allowlist, offset);
                offset += 8;
                _data.WriteU64(max_token_spend, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction BoostJob(BoostJobAccounts accounts, byte direction, ulong boost_amount, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false)};
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction ClaimJobReward(ClaimJobRewardAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.HelperStake, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(10767216470660547513UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction CompleteJob(CompleteJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.HelperStake, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.AdjacentRoom, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PrizePool, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction CreatePlayerProfile(CreatePlayerProfileAccounts accounts, ushort skin_id, string display_name, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Profile, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(3674470262392566090UL, offset);
                offset += 8;
                _data.WriteU16(skin_id, offset);
                offset += 2;
                offset += _data.WriteBorshString(display_name, offset);
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction EndSession(EndSessionAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SessionKey, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(4760298022670038027UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction EnsureStartRoom(EnsureStartRoomAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.StartRoom, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(3961151750032972163UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction EquipItem(EquipItemAccounts accounts, ushort item_id, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(18375847094273481510UL, offset);
                offset += 8;
                _data.WriteU16(item_id, offset);
                offset += 2;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction ExitDungeon(ExitDungeonAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(2950877880480132764UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction ForceResetSeason(ForceResetSeasonAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(15941254558648459401UL, offset);
                offset += 8;
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
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Profile, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(4819994211046333298UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction JoinBossFight(JoinBossFightAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Profile, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.BossFight, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(13497229756135777931UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction JoinJob(JoinJobAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Player, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.HelperStake, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SkrMint, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction JoinJobWithSession(JoinJobWithSessionAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Escrow, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.HelperStake, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerTokenAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SkrMint, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.TokenProgram, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(10588806654058103858UL, offset);
                offset += 8;
                _data.WriteU8(direction, offset);
                offset += 1;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction LootBoss(LootBossAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.BossFight, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.LootReceipt, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(3070053737248129477UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction LootChest(LootChestAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.LootReceipt, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
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
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Profile, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.CurrentRoom, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.TargetRoom, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.CurrentPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.TargetPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction RemoveInventoryItem(RemoveInventoryItemAccounts accounts, ushort item_id, uint amount, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(8898137908046575999UL, offset);
                offset += 8;
                _data.WriteU16(item_id, offset);
                offset += 2;
                _data.WriteU32(amount, offset);
                offset += 4;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction ResetMyPlayer(ResetMyPlayerAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(9865713732384978146UL, offset);
                offset += 8;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction ResetPlayerForTesting(ResetPlayerForTestingAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Profile, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(8654500352465190211UL, offset);
                offset += 8;
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction SetPlayerSkin(SetPlayerSkinAccounts accounts, ushort skin_id, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Profile, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.RoomPresence, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(15470882606742745413UL, offset);
                offset += 8;
                _data.WriteU16(skin_id, offset);
                offset += 2;
                byte[] resultData = new byte[offset];
                Array.Copy(_data, resultData, offset);
                return new Solana.Unity.Rpc.Models.TransactionInstruction{Keys = keys, ProgramId = programId.KeyBytes, Data = resultData};
            }

            public static Solana.Unity.Rpc.Models.TransactionInstruction TickBossFight(TickBossFightAccounts accounts, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Caller, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(18292877083428576667UL, offset);
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

            public static Solana.Unity.Rpc.Models.TransactionInstruction UnlockDoor(UnlockDoorAccounts accounts, byte direction, PublicKey programId = null)
            {
                programId ??= new(ID);
                List<Solana.Unity.Rpc.Models.AccountMeta> keys = new()
                {Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Authority, true), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.Player, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Global, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.PlayerAccount, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Room, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.AdjacentRoom, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.Inventory, false), Solana.Unity.Rpc.Models.AccountMeta.Writable(accounts.SessionAuthority == null ? programId : accounts.SessionAuthority, false), Solana.Unity.Rpc.Models.AccountMeta.ReadOnly(accounts.SystemProgram, false)};
                byte[] _data = new byte[1200];
                int offset = 0;
                _data.WriteU64(1945469879090199389UL, offset);
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