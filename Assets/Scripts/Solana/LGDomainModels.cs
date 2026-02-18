using System;
using System.Collections.Generic;
using System.Linq;
using Chaindepth.Accounts;
using Chaindepth.Types;
using Solana.Unity.Wallet;

namespace SeekerDungeon.Solana
{
    public enum RoomCenterType
    {
        Unknown = 255,
        Empty = 0,
        Chest = 1,
        Boss = 2
    }

    public enum RoomWallState
    {
        Unknown = 255,
        Solid = 0,
        Rubble = 1,
        Open = 2,
        Locked = 3,
        EntranceStairs = 4
    }

    public enum RoomDirection : byte
    {
        North = 0,
        South = 1,
        East = 2,
        West = 3
    }

    public enum ItemId : ushort
    {
        None = 0,

        // Legacy (kept for backward compat with existing inventories)
        LegacyOre = 1,
        LegacyTool = 2,
        LegacyBuff = 3,

        // Wearable Weapons (100-199)
        BronzePickaxe = 100,
        IronPickaxe = 101,
        BronzeSword = 102,
        IronSword = 103,
        DiamondSword = 104,
        Nokia3310 = 105,
        WoodenPipe = 106,
        IronScimitar = 107,
        WoodenTankard = 108,

        // Lootable Valuables (200-299)
        SilverCoin = 200,
        GoldCoin = 201,
        GoldBar = 202,
        Diamond = 203,
        Ruby = 204,
        Sapphire = 205,
        Emerald = 206,
        AncientCrown = 207,
        GoblinTooth = 208,
        DragonScale = 209,
        CursedAmulet = 210,
        DustyTome = 211,
        EnchantedScroll = 212,
        GoldenChalice = 213,
        SkeletonKey = 214,
        MysticOrb = 215,
        RustedCompass = 216,
        DwarfBeardRing = 217,
        PhoenixFeather = 218,
        VoidShard = 219,

        // Consumable Buffs (300-399)
        MinorBuff = 300,
        MajorBuff = 301,
    }

    public enum PlayerSkinId : ushort
    {
        CheekyGoblin = 0,
        ScrappyDwarfCharacter = 1,
        DrunkDwarfCharacter = 2,
        FatDwarfCharacter = 3,
        FriendlyGoblin = 4,
        GingerBearDwarfVariant = 5,
        HappyDrunkDwarf = 6,
        IdleGoblin = 7,
        IdleHumanCharacter = 8,
        JollyDwarfCharacter = 9,
        JollyDwarfVariant = 10,
        OldDwarfCharacter = 11,
        ScrappyDwarfGingerBeard = 12,
        ScrappyDwarfVariant = 13,
        ScrappyHumanAssassin = 14,
        ScrappySkeleton = 15,
        SinisterHoodedFigure = 16,
        NoseGoblin = 17,
        SkeletonCape = 18
    }

    public enum OccupantActivity
    {
        Idle = 0,
        DoorJob = 1,
        BossFight = 2,
        Unknown = 255
    }

    public sealed class DoorJobView
    {
        public RoomDirection Direction { get; init; }
        public RoomWallState WallState { get; init; }
        public byte LockKind { get; init; }
        public ItemId RequiredKeyItemId { get; init; }
        public uint HelperCount { get; init; }
        public ulong Progress { get; init; }
        public ulong RequiredProgress { get; init; }
        public ulong StartSlot { get; init; }
        public bool IsCompleted { get; init; }
        public bool IsOpen => WallState == RoomWallState.Open;
        public bool IsRubble => WallState == RoomWallState.Rubble;
        public bool IsLocked => WallState == RoomWallState.Locked;
        public bool IsEntranceStairs => WallState == RoomWallState.EntranceStairs;
    }

    public sealed class MonsterView
    {
        public ushort MonsterId { get; init; }
        public ulong MaxHp { get; init; }
        public ulong CurrentHp { get; init; }
        public ulong TotalDps { get; init; }
        public uint FighterCount { get; init; }
        public bool IsDead { get; init; }
    }

    public sealed class RoomView
    {
        public sbyte X { get; init; }
        public sbyte Y { get; init; }
        public RoomCenterType CenterType { get; init; }
        public int LootedCount { get; init; }
        public bool HasLocalPlayerLooted { get; set; }
        public bool ForcedKeyDrop { get; init; }
        public PublicKey CreatedBy { get; init; }
        public IReadOnlyDictionary<RoomDirection, DoorJobView> Doors { get; init; }
        private MonsterView _monster;

        public bool HasChest() => CenterType == RoomCenterType.Chest;
        public bool IsEmpty() => CenterType == RoomCenterType.Empty;
        public bool HasBoss() => CenterType == RoomCenterType.Boss;

        public bool TryGetMonster(out MonsterView monster)
        {
            monster = _monster;
            return monster != null;
        }

        internal void SetMonster(MonsterView monster)
        {
            _monster = monster;
        }
    }

