using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Games a player can open from the HUD: the samples shipped with the build (<c>StreamingAssets/Games</c>, copied
    /// from the repo's <c>games/</c> at build time; the repo folder itself in the Editor) and the player's own folder
    /// (<see cref="UserFolder"/>). A game is a <c>.json</c> file, or a folder holding <c>table.json</c> plus its images.
    /// </summary>
    public static class GameLibrary
    {
        public sealed class Entry
        {
            public string Name;
            public string File;
            /// <summary>Folder the game's asset paths are relative to.</summary>
            public string PackageRoot => Path.GetDirectoryName(File);
        }

        public static string UserFolder => Path.Combine(Application.persistentDataPath, "Games");

        public static IEnumerable<string> Folders()
        {
            if (Application.isEditor)
                yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "games"));
            else
                yield return Path.Combine(Application.streamingAssetsPath, "Games");
            yield return UserFolder;
        }

        public static List<Entry> List()
        {
            try
            {
                Directory.CreateDirectory(UserFolder);
            }
            catch (IOException)
            {
                // A read-only profile still lists the shipped games.
            }
            var entries = new List<Entry>();
            foreach (var folder in Folders().Where(Directory.Exists))
            {
                var files = Directory.GetFiles(folder, "*.json")
                    .Concat(Directory.GetDirectories(folder).Select(d => Path.Combine(d, "table.json")).Where(System.IO.File.Exists));
                entries.AddRange(files.Select(f => new Entry { Name = NameOf(f), File = f }));
            }
            return entries.OrderBy(e => e.Name).ToList();
        }

        /// <summary><c>game.name</c> from the file, or the file name when it has none (or is not JSON).</summary>
        private static string NameOf(string file)
        {
            var fallback = Path.GetFileName(file) == "table.json" ? Path.GetFileName(Path.GetDirectoryName(file)) : Path.GetFileNameWithoutExtension(file);
            try
            {
                return (string)JObject.Parse(System.IO.File.ReadAllText(file))["game"]?["name"] ?? fallback;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return fallback;
            }
        }
    }
}
