using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp
{
    /// <summary>
    /// The player's Games folder, where a server started without <c>--file</c> saves each new game so the game window
    /// lists it. The game writes it to <c>%LOCALAPPDATA%/TableWorld/game.json</c> every time it starts (next to the bridge's
    /// <c>bridge.json</c>); before the game has ever run, games go to a <c>games</c> folder in the agent's folder.
    /// </summary>
    internal static class GamesFolder
    {
        public static string InfoPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableWorld", "game.json");

        public static string Find() => Known() ?? Path.Combine(Directory.GetCurrentDirectory(), "games");

        /// <summary>The Games folder given with <c>--games</c>, before what the game wrote.</summary>
        public static string? Override { get; set; }

        /// <summary>The Games folder given with <c>--games</c> or that the game told us about; null when neither is known.</summary>
        public static string? Known()
        {
            if (!string.IsNullOrEmpty(Override))
                return Override;
            try
            {
                if (File.Exists(InfoPath) && (string?)JObject.Parse(File.ReadAllText(InfoPath))["gamesFolder"] is { Length: > 0 } folder)
                    return folder;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Newtonsoft.Json.JsonException)
            {
            }
            return null;
        }

        /// <summary>
        /// Whether the game window's Games tab lists <paramref name="file"/>: a <c>.json</c> right in the Games folder, or a
        /// <c>table.json</c> one folder down. Null when the Games folder is not known.
        /// </summary>
        public static bool? Lists(string? file)
        {
            var folder = Known();
            if (folder == null)
                return null;
            if (file == null)
                return false;
            var games = Path.GetFullPath(folder).TrimEnd('\\', '/');
            var dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
            if (string.Equals(dir, games, StringComparison.OrdinalIgnoreCase))
                return true;
            return Path.GetFileName(file).Equals("table.json", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Path.GetDirectoryName(dir), games, StringComparison.OrdinalIgnoreCase);
        }
    }
}
