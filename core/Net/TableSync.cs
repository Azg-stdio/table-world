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
    /// Each peer receives its seat's view only (<see cref="SeatView"/>, ADR 0012): a command peers cannot replay without
    /// what they may not know (it drew a random number, read or changed a role or a secret choice, ran Lua) is a host-run order, sent
    /// to each peer with its result for that seat (<see cref="TablePatch"/> and the events) instead of for replay.
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

        /// <summary>Every peer the host sent a snapshot to and has not forgotten: host-run orders go to each one.</summary>
        public IReadOnlyCollection<string> Peers => _peers;

        private readonly List<string> _peers = new List<string>();

        /// <summary>A peer left: no more messages for it.</summary>
        public void Forget(string clientId)
        {
            _peers.Remove(clientId);
            Seats.Remove(clientId);
            Watching.Remove(clientId);
        }

        /// <summary>The seat whose view <paramref name="clientId"/> receives; null (the public view) for spectators.</summary>
        public string? ViewerSeat(string clientId) => Seats.TryGetValue(clientId, out var seat) ? seat : null;

        /// <summary>
        /// Orders between state hashes. A hash serializes the whole table (120 ms for a 2 800-object TTS table), so the
        /// host sends one every this many commands; clients check their confirmed table then (T3.5).
        /// </summary>
        public int HashEvery { get; set; } = 32;

        /// <summary>Raised for every command the host executed successfully, with who sent it (the host's view applies it).</summary>
        public event Action<string, Command, CommandResult>? Executed;

        /// <summary>
        /// Client id → the seat that client plays. A seated client may perform rules actions only as its own seat;
        /// free-play table commands stay open to everyone, as on a real table (and in TTS).
        /// </summary>
        public Dictionary<string, string> Seats { get; } = new Dictionary<string, string>();

        /// <summary>Clients watching without a seat (G10): the host refuses their commands.</summary>
        public HashSet<string> Watching { get; } = new HashSet<string>();

        /// <summary>Besides the host, who may run host-only commands (promoted players, G23). Null: nobody else.</summary>
        public Func<string, bool>? MayHost { get; set; }

        /// <summary>When set, snapshots carry its manifest and <c>asset_request</c> messages are answered (T3.4).</summary>
        public AssetHost? Assets { get; set; }

        /// <summary>
        /// A peer joined (or asked to resync): send it its seat's view of the table, and the asset manifest when there is
        /// one. The random generator stays on the host.
        /// </summary>
        public Outgoing Snapshot(string clientId)
        {
            if (!_peers.Contains(clientId))
                _peers.Add(clientId);
            var message = new JObject
            {
                ["kind"] = "snapshot",
                ["order"] = _order,
                ["state"] = SeatView.Serialize(Game.State, ViewerSeat(clientId), Game.Table),
            };
            if (Assets != null)
                message["assets"] = Assets.Manifest();
            return new Outgoing(clientId, message);
        }

        /// <summary>Runs the host's own command; returns its result and the messages for the peers.</summary>
        public (CommandResult Result, List<Outgoing> Messages) ExecuteLocal(Command command) => Run(HostId, 0, command);

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
                    outgoing.AddRange(Run(clientId, (long?)message["seq"] ?? 0, command).Messages);
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

        private (CommandResult Result, List<Outgoing> Messages) Run(string origin, long clientSeq, Command command)
        {
            if (Watching.Contains(origin))
            {
                var watching = CommandResult.Fail("", ErrorCodes.NotYourSeat, "You are watching; pick a seat to play.", "Click a seat.");
                return (watching, new List<Outgoing> { new Outgoing(origin, Rejection(origin, clientSeq, watching.Error!)) });
            }
            if (command is IHostCommand && origin != HostId && !(MayHost?.Invoke(origin) ?? false))
            {
                var hostOnly = CommandResult.Fail("", ErrorCodes.HostOnly, "Only the host can change that.", "Ask the host.");
                return (hostOnly, new List<Outgoing> { new Outgoing(origin, Rejection(origin, clientSeq, hostOnly.Error!)) });
            }
            if ((command is PerformActionCommand || command is EndTurnCommand) && origin != HostId && Seats.TryGetValue(origin, out var seat) && command.Actor != seat)
            {
                var refused = CommandResult.Fail("actor", ErrorCodes.NotYourSeat,
                    $"You play {seat}; this action was sent as {command.Actor ?? "the current seat"}.", $"Send it with actor '{seat}'.");
                return (refused, new List<Outgoing> { new Outgoing(origin, Rejection(origin, clientSeq, refused.Error!)) });
            }
            // Moving, scaling or bagging a locked object is for the host's scripts; a client's request never may.
            if (origin != HostId)
                switch (command)
                {
                    case MoveCommand move: move.EvenLocked = false; break;
                    case ScaleCommand scale: scale.EvenLocked = false; break;
                    case InsertCommand insert: insert.EvenLocked = false; break;
                }
            var randomBefore = Game.Table.Random.State;
            var secretsBefore = SeatView.SecretKey(Game.State);
            var unknownBefore = Unknown();
            var result = Game.Execute(command);
            if (!result.Ok)
                return (result, new List<Outgoing> { new Outgoing(origin == HostId ? null : origin, Rejection(origin, clientSeq, result.Error!)) });
            _order++;
            Executed?.Invoke(origin, command, result);
            var hostRun = result.ScriptsRan || result.ReadsSecrets || Game.Table.Random.State != randomBefore || SeatView.SecretKey(Game.State) != secretsBefore
                          || TouchesUnknown(result, unknownBefore);
            if (hostRun)
                return (result, HostRunOrders(origin, clientSeq, command, result));
            var order = OrderMessage(origin, clientSeq, command);
            if (HashEvery <= 1 || _order % HashEvery == 0)
                order["hash"] = SeatView.Hash(Game.State, Game.Table);
            return (result, new List<Outgoing> { new Outgoing(null, order) });
        }

        /// <summary>The cards and decks someone may not know (strict tables only; empty otherwise).</summary>
        private HashSet<string> Unknown()
        {
            var (cards, decks) = SeatView.Hidden(Game.State, null, Game.Table);
            cards.UnionWith(decks);
            return cards;
        }

        /// <summary>
        /// A command that moved, flipped or dealt a card someone may not know, before or after it ran (a draw from a
        /// face-down deck, a card played from a hand): peers cannot replay it from what they hold (ADR 0012, T11.16).
        /// </summary>
        private bool TouchesUnknown(CommandResult result, HashSet<string> before)
        {
            if (!SeatView.Strict(Game.State) || result.Events.Count == 0)
                return false;
            var after = Unknown();
            return result.Events.Any(e => e.ObjectId.Length > 0 && (before.Contains(e.ObjectId) || after.Contains(e.ObjectId)));
        }

        private JObject OrderMessage(string origin, long clientSeq, Command command) => new JObject
        {
            ["kind"] = "order",
            ["order"] = _order,
            ["origin"] = origin,
            ["seq"] = clientSeq,
            ["ok"] = true,
            ["command"] = CommandCodec.Encode(command),
        };

        /// <summary>
        /// One order per peer, with the command's result for its seat: a patch (or, when the change reaches further, the
        /// whole seat view), the events, and the public hash to check. Another seat's secret choice or vote arrives without
        /// its parameters.
        /// </summary>
        private List<Outgoing> HostRunOrders(string origin, long clientSeq, Command command, CommandResult result)
        {
            var hash = SeatView.Hash(Game.State, Game.Table);
            var messages = new List<Outgoing>();
            foreach (var peer in _peers)
            {
                var seat = ViewerSeat(peer);
                var sent = command;
                // A gesture passes between two players: the others learn nothing of whom it was aimed at.
                if (command is GestureCommand gesture && gesture.Target != null && seat != gesture.Actor && seat != gesture.Target)
                    sent = new GestureCommand { Actor = gesture.Actor, Gesture = gesture.Gesture };
                if (command is PerformActionCommand perform && perform.Params.Count > 0 && perform.Actor != seat
                    && Game.State.Actions.FirstOrDefault(a => a.Id == perform.ActionId) is { } secret && (secret.Secret || secret.Vote != null))
                    sent = new PerformActionCommand { Actor = perform.Actor, ActionId = perform.ActionId };
                var order = OrderMessage(origin, clientSeq, sent);
                order["hash"] = hash;
                // A private line goes to its seat only.
                order["events"] = EventCodec.EncodeAll(result.Events.Where(e => !(e is PrivateMessage p) || p.Seat == seat));
                if (TablePatch.Build(Game.State, result, seat, Game.Table) is { } patch)
                    order["patch"] = patch;
                else
                    order["state"] = SeatView.Serialize(Game.State, seat, Game.Table);
                messages.Add(new Outgoing(peer, order));
            }
            return messages;
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
    /// differs from the host's asks for a snapshot. Host-run orders (ADR 0012) arrive as results to apply, not commands
    /// to replay; this client's own rolls and shuffles are not predicted (it does not hold the host's random generator)
    /// and show when the host's result arrives.
    /// </summary>
    public sealed class SessionClient
    {
        private readonly string _id;
        /// <summary>Commands sent and not yet ordered; <c>Predicted</c> false for those left off the prediction.</summary>
        private readonly List<(long Seq, Command Command, bool Predicted)> _pending = new List<(long, Command, bool)>();
        /// <summary>Objects the pending commands touched on the prediction (their events' objects and the command's targets).</summary>
        private readonly HashSet<string> _touched = new HashSet<string>();
        private long _nextSeq = 1;
        private long _order;

        public GameEngine? Confirmed { get; private set; }
        public GameEngine? Predicted { get; private set; }
        public bool Synced => Confirmed != null;

        /// <summary>The host promoted this client (G23): it may send host-only commands.</summary>
        public bool MayHost { get; set; }
        public int PendingCount => _pending.Count;
        public int Resyncs { get; private set; }
        public int Rebuilds { get; private set; }

        /// <summary>
        /// Raised when <see cref="Predicted"/> is a new engine (snapshot or rebuild), with the ids of the objects that may
        /// differ from the previous prediction, or null when anything may (a snapshot, a mismatch): views point at the
        /// new engine's objects and redraw those (rebuilding every view of a big table each time froze clients).
        /// </summary>
        public event Action<GameEngine, IReadOnlyCollection<string>?>? PredictionReplaced;

        /// <summary>
        /// Raised when a command ordered by the host was applied to <see cref="Predicted"/> in place (the common case:
        /// someone else's command while this client has nothing pending). The view applies the result's events.
        /// </summary>
        public event Action<Command, CommandResult>? Applied;

        /// <summary>
        /// Raised for every command the host ordered, ours included, once it is applied to <see cref="Confirmed"/>: the
        /// origin participant, the command and its result on the confirmed table (the game log reads from this, G11).
        /// </summary>
        public event Action<string, Command, CommandResult>? Ordered;

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
            if (command is IHostCommand && !MayHost)
                return (CommandResult.Fail("", ErrorCodes.HostOnly, "Only the host can change that.", "Ask the host."), null);
            CommandResult result;
            var predict = !DrawsRandom(command);
            if (predict)
            {
                result = Predicted.Execute(command);
                // Refused while reading what this seat may not know (another seat's role): only the host can tell, so it
                // goes to the host unpredicted.
                if (!result.Ok && result.ReadsSecrets)
                {
                    predict = false;
                    result = CommandResult.Success(new List<StateEvent>());
                }
                else if (!result.Ok)
                    return (result, null);
                else
                    Touch(result);
            }
            else
                result = CommandResult.Success(new List<StateEvent>());
            var seq = _nextSeq++;
            _pending.Add((seq, command, predict));
            return (result, new JObject { ["kind"] = "intent", ["seq"] = seq, ["command"] = CommandCodec.Encode(command) });
        }

        /// <summary>Table commands that always draw from the host's random generator: guessing them would show a wrong roll.</summary>
        public static bool DrawsRandom(Command command) => command is RollCommand || command is ShuffleCommand || command is RandomizeZoneCommand;

        /// <summary>Handles a host message; returns a reply to send (a resync request) or null.</summary>
        public JObject? Receive(JObject message)
        {
            switch ((string?)message["kind"])
            {
                case "snapshot":
                    var state = TableJson.Parse((string)message["state"]!).State!;
                    Confirmed = Copy(state, null);
                    _order = (long)message["order"]!;
                    // Commands not yet ordered stay pending and are replayed on top of the snapshot.
                    Rebuild(null);
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
                    Rebuild(Array.Empty<string>());
                }
                return null;
            }

            var order = (long)message["order"]!;
            if (order <= _order)
                return null;
            if (order != _order + 1)
                return Resync();
            _order = order;
            if (message["patch"] != null || message["state"] != null)
                return ApplyResult(message, mine, seq);

            var command = CommandCodec.Decode((JObject)message["command"]!);
            var randomBefore = Confirmed!.Table.Random.State;
            var result = Confirmed.Execute(command);
            // The host's hash comes every few orders (SessionHost.HashEvery): the confirmed table is checked then.
            var hash = (string?)message["hash"];
            if (!result.Ok || hash != null && SeatView.Hash(Confirmed.State, Confirmed.Table) != hash)
                return Resync();
            Ordered?.Invoke((string?)message["origin"] ?? "", command, result);

            var wasNext = mine && _pending.Count > 0 && _pending[0].Seq == seq;
            if (mine)
                _pending.RemoveAll(p => p.Seq == seq);
            if (_pending.Count == 0)
                _touched.Clear();
            if (wasNext)
            {
                // The host ordered our oldest command next: the prediction already includes it (the engine is
                // deterministic, so it came out the same). With nothing left in flight, a hash check now and then.
                if (_pending.Count == 0 && hash != null && SeatView.Hash(Predicted!.State, Predicted.Table) != hash)
                    Rebuild(null);
                return null;
            }
            if (!mine && (_pending.Count == 0 || Commutes(command, result, randomBefore)))
            {
                // Nothing of ours in flight, or ours touch other things: the command applies to the prediction the same
                // way, in place (rebuilding copies the whole table, 320 ms for a 2 800-object TTS table).
                var applied = Predicted!.Execute(command);
                if (applied.Ok && (_pending.Count > 0 || hash == null || SeatView.Hash(Predicted.State, Predicted.Table) == hash))
                {
                    Applied?.Invoke(command, applied);
                    return null;
                }
            }
            // Someone else's command that meets ours (or ours out of order): replay ours on top of the new truth.
            Rebuild(result.Events.Select(e => e.ObjectId));
            return null;
        }

        /// <summary>
        /// A host-run order: the host's result for this seat replaces the command's replay. The prediction takes the same
        /// patch in place when nothing else of ours is in flight; our own unpredicted roll or shuffle plays as if remote.
        /// </summary>
        private JObject? ApplyResult(JObject message, bool mine, long seq)
        {
            var command = CommandCodec.Decode((JObject)message["command"]!);
            var result = CommandResult.Success(EventCodec.DecodeAll(message["events"]));
            var hash = (string?)message["hash"];
            var patch = message["patch"] as JObject;
            HashSet<string>? changed = null;
            if (patch != null)
                changed = TablePatch.Apply(Confirmed!, patch);
            else
                Confirmed = Copy(TableJson.Parse((string)message["state"]!).State!, null);
            if (hash != null && SeatView.Hash(Confirmed!.State, Confirmed.Table) != hash)
                return Resync();
            Ordered?.Invoke((string?)message["origin"] ?? "", command, result);

            var predicted = !mine || _pending.FirstOrDefault(p => p.Seq == seq).Predicted;
            if (mine)
                _pending.RemoveAll(p => p.Seq == seq);
            if (_pending.Count == 0)
                _touched.Clear();
            if (_pending.Count > 0 || patch == null)
            {
                Rebuild(changed);
                return null;
            }
            TablePatch.Apply(Predicted!, patch);
            if (hash != null && SeatView.Hash(Predicted!.State, Predicted.Table) != hash)
            {
                Rebuild(null);
                return null;
            }
            if (mine && predicted)
            {
                // Our own command, already on screen as we guessed it: the view redraws what the host's result changed.
                PredictionReplaced?.Invoke(Predicted!, changed!);
                return null;
            }
            // Patched objects are new instances: every one gets an update so its view rebinds, after the events that
            // animate (a roll, a shuffle).
            var events = result.Events.ToList();
            events.AddRange(changed!.Where(id => Predicted!.Table.Find(id) != null && !events.Any(e => e is ObjectUpdated && e.ObjectId == id)).Select(id => new ObjectUpdated(id)));
            Applied?.Invoke(command, CommandResult.Success(events));
            return null;
        }

        private JObject Resync()
        {
            Resyncs++;
            Confirmed = null;
            return new JObject { ["kind"] = "resync" };
        }

        /// <summary>
        /// Replays this client's commands in flight on a copy of the confirmed table. <paramref name="alsoChanged"/> are
        /// objects the confirmed table changed since the prediction was made; null means anything may have.
        /// </summary>
        private void Rebuild(IEnumerable<string>? alsoChanged)
        {
            Rebuilds++;
            HashSet<string>? changed = alsoChanged == null ? null : new HashSet<string>(alsoChanged.Concat(_touched));
            Predicted = Copy(Confirmed!.State, Confirmed.Table.Random.State);
            _touched.Clear();
            foreach (var (_, command, predicted) in _pending.ToList())
            {
                if (!predicted)
                    continue;
                var replayed = Predicted.Execute(command);
                if (replayed.Ok)
                    Touch(replayed);
                else
                    _pending.RemoveAll(p => p.Command == command);
            }
            changed?.UnionWith(_touched);
            PredictionReplaced?.Invoke(Predicted, changed);
        }

        private void Touch(CommandResult result)
        {
            foreach (var e in result.Events)
                _touched.Add(e.ObjectId);
        }

        /// <summary>
        /// True when someone else's ordered command gives the same table whether it comes before or after this client's
        /// commands in flight: a plain gesture that draws no random number, makes or removes no object (new ids follow
        /// the order) and touches nothing ours touched. Anything else is replayed by a rebuild.
        /// </summary>
        private bool Commutes(Command command, CommandResult result, ulong randomBefore)
        {
            if (!(command is MoveCommand || command is FlipCommand || command is LockCommand || command is PickUpCommand
                  || command is DropCommand || command is SetPropertiesCommand || command is ScaleCommand || command is InsertCommand))
                return false;
            if (Confirmed!.Table.Random.State != randomBefore)
                return false;
            foreach (var e in result.Events)
                if (e is ObjectAdded || e is ObjectRemoved || e.ObjectId.Length == 0 || _touched.Contains(e.ObjectId))
                    return false;
            return true;
        }

        /// <summary>A copy to play on; <paramref name="random"/> null keeps the copy's own generator (peers do not hold the host's).</summary>
        private static GameEngine Copy(TableState state, ulong? random)
        {
            var copy = new GameEngine(TableJson.Copy(state), 1);
            if (random is { } value)
                copy.Table.Random.State = value;
            return copy;
        }
    }
}
