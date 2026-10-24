using System.Linq;
using Core.Engine;
using Core.Net;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Chat and game log (G11, <c>/roll</c> of G24). The host's <see cref="ChatRoom"/> stamps every line with its sender's
    /// seat and broadcasts it; a client only asks to say something. Playing alone, the same room runs locally. The game
    /// log is written on each machine from the commands the host ordered (<see cref="SessionHost.Executed"/>,
    /// <see cref="SessionClient.Ordered"/>), worded for this machine's seat so it never names a card it cannot see.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        private readonly System.Random _dice = new System.Random();
        private ChatRoom _room;

        /// <summary>Every line this machine shows: chat, rolls, notices and the game log.</summary>
        public ChatLog Chat { get; } = new ChatLog();

        private ChatRoom Room => _room ??= new ChatRoom(max => _dice.Next(max));

        /// <summary>Sends a chat line (or a command such as <c>/roll 2d6</c>) as this machine's player.</summary>
        public void Say(string text)
        {
            if (IsClient)
            {
                if (_client.Synced)
                    SendTo(NetworkManager.ServerClientId, ChatRoom.SayMessage(text));
                return;
            }
            var entry = Room.Say(IsHost ? SpeakerOf(SessionHost.HostId)
                : new Speaker { Participant = "local", Seat = _table.LocalSeat, Name = LocalName.Length > 0 ? LocalName : _table.LocalSeat ?? "" },
                text, Time.realtimeSinceStartupAsDouble, out var reply);
            Publish(entry);
            if (reply != null)
                Chat.Add(new ChatEntry { Kind = ChatKind.Notice, Text = reply });
        }

        /// <summary>A notice from the table to everyone (host or alone).</summary>
        private void Announce(string text) => Publish(Room.Notice(text));

        /// <summary>Shows and sends an entry: to everyone, or a team line (G23) to that team only.</summary>
        private void Publish(ChatEntry entry)
        {
            if (entry == null)
                return;
            if (entry.Team == Team.None)
            {
                Chat.Add(entry);
                if (IsHost)
                    Send(new Outgoing(null, ChatRoom.Message(new[] { entry })));
                return;
            }
            var message = ChatRoom.Message(new[] { entry });
            foreach (var member in _roster.OnTeam(entry.Team))
            {
                if (member == SessionHost.HostId)
                    Chat.Add(entry);
                else if (ulong.TryParse(member, out var client))
                    SendTo(client, message);
            }
        }

        /// <summary>Who a participant is when they talk: the host's seating and roster, never what the client claims.</summary>
        private Speaker SpeakerOf(string participant)
        {
            var info = _roster.Of(participant);
            return new Speaker { Participant = participant, Seat = _seating.Seat(participant), Name = info.Name, Team = info.Team, Muted = info.Muted };
        }

        /// <summary>A new session starts a fresh chat (ids restart with each host).</summary>
        private void ResetChat()
        {
            _room = null;
            Chat.Clear();
        }

        private void OnHostSay(ulong sender, JObject message)
        {
            var participant = sender.ToString();
            var entry = Room.Say(SpeakerOf(participant), (string)message["text"] ?? "", Time.realtimeSinceStartupAsDouble, out var reply);
            Publish(entry);
            if (reply != null)
                SendTo(sender, new JObject { ["kind"] = "chat_reply", ["text"] = reply });
        }

        /// <summary>Chat messages a client receives; true when the message was one.</summary>
        private bool OnClientChat(string kind, JObject message)
        {
            switch (kind)
            {
                case "chat":
                    foreach (var entry in ChatRoom.Read(message))
                        Chat.Add(entry);
                    return true;
                case "chat_reply":
                    Chat.Add(new ChatEntry { Kind = ChatKind.Notice, Text = ChatRoom.Clean((string)message["text"]) ?? "" });
                    return true;
                default:
                    return false;
            }
        }


        /// <summary>Everyone is told when the host rewinds time (G26); alone, the log notes it.</summary>
        private void OnRewound(double secondsAgo)
        {
            if (IsClient)
                return;
            var who = IsHost ? _roster.Of(SessionHost.HostId).Name : LocalName.Length > 0 ? LocalName : "You";
            Announce($"{who} rewound the table to {TableWorld.View.Hud.TableOverlay.Ago(secondsAgo)}");
        }

        // ---- game log ---------------------------------------------------------------------------------------------

        /// <summary>Alone at the table: log what this machine does, as the seat now playing.</summary>
        private void OnLocalCommand(Command command, CommandResult result)
        {
            if (IsHost || IsClient)
                return;
            Log(_table.Engine, command, result, LocalName.Length > 0 ? LocalName : _table.LocalSeat);
            ScriptsHear(command, result, command.Actor ?? _table.LocalSeat);
        }

        private void OnHostExecuted(string origin, Command command, CommandResult result)
        {
            Log(_host.Game.Table, command, result, _roster.Of(origin).Name);
            ScriptsHear(command, result, origin == SessionHost.HostId ? command.Actor ?? _table.SeatOverride : _seating.Seat(origin));
        }

        private void OnClientOrdered(string origin, Command command, CommandResult result)
        {
            var seat = _occupants.FirstOrDefault(o => o.Value == origin).Key;
            Log(_client.Confirmed.Table, command, result, NameOf(origin) ?? seat ?? (origin == SessionHost.HostId ? "The host" : "A spectator"));
        }

        private void Log(TableEngine engine, Command command, CommandResult result, string actor)
        {
            if (engine == null)
                return;
            foreach (var (rules, text) in ActionLog.Describe(engine, command, result, actor, _table.Spectating ? null : _table.LocalSeat))
                Chat.Add(new ChatEntry { Kind = rules ? ChatKind.Notice : ChatKind.Action, Text = text });
        }
    }
}
