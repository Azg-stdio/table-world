using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Core.Tts;

namespace Core.Tests.Tts
{
    /// <summary>M7 T8.2: downloads resume, respect the size cap, and report failures; a stub server, nothing leaves the machine.</summary>
    public class AssetDownloaderTests
    {
        private static readonly byte[] Content = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();

        /// <summary>Serves /a (honouring Range), /big (too large) and 404 for anything else.</summary>
        private sealed class Stub : HttpMessageHandler
        {
            public int Requests;
            public long? LastRangeFrom;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Requests);
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/a")
                {
                    var from = request.Headers.Range?.Ranges.First().From ?? 0;
                    LastRangeFrom = request.Headers.Range?.Ranges.First().From;
                    var body = Content.Skip((int)from).ToArray();
                    var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
                    return Task.FromResult(response);
                }
                if (path == "/big")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5000]) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        [Fact]
        public void RetiredSteamAddresses_AreFetchedFromTheNewHost()
        {
            Assert.Equal("https://steamusercontent-a.akamaihd.net/ugc/123/ABC/",
                Core.Rules.Asset.FetchAddress("http://cloud-3.steamusercontent.com/ugc/123/ABC/"));
            Assert.Equal("https://steamusercontent-a.akamaihd.net/ugc/9/X/",
                Core.Rules.Asset.FetchAddress("http://steamusercontent-a.akamaihd.net/ugc/9/X/"));
            Assert.Equal("https://pastebin.com/raw/abc", Core.Rules.Asset.FetchAddress("https://pastebin.com/raw/abc"));
        }

        [Fact]
        public async Task Downloads_Resume_RespectTheCap_AndReportFailures()
        {
            var folder = Path.Combine(Path.GetTempPath(), "tw-dl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var a = Path.Combine(folder, "images", "a.png");
                Directory.CreateDirectory(Path.GetDirectoryName(a)!);
                File.WriteAllBytes(a + ".part", Content.Take(400).ToArray());
                var already = Path.Combine(folder, "images", "kept.png");
                File.WriteAllBytes(already, new byte[] { 9 });
                var stub = new Stub();
                var downloader = new AssetDownloader { Handler = stub, MaxBytes = 2000 };
                var reports = 0;
                var result = await downloader.Download(new[]
                {
                    ("http://cdn.test/a", a),
                    ("http://cdn.test/big", Path.Combine(folder, "big.png")),
                    ("http://cdn.test/missing", Path.Combine(folder, "missing.png")),
                    ("ftp://cdn.test/a", Path.Combine(folder, "ftp.png")),
                    ("http://cdn.test/a", already),
                }, new Progress<(int, int)>(_ => Interlocked.Increment(ref reports)));

                Assert.Equal(Content, File.ReadAllBytes(a));
                Assert.False(File.Exists(a + ".part"));
                Assert.Equal(400, stub.LastRangeFrom);
                Assert.Equal(1, result.Downloaded);
                Assert.Equal(600, result.Bytes);
                Assert.Equal(3, result.Failed.Count);
                Assert.Contains(result.Failed, f => f.Url.EndsWith("big") && f.Reason.Contains("larger"));
                Assert.Contains(result.Failed, f => f.Url.EndsWith("missing") && f.Reason.Contains("404"));
                Assert.Contains(result.Failed, f => f.Url.StartsWith("ftp") && f.Reason.Contains("web address"));
                Assert.False(File.Exists(Path.Combine(folder, "big.png")));
                Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(already));
                Assert.Equal(3, stub.Requests);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
