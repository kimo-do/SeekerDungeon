using System;
using System.Collections.Generic;

namespace SeekerDungeon.Solana
{
    [Serializable]
    public sealed class DungeonExtractionItemSummary
    {
        public ItemId ItemId;
        public uint Amount;
        public ulong UnitScore;
        public ulong StackScore;
    }

    [Serializable]
    public sealed class DungeonExtractionSummary
    {
        public List<DungeonExtractionItemSummary> Items = new();
        public ulong LootScore;
        public ulong TimeScore;
        public ulong RunScore;
        public ulong TotalScoreAfterRun;
    }

    public static class DungeonExtractionSummaryStore
    {
        private static DungeonExtractionSummary _pendingSummary;

        public static bool HasPendingSummary => _pendingSummary != null;

        public static void SetPending(DungeonExtractionSummary summary)
        {
            _pendingSummary = summary;
        }

        public static DungeonExtractionSummary ConsumePending()
        {
            var summary = _pendingSummary;
            _pendingSummary = null;
            return summary;
        }
    }

    /// <summary>
    /// Local wallet-scoped marker for whether a run should be considered resumable.
    /// This complements onchain heuristics where some states at (5,5) are ambiguous.
    /// </summary>
    public static class DungeonRunResumeStore
    {
        private const string PrefKeyPrefix = "seeker_dungeon_run_active_";

        public static bool IsMarkedInRun(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return false;
            }

            return UnityEngine.PlayerPrefs.GetInt(PrefKeyPrefix + walletKey, 0) == 1;
        }

        public static void MarkInRun(string walletKey)
        {
            SetMarkedInRun(walletKey, true);
        }

        public static void ClearRun(string walletKey)
        {
            SetMarkedInRun(walletKey, false);
        }

        private static void SetMarkedInRun(string walletKey, bool isInRun)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return;
            }

            UnityEngine.PlayerPrefs.SetInt(PrefKeyPrefix + walletKey, isInRun ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }
    }

    public static class ExtractionScoreTable
    {
        public static bool IsScoredLoot(ushort itemId)
        {
            return itemId >= 200 && itemId <= 299;
        }

        public static ulong ScoreValueForItem(ushort itemId)
        {
            return itemId switch
            {
                200 => 1,
                201 => 3,
                202 => 8,
                203 => 12,
                204 => 10,
                205 => 9,
                206 => 9,
                207 => 20,
                208 => 2,
                209 => 15,
                210 => 11,
                211 => 4,
                212 => 7,
                213 => 14,
                214 => 0,
                215 => 13,
                216 => 3,
                217 => 8,
                218 => 18,
                219 => 16,
                _ => 0
            };
        }
    }
}
