using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Core.Net;
using Newtonsoft.Json;

namespace Core.Music
{
    /// <summary>A track of a music pack: its file in the pack's folder, what players read, and its content hash.</summary>
    public sealed class LibraryTrack
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string File { get; set; } = "";
        public string Hash { get; set; } = "";
        public long Size { get; set; }
        /// <summary>Length in seconds when the file says it (OGG and WAV); null otherwise.</summary>
        public double? Seconds { get; set; }
        /// <summary>Words that say what it sounds like, from its name and folders: ambient, dark, action, loop, fx...</summary>
        public List<string> Tags { get; set; } = new List<string>();
    }

    /// <summary>A music pack the player imported: its tracks and where it came from.</summary>
    public sealed class MusicPack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Author { get; set; }
        /// <summary>Where to get it (the author's page), for players who do not have it yet.</summary>
        public string? Source { get; set; }
        /// <summary>The licence file copied from the pack, in the pack's folder.</summary>
        public string? LicenseFile { get; set; }
        /// <summary>The importer said they have the right to use this music.</summary>
        public bool RightsConfirmed { get; set; }
        public string Imported { get; set; } = "";
        public List<LibraryTrack> Tracks { get; set; } = new List<LibraryTrack>();
    }

    /// <summary>
    /// The player's music library (M12, T13.3): packs of tracks imported from a zip or a folder into a folder of their
    /// own (<see cref="DefaultFolder"/>), outside the game and its builds. Each pack keeps a <c>pack.json</c>. A game
    /// names a track as <c>library:&lt;pack&gt;/&lt;track&gt;</c> with its hash; a machine that has the same file plays its
    /// own copy, others receive it from the host. Music is never shipped with the game.
    /// </summary>
    public sealed class MusicLibrary
    {
        public const string Prefix = "library:";
        public const string IndexFile = "pack.json";
        public static readonly string[] Extensions = { ".ogg", ".mp3", ".wav" };

        /// <summary><c>%LOCALAPPDATA%/TableWorld/Music</c>: shared by the game and the agent's MCP server.</summary>
        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableWorld", "Music");

        public string Folder { get; }
        private List<MusicPack> _packs = new List<MusicPack>();

        public MusicLibrary(string? folder = null)
        {
            Folder = folder ?? DefaultFolder;
            Reload();
        }

        public IReadOnlyList<MusicPack> Packs => _packs;

        /// <summary>Reads every pack's <c>pack.json</c> again (another process may have imported one).</summary>
        public void Reload()
        {
            var packs = new List<MusicPack>();
            if (Directory.Exists(Folder))
                foreach (var dir in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var index = Path.Combine(dir, IndexFile);
                    if (!System.IO.File.Exists(index))
                        continue;
                    try
                    {
                        if (JsonConvert.DeserializeObject<MusicPack>(System.IO.File.ReadAllText(index)) is { } pack)
                        {
                            pack.Id = Path.GetFileName(dir);
                            packs.Add(pack);
                        }
                    }
                    catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
                    {
                    }
                }
            _packs = packs;
        }

        public static string Url(MusicPack pack, LibraryTrack track) => Prefix + pack.Id + "/" + track.Id;

        public static bool IsLibraryUrl(string? url) => url != null && url.StartsWith(Prefix, StringComparison.Ordinal) && url.IndexOf('/') > Prefix.Length;

        /// <summary>The pack and track a <c>library:</c> address names, or null.</summary>
        public (MusicPack Pack, LibraryTrack Track)? Find(string? url)
        {
            if (!IsLibraryUrl(url))
                return null;
            var rest = url!.Substring(Prefix.Length);
            var slash = rest.IndexOf('/');
            var packId = rest.Substring(0, slash);
            var trackId = rest.Substring(slash + 1);
            var pack = _packs.FirstOrDefault(p => p.Id == packId);
            var track = pack?.Tracks.FirstOrDefault(t => t.Id == trackId);
            return pack != null && track != null ? (pack, track) : ((MusicPack, LibraryTrack)?)null;
        }

        /// <summary>The file of a track: by its address, else by its hash (the same file imported under another name).</summary>
        public string? PathOf(string? url, string? hash = null)
        {
            if (Find(url) is { } found && System.IO.File.Exists(FileOf(found.Pack, found.Track)))
                return FileOf(found.Pack, found.Track);
            return hash != null ? PathByHash(hash) : null;
        }

        public string? PathByHash(string hash)
        {
            foreach (var pack in _packs)
                foreach (var track in pack.Tracks)
                    if (track.Hash == hash && System.IO.File.Exists(FileOf(pack, track)))
                        return FileOf(pack, track);
            return null;
        }

        public string FileOf(MusicPack pack, LibraryTrack track) => Path.Combine(Folder, pack.Id, track.File);

        /// <summary>Every track, with its pack, optionally those whose tags or title match <paramref name="search"/>.</summary>
        public IEnumerable<(MusicPack Pack, LibraryTrack Track)> Search(string? search = null, string? tag = null)
        {
            var words = (search ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pack in _packs)
                foreach (var track in pack.Tracks)
                {
                    if (tag != null && !track.Tags.Contains(tag.ToLowerInvariant()))
                        continue;
                    var text = (track.Title + " " + pack.Name + " " + string.Join(" ", track.Tags)).ToLowerInvariant();
                    if (words.All(w => text.Contains(w)))
                        yield return (pack, track);
                }
        }

        /// <summary>
        /// Imports a pack from a zip or a folder: its audio files (OGG when a track comes in several formats, else MP3,
        /// else WAV), its licence file, and a <c>pack.json</c>. Importing the same pack again replaces it. Requires
        /// <paramref name="rightsConfirmed"/>: the importer says they may use this music.
        /// </summary>
        public MusicPack Import(string source, bool rightsConfirmed, string? name = null, string? author = null, string? sourcePage = null)
        {
            if (!rightsConfirmed)
                throw new InvalidOperationException("Import only music you have the right to use (confirm it to import).");
            var isZip = System.IO.File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (!isZip && !Directory.Exists(source))
                throw new FileNotFoundException($"No zip or folder at '{source}'.");
            var packName = name ?? Regex.Replace(Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/')), @"\s+", " ").Trim();
            var id = Slug(packName);
            var target = Path.Combine(Folder, id);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            Directory.CreateDirectory(target);

            var entries = isZip ? ZipEntries(source) : FolderEntries(source);
            var pack = new MusicPack
            {
                Name = packName, Author = author, Source = sourcePage, RightsConfirmed = true,
                Imported = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            var licence = entries.FirstOrDefault(e => Regex.IsMatch(Path.GetFileName(e.Path), @"licen[cs]e|readme|credits", RegexOptions.IgnoreCase)
                                                      && Regex.IsMatch(e.Path, @"\.(pdf|txt|md|html?)$", RegexOptions.IgnoreCase));
            if (licence.Path != null)
            {
                pack.LicenseFile = "LICENSE" + Path.GetExtension(licence.Path).ToLowerInvariant();
                System.IO.File.WriteAllBytes(Path.Combine(target, pack.LicenseFile), licence.Read());
                pack.Author ??= Regex.Match(Path.GetFileName(licence.Path), @"^(\w+)\s+Music\s+Licen", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
            }

            // One file per track: the smallest format it comes in.
            var audio = entries.Where(e => Extensions.Contains(Path.GetExtension(e.Path).ToLowerInvariant())).ToList();
            var chosen = audio.GroupBy(e => Path.GetFileNameWithoutExtension(e.Path).Trim().ToLowerInvariant())
                .Select(g => g.OrderBy(e => Array.IndexOf(Extensions, Path.GetExtension(e.Path).ToLowerInvariant())).First())
                .OrderBy(e => NaturalKey(e.Path), StringComparer.Ordinal)
                .ToList();
            var ids = new HashSet<string>();
            foreach (var entry in chosen)
            {
                var stem = Path.GetFileNameWithoutExtension(entry.Path).Trim();
                var trackId = Slug(stem);
                for (var n = 2; !ids.Add(trackId); n++)
                    trackId = Slug(stem) + "-" + n;
                var file = trackId + Path.GetExtension(entry.Path).ToLowerInvariant();
                var data = entry.Read();
                System.IO.File.WriteAllBytes(Path.Combine(target, file), data);
                pack.Tracks.Add(new LibraryTrack
                {
                    Id = trackId, Title = Title(stem), File = file, Hash = AssetHash.Of(data), Size = data.Length,
                    Seconds = Length(data, Path.GetExtension(file)), Tags = Tags(entry.Path),
                });
            }
            var index = pack;
            System.IO.File.WriteAllText(Path.Combine(target, IndexFile), JsonConvert.SerializeObject(index, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), new UTF8Encoding(false));
            Reload();
            return _packs.First(p => p.Id == id);
        }

        /// <summary>Removes a pack and its files from the library.</summary>
        public bool Remove(string packId)
        {
            var dir = Path.Combine(Folder, Slug(packId));
            if (!Directory.Exists(dir))
                return false;
            Directory.Delete(dir, true);
            Reload();
            return true;
        }

        private readonly struct Entry
        {
            public readonly string Path;
            public readonly Func<byte[]> Read;

            public Entry(string path, Func<byte[]> read)
            {
                Path = path;
                Read = read;
            }
        }

        private static List<Entry> ZipEntries(string zip)
        {
            // Read into memory once: a pack of a few hundred megabytes is fine, and the archive is closed at once.
            var entries = new List<Entry>();
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 || entry.FullName.EndsWith("/") || entry.FullName.Contains("__MACOSX") || entry.Name.StartsWith("."))
                    continue;
                var ext = System.IO.Path.GetExtension(entry.FullName).ToLowerInvariant();
                if (!Extensions.Contains(ext) && !Regex.IsMatch(entry.Name, @"licen[cs]e|readme|credits", RegexOptions.IgnoreCase))
                    continue;
                entries.Add(new Entry(entry.FullName, () => Array.Empty<byte>()));
            }
            // Decide what to copy first (cheap), then read only those.
            return entries.Select(e => new Entry(e.Path, () => ReadZipEntry(zip, e.Path))).ToList();
        }

        private static byte[] ReadZipEntry(string zip, string path)
        {
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
            using var stream = entry.Open();
            using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static List<Entry> FolderEntries(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => new Entry(f.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/'), () => System.IO.File.ReadAllBytes(f)))
                .ToList();

        /// <summary>"Ambient 10" after "Ambient 9": numbers compared as numbers.</summary>
        private static string NaturalKey(string path) =>
            Regex.Replace(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(6, '0'));

        public static string Slug(string text)
        {
            var slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return slug.Length == 0 ? "track" : slug;
        }

        /// <summary>"10. Moonlit Journey (Loop)" reads "Moonlit Journey (loop)"; "Ambient 1 Loop" reads "Ambient 1 (loop)".</summary>
        private static string Title(string stem)
        {
            var title = Regex.Replace(stem, @"^\d+\s*[.\-_)]\s*", "");
            title = Regex.Replace(title, @"\(+\s*loop\s*\)+|\bloop\b", "", RegexOptions.IgnoreCase).Trim();
            title = Regex.Replace(title, @"\s{2,}", " ");
            var looped = Regex.IsMatch(stem, @"\bloop\b", RegexOptions.IgnoreCase);
            return (title.Length == 0 ? stem : title) + (looped ? " (loop)" : "");
        }

        private static readonly HashSet<string> NotTags = new HashSet<string> { "mp3", "ogg", "wav", "tracks", "track", "music", "pack", "free", "vol", "the", "of", "and" };

        private static List<string> Tags(string path)
        {
            var words = Regex.Split(path.ToLowerInvariant().Replace("sci-fi", "scifi"), @"[^a-z]+")
                .Where(w => w.Length > 1 && !NotTags.Contains(w))
                .Select(w => w == "loops" ? "loop" : w == "ambience" ? "ambient" : w)
                .Distinct()
                .ToList();
            return words;
        }

        /// <summary>A file's length in seconds, read from its headers: OGG (last page's granule over the sample rate) and WAV.</summary>
        public static double? Length(byte[] data, string extension)
        {
            try
            {
                switch (extension.ToLowerInvariant())
                {
                    case ".ogg":
                    {
                        // The Vorbis identification header holds the sample rate; the last page's granule is the last sample.
                        var vorbis = IndexOf(data, Encoding.ASCII.GetBytes("\u0001vorbis"), 0);
                        if (vorbis < 0)
                            return null;
                        var rate = BitConverter.ToUInt32(data, vorbis + 12);
                        var last = LastIndexOf(data, Encoding.ASCII.GetBytes("OggS"));
                        if (last < 0 || rate == 0)
                            return null;
                        var granule = BitConverter.ToInt64(data, last + 6);
                        return granule > 0 ? Math.Round(granule / (double)rate, 1) : (double?)null;
                    }
                    case ".wav":
                    {
                        if (data.Length < 44 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF")
                            return null;
                        var byteRate = BitConverter.ToUInt32(data, 28);
                        var dataChunk = IndexOf(data, Encoding.ASCII.GetBytes("data"), 12);
                        if (dataChunk < 0 || byteRate == 0)
                            return null;
                        var size = BitConverter.ToUInt32(data, dataChunk + 4);
                        return Math.Round(size / (double)byteRate, 1);
                    }
                    default:
                        return null;
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static int IndexOf(byte[] data, byte[] pattern, int from)
        {
            for (var i = from; i <= data.Length - pattern.Length; i++)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return i;
            }
            return -1;
        }

        private static int LastIndexOf(byte[] data, byte[] pattern)
        {
            for (var i = data.Length - pattern.Length - 14; i >= 0; i--)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return i;
            }
            return -1;
        }
    }
}
