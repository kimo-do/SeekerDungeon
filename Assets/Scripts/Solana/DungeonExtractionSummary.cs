using System;
using System.Collections.Generic;

namespace SeekerDungeon.Solana
{
    public enum DungeonRunEndReason : byte
    {
        Extraction = 0,
        Death = 1
    }

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
        public DungeonRunEndReason RunEndReason;
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
    /// This complements onchain heuristics where some states at start-room coordinates are ambiguous.
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
            SetMarkedInRun(walletKey, true, "menu_enter_dungeon");
        }

        public static void ClearRun(string walletKey)
        {
            SetMarkedInRun(walletKey, false, "run_end");
        }

        private static void SetMarkedInRun(string walletKey, bool isInRun, string reason)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return;
            }

            var prefKey = PrefKeyPrefix + walletKey;
            var previous = UnityEngine.PlayerPrefs.GetInt(prefKey, 0) == 1;
            UnityEngine.PlayerPrefs.SetInt(prefKey, isInRun ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
            UnityEngine.Debug.Log(
                $"[DungeonRunResumeStore] wallet={ShortWallet(walletKey)} mark {previous} -> {isInRun} reason={reason}");
        }

        private static string ShortWallet(string walletKey)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return "<null>";
            }

            return walletKey.Length <= 8
                ? walletKey
                : $"{walletKey[..4]}..{walletKey[^4..]}";
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
