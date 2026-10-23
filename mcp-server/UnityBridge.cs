using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp
{
    /// <summary>
    /// Local WebSocket link to the game (T4.5). The server listens on 127.0.0.1 with a random port and writes
    /// <c>{port, token, pid}</c> to <see cref="DiscoveryPath"/>; the running game reads it and connects (it reconnects
    /// whenever a new agent session starts). Messages are JSON objects with a <c>type</c>; requests carry an <c>id</c>
    /// echoed by the reply. One game connection at a time; the token rejects stray clients.
    /// </summary>
    public sealed class UnityBridge : BackgroundService
    {
        public static string DiscoveryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableWorld", "bridge.json");

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly ILogger<UnityBridge> _logger;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        private WebSocket? _socket;
        private int _nextId;

        public UnityBridge(ILogger<UnityBridge> logger)
        {
            _logger = logger;
        }

        public bool Connected => _socket?.State == WebSocketState.Open;
        public int Port { get; private set; }

        /// <summary>Raised on the receive thread when the game reports a new state (a human played in the game window).</summary>
        public event Action<JObject>? StateReported;

        /// <summary>Raised when a game connects, so the session can push the current document.</summary>
        public event Action? GameConnected;

        public async Task<JObject?> Request(JObject message)
        {
            if (!Connected)
                return null;
            var id = Interlocked.Increment(ref _nextId).ToString();
            message["id"] = id;
            var reply = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = reply;
            try
            {
                await Send(message);
                var done = await Task.WhenAny(reply.Task, Task.Delay(RequestTimeout));
                return done == reply.Task ? reply.Task.Result : null;
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        public async Task Send(JObject message)
        {
            var socket = _socket;
            if (socket?.State != WebSocketState.Open)
                return;
            var bytes = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
            await _sendLock.WaitAsync();
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            WriteDiscovery();
            _logger.LogInformation("Bridge listening on 127.0.0.1:{Port}", Port);
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stoppingToken);
                    _ = Task.Run(() => Serve(client, stoppingToken), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                listener.Stop();
                TryDeleteDiscovery();
            }
        }

        private async Task Serve(TcpClient client, CancellationToken stoppingToken)
        {
            using (client)
            {
                var stream = client.GetStream();
                var socket = await Handshake(stream);
                if (socket == null)
                    return;
                _socket?.Abort();
                _socket = socket;
                _logger.LogInformation("Game connected");
                GameConnected?.Invoke();
                var buffer = new byte[64 * 1024];
                var message = new MemoryStream();
                try
                {
                    while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                    {
                        var result = await socket.ReceiveAsync(buffer, stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        message.Write(buffer, 0, result.Count);
                        if (!result.EndOfMessage)
                            continue;
                        var text = Encoding.UTF8.GetString(message.ToArray());
                        message.SetLength(0);
                        Dispatch(JObject.Parse(text));
                    }
                }
                catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is IOException)
                {
                }
                finally
                {
                    if (_socket == socket)
                        _socket = null;
                    _logger.LogInformation("Game disconnected");
                }
            }
        }

        private void Dispatch(JObject message)
        {
            var id = (string?)message["id"];
            if (id != null && _pending.TryRemove(id, out var waiting))
            {
                waiting.TrySetResult(message);
                return;
            }
            if ((string?)message["type"] == "state" && message["document"] is JObject document)
                StateReported?.Invoke(document);
        }

        /// <summary>Minimal RFC 6455 server handshake on a raw TCP stream; checks the token in the request path.</summary>
        private async Task<WebSocket?> Handshake(NetworkStream stream)
        {
            var request = new StringBuilder();
            var buffer = new byte[1];
            while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(buffer) == 0 || request.Length > 8192)
                    return null;
                request.Append((char)buffer[0]);
            }
            var lines = request.ToString().Split("\r\n");
            string? key = null;
            foreach (var line in lines)
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    key = line.Substring(18).Trim();
            var tokenOk = lines[0].Contains($"token={_token}", StringComparison.Ordinal);
            if (key == null || !tokenOk)
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"));
                return null;
            }
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n"));
            return WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(20));
        }

        private void WriteDiscovery()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiscoveryPath)!);
            File.WriteAllText(DiscoveryPath, new JObject
            {
                ["port"] = Port,
                ["token"] = _token,
                ["pid"] = Environment.ProcessId,
                ["started"] = DateTime.UtcNow.ToString("o"),
            }.ToString());
        }

        private void TryDeleteDiscovery()
        {
            try
            {
                var current = JObject.Parse(File.ReadAllText(DiscoveryPath));
                if ((int?)current["pid"] == Environment.ProcessId)
                    File.Delete(DiscoveryPath);
            }
            catch (Exception)
            {
            }
        }
    }
}
