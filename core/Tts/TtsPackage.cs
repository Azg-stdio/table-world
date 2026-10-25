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
        /// <summary>Where a save comes from: a subscribed Workshop game, a game the player saved, or one TTS saved by itself.</summary>
        public enum SaveKind { Workshop, Save, Autosave }

        public sealed class Save
        {
            public string Name = "";
            public string File = "";
            public SaveKind Kind;
            public DateTime Modified;

            /// <summary>The name with where it comes from, so a Workshop game and the autosaves of a session with it tell apart.</summary>
            public string Label => Kind switch
            {
                SaveKind.Workshop => $"{Name} · Workshop",
                SaveKind.Save => $"{Name} · your save, {Modified:d MMM HH:mm}",
                _ => $"{Name} · TTS autosave, {Modified:d MMM HH:mm}",
            };

            /// <summary>The game folder it imports into: a save never replaces the Workshop game's import, or the other way round.</summary>
            public string FolderName => TtsPackage.FolderName(Kind == SaveKind.Workshop ? Name : $"{Name} - {(Kind == SaveKind.Save ? "save" : "autosave")}");
        }

        /// <summary>What an import recorded about itself (<c>import.json</c> in the package).</summary>
        public sealed class Info
        {
            public int Importer;
            public string Source = "";
            public SaveKind Kind;
            public string SaveName = "";
            public DateTime Imported;
            public List<(string Url, string Path)> Missing = new List<(string, string)>();

            /// <summary>The files still missing from <paramref name="packageFolder"/> (some may have been downloaded since).</summary>
            public List<(string Url, string Path)> MissingNow(string packageFolder) =>
                Missing.Where(m => !System.IO.File.Exists(System.IO.Path.Combine(packageFolder, m.Path))).ToList();
        }

        public const string InfoFile = "import.json";

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
                var workshop = folder.EndsWith("Workshop", StringComparison.Ordinal);
                foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
                    if (NameOf(file) is { } name)
                        saves.Add(new Save
                        {
                            Name = name,
                            File = file,
                            Kind = workshop ? SaveKind.Workshop : KindOf(file),
                            Modified = System.IO.File.GetLastWriteTime(file),
                        });
            }
            // A game's Workshop original first, then its saves newest first.
            return saves.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Kind).ThenByDescending(s => s.Modified).ToList();
        }

        /// <summary>TTS names its own saves TS_AutoSave*.json; the rest in Saves are the player's.</summary>
        public static SaveKind KindOf(string file) =>
            Path.GetFileName(file).StartsWith("TS_AutoSave", StringComparison.OrdinalIgnoreCase) ? SaveKind.Autosave : SaveKind.Save;

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
                AssetType.Pdf => (new[] { "PDF" }, new[] { ".PDF", ".pdf" }),
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
            var import = TtsImporter.Import(File.ReadAllText(saveFile), url =>
                CachedFile(ttsFolder, new ImportedAsset { Type = AssetType.Image, CacheKey = TtsImporter.CacheKey(url), Url = url }) is { } file ? ImageSize.Of(file) : null);
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
            var info = new Newtonsoft.Json.Linq.JObject
            {
                ["importer"] = TtsImporter.Version,
                ["source"] = Path.GetFullPath(saveFile),
                ["kind"] = (saveFile.Replace('\\', '/').Contains("/Mods/Workshop/") ? SaveKind.Workshop : KindOf(saveFile)).ToString(),
                ["saveName"] = NameOf(saveFile) ?? "",
                ["imported"] = DateTime.Now.ToString("o"),
                ["missing"] = new Newtonsoft.Json.Linq.JArray(report.Missing.Select(m => new Newtonsoft.Json.Linq.JObject { ["url"] = m.Url, ["path"] = m.Path })),
            };
            File.WriteAllText(Path.Combine(packageFolder, InfoFile), info.ToString());
            return report;
        }

        /// <summary>What the import in <paramref name="packageFolder"/> recorded, or null (not an import, or one made before import.json).</summary>
        public static Info? ReadInfo(string packageFolder)
        {
            var file = Path.Combine(packageFolder, InfoFile);
            if (!File.Exists(file))
                return null;
            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                var info = new Info
                {
                    Importer = (int?)o["importer"] ?? 0,
                    Source = (string?)o["source"] ?? "",
                    Kind = Enum.TryParse<SaveKind>((string?)o["kind"], out var kind) ? kind : SaveKind.Workshop,
                    SaveName = (string?)o["saveName"] ?? "",
                    Imported = (DateTime?)o["imported"] ?? DateTime.MinValue,
                };
                foreach (var m in (o["missing"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray()).OfType<Newtonsoft.Json.Linq.JObject>())
                    if ((string?)m["url"] is { } url && (string?)m["path"] is { } path && Asset.IsSafePath(path))
                        info.Missing.Add((url, path));
                return info;
            }
            catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException || e is FormatException || e is InvalidCastException)
            {
                return null;
            }
        }

        /// <summary>True when the package is an import made by an older importer (or before imports recorded their version).</summary>
        public static bool IsImport(string packageFolder) =>
            File.Exists(Path.Combine(packageFolder, InfoFile)) || File.Exists(Path.Combine(packageFolder, "import-notes.txt"));

        public static bool Outdated(string packageFolder) =>
            IsImport(packageFolder) && (ReadInfo(packageFolder)?.Importer ?? 0) < TtsImporter.Version;

        /// <summary>
        /// The save the package was imported from, when it is still on this machine: the recorded path, or for older
        /// imports the file named on the first line of <c>import-notes.txt</c>, looked for among <paramref name="ttsFolder"/>'s saves.
        /// </summary>
        public static string? SourceOf(string packageFolder, string ttsFolder)
        {
            if (ReadInfo(packageFolder) is { } info && File.Exists(info.Source))
                return info.Source;
            var notes = Path.Combine(packageFolder, "import-notes.txt");
            if (!File.Exists(notes))
                return null;
            var match = Regex.Match(File.ReadLines(notes).FirstOrDefault() ?? "", @"^Imported from (.+?) on ");
            if (!match.Success)
                return null;
            return Find(ttsFolder).FirstOrDefault(s => string.Equals(Path.GetFileName(s.File), match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))?.File;
        }

        /// <summary>A folder name for a save: its name without characters Windows refuses.</summary>
        public static string FolderName(string saveName)
        {
            var name = Regex.Replace(saveName ?? "", "[\\\\/:*?\"<>|]", "").Trim().TrimEnd('.');
            return (name.Length > 0 ? name : "Imported game") + " (TTS)";
        }
    }
}
