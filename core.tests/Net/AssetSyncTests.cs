using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Net
{
    /// <summary>
    /// T3.4 core: a client with an empty cache receives every image of the host's table through the snapshot manifest and
    /// windowed chunk requests; a warm cache fetches nothing; corrupt chunks are refetched; the host serves nothing else.
    /// </summary>
    public class AssetSyncTests
    {
        private static byte[] Bytes(int size, int seed)
        {
            var random = new Random(seed);
            var data = new byte[size];
            random.NextBytes(data);
            return data;
        }

        /// <summary>Four images: big (many chunks), tiny, empty, and a duplicate of the big one under another id; plus builtin and missing.</summary>
        private static (TableState State, Dictionary<string, byte[]> Files) Table()
        {
            var files = new Dictionary<string, byte[]>
            {
                ["img/board.png"] = Bytes(300_000, 1),
                ["img/coin.png"] = Bytes(700, 2),
                ["img/empty.png"] = new byte[0],
                ["img/board-copy.png"] = Bytes(300_000, 1),
            };
            var state = new TableState();
            state.Assets.Add(new Asset { Id = "board", Type = AssetType.Image, Path = "img/board.png" });
            state.Assets.Add(new Asset { Id = "coin", Type = AssetType.Image, Path = "img/coin.png" });
            state.Assets.Add(new Asset { Id = "empty", Type = AssetType.Image, Path = "img/empty.png" });
            state.Assets.Add(new Asset { Id = "board2", Type = AssetType.Image, Path = "img/board-copy.png" });
            state.Assets.Add(new Asset { Id = "deck", Type = AssetType.Image, Path = "builtin:standard_deck" });
            state.Assets.Add(new Asset { Id = "lost", Type = AssetType.Image, Path = "img/lost.png" });
            state.Assets.Add(new Asset { Id = "dragon", Type = AssetType.Model, Path = "models/dragon.glb" });
            return (state, files);
        }

        private static SessionHost Host(TableState state, Dictionary<string, byte[]> files)
        {
            var host = new SessionHost(new GameEngine(state, 1))
            {
                Assets = new AssetHost(a => files.TryGetValue(a.Path, out var d) ? d : null),
            };
            host.Assets.Build(state);
            return host;
        }

        /// <summary>Delivers messages both ways until both queues are empty; <paramref name="tamper"/> may edit chunks in flight.</summary>
        private static int Run(SessionHost host, string id, AssetClient assets, Func<JObject, JObject>? tamper = null)
        {
            var client = new SessionClient(id);
            var toClient = new Queue<JObject>();
            var toHost = new Queue<JObject>();
            toClient.Enqueue(host.Snapshot(id).Message);
            var maxRequested = 0;
            var steps = 0;
            while (toClient.Count > 0 || toHost.Count > 0)
            {
                Assert.True(++steps < 100_000, "transfer did not finish");
                if (toHost.Count > 0)
                {
                    var request = toHost.Dequeue();
                    maxRequested = Math.Max(maxRequested, ((JArray)request["chunks"]!).Count);
                    foreach (var reply in host.Receive(id, request))
                        toClient.Enqueue(tamper == null ? reply.Message : tamper(reply.Message));
                    continue;
                }
                var message = toClient.Dequeue();
                client.Receive(message);
                var ask = assets.Receive(message);
                if (ask != null)
                    toHost.Enqueue(ask);
            }
            Assert.True(client.Synced);
            return maxRequested;
        }

        [Fact]
        public void EmptyCacheReceivesEveryImage()
        {
            var (state, files) = Table();
            var host = Host(state, files);
            var store = new MemoryAssetStore();
            var assets = new AssetClient(store);
            var ready = new List<string>();
            assets.Ready += ready.Add;

            var maxRequested = Run(host, "c1", assets);

            Assert.True(assets.Complete);
            Assert.Equal(new[] { "board", "board2", "coin", "empty" }, ready.OrderBy(x => x));
            Assert.Equal(files["img/board.png"], assets.Read("board"));
            Assert.Equal(files["img/board.png"], assets.Read("board2"));
            Assert.Equal(files["img/coin.png"], assets.Read("coin"));
            Assert.Empty(assets.Read("empty")!);
            Assert.Null(assets.Read("deck"));
            Assert.Null(assets.Read("lost"));
            // The duplicate is fetched once: 3 distinct files, about 300 KB.
            Assert.Equal(3, store.Count);
            Assert.Equal(3, assets.Fetched);
            Assert.Equal(300_700, assets.BytesReceived);
            Assert.True(maxRequested <= AssetClient.Window);
            Assert.Contains(host.Assets!.Warnings, w => w.Contains("'lost'"));
        }

        [Fact]
        public void WarmCacheFetchesNothing()
        {
            var (state, files) = Table();
            var host = Host(state, files);
            var store = new MemoryAssetStore();
            Run(host, "c1", new AssetClient(store));

            var second = new AssetClient(store);
            var ready = new List<string>();
            second.Ready += ready.Add;
            Run(host, "c2", second);

            Assert.Equal(0, second.Fetched);
            Assert.Equal(0, second.BytesReceived);
            Assert.Equal(4, second.FromCache);
            Assert.Equal(4, ready.Count);
        }

        [Fact]
        public void CorruptChunksAreFetchedAgain()
        {
            var (state, files) = Table();
            var host = Host(state, files);
            var assets = new AssetClient(new MemoryAssetStore());
            var corrupted = 0;
            JObject Tamper(JObject chunk)
            {
                // Flip one byte in the first transfer of chunk 3 of every file that has one.
                if ((int)chunk["index"]! != 3 || corrupted > 0)
                    return chunk;
                corrupted++;
                var data = Convert.FromBase64String((string)chunk["data"]!);
                data[10] ^= 0xff;
                var copy = (JObject)chunk.DeepClone();
                copy["data"] = Convert.ToBase64String(data);
                return copy;
            }

            Run(host, "c1", assets, Tamper);

            Assert.Equal(1, corrupted);
            Assert.True(assets.Complete);
            Assert.Equal(files["img/board.png"], assets.Read("board"));
        }

        [Fact]
        public void PersistentCorruptionGivesUpAfterMaxAttempts()
        {
            var (state, files) = Table();
            var host = Host(state, files);
            var assets = new AssetClient(new MemoryAssetStore());
            var failed = new List<string>();
            assets.Failed += (id, why) => failed.Add(id);
            JObject Tamper(JObject chunk)
            {
                if ((string)chunk["hash"]! != AssetHash.Of(files["img/coin.png"]))
                    return chunk;
                var copy = (JObject)chunk.DeepClone();
                copy["data"] = Convert.ToBase64String(new byte[700]);
                return copy;
            }

            Run(host, "c1", assets, Tamper);

            Assert.Equal(new[] { "coin" }, failed);
            Assert.True(assets.Complete);
            Assert.Null(assets.Read("coin"));
            Assert.NotNull(assets.Read("board"));
        }

        [Fact]
        public void HostServesOnlyManifestHashes()
        {
            var (state, files) = Table();
            var host = Host(state, files);
            var unknown = new JObject
            {
                ["kind"] = "asset_request",
                ["chunks"] = new JArray(
                    new JObject { ["hash"] = AssetHash.Of(new byte[] { 1, 2, 3 }), ["index"] = 0 },
                    new JObject { ["hash"] = "../../etc/passwd", ["index"] = 0 },
                    new JObject { ["hash"] = AssetHash.Of(files["img/coin.png"]), ["index"] = 1 },
                    new JObject { ["hash"] = AssetHash.Of(files["img/coin.png"]), ["index"] = -1 }),
            };
            Assert.Empty(host.Receive("c1", unknown));

            var flood = new JObject
            {
                ["kind"] = "asset_request",
                ["chunks"] = new JArray(Enumerable.Range(0, 500).Select(_ => new JObject { ["hash"] = AssetHash.Of(files["img/coin.png"]), ["index"] = 0 })),
            };
            Assert.Equal(AssetHost.MaxChunksPerRequest, host.Receive("c1", flood).Count);
        }

        [Fact]
        public void ManifestListsImagesWithFilesOnly()
        {
            var (state, files) = Table();
            var host = Host(state, files);
            var manifest = host.Snapshot("c1").Message["assets"]!.Select(e => (string)e["id"]!).ToList();
            Assert.Equal(new[] { "board", "coin", "empty", "board2" }, manifest);
        }

        [Fact]
        public void DirectoryStoreRoundTripsAndRejectsBadHashes()
        {
            var folder = Path.Combine(Path.GetTempPath(), "tw-assets-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new DirectoryAssetStore(folder);
                var data = Bytes(5000, 3);
                var hash = AssetHash.Of(data);
                Assert.False(store.Has(hash));
                store.Write(hash, data);
                Assert.True(new DirectoryAssetStore(folder).Has(hash));
                Assert.Equal(data, store.Read(hash));
                Assert.False(store.Has("../x"));
                Assert.Throws<ArgumentException>(() => store.Write("sha256:../../x", data));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Theory]
        [InlineData("img/a.png", true)]
        [InlineData("builtin:standard_deck", true)]
        [InlineData("a/b/c.jpg", true)]
        [InlineData("../a.png", false)]
        [InlineData("img/../../a.png", false)]
        [InlineData("/etc/passwd", false)]
        [InlineData("C:/Windows/win.ini", false)]
        [InlineData("\\\\server\\share\\a.png", false)]
        [InlineData("img//a.png", false)]
        public void AssetPathsStayInsideThePackage(string path, bool safe)
        {
            Assert.Equal(safe, Asset.IsSafePath(path));
            var json = $@"{{ ""schemaVersion"": ""0.1"", ""assets"": [ {{ ""id"": ""a"", ""type"": ""image"", ""path"": {Newtonsoft.Json.JsonConvert.ToString(path)} }} ] }}";
            var parsed = TableJson.Parse(json);
            Assert.Equal(safe, !parsed.Errors.Any(e => e.Code == ErrorCodes.InvalidPath));
        }
    }
}
