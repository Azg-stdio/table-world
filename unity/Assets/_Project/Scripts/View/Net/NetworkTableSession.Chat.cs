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
            var entry = Room.Say(IsHost ? SessionHost.HostId : "local", _table.Spectating ? null : _table.LocalSeat, text, Time.realtimeSinceStartupAsDouble, out var reply);
            Publish(entry);
            if (reply != null)
                Chat.Add(new ChatEntry { Kind = ChatKind.Notice, Text = reply });
        }

        /// <summary>A notice from the table to everyone (host or alone).</summary>
        private void Announce(string text) => Publish(Room.Notice(text));

        private void Publish(ChatEntry entry)
        {
            if (entry == null)
                return;
            Chat.Add(entry);
            if (IsHost)
                Send(new Outgoing(null, ChatRoom.Message(new[] { entry })));
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
            var entry = Room.Say(participant, _seating.Seat(participant), (string)message["text"] ?? "", Time.realtimeSinceStartupAsDouble, out var reply);
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

        /// <summary>How a participant is named in notices until players have names (G23): their seat, or a spectator.</summary>
        private string Who(string participant) => _seating.Seat(participant) ?? (participant == SessionHost.HostId ? "The host" : "A spectator");

        // ---- game log ---------------------------------------------------------------------------------------------

        /// <summary>Alone at the table: log what this machine does, as the seat now playing.</summary>
        private void OnLocalCommand(Command command, CommandResult result)
        {
            if (IsHost || IsClient)
                return;
            Log(_table.Engine, command, result, _table.LocalSeat);
        }

        private void OnHostExecuted(string origin, Command command, CommandResult result) =>
            Log(_host.Game.Table, command, result, origin == SessionHost.HostId ? _table.SeatOverride ?? "The host" : _seating.Seat(origin) ?? "A spectator");

        private void OnClientOrdered(string origin, Command command, CommandResult result)
        {
            var seat = _occupants.FirstOrDefault(o => o.Value == origin).Key;
            Log(_client.Confirmed.Table, command, result, seat ?? (origin == SessionHost.HostId ? "The host" : "A spectator"));
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
