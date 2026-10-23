using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Networked table (T3.2, ADR 0002): Netcode for GameObjects is only a pipe for the core protocol in
    /// <see cref="SessionHost"/>/<see cref="SessionClient"/>. The host runs the authoritative engine; each client
    /// shows its prediction and sends intents. One named message, JSON payloads, reliable fragmented sequenced delivery.
    /// Seats: the host plays the first seat, each client the next free one. Images that are files in the host's game
    /// package travel to clients with <see cref="AssetHost"/>/<see cref="AssetClient"/> into a local cache (T3.4).
    /// </summary>
    public sealed partial class NetworkTableSession : MonoBehaviour
    {
        public const string MessageName = "tw";
        public const ushort DefaultPort = 7777;
        /// <summary>The host's participant id, as the HUD sees it in <see cref="Occupants"/>.</summary>
        public const string HostIdForUi = SessionHost.HostId;
        public const float TouchSeconds = 4f;

        [SerializeField] private TableView _table;

        private NetworkManager _network;
        private SessionHost _host;
        private SessionClient _client;
        private AssetClient _assets;
        private readonly Dictionary<ulong, string> _seatOf = new Dictionary<ulong, string>();
        /// <summary>Host only: who sits where, the host included (G10).</summary>
        private readonly Seating _seating = new Seating();
        private Dictionary<string, string> _occupants = new Dictionary<string, string>();

        /// <summary>Seat id → who sits there (<see cref="SessionHost.HostId"/> or a client id), as last known on this machine.</summary>
        public IReadOnlyDictionary<string, string> Occupants => IsHost ? Seating.Read(_seating.Message()) : _occupants;

        /// <summary>This machine's participant id in <see cref="Occupants"/>.</summary>
        public string LocalParticipant => IsHost ? SessionHost.HostId : _network != null ? _network.LocalClientId.ToString() : null;
        private readonly Dictionary<string, float> _touched = new Dictionary<string, float>();

        public bool IsHost => _host != null;
        public bool IsClient => _client != null;
        public bool Synced => IsHost || (_client?.Synced ?? false);
        public SessionHost Host => _host;
        public SessionClient Client => _client;
        public AssetClient Assets => _assets;

        /// <summary>Seconds from the snapshot to the last image arriving; negative until then.</summary>
        public float AssetSeconds { get; private set; } = -1f;

        private float _snapshotAt;
        private readonly Queue<float> _sentAt = new Queue<float>();

        /// <summary>Round trips of this client's commands (sent → ordered by the host), in seconds, and whether images were still downloading.</summary>
        public List<(float Seconds, bool Downloading)> RoundTrips { get; } = new List<(float, bool)>();

        /// <summary>Content-addressed cache for assets received from hosts. <c>-tw-cache</c> overrides it (tests).</summary>
        public static string CacheFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableWorld", "cache", "assets");
        public string Status { get; private set; } = "offline";

        /// <summary>Players connected to this host (not counting the host).</summary>
        public int JoinedCount => _seatOf.Count;

        public void Configure(TableView table) => _table = table;

        private void OnEnable()
        {
            if (_table == null)
                return;
            _table.Touched += Touch;
            _table.Loaded += OnTableLoaded;
            _table.CommandExecuted += OnLocalCommand;
        }

        private void OnDisable()
        {
            if (_table == null)
                return;
            _table.Touched -= Touch;
            _table.Loaded -= OnTableLoaded;
            _table.CommandExecuted -= OnLocalCommand;
        }

        /// <summary>Starts hosting the current table on <paramref name="port"/> (all interfaces).</summary>
        public bool StartHost(ushort port = DefaultPort)
        {
            Stop();
            EnsureNetwork().SetConnectionData("127.0.0.1", port, "0.0.0.0");
            return LaunchHost($"hosting on port {port}");
        }

        /// <summary>Joins a host at <paramref name="address"/>:<paramref name="port"/>; the table appears when the snapshot arrives.</summary>
        public bool StartClient(string address, ushort port = DefaultPort)
        {
            Stop();
            EnsureNetwork().SetConnectionData(address, port);
            return LaunchClient($"connecting to {address}:{port}");
        }

        // ---- join codes over Relay (T3.3) -------------------------------------------------------------------------

        private ISession _relay;
        private RelayLobby.Handler _relayHandler;

        /// <summary>The code friends type to join, while hosting over Relay.</summary>
        public string JoinCode { get; private set; }

        /// <summary>Hosts over Relay and shows a join code. Returns false (with the reason in <see cref="Status"/>) on failure.</summary>
        public async Task<bool> HostWithCodeAsync()
        {
            Stop();
            Status = "creating a join code…";
            var handler = NewRelayHandler(host: true);
            try
            {
                var session = await RelayLobby.HostAsync(handler, Mathf.Max(2, _table.Engine.State.Players.Seats.Count));
                _relay = session;
                JoinCode = session.Code;
                Status = HostStatus();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                handler.Detached = true;
                ShutdownNetwork();
                Status = "could not host online: " + RelayLobby.Explain(e);
                return false;
            }
        }

        /// <summary>Joins a Relay host by its code.</summary>
        public async Task<bool> JoinWithCodeAsync(string code)
        {
            Stop();
            Status = $"joining {code.Trim().ToUpperInvariant()}…";
            var handler = NewRelayHandler(host: false);
            try
            {
                _relay = await RelayLobby.JoinAsync(code, handler);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                handler.Detached = true;
                ShutdownNetwork();
                Status = "could not join: " + RelayLobby.Explain(e);
                return false;
            }
        }

        private RelayLobby.Handler NewRelayHandler(bool host)
        {
            _relayHandler = new RelayLobby.Handler(configuration =>
            {
                EnsureNetwork().SetRelayServerData(configuration.RelayServerData);
                return host ? LaunchHost("hosting online") : LaunchClient("connecting through Relay");
            }, ShutdownNetwork);
            return _relayHandler;
        }

        private string HostStatus() =>
            (JoinCode != null ? $"code {JoinCode}, " : "hosting, ") + $"{_seatOf.Count} player(s) joined";

        // ---- start / stop -----------------------------------------------------------------------------------------

        private bool LaunchHost(string status)
        {
            ResetChat();
            NewHost();
            _table.Router = HostRoute;
            _seating.Join(SessionHost.HostId, SeatIds());
            _table.SeatOverride = _seating.Seat(SessionHost.HostId);
            _table.Spectating = _table.SeatOverride == null;
            // The host simulates every object, except those a client is (or just was) carrying: that client reports them.
            _table.ReportsPoseOf = view => !RemoteHeldRecently(view.Id);
            _network.OnClientConnectedCallback += OnHostClientConnected;
            _network.OnClientDisconnectCallback += OnHostClientDisconnected;
            if (!_network.StartHost())
            {
                ShutdownNetwork();
                return false;
            }
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnHostMessage);
            RegisterPresence(host: true);
            Status = status;
            return true;
        }

        private bool LaunchClient(string status)
        {
            ResetChat();
            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            if (!_network.StartClient())
            {
                ShutdownNetwork();
                return false;
            }
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnClientMessage);
            RegisterPresence(host: false);
            Status = status;
            return true;
        }

        /// <summary>Leaves the session (and its Relay session, if any) and goes back to local play.</summary>
        public void Stop()
        {
            var relay = _relay;
            _relay = null;
            JoinCode = null;
            if (_relayHandler != null)
                _relayHandler.Detached = true;
            _relayHandler = null;
            ShutdownNetwork();
            if (relay != null)
                _ = LeaveQuietly(relay);
        }

        private static async Task LeaveQuietly(ISession session)
        {
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] Leaving the online session failed: " + e.Message);
            }
        }

        private void ShutdownNetwork()
        {
            if (_network != null)
            {
                _network.OnClientConnectedCallback -= OnHostClientConnected;
                _network.OnClientDisconnectCallback -= OnHostClientDisconnected;
                _network.OnClientConnectedCallback -= OnClientConnected;
                _network.OnClientDisconnectCallback -= OnClientDisconnected;
                if (_network.CustomMessagingManager != null)
                    _network.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
                UnregisterPresence();
                if (_network.IsListening)
                    _network.Shutdown();
            }
            _host = null;
            _client = null;
            _assets = null;
            if (_table != null)
                _table.FetchAsset = null;
            _seatOf.Clear();
            foreach (var participant in _seating.SeatOf.Keys.ToList())
                _seating.Leave(participant);
            _occupants = new Dictionary<string, string>();
            if (_table != null)
            {
                _table.Spectating = false;
                _table.Router = null;
                _table.SeatOverride = null;
                _table.ReportsPoseOf = null;
                _table.HostsTable = true;
            }
            Status = "offline";
        }

        /// <summary>Called by the interaction when the local player touches an object: a client reports its physics for a while.</summary>
        public void Touch(string objectId) => _touched[objectId] = Time.unscaledTime;

        private void OnDestroy() => Stop();

        // ---- host -------------------------------------------------------------------------------------------------

        private void NewHost()
        {
            _host = new SessionHost(_table.Game) { Assets = new AssetHost(asset => _table.Assets?.ReadPackageFile(asset)) };
            _host.Assets.Build(_table.Engine.State);
            foreach (var warning in _host.Assets.Warnings)
                Debug.LogWarning("[Net] " + warning);
            _host.Executed += (origin, command, result) =>
            {
                // The host's own commands are shown by TableView.Execute; a client's command must be shown here.
                if (origin != SessionHost.HostId)
                    _table.ApplyRemote(command, result);
            };
            _host.Executed += OnHostExecuted;
        }

        /// <summary>The host loaded another table: start a new order and send everyone the new table and its assets.</summary>
        private void OnTableLoaded()
        {
            if (_host == null || _host.Game == _table.Game)
                return;
            NewHost();
            // Seats that the new table lacks are left; their players watch until they pick another.
            _seating.Keep(SeatIds());
            if (_table.SeatOverride != null && _seating.Seat(SessionHost.HostId) == null)
                _seating.Join(SessionHost.HostId, SeatIds());
            _table.SeatOverride = _seating.Seat(SessionHost.HostId);
            _table.Spectating = _table.SeatOverride == null;
            foreach (var client in _seatOf.Keys.ToList())
            {
                _seatOf[client] = _seating.Seat(client.ToString());
                if (_seatOf[client] != null)
                    _host.Seats[client.ToString()] = _seatOf[client];
                else
                    _host.Watching.Add(client.ToString());
                var snapshot = _host.Snapshot(client.ToString());
                snapshot.Message["seat"] = _seatOf[client];
                Send(snapshot);
            }
            BroadcastSeating();
        }

        private CommandResult HostRoute(Command command)
        {
            var (result, broadcast) = _host.ExecuteLocal(command);
            if (result.Ok)
                Send(broadcast);
            return result;
        }

        private void OnHostClientConnected(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId)
                return;
            var seat = _seating.Join(clientId.ToString(), SeatIds());
            _seatOf[clientId] = seat;
            if (seat != null)
                _host.Seats[clientId.ToString()] = seat;
            else
                _host.Watching.Add(clientId.ToString());
            var snapshot = _host.Snapshot(clientId.ToString());
            snapshot.Message["seat"] = seat;
            Send(snapshot);
            SendTo(clientId, Room.History());
            Announce(seat != null ? $"{seat} joined" : "A spectator joined");
            BroadcastSeating();
            Status = HostStatus();
        }

        private IEnumerable<string> SeatIds() => _table.Engine.State.Players.Seats.Select(s => s.Id);

        private void BroadcastSeating() => Send(new Outgoing(null, _seating.Message()));

        /// <summary>
        /// Sit at <paramref name="seat"/>, or watch (null) (G10). The host decides: a seat someone else holds is refused.
        /// Solo, it just picks which seat this machine plays.
        /// </summary>
        public void RequestSeat(string seat)
        {
            if (IsHost)
                Sit(SessionHost.HostId, seat);
            else if (IsClient)
                SendTo(NetworkManager.ServerClientId, new JObject { ["kind"] = "sit", ["seat"] = seat });
        }

        private void Sit(string participant, string seat)
        {
            var before = _seating.Seat(participant);
            var who = before ?? (participant == SessionHost.HostId ? "The host" : "A spectator");
            if (!_seating.Sit(participant, seat, SeatIds(), out var error))
            {
                if (participant == SessionHost.HostId)
                    _table.Tell(error);
                else if (ulong.TryParse(participant, out var refused))
                    SendTo(refused, new JObject { ["kind"] = "seat", ["seat"] = before, ["error"] = error });
                return;
            }
            if (before != null && before != seat)
                _presence.Remove(before);
            if (participant == SessionHost.HostId)
            {
                _table.SeatOverride = seat;
                _table.Spectating = seat == null;
            }
            else if (ulong.TryParse(participant, out var client))
            {
                _seatOf[client] = seat;
                if (seat != null)
                    _host.Seats[participant] = seat;
                else
                    _host.Seats.Remove(participant);
                if (seat == null)
                    _host.Watching.Add(participant);
                else
                    _host.Watching.Remove(participant);
                SendTo(client, new JObject { ["kind"] = "seat", ["seat"] = seat });
            }
            Debug.Log($"[Net] {participant} sits at {seat ?? "no seat (watching)"}");
            if (before != seat)
                Announce(seat == null ? $"{who} is watching" : before == null ? $"{who} sat at {seat}" : $"{who} moved to {seat}");
            BroadcastSeating();
        }

        private void OnHostClientDisconnected(ulong clientId)
        {
            if (_seatOf.TryGetValue(clientId, out var leaving) && leaving != null)
                _presence.Remove(leaving);
            if (_host != null && _seatOf.ContainsKey(clientId))
                Announce(leaving != null ? $"{leaving} left" : "A spectator left");
            _room?.Forget(clientId.ToString());
            _seatOf.Remove(clientId);
            _seating.Leave(clientId.ToString());
            _host?.Watching.Remove(clientId.ToString());
            _host?.Seats.Remove(clientId.ToString());
            if (_host != null)
                BroadcastSeating();
            Status = HostStatus();
        }

        private void OnHostMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            var parsed = JObject.Parse(text);
            if ((string)parsed["kind"] == "sit")
            {
                Sit(sender.ToString(), (string)parsed["seat"]);
                return;
            }
            if ((string)parsed["kind"] == "say")
            {
                OnHostSay(sender, parsed);
                return;
            }
            foreach (var message in _host.Receive(sender.ToString(), parsed))
                Send(message);
        }

        // ---- client -----------------------------------------------------------------------------------------------

        private void OnClientConnected(ulong clientId)
        {
            if (clientId != _network.LocalClientId)
                return;
            _client = new SessionClient(clientId.ToString());
            _assets = new AssetClient(new DirectoryAssetStore(CacheFolder));
            _assets.Ready += id =>
            {
                _table.RefreshAsset(id);
                if (_assets.Complete && AssetSeconds < 0f)
                    AssetSeconds = Time.realtimeSinceStartup - _snapshotAt;
            };
            _assets.Failed += (id, why) => Debug.LogWarning($"[Net] Asset '{id}' could not be received: {why}");
            _table.FetchAsset = asset => _assets?.Read(asset.Id);
            _client.PredictionReplaced += game => _table.Adopt(game);
            _client.Applied += (command, result) => _table.ApplyRemote(command, result);
            _client.Ordered += OnClientOrdered;
            _client.Refused += error => Debug.LogWarning($"[Net] The host refused a command: {error}");
            _table.Router = ClientRoute;
            _table.HostsTable = false;
            _table.ReportsPoseOf = view => _touched.TryGetValue(view.Id, out var at) && Time.unscaledTime - at < TouchSeconds && !RemoteHeldRecently(view.Id);
            Status = "connected, waiting for the table";
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (clientId == _network.LocalClientId || clientId == NetworkManager.ServerClientId)
            {
                Status = "disconnected";
                _table.Router = null;
            }
        }

        private CommandResult ClientRoute(Command command)
        {
            if (command is MoveCommand move)
                Touch(move.ObjectId);
            var (result, intent) = _client.Submit(command);
            if (intent != null)
            {
                _sentAt.Enqueue(Time.realtimeSinceStartup);
                SendTo(NetworkManager.ServerClientId, intent);
            }
            return result;
        }

        private void OnClientMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            var message = JObject.Parse(text);
            // The host answers this client's intents in send order, with an order (accepted or refused) whose origin is us.
            var kind = (string)message["kind"];
            if (_sentAt.Count > 0 && kind == "order" && (string)message["origin"] == _network.LocalClientId.ToString())
                RoundTrips.Add((Time.realtimeSinceStartup - _sentAt.Dequeue(), !_assets.Complete));
            if (kind == "seat")
            {
                _table.SeatOverride = (string)message["seat"];
                _table.Spectating = _table.SeatOverride == null;
                if ((string)message["error"] is { } error)
                    _table.Tell(error);
                return;
            }
            if (OnClientChat(kind, message))
                return;
            if (kind == "seating")
            {
                _occupants = Seating.Read(message);
                return;
            }
            if (kind == "snapshot")
            {
                _table.SeatOverride = (string)message["seat"];
                _table.Spectating = _table.SeatOverride == null;
                _snapshotAt = Time.realtimeSinceStartup;
                AssetSeconds = -1f;
            }
            // The manifest first, so the snapshot's views already find cached images.
            var request = _assets.Receive(message);
            if (request != null)
                SendTo(NetworkManager.ServerClientId, request);
            var reply = _client.Receive(message);
            if (reply != null)
                SendTo(NetworkManager.ServerClientId, reply);
            if (_client.Synced)
                Status = _assets.Complete ? (_table.Spectating ? "watching" : $"playing as {_table.SeatOverride}") : (_table.Spectating ? "watching" : $"playing as {_table.SeatOverride}") + $", receiving {_assets.Pending} image(s)";
        }

        // ---- transport --------------------------------------------------------------------------------------------

        private UnityTransport EnsureNetwork()
        {
            // A session must keep exchanging messages while its window is in the background. Set here as well as in
            // the player settings, which tooling has reset more than once.
            Application.runInBackground = true;
            if (_network == null)
            {
                // NGO requires the NetworkManager on a root GameObject.
                var go = new GameObject("Network");
                _network = go.AddComponent<NetworkManager>();
                var transport = go.AddComponent<UnityTransport>();
                _network.NetworkConfig = new NetworkConfig { NetworkTransport = transport, ConnectionApproval = false };
            }
            return (UnityTransport)_network.NetworkConfig.NetworkTransport;
        }

        private void Send(Outgoing outgoing)
        {
            if (outgoing.To != null)
            {
                if (ulong.TryParse(outgoing.To, out var id))
                    SendTo(id, outgoing.Message);
                return;
            }
            foreach (var client in _network.ConnectedClientsIds.Where(c => c != NetworkManager.ServerClientId).ToList())
                SendTo(client, outgoing.Message);
        }

        private void SendTo(ulong client, JObject message)
        {
            var text = message.ToString(Newtonsoft.Json.Formatting.None);
            using var writer = new FastBufferWriter(text.Length * 2 + 64, Allocator.Temp, 4 * 1024 * 1024);
            writer.WriteValueSafe(text);
            _network.CustomMessagingManager.SendNamedMessage(MessageName, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }
}
