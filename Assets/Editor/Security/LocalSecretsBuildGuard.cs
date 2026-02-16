using System.IO;
using System.Linq;
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

            var joinedPaths = string.Join(", ", forbiddenAssets);
            throw new BuildFailedException(
                "Release build blocked: found local secret assets under Assets/Resources/LocalSecrets. " +
                $"Move/remove them before building. Files: {joinedPaths}");
        }
    }
}
