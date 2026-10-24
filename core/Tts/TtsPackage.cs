using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Rules;

namespace Core.Tts
{
    /// <summary>
    /// Turns a Tabletop Simulator save on this machine into one of our game packages (M7 T8.1): <c>table.json</c> plus
    /// the images, models and bundles the table needs, copied from the player's own TTS cache. Nothing is downloaded or
    /// shared here; what the cache lacks is listed (T8.2 fetches it).
    /// </summary>
    public static class TtsPackage
    {
        public sealed class Save
        {
            public string Name = "";
            public string File = "";
        }

        public sealed class Report
        {
            public string TableFile = "";
            public int Objects;
            public int Copied;
            public List<ImportedAsset> Missing = new List<ImportedAsset>();
            public List<string> Notes = new List<string>();
        }

        /// <summary>The usual Tabletop Simulator folder (Documents/My Games/Tabletop Simulator).</summary>
        public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tabletop Simulator");

        /// <summary>
        /// The saves TTS keeps: subscribed Workshop games and the player's own saves. Names come from the start of each
        /// file, where TTS writes <c>SaveName</c>, so even 30 MB saves list quickly.
        /// </summary>
        public static List<Save> Find(string ttsFolder)
        {
            var saves = new List<Save>();
            foreach (var folder in new[] { Path.Combine(ttsFolder, "Mods", "Workshop"), Path.Combine(ttsFolder, "Saves") })
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
                    if (NameOf(file) is { } name)
                        saves.Add(new Save { Name = name, File = file });
            }
            return saves.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string? NameOf(string file)
        {
            try
            {
                using var reader = new StreamReader(file);
                var buffer = new char[4096];
                var head = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length));
                if (!head.TrimStart().StartsWith("{", StringComparison.Ordinal) || !head.Contains("\"SaveName\""))
                    return null;
                var match = Regex.Match(head, "\"SaveName\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                var name = match.Success ? Regex.Unescape(match.Groups[1].Value) : "";
                return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(file);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Where TTS cached an asset, or null when it has not.</summary>
        public static string? CachedFile(string ttsFolder, ImportedAsset asset)
        {
            var (folders, extensions) = asset.Type switch
            {
                AssetType.AssetBundle => (new[] { "Assetbundles" }, new[] { ".unity3d" }),
                AssetType.Model => (new[] { "Models" }, new[] { ".obj" }),
                _ => (new[] { "Images", "Images Raw" }, new[] { ".png", ".jpg", ".jpeg", ".bmp" }),
            };
            foreach (var folder in folders)
                foreach (var extension in extensions)
                {
                    var file = Path.Combine(ttsFolder, "Mods", folder, asset.CacheKey + extension);
                    if (File.Exists(file))
                        return file;
                }
            return null;
        }

        /// <summary>
        /// Imports <paramref name="saveFile"/> into <paramref name="packageFolder"/>: the table, the assets found in the
        /// TTS cache (a file already there with the same size is kept), and <c>import-notes.txt</c> with what did not map.
        /// </summary>
        public static Report Write(string saveFile, string ttsFolder, string packageFolder)
        {
            var import = TtsImporter.Import(File.ReadAllText(saveFile));
            Directory.CreateDirectory(packageFolder);
            var report = new Report { Objects = import.State.Objects.Count, Notes = import.Notes };
            foreach (var asset in import.Assets)
            {
                var source = CachedFile(ttsFolder, asset);
                if (source == null)
                {
                    report.Missing.Add(asset);
                    continue;
                }
                var target = Path.Combine(packageFolder, asset.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                    File.Copy(source, target, overwrite: true);
                report.Copied++;
            }
            report.TableFile = Path.Combine(packageFolder, "table.json");
            File.WriteAllText(report.TableFile, TableJson.Serialize(import.State));
            var notes = new List<string> { $"Imported from {Path.GetFileName(saveFile)} on {DateTime.Now:yyyy-MM-dd HH:mm}.",
                $"{report.Objects} objects; {report.Copied} of {import.Assets.Count} files found in the Tabletop Simulator cache." };
            notes.AddRange(report.Missing.Select(m => $"Not in the cache yet: {m.Url}"));
            notes.AddRange(import.Notes);
            File.WriteAllLines(Path.Combine(packageFolder, "import-notes.txt"), notes);
            return report;
        }

        /// <summary>A folder name for a save: its name without characters Windows refuses.</summary>
        public static string FolderName(string saveName)
        {
            var name = Regex.Replace(saveName ?? "", "[\\\\/:*?\"<>|]", "").Trim().TrimEnd('.');
            return (name.Length > 0 ? name : "Imported game") + " (TTS)";
        }
    }
}
