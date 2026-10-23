using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>
    /// The rules interpreter. Wraps a <see cref="TableEngine"/>: table commands pass through (free play is always allowed),
    /// <see cref="StartGameCommand"/> and <see cref="PerformActionCommand"/> run the game's phases, actions, effects,
    /// conditions, events and win checks. A rules command is transactional: if an effect fails midway, the objects, zones,
    /// session and random state are restored and the result carries <see cref="CommandResult.StateRestored"/>.
    /// </summary>
    public sealed class GameEngine
    {
        public const int MaxTransitions = 200;
        public const int MaxLog = 50;

        private Context _ctx = new Context();
        private List<StateEvent> _events = new List<StateEvent>();
        private string? _pendingGoto;
        private bool _pendingEndPhase;
        private bool _pendingEndTurn;

        public TableEngine Table { get; }
        public TableState State => Table.State;
        public Session? Session => State.Session;

        public GameEngine(TableState state, ulong seed)
        {
            Table = new TableEngine(state, seed);
            if (state.Session is { RandomState: not 0 } session)
                Table.Random.State = session.RandomState;
        }

        public CommandResult Execute(Command command)
        {
            switch (command)
            {
                case StartGameCommand start:
                    return Transaction(() => StartGame(start));
                case PerformActionCommand perform:
                    return Transaction(() => Perform(perform));
                default:
                    var result = Table.Execute(command);
                    if (result.Ok && Session is { Started: true, Over: false })
                    {
                        _events = new List<StateEvent>(result.Events);
                        CheckWins(WinCheck.AnyCommand);
                        SaveRandom();
                        return result.WithEvents(_events);
                    }
                    return result;
            }
        }

        /// <summary>Actions <paramref name="seat"/> may perform now, with their valid parameter combinations.</summary>
        public List<LegalAction> LegalActions(string seat)
        {
            var legal = new List<LegalAction>();
            if (!(Session is { Started: true, Over: false }) || CurrentPhase() is not { } phase)
                return legal;
            foreach (var actionId in phase.Actions)
            {
                var action = State.Actions.FirstOrDefault(a => a.Id == actionId);
                if (action == null || CanAct(action, seat) != null)
                    continue;
                var choices = new List<Dictionary<string, string>>();
                foreach (var combo in ParamCombinations(action, seat))
                {
                    _ctx = new Context { Actor = seat, Params = combo };
                    if (action.Preconditions.All(c => Holds(c)))
                        choices.Add(combo);
                }
                if (choices.Count > 0)
                    legal.Add(new LegalAction(action.Id, action.Name, choices));
            }
            return legal;
        }

        // ---- commands ---------------------------------------------------------------------------------------------

        private CommandResult Transaction(Action body)
        {
            var objects = TableJson.Serialize(new TableState { Objects = State.Objects, Zones = State.Zones }, indented: false);
            var session = State.Session;
            var sessionJson = session == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(session);
            var random = Table.Random.State;
            _events = new List<StateEvent>();
            _pendingGoto = null;
            _pendingEndPhase = _pendingEndTurn = false;
            try
            {
                body();
                SaveRandom();
                var events = _events;
                return CommandResult.Success(events);
            }
            catch (RuleException e)
            {
                var mutated = _events.Count > 0;
                if (mutated)
                {
                    var restored = TableJson.Parse(objects).State!;
                    State.Objects = restored.Objects;
                    State.Zones = restored.Zones;
                    Table.Reindex();
                }
                State.Session = sessionJson == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<Session>(sessionJson);
                Table.Random.State = random;
                var fail = CommandResult.Fail(e.Error.Path, e.Error.Code, e.Error.Message, e.Error.Suggestion);
                fail.StateRestored = mutated;
                return fail;
            }
        }

        private void StartGame(StartGameCommand command)
        {
            if (!State.HasRules)
                throw new RuleException("", ErrorCodes.NoRules, "This table has no rules (turn and phases).", "Add a turn and phases, or play freely.");
            if (Session is { Started: true })
                throw new RuleException("", ErrorCodes.GameAlreadyStarted, "The game has already started.");
            var seats = State.Players.Seats;
            if (seats.Count == 0)
                throw new RuleException("players.seats", ErrorCodes.PlayerCount, "The game needs seats.");
            if (State.Game != null && (seats.Count < State.Game.MinPlayers || seats.Count > State.Game.MaxPlayers))
                throw new RuleException("players.seats", ErrorCodes.PlayerCount, $"{seats.Count} seats; the game takes {State.Game.MinPlayers}–{State.Game.MaxPlayers}.");

            var turn = State.Turn!;
            var session = new Session { Started = true, Turn = 1 };
            foreach (var pair in State.Variables)
                session.Variables[pair.Key] = pair.Value.DeepClone();
            foreach (var seat in seats)
                session.Resources[seat.Id] = State.Resources.ToDictionary(r => r.Id, r => r.Initial);
            session.CurrentSeat = turn.StartingPlayer switch
            {
                "first" => seats[0].Id,
                "random" => seats[Table.Random.Next(seats.Count)].Id,
                _ => turn.StartingPlayer,
            };
            State.Session = session;

            RunEvent(GameEvents.OnGameStart);
            RunEvent(GameEvents.OnTurnStart);
            EnterPhase(turn.Phases[0]);
            AdvanceLoop();
            CheckWins(WinCheck.AnyCommand);
        }

        private void Perform(PerformActionCommand command)
        {
            var session = RequireRunning();
            var action = State.Actions.FirstOrDefault(a => a.Id == command.ActionId)
                ?? throw new RuleException("actionId", ErrorCodes.UnknownAction, $"Action '{command.ActionId}' does not exist.",
                    Suggest.OneOf(command.ActionId, State.Actions.Select(a => a.Id), "an action id"));
            var seat = command.Actor ?? session.CurrentSeat!;
            if (CanAct(action, seat) is { } refusal)
                throw refusal;

            foreach (var param in action.Params)
            {
                if (!command.Params.TryGetValue(param.Name, out var value))
                    throw new RuleException($"params.{param.Name}", ErrorCodes.MissingParam, $"Action '{action.Id}' needs parameter '{param.Name}'.");
                if (!ParamValid(param, value, seat))
                    throw new RuleException($"params.{param.Name}", ErrorCodes.InvalidParam, $"'{value}' is not a valid '{param.Name}' for '{action.Id}'.",
                        DescribeFilter(param));
            }

            _ctx = new Context { Actor = seat, Params = command.Params };
            for (var i = 0; i < action.Preconditions.Count; i++)
                if (!Holds(action.Preconditions[i]))
                    throw new RuleException($"preconditions[{i}]", ErrorCodes.PreconditionFailed,
                        $"Action '{action.Id}' is not possible now: precondition {i} ({action.Preconditions[i].Kind}) does not hold.");

            RunEffects(action.Effects);
            if (!session.Acted.Contains(seat))
                session.Acted.Add(seat);
            var key = $"{seat}/{action.Id}";
            session.ActionCounts[key] = session.ActionCounts.TryGetValue(key, out var n) ? n + 1 : 1;
            AdvanceLoop();
            CheckWins(WinCheck.AnyCommand);
        }

        private RuleException? CanAct(ActionDef action, string seat)
        {
            var session = Session!;
            if (State.Players.Seats.All(s => s.Id != seat))
                return new RuleException("actor", ErrorCodes.UnknownSeat, $"Seat '{seat}' does not exist.");
            if (CurrentPhase() is not { } phase || !phase.Actions.Contains(action.Id))
                return new RuleException("actionId", ErrorCodes.ActionNotAvailable, $"'{action.Id}' is not allowed in phase '{session.Phase}'.",
                    CurrentPhase() is { } p && p.Actions.Count > 0 ? $"Allowed now: {string.Join(", ", p.Actions)}." : null);
            if (action.Actor == ActorRule.Current && seat != session.CurrentSeat)
                return new RuleException("actor", ErrorCodes.NotYourTurn, $"It is {session.CurrentSeat}'s turn, not {seat}'s.");
            if (action.LimitPerPhase is { } limit && session.ActionCounts.TryGetValue($"{seat}/{action.Id}", out var used) && used >= limit)
                return new RuleException("actionId", ErrorCodes.LimitReached, $"{seat} already did '{action.Id}' {used} time(s) this phase.");
            return null;
        }

        private Session RequireRunning()
        {
            if (!(Session is { Started: true } session))
                throw new RuleException("", ErrorCodes.GameNotStarted, "The game has not started.", "Send start_game first.");
            if (session.Over)
                throw new RuleException("", ErrorCodes.GameOver, $"The game is over; {session.Winner ?? "nobody"} won.");
            return session;
        }

        // ---- turns and phases -------------------------------------------------------------------------------------

        private PhaseDef? CurrentPhase() => Session?.Phase == null ? null : State.Phases.FirstOrDefault(p => p.Id == Session.Phase);

        private void EnterPhase(string phaseId)
        {
            var session = Session!;
            session.Phase = phaseId;
            session.Acted.Clear();
            session.ActionCounts.Clear();
            var phase = CurrentPhase()!;
            WithContext(new Context { Actor = session.CurrentSeat }, () => RunEffects(phase.OnEnter));
        }

        private void ExitPhase()
        {
            var phase = CurrentPhase();
            if (phase != null)
                WithContext(new Context { Actor = Session!.CurrentSeat }, () => RunEffects(phase.OnExit));
        }

        private void AdvanceLoop()
        {
            var session = Session!;
            for (var guard = 0; guard < MaxTransitions; guard++)
            {
                if (session.Over)
                    return;
                if (_pendingGoto != null)
                {
                    var target = _pendingGoto;
                    _pendingGoto = null;
                    ExitPhase();
                    EnterPhase(target);
                    continue;
                }
                if (_pendingEndTurn)
                {
                    _pendingEndTurn = _pendingEndPhase = false;
                    ExitPhase();
                    EndTurn();
                    continue;
                }
                var phase = CurrentPhase()!;
                var rule = phase.AdvanceWhen;
                var advance = _pendingEndPhase
                    || rule != null && (rule.Immediate
                        || rule.AllPlayersActed && State.Players.Seats.All(s => session.Acted.Contains(s.Id))
                        || rule.Condition != null && WithContext(new Context { Actor = session.CurrentSeat }, () => Holds(rule.Condition)));
                if (!advance)
                    return;
                _pendingEndPhase = false;
                ExitPhase();
                var phases = State.Turn!.Phases;
                var next = phases.IndexOf(phase.Id) + 1;
                if (next < phases.Count)
                    EnterPhase(phases[next]);
                else
                    EndTurn();
            }
            throw new RuleException("phases", ErrorCodes.RulesLoop, $"The rules changed phase {MaxTransitions} times without waiting for a player.",
                "Check advanceWhen: a cycle of 'immediate' phases or conditions that always hold never stops.");
        }

        private void EndTurn()
        {
            var session = Session!;
            RunEvent(GameEvents.OnTurnEnd);
            CheckWins(WinCheck.TurnEnd);
            if (session.Over)
                return;
            session.Turn++;
            session.CurrentSeat = NextSeat(session.CurrentSeat!);
            RunEvent(GameEvents.OnTurnStart);
            EnterPhase(State.Turn!.Phases[0]);
        }

        private string NextSeat(string seat)
        {
            var seats = State.Players.Seats;
            var i = seats.FindIndex(s => s.Id == seat);
            var step = State.Turn?.Order == TurnOrder.Counterclockwise ? seats.Count - 1 : 1;
            return seats[(i + step) % seats.Count].Id;
        }

        private void RunEvent(string name)
        {
            if (State.Events.TryGetValue(name, out var effects))
                WithContext(new Context { Actor = Session!.CurrentSeat }, () => RunEffects(effects));
        }

        private void CheckWins(WinCheck when)
        {
            var session = Session!;
            if (session.Over)
                return;
            foreach (var win in State.WinConditions.Where(w => w.CheckAfter == when && w.When != null))
            {
                foreach (var seat in State.Players.Seats)
                {
                    var holds = WithContext(new Context { Actor = session.CurrentSeat, Player = seat.Id }, () => Holds(win.When!));
                    if (!holds)
                        continue;
                    var winner = WithContext(new Context { Actor = session.CurrentSeat, Player = seat.Id }, () => ResolveSeat(win.Winner, "winner"));
                    Win(winner);
                    return;
                }
            }
        }

        private void Win(string seat)
        {
            var session = Session!;
            session.Over = true;
            session.Winner = seat;
            Log($"{seat} wins.");
        }

        // ---- effects ----------------------------------------------------------------------------------------------

        private void RunEffects(List<Effect> effects)
        {
            foreach (var effect in effects)
            {
                if (Session!.Over)
                    return;
                Run(effect);
            }
        }

        private void Run(Effect effect)
        {
            switch (effect)
            {
                case MoveEffect move: Move(move); break;
                case DealEffect deal: Deal(deal); break;
                case ShuffleEffect shuffle: TableCommand(new ShuffleCommand { ObjectId = ResolveObject(shuffle.Object, "shuffle.object").Id }); break;
                case FlipEffect flip: TableCommand(new FlipCommand { ObjectId = ResolveObject(flip.Object, "flip.object").Id }); break;
                case LockEffect lockEffect:
                    TableCommand(new LockCommand { ObjectId = ResolveObject(lockEffect.Object, "lock.object").Id, Locked = lockEffect.Locked ?? true });
                    break;
                case RollEffect roll:
                {
                    var die = ResolveObject(roll.Object, "roll.object");
                    TableCommand(new RollCommand { ObjectId = die.Id });
                    if (roll.Store != null)
                        Session!.Variables[roll.Store] = ((Die)die).Value;
                    break;
                }
                case SetVariableEffect set:
                {
                    var vars = Session!.Variables;
                    vars[set.Name] = set.Delta is { } delta
                        ? new JValue(Number(vars.TryGetValue(set.Name, out var old) ? old : null) + delta)
                        : Value(set.Value) ?? JValue.CreateNull();
                    break;
                }
                case SetResourceEffect res:
                {
                    var seat = ResolveSeat(res.Player, "setResource.player");
                    var def = State.Resources.First(r => r.Id == res.Resource);
                    var values = Session!.Resources[seat];
                    var next = res.Delta is { } delta ? values[res.Resource] + delta : Number(Value(res.Value));
                    if (def.Min is { } min) next = Math.Max(min, next);
                    if (def.Max is { } max) next = Math.Min(max, next);
                    values[res.Resource] = next;
                    break;
                }
                case IfEffect ifEffect:
                    RunEffects(Holds(ifEffect.Condition!) ? ifEffect.Then : ifEffect.Else);
                    break;
                case ForEachEffect each:
                {
                    var items = each.Players
                        ? State.Players.Seats.Select(s => (object)s).ToList()
                        : ItemsIn(ResolveTarget(each.ObjectsIn!, "forEach.objectsIn")).ToList();
                    foreach (var item in items)
                        WithContext(_ctx.With(it: item), () => RunEffects(each.Do));
                    break;
                }
                case MessageEffect message: Log(Template(message.Text)); break;
                case EndTurnEffect _: _pendingEndTurn = true; break;
                case EndPhaseEffect _: _pendingEndPhase = true; break;
                case GotoPhaseEffect go: _pendingGoto = go.Phase; break;
                case WinEffect win: Win(ResolveSeat(win.Player, "win.player")); break;
                default: throw new RuleException(effect.Kind, ErrorCodes.RuleFailed, $"Effect '{effect.Kind}' is not supported.");
            }
        }

        private void Move(MoveEffect move)
        {
            var target = ResolveTarget(move.To, "move.to");
            if (move.Object != null)
            {
                Deliver(Loosen(ResolveTarget(move.Object, "move.object"), target, move.FaceUp), target, move.At, move.FaceUp);
                return;
            }
            var source = ResolveTarget(move.From!, "move.from");
            var count = SourceCount(source);
            var take = move.Take switch
            {
                "all" => count,
                "allButTop" => Math.Max(0, count - 1),
                _ => Math.Min(move.Count, count),
            };
            for (var i = 0; i < take; i++)
            {
                // allButTop takes from the bottom so the top stays.
                var fromBottom = move.Take == "bottom" || move.Take == "allButTop";
                Deliver(TakeOne(source, fromBottom, target, move.FaceUp), target, move.At, move.FaceUp);
            }
        }

        private void Deal(DealEffect deal)
        {
            var source = ResolveTarget(deal.From, "deal.from");
            var seats = deal.Seats.Count == 0
                ? State.Players.Seats.Select(s => s.Id).ToList()
                : deal.Seats.Select((s, i) => ResolveSeat(s, $"deal.seats[{i}]")).ToList();
            for (var round = 0; round < deal.Count; round++)
            {
                foreach (var seatId in seats)
                {
                    if (SourceCount(source) == 0)
                        return;
                    var seat = State.Players.Seats.First(s => s.Id == seatId);
                    var target = deal.ToTag != null
                        ? TaggedFor(seat, deal.ToTag) ?? throw new RuleException("deal.toTag", ErrorCodes.UnknownObject, $"No object tagged '{deal.ToTag}:{seat.Id}'.")
                        : WithContext(_ctx.With(it: seat), () => ResolveTarget(deal.ToZone!, "deal.toZone"));
                    Deliver(TakeOne(source, fromBottom: false, target, !deal.FaceDown), target, "top", !deal.FaceDown);
                }
            }
        }

        private int SourceCount(object source) => source switch
        {
            Deck deck => deck.Cards.Count,
            Zone zone => Table.ObjectsIn(zone.Id).Count(),
            _ => 0,
        };

        /// <summary>Takes the top (or bottom) item off a deck or a zone's stack, as a loose object near the target.</summary>
        private TableObject TakeOne(object source, bool fromBottom, object target, bool? faceUp)
        {
            switch (source)
            {
                case Deck deck:
                {
                    var result = TableCommand(new TakeCommand
                    {
                        ContainerId = deck.Id,
                        FromBottom = fromBottom,
                        Position = PositionOf(target),
                        Rotation = faceUp is { } up ? FaceRotation(target, up) : (Vec3?)null,
                    });
                    return Table.Find(result.CreatedId!)!;
                }
                case Zone zone:
                {
                    var stack = Table.ObjectsIn(zone.Id).OrderByDescending(o => o.Transform.Position.Y).ToList();
                    if (stack.Count == 0)
                        throw new RuleException("move.from", ErrorCodes.ContainerEmpty, $"Zone '{zone.Id}' is empty.");
                    return fromBottom ? stack[stack.Count - 1] : stack[0];
                }
                default:
                    throw new RuleException("move.from", ErrorCodes.WrongObjectType, "Cards can be taken from a deck or a zone.");
            }
        }

        /// <summary>A specific thing to move: a loose object stays as is; a card inside a deck is taken out first.</summary>
        private TableObject Loosen(object thing, object target, bool? faceUp)
        {
            switch (thing)
            {
                case TableObject obj:
                    return obj;
                case CardRef card:
                {
                    var result = TableCommand(new TakeCommand { ContainerId = card.Deck.Id, EntryId = card.Entry.Id, Position = PositionOf(target),
                        Rotation = faceUp is { } up ? FaceRotation(target, up) : (Vec3?)null });
                    return Table.Find(result.CreatedId!)!;
                }
                default:
                    throw new RuleException("move.object", ErrorCodes.WrongObjectType, "Only objects and cards can be moved.");
            }
        }

        private void Deliver(TableObject obj, object target, string at, bool? faceUp)
        {
            switch (target)
            {
                case Deck deck when obj is Card || obj is Deck:
                    TableCommand(new InsertCommand { ObjectId = obj.Id, IntoId = deck.Id, AtBottom = at == "bottom" });
                    break;
                case Bag bag:
                    TableCommand(new InsertCommand { ObjectId = obj.Id, IntoId = bag.Id, AtBottom = at == "bottom" });
                    break;
                case Zone zone:
                {
                    var rotation = faceUp is { } up ? FaceRotation(zone, up) : obj.Transform.Rotation;
                    TableCommand(new MoveCommand { ObjectId = obj.Id, Position = Table.SlotFor(zone), Rotation = rotation });
                    break;
                }
                default:
                    throw new RuleException("move.to", ErrorCodes.InvalidTarget, $"Cannot move a {obj.Type} into that target.");
            }
        }

        private Vec3 PositionOf(object target) => target switch
        {
            Zone zone => Table.SlotFor(zone),
            TableObject obj => obj.Transform.Position,
            _ => Vec3.Zero,
        };

        private static Vec3 FaceRotation(object target, bool faceUp)
        {
            var yaw = target switch { Zone z => z.Transform.Rotation.Y, TableObject o => o.Transform.Rotation.Y, _ => 0f };
            return new Vec3(0f, yaw, faceUp ? 0f : 180f);
        }

        private IEnumerable<object> ItemsIn(object container) => container switch
        {
            Zone zone => Table.ObjectsIn(zone.Id).Cast<object>().ToList(),
            Deck deck => deck.Cards.Select(c => (object)new CardRef(deck, c)).ToList(),
            Bag bag => Table.ContentsOf(bag.Id).Cast<object>().ToList(),
            _ => Enumerable.Empty<object>(),
        };

        private CommandResult TableCommand(Command command)
        {
            command.Actor = _ctx.Actor;
            var result = Table.Execute(command);
            if (!result.Ok)
                throw new RuleException(result.Error!.Path, result.Error.Code, result.Error.Message, result.Error.Suggestion);
            _events.AddRange(result.Events);
            return result;
        }

        private void Log(string line)
        {
            var log = Session!.Log;
            log.Add(line);
            if (log.Count > MaxLog)
                log.RemoveAt(0);
            _events.Add(new GameMessage(line));
        }

        private void SaveRandom()
        {
            if (Session != null)
                Session.RandomState = Table.Random.State;
        }

        // ---- conditions -------------------------------------------------------------------------------------------

        private bool Holds(Condition condition) => condition switch
        {
            ZoneCountCondition zc => Compare(new JValue(CountOf(ResolveTarget(zc.Zone, "zoneCount.zone"))), zc.Op, Value(zc.Value)),
            CompareCondition cmp => Compare(Value(cmp.Left), cmp.Op, Value(cmp.Right)),
            VariableCondition v => Compare(Session!.Variables.TryGetValue(v.Name, out var x) ? x : null, v.Op, Value(v.Value)),
            ResourceCondition r => Compare(new JValue(Session!.Resources[ResolveSeat(r.Player, "resource.player")][r.Resource]), r.Op, Value(r.Value)),
            IsCurrentPlayerCondition p => ResolveSeat(p.Player, "isCurrentPlayer.player") == Session!.CurrentSeat,
            ObjectInCondition oi => Contains(ResolveTarget(oi.Zone, "objectIn.zone"), ResolveTarget(oi.Object, "objectIn.object")),
            HasTagCondition ht => ResolveTarget(ht.Object, "hasTag.object") switch
            {
                TableObject o => o.Tags.Contains(ht.Tag),
                CardRef c => c.Entry.Tags.Contains(ht.Tag),
                _ => false,
            },
            AndCondition and => and.Conditions.All(Holds),
            OrCondition or => or.Conditions.Any(Holds),
            NotCondition not => !Holds(not.Condition!),
            _ => throw new RuleException(condition.Kind, ErrorCodes.RuleFailed, $"Condition '{condition.Kind}' is not supported."),
        };

        private int CountOf(object target) => target switch
        {
            Deck deck => deck.Cards.Count,
            Bag bag => Table.ContentsOf(bag.Id).Count(),
            Zone zone => Table.ObjectsIn(zone.Id).Sum(o => o is Deck d ? d.Cards.Count : 1),
            _ => 1,
        };

        private bool Contains(object container, object thing) => (container, thing) switch
        {
            (Zone zone, TableObject obj) => Table.ZonesOf(obj.Id).Contains(zone.Id),
            (Deck deck, CardRef card) => card.Deck == deck,
            (Bag bag, TableObject obj) => obj.Container == bag.Id,
            _ => false,
        };

        private static bool Compare(JToken? left, string op, JToken? right)
        {
            if (IsNull(left) || IsNull(right))
                return op == "!=" ? !(IsNull(left) && IsNull(right)) : op == "==" && IsNull(left) && IsNull(right);
            if (IsNumber(left!) && IsNumber(right!))
            {
                var a = (double)left!;
                var b = (double)right!;
                return op switch { "==" => a == b, "!=" => a != b, "<" => a < b, "<=" => a <= b, ">" => a > b, ">=" => a >= b, _ => false };
            }
            var c = string.CompareOrdinal(left!.ToString(), right!.ToString());
            return op switch { "==" => c == 0, "!=" => c != 0, "<" => c < 0, "<=" => c <= 0, ">" => c > 0, ">=" => c >= 0, _ => false };
        }

        private static bool IsNull(JToken? t) => t == null || t.Type == JTokenType.Null;
        private static bool IsNumber(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;

        private static double Number(JToken? t) =>
            t != null && (IsNumber(t) || t.Type == JTokenType.String && double.TryParse((string)t!, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                ? (double)t : 0;

        // ---- references -------------------------------------------------------------------------------------------

        /// <summary>A literal, or the value of a <c>$</c> reference (entities become their id).</summary>
        private JToken? Value(JToken? token)
        {
            if (token is JValue { Type: JTokenType.String } v && References.IsReference((string)v!))
                return ToValue(Resolve((string)v!, "value"));
            return token;
        }

        private static JToken? ToValue(object? x) => x switch
        {
            null => null,
            JToken t => t,
            Seat s => new JValue(s.Id),
            TableObject o => new JValue(o.Id),
            Zone z => new JValue(z.Id),
            CardRef c => new JValue(c.Entry.Id ?? c.Entry.Name),
            _ => new JValue(x.ToString()),
        };

        /// <summary>A UI text with its <c>{$ref}</c> parts filled in, as <paramref name="viewer"/> sees it.</summary>
        public string Format(string text, string? viewer) =>
            WithContext(new Context { Actor = viewer ?? Session?.CurrentSeat }, () => Template(text));

        /// <summary>The value of a reference as <paramref name="viewer"/> sees it (a counter), or null.</summary>
        public JToken? Evaluate(string reference, string? viewer) =>
            WithContext(new Context { Actor = viewer ?? Session?.CurrentSeat }, () => ToValue(TryResolve(reference)));

        /// <summary>The seat id a seat reference names for <paramref name="viewer"/>, or null.</summary>
        public string? SeatFor(string reference, string? viewer) =>
            WithContext(new Context { Actor = viewer ?? Session?.CurrentSeat }, () =>
            {
                if (!References.IsReference(reference))
                    return State.Players.Seats.Any(s => s.Id == reference) ? reference : null;
                return TryResolve(reference) is Seat seat ? seat.Id : null;
            });

        private string Template(string text)
        {
            var result = text;
            var start = result.IndexOf("{$", StringComparison.Ordinal);
            while (start >= 0)
            {
                var end = result.IndexOf('}', start);
                if (end < 0)
                    break;
                var reference = result.Substring(start + 1, end - start - 1);
                var value = ToValue(TryResolve(reference))?.ToString() ?? "";
                result = result.Substring(0, start) + value + result.Substring(end + 1);
                start = result.IndexOf("{$", start + value.Length, StringComparison.Ordinal);
            }
            return result;
        }

        private object? TryResolve(string reference)
        {
            try { return Resolve(reference, "message"); }
            catch (RuleException) { return null; }
        }

        private object ResolveTarget(string text, string field)
        {
            if (References.IsReference(text))
                return Resolve(text, field) ?? throw new RuleException(field, ErrorCodes.InvalidReference, $"'{text}' resolves to nothing right now.");
            return (object?)Table.Find(text) ?? (object?)State.Zones.FirstOrDefault(z => z.Id == text) ?? (object?)FindCard(text)
                ?? throw new RuleException(field, ErrorCodes.UnknownObject, $"No object, zone or card '{text}'.");
        }

        private TableObject ResolveObject(string text, string field) =>
            ResolveTarget(text, field) as TableObject ?? throw new RuleException(field, ErrorCodes.WrongObjectType, $"'{text}' is not an object on the table.");

        private string ResolveSeat(string text, string field)
        {
            if (!References.IsReference(text))
                return text;
            return Resolve(text, field) switch
            {
                Seat seat => seat.Id,
                JValue { Type: JTokenType.String } v when State.Players.Seats.Any(s => s.Id == (string)v!) => (string)v!,
                _ => throw new RuleException(field, ErrorCodes.InvalidReference, $"'{text}' is not a seat."),
            };
        }

        private object? Resolve(string reference, string field)
        {
            var parts = References.Parse(reference);
            object? current;
            var i = 1;
            switch (parts[0])
            {
                case "actor":
                case "viewer": current = SeatById(_ctx.Actor); break;
                case "current": current = SeatById(Session?.CurrentSeat); break;
                case "player": current = SeatById(_ctx.Player); break;
                case "it": current = _ctx.It; break;
                case "other":
                {
                    var anchor = (_ctx.It as Seat)?.Id ?? _ctx.Player ?? _ctx.Actor ?? Session?.CurrentSeat;
                    current = anchor == null ? null : SeatById(NextSeat(anchor));
                    break;
                }
                case "param":
                {
                    i = 2;
                    if (!_ctx.Params.TryGetValue(parts[1], out var raw))
                        return null;
                    current = (object?)Table.Find(raw) ?? (object?)SeatById(raw) ?? (object?)FindCard(raw) ?? new JValue(raw);
                    break;
                }
                case "var":
                    i = 2;
                    current = Session != null && Session.Variables.TryGetValue(parts[1], out var v) ? v : State.Variables.TryGetValue(parts[1], out var d) ? d : null;
                    break;
                case "seat": i = 2; current = SeatById(parts[1]); break;
                case "obj": i = 2; current = (object?)Table.Find(parts[1]) ?? State.Zones.FirstOrDefault(z => z.Id == parts[1]); break;
                default: throw new RuleException(field, ErrorCodes.InvalidReference, $"Unknown reference root in '{reference}'.");
            }
            for (; i < parts.Length && current != null; i++)
                current = Step(current, parts[i], parts, ref i);
            return current;
        }

        private object? Step(object current, string part, string[] parts, ref int i)
        {
            switch (current)
            {
                case Seat seat:
                    switch (part)
                    {
                        case "id": return new JValue(seat.Id);
                        case "color": return new JValue(seat.Color);
                        case "hand": return State.Zones.FirstOrDefault(z => z.Id == seat.HandZone);
                        case "res":
                            if (++i >= parts.Length || Session == null)
                                return null;
                            return Session.Resources[seat.Id].TryGetValue(parts[i], out var r) ? new JValue(r) : null;
                        default: return TaggedFor(seat, part);
                    }
                case Deck deck:
                    switch (part)
                    {
                        case "top": return deck.Cards.Count == 0 ? null : new CardRef(deck, deck.Cards[0]);
                        case "bottom": return deck.Cards.Count == 0 ? null : new CardRef(deck, deck.Cards[deck.Cards.Count - 1]);
                        case "count": return new JValue(deck.Cards.Count);
                    }
                    return ObjectProperty(deck, part, parts, ref i);
                case TableObject obj:
                    return ObjectProperty(obj, part, parts, ref i);
                case Zone zone:
                {
                    var stack = Table.ObjectsIn(zone.Id).OrderByDescending(o => o.Transform.Position.Y).ToList();
                    return part switch
                    {
                        "count" => new JValue(CountOf(zone)),
                        "top" => stack.FirstOrDefault(),
                        "bottom" => stack.LastOrDefault(),
                        "id" => new JValue(zone.Id),
                        "name" => zone.Name == null ? null : new JValue(zone.Name),
                        _ => null,
                    };
                }
                case CardRef card:
                    return part switch
                    {
                        "meta" => card.Entry.Meta,
                        "name" => card.Entry.Name == null ? null : new JValue(card.Entry.Name),
                        "id" => card.Entry.Id == null ? null : new JValue(card.Entry.Id),
                        "faceIndex" => new JValue(card.Entry.FaceIndex),
                        _ => null,
                    };
                case JObject jo:
                    return jo[part];
                default:
                    return null;
            }
        }

        private static object? ObjectProperty(TableObject obj, string part, string[] parts, ref int i) => part switch
        {
            "id" => new JValue(obj.Id),
            "name" => obj.Name == null ? null : new JValue(obj.Name),
            "meta" => obj is Card card ? card.Meta : null,
            "faceUp" => new JValue(obj.IsFaceUp),
            "value" => obj is Die { Value: { } value } ? new JValue(value) : null,
            "count" => new JValue(1),
            _ => null,
        };

        private Seat? SeatById(string? id) => id == null ? null : State.Players.Seats.FirstOrDefault(s => s.Id == id);

        /// <summary>The object or zone tagged <c>&lt;tag&gt;:&lt;seat&gt;</c>.</summary>
        private object? TaggedFor(Seat seat, string tag)
        {
            var full = $"{tag}:{seat.Id}";
            return (object?)State.Objects.FirstOrDefault(o => o.Container == null && o.Tags.Contains(full)) ?? State.Zones.FirstOrDefault(z => z.Tags.Contains(full));
        }

        private CardRef? FindCard(string entryId)
        {
            foreach (var deck in State.Objects.OfType<Deck>())
            {
                var entry = deck.Cards.FirstOrDefault(c => c.Id == entryId);
                if (entry != null)
                    return new CardRef(deck, entry);
            }
            return null;
        }

        // ---- parameters -------------------------------------------------------------------------------------------

        private bool ParamValid(ParamDef param, string value, string seat)
        {
            switch (param.Type)
            {
                case ParamType.Seat: return State.Players.Seats.Any(s => s.Id == value);
                case ParamType.Number: return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                case ParamType.Choice: return param.Options.Contains(value);
                default: return Candidates(param, seat).Contains(value);
            }
        }

        private List<string> Candidates(ParamDef param, string seat)
        {
            var filter = param.Filter;
            IEnumerable<object> pool;
            if (filter?.InZone != null)
            {
                var zone = WithContext(new Context { Actor = seat }, () => ResolveTarget(filter.InZone, "filter.inZone"));
                pool = ItemsIn(zone);
            }
            else
            {
                pool = State.Objects.Where(o => o.Container == null);
            }
            return pool
                .Where(x => filter?.Type == null || (x is TableObject o ? o.Type : "Card") == filter.Type)
                .Where(x => filter?.HasTag == null || (x is TableObject o ? o.Tags : ((CardRef)x).Entry.Tags).Contains(filter.HasTag))
                .Select(x => x is TableObject o ? o.Id : ((CardRef)x).Entry.Id)
                .Where(id => id != null)
                .Select(id => id!)
                .ToList();
        }

        private IEnumerable<Dictionary<string, string>> ParamCombinations(ActionDef action, string seat)
        {
            IEnumerable<Dictionary<string, string>> combos = new[] { new Dictionary<string, string>() };
            foreach (var param in action.Params)
            {
                var values = param.Type switch
                {
                    ParamType.Object => Candidates(param, seat),
                    ParamType.Seat => State.Players.Seats.Select(s => s.Id).ToList(),
                    ParamType.Choice => param.Options,
                    _ => new List<string>(),   // free numbers cannot be enumerated
                };
                combos = combos.SelectMany(c => values.Select(v => new Dictionary<string, string>(c) { [param.Name] = v })).ToList();
            }
            return combos;
        }

        private static string? DescribeFilter(ParamDef param)
        {
            var f = param.Filter;
            if (f == null)
                return param.Type == ParamType.Choice ? $"Use one of: {string.Join(", ", param.Options)}." : null;
            var parts = new List<string>();
            if (f.InZone != null) parts.Add($"in {f.InZone}");
            if (f.Type != null) parts.Add($"of type {f.Type}");
            if (f.HasTag != null) parts.Add($"tagged {f.HasTag}");
            return $"It must be an object {string.Join(", ", parts)}.";
        }

        // ---- context ----------------------------------------------------------------------------------------------

        private T WithContext<T>(Context ctx, Func<T> body)
        {
            var saved = _ctx;
            _ctx = ctx;
            try { return body(); }
            finally { _ctx = saved; }
        }

        private void WithContext(Context ctx, Action body) => WithContext(ctx, () => { body(); return 0; });

        private sealed class Context
        {
            public string? Actor;
            public string? Player;
            public object? It;
            public Dictionary<string, string> Params = new Dictionary<string, string>();

            public Context With(object? it) => new Context { Actor = Actor, Player = Player, It = it, Params = Params };
        }

        /// <summary>A card inside a deck, as rules see it.</summary>
        private sealed class CardRef
        {
            public readonly Deck Deck;
            public readonly DeckCard Entry;

            public CardRef(Deck deck, DeckCard entry)
            {
                Deck = deck;
                Entry = entry;
            }
        }

        private sealed class RuleException : Exception
        {
            public readonly ValidationError Error;

            public RuleException(string path, string code, string message, string? suggestion = null) : base(message)
            {
                Error = new ValidationError(path, code, message, suggestion);
            }
        }
    }
}
