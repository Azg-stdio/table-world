using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Tts
{
    /// <summary>
    /// Fetches the files an imported table names but the player's TTS cache lacks (M7 T8.2), the way TTS itself fetches
    /// them when a game loads: only the URLs the save holds (most on Steam's CDN), never the Workshop itself. A few at a
    /// time, each capped in size, and resumed from where an interrupted download stopped (<c>.part</c> files).
    /// </summary>
    public sealed class AssetDownloader
    {
        public int Concurrency { get; set; } = 4;
        public long MaxBytes { get; set; } = 300L * 1024 * 1024;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);
        /// <summary>The HTTP handler (tests give their own; null is the system's).</summary>
        public HttpMessageHandler? Handler { get; set; }

        public sealed class Result
        {
            public int Downloaded;
            public long Bytes;
            /// <summary>URL and why it failed.</summary>
            public List<(string Url, string Reason)> Failed = new List<(string, string)>();
        }

        /// <summary>
        /// Downloads each (url, target file) pair whose target does not exist yet. <paramref name="progress"/> hears
        /// (finished, total) after each file.
        /// </summary>
        public async Task<Result> Download(IReadOnlyList<(string Url, string Target)> files, IProgress<(int Done, int Total)>? progress = null,
            CancellationToken cancel = default)
        {
            var result = new Result();
            var todo = files.Where(f => !File.Exists(f.Target)).ToList();
            using var client = Handler != null ? new HttpClient(Handler, disposeHandler: false) : new HttpClient();
            client.Timeout = Timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TableWorld/0.1");
            var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
            var done = 0;
            var tasks = todo.Select(async file =>
            {
                await gate.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    var bytes = await One(client, file.Url, file.Target, cancel).ConfigureAwait(false);
                    lock (result)
                    {
                        result.Downloaded++;
                        result.Bytes += bytes;
                    }
                }
                catch (Exception e) when (!(e is OperationCanceledException) || !cancel.IsCancellationRequested)
                {
                    lock (result)
                        result.Failed.Add((file.Url, e is TaskCanceledException ? "timed out" : e.Message));
                }
                finally
                {
                    gate.Release();
                    progress?.Report((Interlocked.Increment(ref done), todo.Count));
                }
            }).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return result;
        }

        private async Task<long> One(HttpClient client, string url, string target, CancellationToken cancel)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("not a web address");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var part = target + ".part";
            var have = File.Exists(part) ? new FileInfo(part).Length : 0L;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (have > 0)
                request.Headers.Range = new RangeHeaderValue(have, null);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0)
            {
                // The part file already holds everything.
                Finish(part, target);
                return 0;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"the server answered {(int)response.StatusCode}");
            var resumed = response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed)
                have = 0;
            if (response.Content.Headers.ContentLength is { } length && have + length > MaxBytes)
                throw new InvalidOperationException($"larger than {MaxBytes / (1024 * 1024)} MB");
            long read = 0;
            using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var sink = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                {
                    read += n;
                    if (have + read > MaxBytes)
                        throw new InvalidOperationException($"larger than {MaxBytes / (1024 * 1024)} MB");
                    await sink.WriteAsync(buffer, 0, n, cancel).ConfigureAwait(false);
                }
            }
            Finish(part, target);
            return read;
        }

        private static void Finish(string part, string target)
        {
            if (File.Exists(target))
                File.Delete(target);
            File.Move(part, target);
        }
    }
}
