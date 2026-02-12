using System;
using System.Collections.Generic;
using SeekerDungeon.Solana;

namespace SeekerDungeon.Dungeon
{
    public sealed class DungeonOccupantVisual
    {
        public string WalletKey { get; init; }
        public string DisplayName { get; init; }
        public PlayerSkinId SkinId { get; init; }
        public ItemId EquippedItemId { get; init; }
        public OccupantActivity Activity { get; init; }
        public RoomDirection? ActivityDirection { get; init; }
        public bool IsFightingBoss { get; init; }
    }

    public sealed class DungeonRoomSnapshot
    {
        public RoomView Room { get; init; }
        public IReadOnlyDictionary<RoomDirection, IReadOnlyList<DungeonOccupantVisual>> DoorOccupants { get; init; }
        public IReadOnlyList<DungeonOccupantVisual> BossOccupants { get; init; }
        public IReadOnlyList<DungeonOccupantVisual> IdleOccupants { get; init; }

        /// <summary>
        /// Directions where the local player currently has an active job.
        /// Used to toggle VisualInteractable highlight off on doors the player is already working.
        /// </summary>
        public HashSet<RoomDirection> LocalPlayerActiveJobDirections { get; init; }

        /// <summary>Whether the local player is currently fighting the boss in this room.</summary>
        public bool LocalPlayerFightingBoss { get; init; }
    }

    [Serializable]
    public sealed class DoorOccupancyDelta
    {
        public RoomDirection Direction { get; init; }
        public IReadOnlyList<DungeonOccupantVisual> Joined { get; init; }
        public IReadOnlyList<DungeonOccupantVisual> Left { get; init; }
    }
}
