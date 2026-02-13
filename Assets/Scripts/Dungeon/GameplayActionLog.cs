using System;
using System.IO;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Writes a clean, structured gameplay action log to a separate file.
    /// Only active in the Unity Editor. Each entry is timestamped and
    /// captures the before/after state so debugging is straightforward.
    /// The log file is at: Logs/gameplay_actions.log (project root).
    /// </summary>
    public static class GameplayActionLog
    {
#if UNITY_EDITOR
        private static readonly string LogPath;
        private static readonly object WriteLock = new object();

        static GameplayActionLog()
        {
            var logsDir = Path.Combine(Application.dataPath, "..", "Logs");
            Directory.CreateDirectory(logsDir);
            LogPath = Path.Combine(logsDir, "gameplay_actions.log");

            // Write session header
            lock (WriteLock)
            {
                File.AppendAllText(LogPath,
                    $"\n{"".PadRight(60, '=')}\n" +
                    $"Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"{"".PadRight(60, '=')}\n");
            }
        }

        private static void Write(string line)
        {
            lock (WriteLock)
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n");
            }
        }
#endif

        // ----------------------------------------------------------------
        // Door interactions
        // ----------------------------------------------------------------

        public static void DoorClicked(
            string direction,
            int roomX, int roomY,
            string wallState,
            bool hasActiveJob)
        {
#if UNITY_EDITOR
            Write($"CLICK DOOR  | room=({roomX},{roomY}) dir={direction} wall={wallState} activeJob={hasActiveJob}");
#endif
        }

        public static void DoorTxResult(
            string direction,
            bool success,
            string signatureOrError)
        {
#if UNITY_EDITOR
            var status = success ? "OK" : "FAIL";
            var detail = success
                ? (signatureOrError?.Length > 16 ? signatureOrError.Substring(0, 16) + "..." : signatureOrError)
                : signatureOrError;
            Write($"  TX RESULT | dir={direction} status={status} {detail}");
#endif
        }

        public static void DoorPostState(
            int playerRoomX, int playerRoomY,
            bool playerMovedRooms,
            bool shouldTransitionRoom,
            bool hasHelperStake)
        {
#if UNITY_EDITOR
            Write($"  POST      | playerRoom=({playerRoomX},{playerRoomY}) moved={playerMovedRooms} transition={shouldTransitionRoom} stake={hasHelperStake}");
#endif
        }

        public static void DoorOutcome(string outcome)
        {
#if UNITY_EDITOR
            Write($"  OUTCOME   | {outcome}");
#endif
        }

        // ----------------------------------------------------------------
        // Room transitions
        // ----------------------------------------------------------------

        public static void RoomTransitionStart(int fromX, int fromY, int toX, int toY)
        {
#if UNITY_EDITOR
            Write($"TRANSITION  | from=({fromX},{fromY}) to=({toX},{toY})");
#endif
        }

        public static void RoomTransitionEnd(int roomX, int roomY, bool success, string detail = null)
        {
#if UNITY_EDITOR
            var status = success ? "OK" : "FAIL";
            Write($"  ARRIVED   | room=({roomX},{roomY}) status={status}" +
                  (detail != null ? $" {detail}" : ""));
#endif
        }

        // ----------------------------------------------------------------
        // Center interactions
        // ----------------------------------------------------------------

        public static void CenterClicked(int roomX, int roomY, string centerType)
        {
#if UNITY_EDITOR
            Write($"CLICK CTR   | room=({roomX},{roomY}) type={centerType}");
#endif
        }

        public static void CenterTxResult(bool success, string signatureOrError)
        {
#if UNITY_EDITOR
            var status = success ? "OK" : "FAIL";
            var detail = success
                ? (signatureOrError?.Length > 16 ? signatureOrError.Substring(0, 16) + "..." : signatureOrError)
                : signatureOrError;
            Write($"  TX RESULT | status={status} {detail}");
#endif
        }

        // ----------------------------------------------------------------
        // Job auto-complete
        // ----------------------------------------------------------------

        public static void AutoComplete(string direction, string step, bool success, string detail = null)
        {
#if UNITY_EDITOR
            var status = success ? "OK" : "FAIL";
            Write($"AUTO-COMPL  | dir={direction} step={step} status={status}" +
                  (detail != null ? $" {detail}" : ""));
#endif
        }

        // ----------------------------------------------------------------
        // Generic
        // ----------------------------------------------------------------

        public static void Info(string message)
        {
#if UNITY_EDITOR
            Write($"INFO        | {message}");
#endif
        }

        public static void Error(string message)
        {
#if UNITY_EDITOR
            Write($"ERROR       | {message}");
#endif
        }
    }
}
