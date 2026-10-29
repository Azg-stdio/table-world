using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>Content hashes in the same <c>sha256:&lt;hex&gt;</c> form as <see cref="Asset.Hash"/> and state hashes.</summary>
    public static class AssetHash
    {
        public static string Of(byte[] data)
        {
            using var sha = SHA256.Create();
            return "sha256:" + string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        /// <summary>True for <c>sha256:</c> plus 64 lowercase hex digits: safe to use as a file name.</summary>
        public static bool IsValid(string? hash) =>
            hash != null && hash.Length == 71 && hash.StartsWith("sha256:", StringComparison.Ordinal) &&
            hash.Skip(7).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    /// <summary>
    /// The client cache, plus the player's music library (M12): a track the player already imported is in the cache by
    /// its hash, so it is never downloaded. Written files go to the cache only.
    /// </summary>
    public sealed class LibraryAwareStore : IAssetStore
    {
        private readonly IAssetStore _inner;
        private readonly Func<string, string?> _pathByHash;

        public LibraryAwareStore(IAssetStore inner, Func<string, string?> pathByHash)
        {
            _inner = inner;
            _pathByHash = pathByHash;
        }

        public bool Has(string hash) => _inner.Has(hash) || _pathByHash(hash) != null;

        public byte[]? Read(string hash)
        {
            if (_inner.Read(hash) is { } data)
                return data;
            var path = _pathByHash(hash);
            try
            {
                return path != null ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Write(string hash, byte[] data) => _inner.Write(hash, data);
    }

    /// <summary>Content-addressed asset bytes: the client cache.</summary>
    public interface IAssetStore
    {
        bool Has(string hash);
        byte[]? Read(string hash);
        void Write(string hash, byte[] data);
    }

    public sealed class MemoryAssetStore : IAssetStore
    {
        private readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();

        public int Count => _data.Count;
        public bool Has(string hash) => _data.ContainsKey(hash);
        public byte[]? Read(string hash) => _data.TryGetValue(hash, out var d) ? d : null;
        public void Write(string hash, byte[] data) => _data[hash] = data;
    }

    /// <summary>One file per hash (<c>&lt;hex&gt;</c>) in a folder; writes go through a temp file so a crash never leaves half an asset.</summary>
    public sealed class DirectoryAssetStore : IAssetStore
    {
        public string Folder { get; }

        public DirectoryAssetStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
        }

        private string FileOf(string hash) =>
            AssetHash.IsValid(hash) ? System.IO.Path.Combine(Folder, hash.Substring(7)) : throw new ArgumentException($"Invalid hash '{hash}'.");

        public bool Has(string hash) => AssetHash.IsValid(hash) && File.Exists(FileOf(hash));

        public byte[]? Read(string hash)
        {
            if (!Has(hash))
                return null;
            var file = FileOf(hash);
            // Reading marks it used, for the least-recently-used cleanup (Windows may not keep access times itself).
            try
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
            }
            catch (IOException)
            {
            }
            return File.ReadAllBytes(file);
        }

        /// <summary>Default size of the cache (T5.3): TTS tables with their models and bundles run to gigabytes.</summary>
        public const long DefaultMaxBytes = 10L * 1024 * 1024 * 1024;

        /// <summary>
        /// Cleanup (M4 T5.3): deletes interrupted downloads older than a day, then the least recently used files until the
        /// cache holds at most <paramref name="maxBytes"/>. Returns the bytes freed.
        /// </summary>
        public long Trim(long maxBytes = DefaultMaxBytes)
        {
            long freed = 0;
            var files = new DirectoryInfo(Folder).GetFiles();
            foreach (var part in files.Where(f => f.Name.EndsWith(".part", StringComparison.Ordinal) && f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
                freed += Delete(part);
            var kept = files.Where(f => !f.Name.EndsWith(".part", StringComparison.Ordinal) && f.Exists).OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = kept.Sum(f => f.Length);
            foreach (var oldest in kept)
            {
                if (total <= maxBytes)
                    break;
                var size = Delete(oldest);
                freed += size;
                total -= size;
            }
            return freed;
        }

        private static long Delete(FileInfo file)
        {
            try
            {
                var size = file.Length;
                file.Delete();
                return size;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        public void Write(string hash, byte[] data)
        {
            var file = FileOf(hash);
            var temp = file + ".part";
            File.WriteAllBytes(temp, data);
            if (File.Exists(file))
                File.Delete(file);
            File.Move(temp, file);
        }
    }

    /// <summary>One asset the host can send: which table asset, its content hash and size.</summary>
    public sealed class ManifestEntry
    {
        public string Id { get; set; } = "";
        public string Hash { get; set; } = "";
        public long Size { get; set; }
    }

    /// <summary>
    /// Host side of asset sync (T3.4). The host lists the files of the table's non-builtin image assets in a manifest
    /// (<c>id → hash, size</c>) that travels with every snapshot; clients fetch what their cache lacks with
    /// <c>asset_request</c> and the host answers each requested chunk with <c>asset_chunk</c>. The host serves only hashes
    /// in its manifest, so a client can never ask for an arbitrary file. Images only for the MVP; models after M4.
    /// </summary>
    public sealed class AssetHost
    {
        public const int ChunkSize = 16 * 1024;
        public const long MaxAssetSize = 32L * 1024 * 1024;
        public const int MaxChunksPerRequest = 64;

        private readonly Func<Asset, byte[]?> _read;
        private readonly Dictionary<string, byte[]> _byHash = new Dictionary<string, byte[]>();
        private List<ManifestEntry> _entries = new List<ManifestEntry>();
        /// <summary>Files read and hashed by an earlier build, by id and path: a build after a small change reads only what is new.</summary>
        private readonly Dictionary<(string, string), (string Hash, byte[] Data)> _readCache = new Dictionary<(string, string), (string, byte[])>();

        /// <summary>The id a library track of the table's music travels under: <c>music:library:pack/track</c>.</summary>
        public static string MusicId(MusicTrack track) => "music:" + track.Url;

        /// <summary>
        /// Reads a library track of the table's music (<c>library:pack/track</c>, M12) from the host's own music library;
        /// null leaves it out. Without it no library track travels.
        /// </summary>
        public Func<MusicTrack, byte[]?>? ReadTrack { get; set; }

        /// <summary><paramref name="read"/> returns an asset's file bytes, or null when the host does not have it.</summary>
        public AssetHost(Func<Asset, byte[]?> read)
        {
            _read = read;
        }

        public IReadOnlyList<ManifestEntry> Entries => _entries;

        /// <summary>Chunks sent so far, to all clients.</summary>
        public long ChunksServed { get; private set; }

        /// <summary>Problems found while building the manifest (missing files, oversize assets, hash mismatches).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Reads and hashes the table's assets. Call again after the table's asset list changes.</summary>
        public void Build(TableState state)
        {
            _byHash.Clear();
            Warnings.Clear();
            var entries = new List<ManifestEntry>();
            // Images, model meshes, asset bundles and PDFs travel, and each language's file of an image with words in it
            // (ADR 0011) as its own entry, <id>@<language>.
            var travelling = state.Assets.Where(a => (a.Type == AssetType.Image || a.Type == AssetType.Model || a.Type == AssetType.AssetBundle || a.Type == AssetType.Pdf) && !a.IsBuiltin).ToList();
            foreach (var asset in travelling.ToList())
                foreach (var (language, path) in asset.Localized)
                    travelling.Add(LocalizedAsset(asset, language, path));
            // Sound last (M12): what the table shows arrives first, then its audio and music, the first track to play first.
            travelling.AddRange(state.Assets.Where(a => a.Type == AssetType.Audio && !a.IsBuiltin));
            var tracks = new Dictionary<string, MusicTrack>();
            if (ReadTrack != null && state.Music != null)
                foreach (var track in state.Music.AllTracks().Where(t => t.IsLibrary))
                {
                    var id = MusicId(track);
                    tracks[id] = track;
                    travelling.Add(new Asset { Id = id, Type = AssetType.Audio, Path = track.Url, Hash = track.Hash });
                }
            foreach (var asset in travelling)
            {
                byte[]? data;
                string? cachedHash = null;
                try
                {
                    if (_readCache.TryGetValue((asset.Id, asset.Path), out var cached))
                    {
                        data = cached.Data;
                        cachedHash = cached.Hash;
                    }
                    else
                        data = tracks.TryGetValue(asset.Id, out var track) ? ReadTrack!(track) : _read(asset);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    data = null;
                    Warnings.Add($"Asset '{asset.Id}': {e.Message}");
                }
                if (data == null)
                {
                    Warnings.Add($"Asset '{asset.Id}': no file at '{asset.Path}'; players will see a placeholder.");
                    continue;
                }
                if (data.Length > MaxAssetSize)
                {
                    Warnings.Add($"Asset '{asset.Id}' is {data.Length / (1024 * 1024)} MB; the limit is {MaxAssetSize / (1024 * 1024)} MB.");
                    continue;
                }
                var hash = cachedHash ?? AssetHash.Of(data);
                _readCache[(asset.Id, asset.Path)] = (hash, data);
                if (asset.Hash != null && asset.Hash != hash)
                    Warnings.Add($"Asset '{asset.Id}': the table says {asset.Hash} but the file is {hash}; sending the file.");
                _byHash[hash] = data;
                entries.Add(new ManifestEntry { Id = asset.Id, Hash = hash, Size = data.Length });
            }
            _entries = entries;
        }

        /// <summary>
        /// The stand-in for <paramref name="asset"/>'s file in <paramref name="language"/>: id <c>&lt;id&gt;@&lt;language&gt;</c>,
        /// the localized path. The host sends it and a client asks for it under that id.
        /// </summary>
        public static Asset LocalizedAsset(Asset asset, string language, string path) =>
            new Asset { Id = asset.Id + "@" + language, Type = asset.Type, Path = path, Columns = asset.Columns, Rows = asset.Rows, MeshScale = asset.MeshScale };

        public JArray Manifest() => new JArray(_entries.Select(e => new JObject { ["id"] = e.Id, ["hash"] = e.Hash, ["size"] = e.Size }));

        /// <summary>
        /// The manifest alone, for players already at the table when the files change (an asset added, a playlist
        /// changed while playing): they fetch what they lack, as they did when they joined.
        /// </summary>
        public JObject ManifestMessage() => new JObject { ["kind"] = "assets", ["assets"] = Manifest() };

        /// <summary>A key that changes when the manifest does.</summary>
        public string ManifestKey => string.Join(";", _entries.Select(e => e.Id + "=" + e.Hash));

        /// <summary>Answers an <c>asset_request</c>; anything else, or a request for an unknown hash, yields nothing.</summary>
        public List<Outgoing> Receive(string clientId, JObject message)
        {
            var replies = new List<Outgoing>();
            if ((string?)message["kind"] != "asset_request" || !(message["chunks"] is JArray chunks))
                return replies;
            foreach (var chunk in chunks.OfType<JObject>().Take(MaxChunksPerRequest))
            {
                var hash = (string?)chunk["hash"];
                var index = (int?)chunk["index"] ?? -1;
                if (hash == null || !_byHash.TryGetValue(hash, out var data) || index < 0 || index >= ChunkCount(data.Length))
                    continue;
                var offset = index * ChunkSize;
                var length = Math.Min(ChunkSize, data.Length - offset);
                ChunksServed++;
                replies.Add(new Outgoing(clientId, new JObject
                {
                    ["kind"] = "asset_chunk",
                    ["hash"] = hash,
                    ["index"] = index,
                    ["data"] = Convert.ToBase64String(data, offset, length),
                }));
            }
            return replies;
        }

        public static int ChunkCount(long size) => size == 0 ? 1 : (int)((size + ChunkSize - 1) / ChunkSize);
    }

    /// <summary>
    /// Client side of asset sync. Reads the manifest from a snapshot, requests the chunks of every hash the cache lacks
    /// with at most <see cref="Window"/> chunks in flight (receiver-driven flow control, so a slow or lossy link never
    /// floods the reliable pipeline), verifies each finished file against its hash and stores it. A file that fails
    /// verification is fetched again, up to <see cref="MaxAttempts"/> times.
    /// </summary>
    public sealed class AssetClient
    {
        public const int Window = 4;
        public const int MaxAttempts = 3;

        private sealed class Download
        {
            public string Hash = "";
            public byte[] Data = Array.Empty<byte>();
            public bool[] Received = Array.Empty<bool>();
            public int ReceivedCount;
            public int NextIndex;
            public int Attempts = 1;
        }

        private readonly IAssetStore _store;
        private readonly Dictionary<string, string> _hashOf = new Dictionary<string, string>();
        private readonly Dictionary<string, Download> _downloads = new Dictionary<string, Download>();
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<(string, int)> _inFlight = new HashSet<(string, int)>();
        private readonly HashSet<string> _failed = new HashSet<string>();

        public AssetClient(IAssetStore store)
        {
            _store = store;
        }

        /// <summary>Raised when an asset's bytes are in the cache (fetched now or already there), with the asset id.</summary>
        public event Action<string>? Ready;

        /// <summary>Raised when an asset could not be fetched intact, with the asset id and why.</summary>
        public event Action<string, string>? Failed;

        public long BytesReceived { get; private set; }
        public int Fetched { get; private set; }
        public int FromCache { get; private set; }

        /// <summary>Files still downloading or waiting their turn.</summary>
        public int Pending => _downloads.Count;

        public bool Complete => Pending == 0;

        /// <summary>The asset's bytes once they are in the cache; null while it is missing.</summary>
        public byte[]? Read(string assetId) => _hashOf.TryGetValue(assetId, out var hash) ? _store.Read(hash) : null;

        /// <summary>
        /// Handles <c>snapshot</c> (reads its <c>assets</c> manifest) and <c>asset_chunk</c>; returns the
        /// <c>asset_request</c> to send, or null.
        /// </summary>
        public JObject? Receive(JObject message)
        {
            switch ((string?)message["kind"])
            {
                case "snapshot" when message["assets"] is JArray manifest:
                    return ReadManifest(manifest);
                case "assets" when message["assets"] is JArray changed:
                    return ReadManifest(changed);
                case "asset_chunk":
                    return ReadChunk(message);
                default:
                    return null;
            }
        }

        private JObject? ReadManifest(JArray manifest)
        {
            // A new snapshot (join or resync) restarts the requests of unfinished files: chunks already received are kept,
            // and a request the host dropped can no longer stall the window.
            _inFlight.Clear();
            foreach (var download in _downloads.Values)
                download.NextIndex = 0;
            _hashOf.Clear();
            var cached = new List<string>();
            foreach (var entry in manifest.OfType<JObject>())
            {
                var id = (string?)entry["id"];
                var hash = (string?)entry["hash"];
                var size = (long?)entry["size"] ?? -1;
                if (id == null || !AssetHash.IsValid(hash) || size < 0 || size > AssetHost.MaxAssetSize)
                    continue;
                _hashOf[id] = hash!;
                if (_store.Has(hash!))
                {
                    cached.Add(id);
                    continue;
                }
                if (_downloads.ContainsKey(hash!))
                {
                    if (!_queue.Contains(hash!))
                        _queue.Enqueue(hash!);
                    continue;
                }
                if (_failed.Contains(hash!))
                    continue;
                _downloads[hash!] = new Download
                {
                    Hash = hash!,
                    Data = new byte[size],
                    Received = new bool[AssetHost.ChunkCount(size)],
                };
                _queue.Enqueue(hash!);
            }
            foreach (var id in cached)
            {
                FromCache++;
                Ready?.Invoke(id);
            }
            return Request();
        }

        private JObject? ReadChunk(JObject message)
        {
            var hash = (string?)message["hash"];
            var index = (int?)message["index"] ?? -1;
            if (hash == null || !_downloads.TryGetValue(hash, out var download) || index < 0 || index >= download.Received.Length)
                return null;
            _inFlight.Remove((hash, index));
            if (download.Received[index])
                return Request();

            byte[] data;
            try
            {
                data = Convert.FromBase64String((string?)message["data"] ?? "");
            }
            catch (FormatException)
            {
                return Retry(download, $"chunk {index} is not base64");
            }
            var offset = index * AssetHost.ChunkSize;
            var expected = Math.Min(AssetHost.ChunkSize, download.Data.Length - offset);
            if (data.Length != Math.Max(0, expected))
                return Retry(download, $"chunk {index} has {data.Length} bytes, expected {expected}");
            Buffer.BlockCopy(data, 0, download.Data, offset, data.Length);
            download.Received[index] = true;
            download.ReceivedCount++;
            BytesReceived += data.Length;

            if (download.ReceivedCount == download.Received.Length)
            {
                if (AssetHash.Of(download.Data) != hash)
                    return Retry(download, "the finished file does not match its hash");
                _store.Write(hash, download.Data);
                _downloads.Remove(hash);
                Fetched++;
                foreach (var id in _hashOf.Where(p => p.Value == hash).Select(p => p.Key).ToList())
                    Ready?.Invoke(id);
            }
            return Request();
        }

        /// <summary>Starts the file over, or gives up on it after <see cref="MaxAttempts"/>.</summary>
        private JObject? Retry(Download download, string why)
        {
            _inFlight.RemoveWhere(f => f.Item1 == download.Hash);
            if (download.Attempts >= MaxAttempts)
            {
                _downloads.Remove(download.Hash);
                _failed.Add(download.Hash);
                foreach (var id in _hashOf.Where(p => p.Value == download.Hash).Select(p => p.Key).ToList())
                    Failed?.Invoke(id, why);
                return Request();
            }
            download.Attempts++;
            Array.Clear(download.Received, 0, download.Received.Length);
            download.ReceivedCount = 0;
            download.NextIndex = 0;
            if (!_queue.Contains(download.Hash))
                _queue.Enqueue(download.Hash);
            return Request();
        }

        /// <summary>Fills the window with the next chunks, oldest file first.</summary>
        private JObject? Request()
        {
            var chunks = new JArray();
            while (_inFlight.Count < Window && _queue.Count > 0)
            {
                if (!_downloads.TryGetValue(_queue.Peek(), out var download))
                {
                    _queue.Dequeue();
                    continue;
                }
                while (download.NextIndex < download.Received.Length && download.Received[download.NextIndex])
                    download.NextIndex++;
                if (download.NextIndex >= download.Received.Length)
                {
                    _queue.Dequeue();
                    continue;
                }
                var index = download.NextIndex++;
                _inFlight.Add((download.Hash, index));
                chunks.Add(new JObject { ["hash"] = download.Hash, ["index"] = index });
            }
            return chunks.Count == 0 ? null : new JObject { ["kind"] = "asset_request", ["chunks"] = chunks };
        }
    }
}