    public sealed class InventoryItemView
    {
        public ItemId ItemId { get; init; }
        public uint Amount { get; init; }
        public ushort Durability { get; init; }
    }

    public sealed class LootResult
    {
        public IReadOnlyList<InventoryItemView> Items { get; init; }
    }

    public sealed class PlayerStateView
    {
        public PublicKey Owner { get; init; }
        public sbyte RoomX { get; init; }
        public sbyte RoomY { get; init; }
        public ulong JobsCompleted { get; init; }
        public ulong TotalScore { get; init; }
        public ulong RunsExtracted { get; init; }
        public bool InDungeon { get; init; }
        public ItemId EquippedItemId { get; init; }
        public int SkinId { get; init; }
        public string DisplayName { get; init; }
        public IReadOnlyList<ActiveJobView> ActiveJobs { get; init; }

        public string GetDisplayNameOrWallet()
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            var wallet = Owner?.Key;
            if (string.IsNullOrEmpty(wallet) || wallet.Length < 10)
            {
                return "Unknown";
            }

            return $"{wallet.Substring(0, 4)}...{wallet.Substring(wallet.Length - 4)}";
        }
    }

    public sealed class ActiveJobView
    {
        public sbyte RoomX { get; init; }
        public sbyte RoomY { get; init; }
        public RoomDirection Direction { get; init; }
    }

    public sealed class RoomOccupantView
    {
        public PublicKey Wallet { get; init; }
        public ItemId EquippedItemId { get; init; }
        public int SkinId { get; init; }
        public OccupantActivity Activity { get; init; }
        public RoomDirection? ActivityDirection { get; init; }
        public bool IsFightingBoss { get; init; }
    }

    public static class LGDomainMapper
    {
        public static RoomView ToRoomView(this RoomAccount room, PublicKey localPlayerWallet = null)
        {
            if (room == null)
            {
                return null;
            }

            var doors = new Dictionary<RoomDirection, DoorJobView>(4);
            doors[RoomDirection.North] = BuildDoor(room, RoomDirection.North);
            doors[RoomDirection.South] = BuildDoor(room, RoomDirection.South);
            doors[RoomDirection.East] = BuildDoor(room, RoomDirection.East);
            doors[RoomDirection.West] = BuildDoor(room, RoomDirection.West);

            // HasLocalPlayerLooted is now determined by checking the LootReceipt PDA
            // via RPC (done asynchronously after mapping). Defaults to false here.

            var roomView = new RoomView
            {
                X = room.X,
                Y = room.Y,
                CenterType = ToCenterType(room.CenterType),
                LootedCount = (int)room.LootedCount,
                HasLocalPlayerLooted = false,
                ForcedKeyDrop = room.ForcedKeyDrop,
                CreatedBy = room.CreatedBy,
                Doors = doors
            };

            if (roomView.CenterType == RoomCenterType.Boss)
            {
                roomView.SetMonster(new MonsterView
                {
                    MonsterId = room.CenterId,
                    MaxHp = room.BossMaxHp,
                    CurrentHp = room.BossCurrentHp,
                    TotalDps = room.BossTotalDps,
                    FighterCount = room.BossFighterCount,
                    IsDead = room.BossDefeated
                });
            }

            return roomView;
        }

        public static PlayerStateView ToPlayerView(
            this PlayerAccount player,
            PlayerProfile profile = null,
            int defaultSkinId = 0)
        {
            if (player == null)
            {
                return null;
            }

            var jobs = (player.ActiveJobs ?? Array.Empty<ActiveJob>())
                .Select(job => new ActiveJobView
                {
                    RoomX = job.RoomX,
                    RoomY = job.RoomY,
                    Direction = ToDirection(job.Direction)
                })
                .ToArray();

            return new PlayerStateView
            {
                Owner = player.Owner,
                RoomX = player.CurrentRoomX,
                RoomY = player.CurrentRoomY,
                JobsCompleted = player.JobsCompleted,
                TotalScore = player.TotalScore,
                RunsExtracted = player.RunsExtracted,
                InDungeon = player.InDungeon,
                EquippedItemId = ToItemId(player.EquippedItemId),
                SkinId = profile != null ? profile.SkinId : defaultSkinId,
                DisplayName = profile?.DisplayName ?? string.Empty,
                ActiveJobs = jobs
            };
        }

        public static RoomWallState ToWallState(byte wallState)
        {
            return wallState switch
            {
                0 => RoomWallState.Solid,
                1 => RoomWallState.Rubble,
                2 => RoomWallState.Open,
                3 => RoomWallState.Locked,
                4 => RoomWallState.EntranceStairs,
                _ => RoomWallState.Unknown
            };
        }

        public static RoomCenterType ToCenterType(byte centerType)
        {
            return centerType switch
            {
                0 => RoomCenterType.Empty,
                1 => RoomCenterType.Chest,
                2 => RoomCenterType.Boss,
                _ => RoomCenterType.Unknown
            };
        }

        public static RoomDirection ToDirection(byte direction)
        {
            return direction switch
            {
                0 => RoomDirection.North,
                1 => RoomDirection.South,
                2 => RoomDirection.East,
                3 => RoomDirection.West,
                _ => RoomDirection.North
            };
        }

        public static bool TryToDirection(byte direction, out RoomDirection result)
        {
            switch (direction)
            {
                case 0:
                    result = RoomDirection.North;
                    return true;
                case 1:
                    result = RoomDirection.South;
                    return true;
                case 2:
                    result = RoomDirection.East;
                    return true;
                case 3:
                    result = RoomDirection.West;
                    return true;
                default:
                    result = RoomDirection.North;
                    return false;
            }
        }

        public static ItemId ToItemId(ushort itemId)
        {
            if (System.Enum.IsDefined(typeof(ItemId), itemId))
                return (ItemId)itemId;
            return ItemId.None;
        }

        public static OccupantActivity ToOccupantActivity(byte activity)
        {
            return activity switch
            {
                0 => OccupantActivity.Idle,
                1 => OccupantActivity.DoorJob,
                2 => OccupantActivity.BossFight,
                _ => OccupantActivity.Unknown
            };
        }

        public static IReadOnlyList<InventoryItemView> ToInventoryItemViews(this InventoryAccount inventory)
        {
            if (inventory?.Items == null || inventory.Items.Length == 0)
            {
                return Array.Empty<InventoryItemView>();
            }

            return inventory.Items
                .Where(item => item != null && item.Amount > 0)
                .Select(item => new InventoryItemView
                {
                    ItemId = ToItemId(item.ItemId),
                    Amount = item.Amount,
                    Durability = item.Durability
                })
                .ToArray();
        }

        public static LootResult ComputeLootDiff(
            InventoryAccount before,
            InventoryAccount after)
        {
            var beforeTotals = new Dictionary<(ushort itemId, ushort durability), uint>();
            var afterTotals = new Dictionary<(ushort itemId, ushort durability), uint>();

            if (before?.Items != null)
            {
                foreach (var item in before.Items)
                {
                    if (item == null || item.Amount == 0)
                    {
                        continue;
                    }

                    var key = (item.ItemId, item.Durability);
                    if (beforeTotals.TryGetValue(key, out var existing))
                    {
                        beforeTotals[key] = existing + item.Amount;
                    }
                    else
                    {
                        beforeTotals[key] = item.Amount;
                    }
                }
            }

            if (after?.Items != null)
            {
                foreach (var item in after.Items)
                {
                    if (item == null || item.Amount == 0)
                    {
                        continue;
                    }

                    var key = (item.ItemId, item.Durability);
                    if (afterTotals.TryGetValue(key, out var existing))
                    {
                        afterTotals[key] = existing + item.Amount;
                    }
                    else
                    {
                        afterTotals[key] = item.Amount;
                    }
                }
            }

            var gained = new List<InventoryItemView>();
            foreach (var kvp in afterTotals)
            {
                beforeTotals.TryGetValue(kvp.Key, out var previousAmount);
                var currentAmount = kvp.Value;
                if (currentAmount <= previousAmount)
                {
                    continue;
                }

                gained.Add(new InventoryItemView
                {
                    ItemId = ToItemId(kvp.Key.itemId),
                    Amount = currentAmount - previousAmount,
                    Durability = kvp.Key.durability
                });
            }

            return new LootResult { Items = gained };
        }

        private static DoorJobView BuildDoor(RoomAccount room, RoomDirection direction)
        {
            var directionIndex = (int)direction;
            var walls = room.Walls ?? Array.Empty<byte>();
            var helperCounts = room.HelperCounts ?? Array.Empty<uint>();
            var progress = room.Progress ?? Array.Empty<ulong>();
            var required = room.BaseSlots ?? Array.Empty<ulong>();
            var completed = room.JobCompleted ?? Array.Empty<bool>();
            var startSlots = room.StartSlot ?? Array.Empty<ulong>();
            var lockKinds = room.DoorLockKinds ?? Array.Empty<byte>();
            var lockKind = directionIndex < lockKinds.Length ? lockKinds[directionIndex] : (byte)0;

            return new DoorJobView
            {
                Direction = direction,
                WallState = directionIndex < walls.Length
                    ? ToWallState(walls[directionIndex])
                    : RoomWallState.Unknown,
                LockKind = lockKind,
                RequiredKeyItemId = ToRequiredKeyItem(lockKind),
                HelperCount = directionIndex < helperCounts.Length ? helperCounts[directionIndex] : 0,
                Progress = directionIndex < progress.Length ? progress[directionIndex] : 0,
                RequiredProgress = directionIndex < required.Length ? required[directionIndex] : 0,
                StartSlot = directionIndex < startSlots.Length ? startSlots[directionIndex] : 0,
                IsCompleted = directionIndex < completed.Length && completed[directionIndex]
            };
        }

        private static ItemId ToRequiredKeyItem(byte lockKind)
        {
            return LGConfig.GetRequiredKeyItemForLockKind(lockKind);
        }
    }
}
