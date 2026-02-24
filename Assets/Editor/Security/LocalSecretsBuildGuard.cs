using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace SeekerDungeon.Editor.Security
{
    public sealed class LocalSecretsBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            if (isDevelopmentBuild)
            {
                return;
            }

            var localSecretsDirectory = Path.Combine("Assets", "Resources", "LocalSecrets");
            if (!Directory.Exists(localSecretsDirectory))
            {
                return;
            }

            var forbiddenAssets = Directory
                .GetFiles(localSecretsDirectory, "*.asset", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".gitkeep"))
                .ToArray();

            if (forbiddenAssets.Length == 0)
            {
                return;
            }

            var secretBearingAssets = forbiddenAssets
                .Where(ContainsLikelySecretValue)
                .ToArray();

            if (secretBearingAssets.Length == 0)
            {
                UnityEngine.Debug.Log(
                    "[LocalSecretsBuildGuard] Release build allowed: local assets found but no secret-like values were detected.");
                return;
            }

            var joinedPaths = string.Join(", ", secretBearingAssets);
            throw new BuildFailedException(
                "Release build blocked: found local assets with secret-like values under Assets/Resources/LocalSecrets. " +
                $"Move/remove them before building. Files: {joinedPaths}");
        }

        private static bool ContainsLikelySecretValue(string assetPath)
        {
            string text;
            try
            {
                text = File.ReadAllText(assetPath);
            }
            catch
            {
                // Fail closed if the asset cannot be inspected.
                return true;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // YAML-serialized ScriptableObject field checks for non-empty high-risk secret values.
            var hasNonEmptySecretField =
                Regex.IsMatch(text, @"(?im)^\s*(api[-_]?key|apikey|secret|password|private[-_]?key|mnemonic|seed(phrase)?)\s*:\s*\S+");
            if (hasNonEmptySecretField)
            {
                return true;
            }

            // Detect high-risk secret-bearing URL query parameters.
            return
                Regex.IsMatch(text, @"(?i)[?&](api[-_]?key|apikey|x-api-key|secret|password)=\S+");
        }
    }
}
