using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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
            var transport = EnsureNetwork();
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            NewHost();
            _table.Router = HostRoute;
            _table.SeatOverride = _table.Engine.State.Players.Seats.FirstOrDefault()?.Id;
            _network.OnClientConnectedCallback += OnHostClientConnected;
            _network.OnClientDisconnectCallback += OnHostClientDisconnected;
            if (!_network.StartHost())
            {
                Stop();
                return false;
            }
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnHostMessage);
            Status = $"hosting on port {port}";
            return true;
        }

        /// <summary>Joins a host at <paramref name="address"/>:<paramref name="port"/>; the table appears when the snapshot arrives.</summary>
        public bool StartClient(string address, ushort port = DefaultPort)
        {
            Stop();
            var transport = EnsureNetwork();
            transport.SetConnectionData(address, port);
            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            if (!_network.StartClient())
            {
                Stop();
                return false;
            }
            _network.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnClientMessage);
            Status = $"connecting to {address}:{port}";
            return true;
        }

        public void Stop()
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
            var snapshot = _host.Snapshot(clientId.ToString());
            snapshot.Message["seat"] = seat;
            Send(snapshot);
            Status = $"hosting, {_seatOf.Count} player(s) joined";
        }

        private void OnHostClientDisconnected(ulong clientId)
        {
            _seatOf.Remove(clientId);
            Status = $"hosting, {_seatOf.Count} player(s) joined";
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
            _assets.Ready += id => _table.RefreshAsset(id);
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
                SendTo(NetworkManager.ServerClientId, intent);
            return result;
        }

        private void OnClientMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            var message = JObject.Parse(text);
            if ((string)message["kind"] == "snapshot" && message["seat"] != null)
                _table.SeatOverride = (string)message["seat"];
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
