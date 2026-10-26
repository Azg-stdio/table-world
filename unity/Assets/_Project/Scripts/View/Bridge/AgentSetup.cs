using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TableWorld.View.Bridge
{
    /// <summary>
    /// How a player connects their own agent to Table World: the MCP server ships next to the game
    /// (<c>TableWorld.Mcp.exe</c>, published by the build), and these are the lines that register it with Claude Code,
    /// Claude Desktop and Codex, with this install's path filled in. The game also tells the server where the player's
    /// Games folder is (<see cref="GameInfoPath"/>), so the games an agent builds appear in Games.
    /// </summary>
    public static class AgentSetup
    {
        public const string ServerName = "table-world";
        public const string ExeName = "TableWorld.Mcp.exe";

        /// <summary>Read by the server at start: <c>{ gamesFolder, server }</c>, next to the bridge's discovery file.</summary>
        public static string GameInfoPath => Path.Combine(Path.GetDirectoryName(BridgeClient.DiscoveryPath)!, "game.json");

        /// <summary>
        /// The server to run: the published exe next to a build; in the Editor, the repository's Release build, run with
        /// <c>dotnet</c> (<c>dotnet build mcp-server -c Release</c>).
        /// </summary>
        public static string ServerPath => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "mcp-server", "bin", "Release", "net9.0", "TableWorld.Mcp.dll"))
            : Path.Combine(Path.GetDirectoryName(Application.dataPath)!, ExeName);

        public static bool ServerFound => File.Exists(ServerPath);

        /// <summary>The player's Games folder as Windows writes it (Unity's data path comes with forward slashes).</summary>
        public static string GamesFolder => Path.GetFullPath(GameLibrary.UserFolder);

        /// <summary>A .dll needs <c>dotnet</c> in front; the published exe runs on its own.</summary>
        private static bool NeedsDotnet => ServerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        public static string ClaudeCodeCommand =>
            $"claude mcp add --scope user {ServerName} -- {(NeedsDotnet ? "dotnet " : "")}\"{ServerPath}\"";

        public static string ClaudeDesktopConfig
        {
            get
            {
                var command = NeedsDotnet ? "dotnet" : ServerPath;
                var args = NeedsDotnet ? new JArray(ServerPath) : new JArray();
                return new JObject { ["mcpServers"] = new JObject { [ServerName] = new JObject { ["command"] = command, ["args"] = args } } }
                    .ToString(Newtonsoft.Json.Formatting.Indented);
            }
        }

        /// <summary>TOML literal strings (single quotes) keep Windows backslashes as they are.</summary>
        public static string CodexConfig => NeedsDotnet
            ? $"[mcp_servers.{ServerName}]\ncommand = 'dotnet'\nargs = ['{ServerPath}']"
            : $"[mcp_servers.{ServerName}]\ncommand = '{ServerPath}'";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WriteGameInfo()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GameInfoPath)!);
                Directory.CreateDirectory(GameLibrary.UserFolder);
                File.WriteAllText(GameInfoPath, new JObject { ["gamesFolder"] = GamesFolder, ["server"] = ServerPath }.ToString());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[AgentSetup] Could not write {GameInfoPath}: {e.Message}");
            }
        }
    }
}
