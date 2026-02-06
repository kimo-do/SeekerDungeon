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
        Open = 2
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
        Ore = 1,
        Tool = 2,
        Buff = 3
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
        public uint HelperCount { get; init; }
        public ulong Progress { get; init; }
        public ulong RequiredProgress { get; init; }
        public bool IsCompleted { get; init; }
        public bool IsOpen => WallState == RoomWallState.Open;
        public bool IsRubble => WallState == RoomWallState.Rubble;
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

    public sealed class PlayerStateView
    {
        public PublicKey Owner { get; init; }
        public sbyte RoomX { get; init; }
        public sbyte RoomY { get; init; }
        public ulong JobsCompleted { get; init; }
        public ItemId EquippedItemId { get; init; }
        public int SkinId { get; init; }
        public IReadOnlyList<ActiveJobView> ActiveJobs { get; init; }
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

    public static class ChainDepthDomainMapper
    {
        public static RoomView ToRoomView(this RoomAccount room)
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

            var roomView = new RoomView
            {
                X = room.X,
                Y = room.Y,
                CenterType = ToCenterType(room.CenterType),
                LootedCount = room.LootedBy?.Length ?? 0,
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

        public static PlayerStateView ToPlayerView(this PlayerAccount player, int defaultSkinId = 0)
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
                EquippedItemId = ToItemId(player.EquippedItemId),
                SkinId = defaultSkinId,
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

        public static ItemId ToItemId(ushort itemId)
        {
            return itemId switch
            {
                1 => ItemId.Ore,
                2 => ItemId.Tool,
                3 => ItemId.Buff,
                0 => ItemId.None,
                _ => ItemId.None
            };
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

        private static DoorJobView BuildDoor(RoomAccount room, RoomDirection direction)
        {
            var directionIndex = (int)direction;
            var walls = room.Walls ?? Array.Empty<byte>();
            var helperCounts = room.HelperCounts ?? Array.Empty<uint>();
            var progress = room.Progress ?? Array.Empty<ulong>();
            var required = room.BaseSlots ?? Array.Empty<ulong>();
            var completed = room.JobCompleted ?? Array.Empty<bool>();

            return new DoorJobView
            {
                Direction = direction,
                WallState = directionIndex < walls.Length
                    ? ToWallState(walls[directionIndex])
                    : RoomWallState.Unknown,
                HelperCount = directionIndex < helperCounts.Length ? helperCounts[directionIndex] : 0,
                Progress = directionIndex < progress.Length ? progress[directionIndex] : 0,
                RequiredProgress = directionIndex < required.Length ? required[directionIndex] : 0,
                IsCompleted = directionIndex < completed.Length && completed[directionIndex]
            };
        }
    }
}
