using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TableWorld.View.Bridge
{
    /// <summary>
    /// Connects the game window to an agent's MCP server (T4.5). The server writes <c>{port, token}</c> to
    /// <c>%LOCALAPPDATA%/TableWorld/bridge.json</c>; this client watches that file and (re)connects to
    /// <c>ws://127.0.0.1:port/?token=…</c>. The game is the authoritative host: the server pushes tables (<c>load</c>) and
    /// sends play commands (<c>command</c>); every change made here (HUD, physics) is reported back as <c>state</c>.
    /// Socket work happens on background tasks; messages are handled on the main thread in <see cref="Update"/>.
    /// </summary>
    public sealed class BridgeClient : MonoBehaviour
    {
        public const float PollSeconds = 2f;
        public const float ReportDelaySeconds = 0.5f;

        [SerializeField] private TableView _table;

        private readonly ConcurrentQueue<JObject> _inbox = new ConcurrentQueue<JObject>();
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancel;
        private string _endpoint;
        private float _nextPoll;
        private float _reportAt = -1f;
        private bool _executingRemote;

        public bool Connected => _socket?.State == WebSocketState.Open;

        public static string DiscoveryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableWorld", "bridge.json");

        public void Configure(TableView table) => _table = table;

        private void OnEnable()
        {
            if (_table != null)
                _table.CommandExecuted += OnCommand;
        }

        private void OnDisable()
        {
            if (_table != null)
                _table.CommandExecuted -= OnCommand;
            Disconnect();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                PollDiscovery();
            }
            while (_inbox.TryDequeue(out var message))
                Handle(message);
            if (_reportAt >= 0f && Time.unscaledTime >= _reportAt && Connected)
            {
                _reportAt = -1f;
                Send(new JObject { ["type"] = "state", ["document"] = Document() });
            }
        }

        // ---- connection -------------------------------------------------------------------------------------------

        private void PollDiscovery()
        {
            string text;
            try
            {
                if (!File.Exists(DiscoveryPath))
                    return;
                text = File.ReadAllText(DiscoveryPath);
            }
            catch (IOException)
            {
                return;
            }
            JObject info;
            try
            {
                info = JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return;
            }
            var endpoint = $"ws://127.0.0.1:{(int)info["port"]}/?token={(string)info["token"]}";
            if (Connected && endpoint == _endpoint)
                return;
            Disconnect();
            _endpoint = endpoint;
            _ = Connect(endpoint);
        }

        private async Task Connect(string endpoint)
        {
            var socket = new ClientWebSocket();
            var cancel = new CancellationTokenSource();
            _socket = socket;
            _cancel = cancel;
            try
            {
                await socket.ConnectAsync(new Uri(endpoint), cancel.Token);
                Debug.Log("[Bridge] Connected to the agent's MCP server.");
                await Receive(socket, cancel.Token);
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is IOException)
            {
                // The server is gone (agent session ended) or not ready yet; the next poll retries.
            }
            finally
            {
                if (_socket == socket)
                {
                    _socket = null;
                    _endpoint = null;
                }
                socket.Dispose();
            }
        }

        private async Task Receive(ClientWebSocket socket, CancellationToken cancel)
        {
            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer.Array, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;
                _inbox.Enqueue(JObject.Parse(Encoding.UTF8.GetString(message.ToArray())));
                message.SetLength(0);
            }
        }

        private void Send(JObject message)
        {
            var socket = _socket;
            if (socket?.State != WebSocketState.Open)
                return;
            var bytes = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
            // ClientWebSocket allows one send at a time; messages are small and sent from the main thread only.
            socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(2000);
        }

        private void Disconnect()
        {
            _cancel?.Cancel();
            _cancel = null;
        }

        // ---- messages ---------------------------------------------------------------------------------------------

        private void Handle(JObject message)
        {
            switch ((string)message["type"])
            {
                case "load" when message["document"] is JObject document:
                    _executingRemote = true;
                    try
                    {
                        _table.Load(document.ToString(), packageRoot: null);
                    }
                    finally
                    {
                        _executingRemote = false;
                    }
                    break;
                case "command" when message["command"] is JObject command:
                    Send(Execute(command, (string)message["id"]));
                    break;
            }
        }

        private JObject Execute(JObject command, string id)
        {
            Command engineCommand;
            switch ((string)command["kind"])
            {
                case "start":
                    engineCommand = new StartGameCommand();
                    break;
                case "perform":
                    var perform = new PerformActionCommand { Actor = (string)command["seat"], ActionId = (string)command["action"] };
                    if (command["params"] is JObject parameters)
                        foreach (var p in parameters.Properties())
                            perform.Params[p.Name] = p.Value.ToString();
                    engineCommand = perform;
                    break;
                case "deal":
                    var deal = new DealCommand { DeckId = (string)command["deck"], Count = (int?)command["count"] ?? 1 };
                    if (command["seats"] is JArray seats)
                        deal.Seats.AddRange(seats.Select(s => (string)s));
                    engineCommand = deal;
                    break;
                default:
                    return Reply(id, false, new[] { new ValidationError("kind", ErrorCodes.UnknownCommand, $"Unknown command '{command["kind"]}'.") }, new List<string>());
            }

            _executingRemote = true;
            CommandResult result;
            try
            {
                result = _table.Execute(engineCommand);
            }
            finally
            {
                _executingRemote = false;
            }
            var messages = result.Events.OfType<GameMessage>().Select(m => m.Text).ToList();
            return Reply(id, result.Ok, result.Ok ? new ValidationError[0] : new[] { result.Error }, messages);
        }

        private JObject Reply(string id, bool ok, IEnumerable<ValidationError> errors, List<string> messages) => new JObject
        {
            ["type"] = "result",
            ["id"] = id,
            ["ok"] = ok,
            ["errors"] = new JArray(errors.Select(e => new JObject { ["path"] = e.Path, ["code"] = e.Code, ["message"] = e.Message, ["suggestion"] = e.Suggestion })),
            ["messages"] = new JArray(messages),
            ["document"] = Document(),
        };

        private JObject Document() => JObject.Parse(TableJson.Serialize(_table.Engine.State, indented: false));

        /// <summary>Changes made in this window (HUD, physics) are reported shortly after, batched.</summary>
        private void OnCommand(Command command, CommandResult result)
        {
            if (_executingRemote || !result.Ok || !Connected)
                return;
            if (_reportAt < 0f)
                _reportAt = Time.unscaledTime + ReportDelaySeconds;
        }
    }
}
