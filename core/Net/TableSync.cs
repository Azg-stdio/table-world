using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>
    /// Commands on the wire: <c>{ "type": "FlipCommand", "data": { ... } }</c>. Any <see cref="Command"/> subclass in
    /// <c>Core.Engine</c> round-trips.
    /// </summary>
    public static class CommandCodec
    {
        public static JObject Encode(Command command) => new JObject
        {
            ["type"] = command.GetType().Name,
            ["data"] = JObject.FromObject(command),
        };

        public static Command Decode(JObject message)
        {
            var name = (string?)message["type"] ?? throw new FormatException("Command without type.");
            var type = typeof(Command).Assembly.GetType("Core.Engine." + name);
            if (type == null || !typeof(Command).IsAssignableFrom(type) || type.IsAbstract)
                throw new FormatException($"Unknown command type '{name}'.");
            return (Command)(message["data"] as JObject ?? new JObject()).ToObject(type)!;
        }
    }

    /// <summary>A message to send: to one peer (<see cref="To"/>) or to every client (null).</summary>
    public sealed class Outgoing
    {
        public string? To { get; }
        public JObject Message { get; }

        public Outgoing(string? to, JObject message)
        {
            To = to;
            Message = message;
        }
    }

    /// <summary>
    /// Authoritative host side of the table protocol (ADR 0002). Messages are JSON objects with a <c>kind</c>:
    /// clients send <c>intent</c> (a command with the client's sequence number) and <c>resync</c>; the host sends
    /// <c>order</c> (every executed command in host order, with its outcome and the resulting state hash) and
    /// <c>snapshot</c> (the whole table, on join or resync). The host's own commands go through <see cref="ExecuteLocal"/>.
    /// </summary>
    public sealed class SessionHost
    {
        public const string HostId = "host";

        private long _order;

        public GameEngine Game { get; }

        public SessionHost(GameEngine game)
        {
            Game = game;
        }

        public long Order => _order;

        /// <summary>Raised for every command the host executed successfully, with who sent it (the host's view applies it).</summary>
        public event Action<string, Command, CommandResult>? Executed;

        /// <summary>
        /// Client id → the seat that client plays. A seated client may perform rules actions only as its own seat;
        /// free-play table commands stay open to everyone, as on a real table (and in TTS).
        /// </summary>
        public Dictionary<string, string> Seats { get; } = new Dictionary<string, string>();

        /// <summary>When set, snapshots carry its manifest and <c>asset_request</c> messages are answered (T3.4).</summary>
        public AssetHost? Assets { get; set; }

        /// <summary>A peer joined (or asked to resync): send it the whole table, and the asset manifest when there is one.</summary>
        public Outgoing Snapshot(string clientId)
        {
            var message = new JObject
            {
                ["kind"] = "snapshot",
                ["order"] = _order,
                ["state"] = TableJson.Serialize(Game.State, indented: false),
                ["random"] = Game.Table.Random.State.ToString(),
            };
            if (Assets != null)
                message["assets"] = Assets.Manifest();
            return new Outgoing(clientId, message);
        }

        public (CommandResult Result, Outgoing Broadcast) ExecuteLocal(Command command) => Run(HostId, 0, command);

        public List<Outgoing> Receive(string clientId, JObject message)
        {
            var outgoing = new List<Outgoing>();
            switch ((string?)message["kind"])
            {
                case "intent":
                    Command command;
                    try
                    {
                        command = CommandCodec.Decode((JObject)message["command"]!);
                    }
                    catch (Exception e) when (e is FormatException || e is Newtonsoft.Json.JsonException || e is InvalidCastException)
                    {
                        outgoing.Add(new Outgoing(clientId, Rejection(clientId, (long?)message["seq"] ?? 0, new ValidationError("command", ErrorCodes.UnknownCommand, e.Message))));
                        break;
                    }
                    outgoing.Add(Run(clientId, (long?)message["seq"] ?? 0, command).Broadcast);
                    break;
                case "resync":
                    outgoing.Add(Snapshot(clientId));
                    break;
                case "asset_request" when Assets != null:
                    outgoing.AddRange(Assets.Receive(clientId, message));
                    break;
            }
            return outgoing;
        }

        private (CommandResult Result, Outgoing Broadcast) Run(string origin, long clientSeq, Command command)
        {
            if (command is PerformActionCommand && origin != HostId && Seats.TryGetValue(origin, out var seat) && command.Actor != seat)
            {
                var refused = CommandResult.Fail("actor", ErrorCodes.NotYourSeat,
                    $"You play {seat}; this action was sent as {command.Actor ?? "the current seat"}.", $"Send it with actor '{seat}'.");
                return (refused, new Outgoing(origin, Rejection(origin, clientSeq, refused.Error!)));
            }
            var result = Game.Execute(command);
            if (!result.Ok)
                return (result, new Outgoing(origin == HostId ? null : origin, Rejection(origin, clientSeq, result.Error!)));
            _order++;
            Executed?.Invoke(origin, command, result);
            return (result, new Outgoing(null, new JObject
            {
                ["kind"] = "order",
                ["order"] = _order,
                ["origin"] = origin,
                ["seq"] = clientSeq,
                ["ok"] = true,
                ["command"] = CommandCodec.Encode(command),
                ["hash"] = TableJson.Hash(Game.State),
            }));
        }

        /// <summary>A refused command changes nothing; only its sender needs to know (it may have predicted it).</summary>
        private JObject Rejection(string origin, long clientSeq, ValidationError error) => new JObject
        {
            ["kind"] = "order",
            ["order"] = _order,
            ["origin"] = origin,
            ["seq"] = clientSeq,
            ["ok"] = false,
            ["error"] = new JObject { ["path"] = error.Path, ["code"] = error.Code, ["message"] = error.Message },
        };
    }

    /// <summary>
    /// Client side of the table protocol. <see cref="Confirmed"/> follows the host's order exactly; <see cref="Predicted"/>
    /// is what the player sees: the confirmed table plus this client's own commands the host has not ordered yet, so
    /// local gestures feel immediate. When the host orders things differently, or refuses a predicted command, the
    /// prediction is rebuilt and <see cref="PredictionReplaced"/> is raised (the view reloads). A confirmed hash that
    /// differs from the host's asks for a snapshot.
    /// </summary>
    public sealed class SessionClient
    {
        private readonly string _id;
        private readonly List<(long Seq, Command Command)> _pending = new List<(long, Command)>();
        private long _nextSeq = 1;
        private long _order;

        public GameEngine? Confirmed { get; private set; }
        public GameEngine? Predicted { get; private set; }
        public bool Synced => Confirmed != null;
        public int PendingCount => _pending.Count;
        public int Resyncs { get; private set; }
        public int Rebuilds { get; private set; }

        /// <summary>Raised when <see cref="Predicted"/> is a new engine (snapshot or rebuild): views must reload from it.</summary>
        public event Action<GameEngine>? PredictionReplaced;

        /// <summary>
        /// Raised when a command ordered by the host was applied to <see cref="Predicted"/> in place (the common case:
        /// someone else's command while this client has nothing pending). The view applies the result's events.
        /// </summary>
        public event Action<Command, CommandResult>? Applied;

        /// <summary>Raised with the host's refusal of one of this client's commands.</summary>
        public event Action<ValidationError>? Refused;

        public SessionClient(string id)
        {
            _id = id;
        }

        /// <summary>Runs <paramref name="command"/> on the prediction now and returns the intent to send to the host.</summary>
        public (CommandResult Result, JObject? Intent) Submit(Command command)
        {
            if (Predicted == null)
                throw new InvalidOperationException("Not synced yet: wait for the host's snapshot.");
            var result = Predicted.Execute(command);
            if (!result.Ok)
                return (result, null);
            var seq = _nextSeq++;
            _pending.Add((seq, command));
            return (result, new JObject { ["kind"] = "intent", ["seq"] = seq, ["command"] = CommandCodec.Encode(command) });
        }

        /// <summary>Handles a host message; returns a reply to send (a resync request) or null.</summary>
        public JObject? Receive(JObject message)
        {
            switch ((string?)message["kind"])
            {
                case "snapshot":
                    var state = TableJson.Parse((string)message["state"]!).State!;
                    var random = ulong.Parse((string)message["random"]!);
                    Confirmed = Copy(state, random);
                    _order = (long)message["order"]!;
                    // Commands not yet ordered stay pending and are replayed on top of the snapshot.
                    Rebuild();
                    return null;
                case "order":
                    if (Confirmed == null)
                        return null;
                    return ApplyOrder(message);
                default:
                    return null;
            }
        }

        private JObject? ApplyOrder(JObject message)
        {
            var mine = (string?)message["origin"] == _id;
            var seq = (long?)message["seq"] ?? 0;
            if (!(bool)message["ok"]!)
            {
                if (mine)
                {
                    _pending.RemoveAll(p => p.Seq == seq);
                    var e = message["error"]!;
                    Refused?.Invoke(new ValidationError((string?)e["path"] ?? "", (string?)e["code"] ?? "", (string?)e["message"] ?? ""));
                    Rebuild();
                }
                return null;
            }

            var order = (long)message["order"]!;
            if (order <= _order)
                return null;
            if (order != _order + 1)
                return Resync();
            _order = order;

            var command = CommandCodec.Decode((JObject)message["command"]!);
            var result = Confirmed!.Execute(command);
            if (!result.Ok || TableJson.Hash(Confirmed.State) != (string)message["hash"]!)
                return Resync();

            var wasNext = mine && _pending.Count > 0 && _pending[0].Seq == seq;
            if (mine)
                _pending.RemoveAll(p => p.Seq == seq);
            if (wasNext)
            {
                // The host ordered our oldest command next: the prediction already includes it, nothing to redo.
                if (_pending.Count == 0 && TableJson.Hash(Predicted!.State) != TableJson.Hash(Confirmed.State))
                    Rebuild();
                return null;
            }
            if (!mine && _pending.Count == 0)
            {
                // Nothing of ours in flight: the prediction is the confirmed table, so the command applies to it the same way.
                var applied = Predicted!.Execute(command);
                if (applied.Ok && TableJson.Hash(Predicted.State) == TableJson.Hash(Confirmed.State))
                {
                    Applied?.Invoke(command, applied);
                    return null;
                }
            }
            // Someone else's command while ours are in flight (or ours out of order): replay ours on top of the new truth.
            Rebuild();
            return null;
        }

        private JObject Resync()
        {
            Resyncs++;
            Confirmed = null;
            return new JObject { ["kind"] = "resync" };
        }

        private void Rebuild()
        {
            Rebuilds++;
            Predicted = Copy(Confirmed!.State, Confirmed.Table.Random.State);
            foreach (var (_, command) in _pending.ToList())
                if (!Predicted.Execute(command).Ok)
                    _pending.RemoveAll(p => p.Command == command);
            PredictionReplaced?.Invoke(Predicted);
        }

        private static GameEngine Copy(TableState state, ulong random)
        {
            var copy = new GameEngine(TableJson.Parse(TableJson.Serialize(state, indented: false)).State!, 1);
            copy.Table.Random.State = random;
            return copy;
        }
    }
}
