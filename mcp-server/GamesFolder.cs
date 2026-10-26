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

        public static string Find()
        {
            try
            {
                if (File.Exists(InfoPath) && (string?)JObject.Parse(File.ReadAllText(InfoPath))["gamesFolder"] is { Length: > 0 } folder)
                    return folder;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Newtonsoft.Json.JsonException)
            {
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "games");
        }
    }
}
