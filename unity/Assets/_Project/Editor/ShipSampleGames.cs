using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TableWorld.Editor
{
    /// <summary>
    /// Copies the repo's sample games (<c>games/*.json</c>) next to a Windows build, into
    /// <c>&lt;Product&gt;_Data/StreamingAssets/Games</c>, so a player can open them from the HUD without the agent.
    /// Copied after the build so the repo keeps a single copy of each game.
    /// </summary>
    public sealed class ShipSampleGames : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 && report.summary.platform != BuildTarget.StandaloneWindows)
                return;
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "games"));
            if (!Directory.Exists(source))
                return;
            var exe = report.summary.outputPath;
            var data = Path.Combine(Path.GetDirectoryName(exe) ?? ".", Path.GetFileNameWithoutExtension(exe) + "_Data");
            var target = Path.Combine(data, "StreamingAssets", "Games");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            Debug.Log($"[ShipSampleGames] Copied {Directory.GetFiles(source, "*.json").Length} games to {target}.");
        }
    }
}
