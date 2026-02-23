using System;
using System.Collections.Generic;

namespace SeekerDungeon.Dungeon
{
    internal static class DuelVisualLockRegistry
    {
        private static readonly HashSet<string> LockedWallets = new(StringComparer.Ordinal);

        public static void Lock(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return;
            }

            LockedWallets.Add(walletKey.Trim());
        }

        public static void Unlock(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return;
            }

            LockedWallets.Remove(walletKey.Trim());
        }

        public static bool IsLocked(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return false;
            }

            return LockedWallets.Contains(walletKey.Trim());
        }
    }
}

