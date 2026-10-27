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
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TableWorld.View.Bridge
{
    /// <summary>
    /// Connects the game window to an agent's MCP server (T4.5). The server writes <c>{port, token}</c> to
    /// <c>%LOCALAPPDATA%/TableWorld/bridge.json</c>; this client watches that file and (re)connects to
    /// <c>ws://127.0.0.1:port/?token=…</c>. The game is the authoritative host: the server pushes tables (<c>load</c>) and
    /// sends play commands (<c>command</c>); a change made here (HUD, physics) is announced as a small <c>changed</c>, and
    /// the server asks for the table (<c>get_state</c>) only when a tool needs it: serializing a big imported table on
    /// every change stalled the game. Socket work, sends included, happens on background tasks; messages are handled on
    /// the main thread in <see cref="Update"/>.
    /// </summary>
    public sealed class BridgeClient : MonoBehaviour
    {
        public const float PollSeconds = 2f;
        public const float ReportDelaySeconds = 0.5f;

        [SerializeField] private TableView _table;

        private readonly ConcurrentQueue<JObject> _inbox = new ConcurrentQueue<JObject>();
        private readonly ConcurrentQueue<string> _outbox = new ConcurrentQueue<string>();
        private readonly SemaphoreSlim _outboxReady = new SemaphoreSlim(0);
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

        /// <summary>
        /// Off while PlayMode tests run: an agent's MCP server running elsewhere on this machine would otherwise connect
        /// and load its own table over the test's. Back on at every Play start, so a cancelled run cannot leave it off.
        /// </summary>
        public static bool Disabled { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay() => Disabled = false;

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
                Send(new JObject { ["type"] = "changed" });
            }
        }

        // ---- connection -------------------------------------------------------------------------------------------

        private void PollDiscovery()
        {
            if (Disabled)
            {
                if (_socket != null)
                    Disconnect();
                return;
            }
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
                while (_outbox.TryDequeue(out _))
                {
                }
                _ = SendLoop(socket, cancel.Token);
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

        /// <summary>Queues a message; <see cref="SendLoop"/> sends it, so the main thread never waits for the socket.</summary>
        private void Send(JObject message)
        {
            if (_socket?.State != WebSocketState.Open)
                return;
            _outbox.Enqueue(message.ToString(Newtonsoft.Json.Formatting.None));
            _outboxReady.Release();
        }

        /// <summary>Sends queued messages one at a time (ClientWebSocket allows one send at a time) until the socket closes.</summary>
        private async Task SendLoop(ClientWebSocket socket, CancellationToken cancel)
        {
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    await _outboxReady.WaitAsync(cancel);
                    if (_outbox.TryDequeue(out var text))
                        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, cancel);
                }
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is IOException || e is ObjectDisposedException)
            {
                // Closed: Connect cleans up.
            }
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
                        // The agent only saw this seat's view: what it could not see stays as it is (ADR 0012).
                        var text = document.ToString();
                        // Unknown cards (a seat's view of a table with secret cards) too, before the game starts as well.
                        if (_table.Engine != null && TableJson.Parse(text) is { State: { } incoming, Structural: false })
                        {
                            SeatView.Restore(incoming, _table.Engine.State);
                            text = TableJson.Serialize(incoming, indented: false);
                        }
                        _table.Load(text, packageRoot: (string)message["root"]);
                    }
                    finally
                    {
                        _executingRemote = false;
                    }
                    break;
                case "command" when message["command"] is JObject command:
                    JObject reply;
                    try
                    {
                        reply = Execute(command, (string)message["id"]);
                    }
                    catch (Exception e)
                    {
                        // Never leave the agent waiting for a timeout: report the failure.
                        Debug.LogException(e);
                        reply = Reply((string)message["id"], false, new[] { new ValidationError("command", ErrorCodes.RuleFailed, e.Message) }, new List<string>());
                    }
                    Send(reply);
                    break;
                case "get_state":
                    Send(new JObject { ["id"] = (string)message["id"], ["type"] = "state", ["document"] = Document() });
                    break;
                case "screenshot":
                    StartCoroutine(Screenshot(message));
                    break;
            }
        }

        /// <summary>
        /// The agent's eyes: frames a view (a seat's, the whole table from above, one object up close, or what is on screen
        /// now), captures the game window with its HUD, and replies with a JPEG. The camera goes back where it was.
        /// </summary>
        private System.Collections.IEnumerator Screenshot(JObject message)
        {
            var id = (string)message["id"];
            var view = (string)message["view"] ?? "seat";
            var width = Mathf.Clamp((int?)message["width"] ?? 1280, 320, 2560);
            var camera = Camera.main != null ? Camera.main.GetComponent<TableCamera>() : null;
            var before = camera != null ? camera.Current : default;
            string problem = null;
            if (camera != null && view != "current")
            {
                var size = TableGeometry.Size;
                var reach = Mathf.Max(size.x, size.y);
                switch (view)
                {
                    case "top":
                        camera.SetView(new TableCamera.View { Pivot = Vector3.zero, Yaw = 0f, Pitch = 89f, Distance = reach * 1.1f + 0.3f });
                        break;
                    case "object":
                        var target = (string)message["target"];
                        var objectView = target != null ? _table.ViewOf(target) : null;
                        if (objectView == null)
                        {
                            problem = $"No object '{target}' on the table.";
                            break;
                        }
                        var bounds = objectView.Renderer != null ? objectView.Renderer.bounds : new Bounds(objectView.transform.position, Vector3.one * 0.1f);
                        camera.SetView(new TableCamera.View { Pivot = bounds.center, Yaw = before.Yaw, Pitch = 70f, Distance = Mathf.Max(bounds.size.x, bounds.size.z) * 1.6f + 0.12f });
                        break;
                    default:
                        var state = _table.Engine.State;
                        var seatId = (string)message["seat"] ?? _table.LocalSeat ?? state.Players.Seats.FirstOrDefault()?.Id;
                        var seat = state.Players.Seats.FirstOrDefault(s => s.Id == seatId);
                        var hand = seat == null ? null : state.Zones.FirstOrDefault(z => z.Id == seat.HandZone);
                        if (seatId != null && seat == null)
                        {
                            problem = $"No seat '{seatId}'.";
                            break;
                        }
                        // Behind the seat's hand, looking across the table, as the player sits.
                        var yaw = hand != null ? hand.Transform.Rotation.Y : 0f;
                        camera.SetView(new TableCamera.View { Pivot = Vector3.zero, Yaw = yaw, Pitch = 52f, Distance = reach * 0.95f + 0.45f });
                        break;
                }
            }
            if (problem != null)
            {
                Send(new JObject { ["id"] = id, ["type"] = "screenshot", ["ok"] = false, ["error"] = problem });
                yield break;
            }
            // Two frames for the view (and anything the move streams in) to draw, then the frame with its HUD.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (camera != null && view != "current")
                camera.SetView(before);
            var height = Mathf.RoundToInt(width * (float)shot.height / Mathf.Max(1, shot.width));
            var scaled = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(shot, scaled);
            var previous = RenderTexture.active;
            RenderTexture.active = scaled;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(scaled);
            var jpeg = image.EncodeToJPG(82);
            Destroy(shot);
            Destroy(image);
            Send(new JObject
            {
                ["id"] = id, ["type"] = "screenshot", ["ok"] = true, ["width"] = width, ["height"] = height,
                ["image"] = Convert.ToBase64String(jpeg),
            });
        }

        private JObject Execute(JObject command, string id)
        {
            Command engineCommand;
            switch ((string)command["kind"])
            {
                case "start":
                    var start = new StartGameCommand();
                    if (command["seats"] is JArray startSeats)
                        start.Seats.AddRange(startSeats.Select(s => (string)s));
                    if (command["roles"] is JArray startRoles)
                        start.Roles.AddRange(startRoles.Select(r => (string)r));
                    engineCommand = start;
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
                case "table":
                    // A physical command, by its core type name (e.g. "CutCommand").
                    var type = typeof(Command).Assembly.GetType("Core.Engine." + (string)command["type"]);
                    if (type == null || !typeof(Command).IsAssignableFrom(type) || !(command["command"] is JObject fields))
                        return Reply(id, false, new[] { new ValidationError("type", ErrorCodes.UnknownCommand, $"Unknown table command '{command["type"]}'.") }, new List<string>());
                    engineCommand = (Command)fields.ToObject(type);
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
            // The shared lines, and the private ones of the seat this window plays (all of them for a host without one).
            var viewer = Viewer;
            var messages = result.Events.Select(e => e is GameMessage m ? m.Text : e is PrivateMessage p && (Narrating || p.Seat == viewer) ? p.Text : null)
                .Where(t => t != null).ToList();
            var reply = Reply(id, result.Ok, result.Ok ? new ValidationError[0] : new[] { result.Error }, messages);
            var created = result.CreatedIds ?? (result.CreatedId != null ? new[] { result.CreatedId } : new string[0]);
            reply["created"] = new JArray(created);
            return reply;
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

        /// <summary>
        /// The table as JSON, written into the message as it is (not parsed back into a JObject first). The agent plays
        /// for this window's player: in a session with others it reads that seat's view (ADR 0012, T11.2b), the whole
        /// table only alone or as a host without a seat (the narrator).
        /// </summary>
        private JToken Document() => new JRaw(Narrating
            ? TableJson.Serialize(_table.Engine.State, indented: false)
            : SeatView.Serialize(_table.Engine.State, Viewer));

        private string Viewer => _table.Spectating ? null : _table.LocalSeat;

        /// <summary>
        /// The whole table is for playing alone (a designer trying the game) and for a host who sits at no seat (the
        /// narrator); anyone seated in a session with others sees their seat's view.
        /// </summary>
        private bool Narrating => !_table.Networked || _table.HostsTable && Viewer == null;

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
