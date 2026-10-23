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
    public sealed class NetworkTableSession : MonoBehaviour
    {
        public const string MessageName = "tw";
        public const ushort DefaultPort = 7777;
        public const float TouchSeconds = 4f;

        [SerializeField] private TableView _table;

        private NetworkManager _network;
        private SessionHost _host;
        private SessionClient _client;
        private AssetClient _assets;
        private readonly Dictionary<ulong, string> _seatOf = new Dictionary<ulong, string>();
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

        public void Configure(TableView table) => _table = table;

        private void OnEnable()
        {
            if (_table == null)
                return;
            _table.Touched += Touch;
            _table.Loaded += OnTableLoaded;
        }

        private void OnDisable()
        {
            if (_table == null)
                return;
            _table.Touched -= Touch;
            _table.Loaded -= OnTableLoaded;
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
            NewHost();
            _table.Router = HostRoute;
            _table.SeatOverride = _table.Engine.State.Players.Seats.FirstOrDefault()?.Id;
            _network.OnClientConnectedCallback += OnHostClientConnected;
            _network.OnClientDisconnectCallback += OnHostClientDisconnected;
            if (!_network.StartHost())
            {
                ShutdownNetwork();
                return false;
            }
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnHostMessage);
            Status = status;
            return true;
        }

        private bool LaunchClient(string status)
        {
            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            if (!_network.StartClient())
            {
                ShutdownNetwork();
                return false;
            }
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnClientMessage);
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
                if (_network.IsListening)
                    _network.Shutdown();
            }
            _host = null;
            _client = null;
            _assets = null;
            if (_table != null)
                _table.FetchAsset = null;
            _seatOf.Clear();
            if (_table != null)
            {
                _table.Router = null;
                _table.SeatOverride = null;
                _table.ReportsPoseOf = null;
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
        }

        /// <summary>The host loaded another table: start a new order and send everyone the new table and its assets.</summary>
        private void OnTableLoaded()
        {
            if (_host == null || _host.Game == _table.Game)
                return;
            NewHost();
            foreach (var client in _seatOf.Keys.ToList())
            {
                if (_seatOf[client] != null)
                    _host.Seats[client.ToString()] = _seatOf[client];
                var snapshot = _host.Snapshot(client.ToString());
                snapshot.Message["seat"] = _seatOf[client];
                Send(snapshot);
            }
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
            var taken = new HashSet<string>(_seatOf.Values) { _table.SeatOverride };
            var seat = _table.Engine.State.Players.Seats.Select(s => s.Id).FirstOrDefault(id => !taken.Contains(id));
            _seatOf[clientId] = seat;
            if (seat != null)
                _host.Seats[clientId.ToString()] = seat;
            var snapshot = _host.Snapshot(clientId.ToString());
            snapshot.Message["seat"] = seat;
            Send(snapshot);
            Status = HostStatus();
        }

        private void OnHostClientDisconnected(ulong clientId)
        {
            _seatOf.Remove(clientId);
            _host?.Seats.Remove(clientId.ToString());
            Status = HostStatus();
        }

        private void OnHostMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            foreach (var message in _host.Receive(sender.ToString(), JObject.Parse(text)))
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
            _client.Refused += error => Debug.LogWarning($"[Net] The host refused a command: {error}");
            _table.Router = ClientRoute;
            _table.ReportsPoseOf = view => _touched.TryGetValue(view.Id, out var at) && Time.unscaledTime - at < TouchSeconds;
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
            if (kind == "snapshot")
            {
                if (message["seat"] != null)
                    _table.SeatOverride = (string)message["seat"];
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
                Status = _assets.Complete ? $"playing as {_table.SeatOverride}" : $"playing as {_table.SeatOverride}, receiving {_assets.Pending} image(s)";
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
