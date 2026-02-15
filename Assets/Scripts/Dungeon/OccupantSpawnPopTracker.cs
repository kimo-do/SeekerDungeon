using System;
using System.Collections.Generic;

namespace SeekerDungeon.Dungeon
{
    internal static class OccupantSpawnPopTracker
    {
        private static readonly HashSet<string> SeenKeys = new(StringComparer.Ordinal);

        public static bool HasSeen(string occupantKey)
        {
            if (string.IsNullOrWhiteSpace(occupantKey))
            {
                return false;
            }

            return SeenKeys.Contains(occupantKey);
        }

        public static void MarkSeen(string occupantKey)
        {
            if (string.IsNullOrWhiteSpace(occupantKey))
            {
                return;
            }

            SeenKeys.Add(occupantKey);
        }

        public static void Reset()
        {
            SeenKeys.Clear();
        }
    }
}
