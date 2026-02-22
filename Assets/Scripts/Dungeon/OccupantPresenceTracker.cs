using System;
using System.Collections.Generic;

namespace SeekerDungeon.Dungeon
{
    public enum OccupantPresenceState
    {
        Active = 0,
        Idle = 1,
        Afk = 2
    }

    public static class OccupantPresenceTracker
    {
        private sealed class PresenceEntry
        {
            public string Signature;
            public float LastChangedRealtime;
            public float LastSeenRealtime;
        }

        private static readonly Dictionary<string, PresenceEntry> Entries = new(StringComparer.Ordinal);
        private const float StaleCleanupSeconds = 900f;

        public static OccupantPresenceState UpdateAndGetState(
            string walletKey,
            string signature,
            float nowRealtime,
            float activeThresholdSeconds,
            float idleThresholdSeconds,
            float initialElapsedSecondsEstimate = 0f)
        {
            if (string.IsNullOrWhiteSpace(walletKey))
            {
                return OccupantPresenceState.Active;
            }

            if (!Entries.TryGetValue(walletKey, out var entry) || entry == null)
            {
                entry = new PresenceEntry
                {
                    Signature = signature ?? string.Empty,
                    LastChangedRealtime = nowRealtime - Math.Max(0f, initialElapsedSecondsEstimate),
                    LastSeenRealtime = nowRealtime
                };
                Entries[walletKey] = entry;
            }
            else
            {
                var newSignature = signature ?? string.Empty;
                if (!string.Equals(entry.Signature, newSignature, StringComparison.Ordinal))
                {
                    entry.Signature = newSignature;
                    entry.LastChangedRealtime = nowRealtime;
                }
            }

            // If chain-derived activity indicates a more recent action than we have locally,
            // move the "last changed" cursor forward even when the visual signature is unchanged.
            var estimatedLastChangedRealtime = nowRealtime - Math.Max(0f, initialElapsedSecondsEstimate);
            if (estimatedLastChangedRealtime > entry.LastChangedRealtime)
            {
                entry.LastChangedRealtime = estimatedLastChangedRealtime;
            }

            entry.LastSeenRealtime = nowRealtime;
            CleanupStale(nowRealtime);

            var elapsed = nowRealtime - entry.LastChangedRealtime;
            var activeThreshold = Math.Max(0f, activeThresholdSeconds);
            var idleThreshold = Math.Max(activeThreshold, idleThresholdSeconds);
            if (elapsed <= activeThreshold)
            {
                return OccupantPresenceState.Active;
            }

            if (elapsed <= idleThreshold)
            {
                return OccupantPresenceState.Idle;
            }

            return OccupantPresenceState.Afk;
        }

        private static void CleanupStale(float nowRealtime)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            var staleKeys = new List<string>();
            foreach (var pair in Entries)
            {
                if (nowRealtime - pair.Value.LastSeenRealtime > StaleCleanupSeconds)
                {
                    staleKeys.Add(pair.Key);
                }
            }

            for (var index = 0; index < staleKeys.Count; index += 1)
            {
                Entries.Remove(staleKeys[index]);
            }
        }
    }
}
