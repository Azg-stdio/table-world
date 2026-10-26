using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TableWorld.Editor
{
    /// <summary>
    /// Publishes the MCP server next to a Windows build as one self-contained <c>TableWorld.Mcp.exe</c> (no .NET needed on
    /// the player's machine), so the game's "Connect an AI agent" page can point the player's agent at it. The server
    /// ships with the game it talks to: their bridge protocol changes together. Needs the .NET 9 SDK where the game is
    /// built; without it the build is kept and the error says what is missing.
    /// </summary>
    public sealed class ShipMcpServer : IPostprocessBuildWithReport
    {
        public int callbackOrder => 1;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.StandaloneWindows64)
                Publish(Path.GetDirectoryName(report.summary.outputPath) ?? ".");
        }

        /// <summary>Publishes <c>TableWorld.Mcp.exe</c> into <paramref name="folder"/>; returns its path, or null (logged).</summary>
        public static string Publish(string folder)
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "mcp-server", "McpServer.csproj"));
            var output = Path.Combine(Path.GetTempPath(), "TableWorld.Mcp.publish");
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            var arguments = $"publish \"{project}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true " +
                            $"-p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o \"{output}\"";
            var start = new ProcessStartInfo("dotnet", arguments)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            string log;
            int exit;
            try
            {
                using var process = Process.Start(start)!;
                log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                exit = process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                Debug.LogError($"[ShipMcpServer] Could not run dotnet ({e.Message}): install the .NET 9 SDK to ship the MCP server with the build.");
                return null;
            }
            var exe = Path.Combine(output, "TableWorld.Mcp.exe");
            if (exit != 0 || !File.Exists(exe))
            {
                Debug.LogError($"[ShipMcpServer] dotnet publish failed ({exit}); the build has no MCP server:\n{log}");
                return null;
            }
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "TableWorld.Mcp.exe");
            File.Copy(exe, target, overwrite: true);
            Debug.Log($"[ShipMcpServer] Published {target} ({new FileInfo(target).Length / 1048576} MB).");
            return target;
        }
    }
}
