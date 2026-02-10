using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SeekerDungeon.Editor
{
    /// <summary>
    /// Headless Android build helper. Callable from the command line via:
    ///   Unity -batchmode -executeMethod SeekerDungeon.Editor.AndroidBuilder.BuildAndRun
    ///
    /// Supports the following command-line args (parsed from System.Environment.GetCommandLineArgs):
    ///   -buildPath path   Override the APK output path (default: Builds/Android/SeekerDungeon.apk)
    ///   -patchAndRun      Use Patch and Run instead of full Build and Run
    ///   -buildOnly        Build without running on device
    ///   -release          Non-development build (default is development)
    /// </summary>
    public static class AndroidBuilder
    {
        private const string DefaultBuildPath = "Builds/Android/SeekerDungeon.apk";

        [MenuItem("Build/Android - Build and Run (Dev)")]
        public static void MenuBuildAndRun()
        {
            RunBuild(autoRun: true, patch: false, development: true);
        }

        [MenuItem("Build/Android - Patch and Run (Dev)")]
        public static void MenuPatchAndRun()
        {
            RunBuild(autoRun: true, patch: true, development: true);
        }

        [MenuItem("Build/Android - Build Only (Dev)")]
        public static void MenuBuildOnly()
        {
            RunBuild(autoRun: false, patch: false, development: true);
        }

        // Command-line entry points
        public static void BuildAndRun() => RunFromCommandLine();
        public static void PatchAndRun() => RunFromCommandLine(forcePatch: true);
        public static void BuildOnly() => RunFromCommandLine(forceBuildOnly: true);

        private static void RunFromCommandLine(bool forcePatch = false, bool forceBuildOnly = false)
        {
            var args = Environment.GetCommandLineArgs();
            var buildPath = GetArgValue(args, "-buildPath") ?? DefaultBuildPath;
            var patch = forcePatch || HasArg(args, "-patchAndRun");
            var buildOnly = forceBuildOnly || HasArg(args, "-buildOnly");
            var development = !HasArg(args, "-release");

            RunBuild(autoRun: !buildOnly, patch: patch, development: development, outputPath: buildPath);
        }

        private static void RunBuild(bool autoRun, bool patch, bool development, string outputPath = null)
        {
            outputPath ??= DefaultBuildPath;

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[AndroidBuilder] No scenes enabled in Build Settings.");
                ExitIfBatchMode(1);
                return;
            }

            var options = BuildOptions.None;

            if (development)
                options |= BuildOptions.Development;

            if (autoRun && !patch)
                options |= BuildOptions.AutoRunPlayer;

            if (patch)
                options |= BuildOptions.PatchPackage | BuildOptions.AutoRunPlayer;

            // Ensure Android is the active build target
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[AndroidBuilder] Switching active build target to Android...");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            Debug.Log($"[AndroidBuilder] Starting build. scenes={scenes.Length} output={outputPath} " +
                      $"dev={development} autoRun={autoRun} patch={patch} options={options}");

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = options
            };

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            var summary = report.summary;

            Debug.Log($"[AndroidBuilder] Build result: {summary.result} " +
                      $"duration={summary.totalTime} size={summary.totalSize} " +
                      $"errors={summary.totalErrors} warnings={summary.totalWarnings}");

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[AndroidBuilder] Build FAILED: {summary.result}");
                ExitIfBatchMode(1);
                return;
            }

            Debug.Log("[AndroidBuilder] Build succeeded.");
            ExitIfBatchMode(0);
        }

        private static void ExitIfBatchMode(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static bool HasArg(string[] args, string flag)
        {
            return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetArgValue(string[] args, string flag)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
