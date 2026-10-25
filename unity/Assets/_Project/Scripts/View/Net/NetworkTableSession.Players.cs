using System.Collections.Generic;
using System.Linq;
using Core.Net;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Players at a hosted table (G23): names (each player sends theirs in a <c>hello</c>), and what the host gives them:
    /// promotion (host-only settings), Game Master (sees every hand and hidden zone), a team (team chat), mute, kick.
    /// The host keeps the <see cref="Roster"/> and sends it with every seating broadcast.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        private readonly Roster _roster = new Roster();
        private Dictionary<string, PlayerInfo> _players = new Dictionary<string, PlayerInfo>();

        /// <summary>This machine's player name (from the settings); empty means "Player N".</summary>
        public string LocalName { get; private set; } = "";

        /// <summary>This machine's chalker personality (T7.3, from the settings), a <c>ChalkerPersonality</c> name.</summary>
        public string LocalLook { get; private set; } = "";

        /// <summary>Participant → who they are, as last known on this machine.</summary>
        public IReadOnlyDictionary<string, PlayerInfo> Players => IsHost ? _roster.Players : _players;

        /// <summary>The name to show for <paramref name="participant"/>, or null when unknown.</summary>
        public string NameOf(string participant) =>
            participant != null && Players.TryGetValue(participant, out var info) && info.Name.Length > 0 ? info.Name : null;

        /// <summary>The name of whoever sits at <paramref name="seat"/>, or null.</summary>
        public string NameAtSeat(string seat) =>
            seat != null && Occupants.TryGetValue(seat, out var participant) ? NameOf(participant) : null;

        /// <summary>The chalker personality of whoever sits at <paramref name="seat"/>, or empty.</summary>
        public string LookAtSeat(string seat) =>
            seat != null && Occupants.TryGetValue(seat, out var participant) && Players.TryGetValue(participant, out var info) ? info.Look : "";

        /// <summary>Sets this machine's chalker personality; online it travels like the name.</summary>
        public void SetLocalLook(string look)
        {
            look = PlayerInfo.CleanLook(look);
            if (look == LocalLook)
                return;
            LocalLook = look;
            if (IsHost)
            {
                _roster.SetLook(SessionHost.HostId, LocalLook);
                BroadcastSeating();
            }
            else if (IsClient && _network != null && _network.IsConnectedClient)
                SendHello();
        }

        /// <summary>Sets this machine's name; online it is sent to the host (or applied, when hosting).</summary>
        public void SetLocalName(string name)
        {
            LocalName = PlayerInfo.CleanName(name) ?? "";
            if (IsHost)
            {
                if (_roster.Rename(SessionHost.HostId, LocalName))
                    BroadcastSeating();
            }
            else if (IsClient && _network != null && _network.IsConnectedClient)
                SendHello();
        }

        private void SendHello() => SendTo(NetworkManager.ServerClientId, new JObject { ["kind"] = "hello", ["name"] = LocalName, ["look"] = LocalLook });

        /// <summary>The seating broadcast with the roster in it.</summary>
        private JObject SeatingMessage()
        {
            var message = _seating.Message();
            message["players"] = _roster.ToJson();
            return message;
        }

        private void OnHostHello(ulong sender, JObject message)
        {
            var participant = sender.ToString();
            var first = !_greeted.Contains(participant);
            _roster.Rename(participant, (string)message["name"]);
            _roster.SetLook(participant, message["look"]?.Type == JTokenType.String ? (string)message["look"] : null);
            if (first)
            {
                _greeted.Add(participant);
                var seat = _seating.Seat(participant);
                Announce($"{_roster.Of(participant).Name} joined" + (seat != null ? $" at {seat}" : " to watch"));
                // TTS onPlayerConnect, for a player who sits down.
                if (seat != null && _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == seat) is { } joined)
                    _scripts?.OnPlayerConnect(joined.Color);
            }
            BroadcastSeating();
        }

        private readonly HashSet<string> _greeted = new HashSet<string>();

        /// <summary>A client reads the roster from a seating broadcast and applies what the host gave it.</summary>
        private void OnClientRoster(JObject message)
        {
            _players = Roster.Read(message["players"]);
            var me = LocalParticipant != null && _players.TryGetValue(LocalParticipant, out var info) ? info : null;
            ApplyLocalRole(me);
        }

        /// <summary>Game Master sees everything; a promoted player may change host-only settings.</summary>
        private void ApplyLocalRole(PlayerInfo me)
        {
            _table.SeesEverything = me?.GameMaster ?? false;
            _table.MayChangeTable = IsHost || (me?.Promoted ?? false);
            if (_client != null)
                _client.MayHost = me?.Promoted ?? false;
        }

        // ---- host actions ---------------------------------------------------------------------------------------

        /// <summary>Host only: promote or demote a player.</summary>
        public void SetPromoted(string participant, bool promoted) =>
            ChangePlayer(participant, p => p.Promoted = promoted, p => promoted ? $"{p.Name} was promoted" : $"{p.Name} is no longer promoted");

        /// <summary>Host only: make a player Game Master (sees everything) or take it back.</summary>
        public void SetGameMaster(string participant, bool gameMaster) =>
            ChangePlayer(participant, p => p.GameMaster = gameMaster, p => gameMaster ? $"{p.Name} is the Game Master" : $"{p.Name} is no longer Game Master");

        /// <summary>Host only: put a player on a team (team chat), or on none.</summary>
        public void SetTeam(string participant, Team team) =>
            ChangePlayer(participant, p => p.Team = team, p => team == Team.None ? $"{p.Name} left their team" : $"{p.Name} joined team {team}");

        /// <summary>Host only: mute or unmute a player's chat.</summary>
        public void SetMuted(string participant, bool muted) =>
            ChangePlayer(participant, p => p.Muted = muted, p => muted ? $"{p.Name} was muted" : $"{p.Name} can chat again");

        /// <summary>Host only: remove a client from the table.</summary>
        public void Kick(string participant)
        {
            if (!IsHost || participant == SessionHost.HostId || !ulong.TryParse(participant, out var client) || !_seatOf.ContainsKey(client))
                return;
            Announce($"{_roster.Of(participant).Name} was removed by the host");
            SendTo(client, new JObject { ["kind"] = "kicked" });
            _kicked.Add(client);
        }

        private readonly HashSet<ulong> _kicked = new HashSet<ulong>();

        /// <summary>Kicked clients are disconnected a moment after their notice, so it arrives first.</summary>
        private void DisconnectKicked()
        {
            if (_kicked.Count == 0 || _network == null || !_network.IsServer)
                return;
            foreach (var client in _kicked.ToList())
            {
                if (_network.ConnectedClientsIds.Contains(client))
                    _network.DisconnectClient(client);
                _kicked.Remove(client);
            }
        }

        private void ChangePlayer(string participant, System.Action<PlayerInfo> change, System.Func<PlayerInfo, string> notice)
        {
            if (!IsHost || participant == null || (participant != SessionHost.HostId && !_roster.Players.ContainsKey(participant)))
                return;
            var info = _roster.Of(participant);
            change(info);
            Announce(notice(info));
            if (participant == SessionHost.HostId)
                ApplyLocalRole(info);
            BroadcastSeating();
        }

        private void ResetPlayers()
        {
            foreach (var participant in _roster.Players.Keys.ToList())
                _roster.Leave(participant);
            _players = new Dictionary<string, PlayerInfo>();
            _greeted.Clear();
            _kicked.Clear();
            if (_table != null)
            {
                _table.SeesEverything = false;
                _table.MayChangeTable = true;
            }
        }
    }
}
