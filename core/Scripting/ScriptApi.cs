using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization.Json;

namespace Core.Scripting
{
    /// <summary>The functions scripts see (docs/lua-api.md): TTS names, TTS units, changes as commands.</summary>
    public sealed partial class ScriptHost
    {
        /// <summary>One proxy table per object per script, so <c>a == b</c> holds for the same object.</summary>
        private readonly Dictionary<(Script, string), DynValue> _proxies = new Dictionary<(Script, string), DynValue>();

        private void InstallApi(Script script, string? selfId)
        {
            var g = script.Globals;
            DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => body(args));

            // Strings: string.rep is the cheap way to fill memory.
            var strings = g.Get("string").Table;
            var rep = strings.Get("rep");
            strings["rep"] = Fn(args =>
            {
                var text = args.Count > 0 ? args[0].CastToString() ?? "" : "";
                var times = args.Count > 1 && args[1].Type == DataType.Number ? args[1].Number : 0;
                if (text.Length * Math.Max(0, times) > MaxStringLength)
                    throw new LimitException($"string.rep would build more than {MaxStringLength:N0} characters.");
                return script.Call(rep, args.GetArray());
            });

            if (selfId != null)
                g["self"] = Proxy(script, selfId);

            g["getObjectFromGUID"] = Fn(args => Proxy(script, args.Count > 0 ? args[0].CastToString() : null));
            g["startLuaCoroutine"] = Fn(args =>
            {
                // startLuaCoroutine(owner, "name"): owner is Global or an object; its script runs the function.
                var owner = args.Count > 0 && args[0].Type == DataType.Table ? args[0].Table.Get("guid").CastToString() : null;
                var target = owner != null && _objectScripts.TryGetValue(owner, out var s) ? s : _global ?? script;
                return DynValue.NewBoolean(StartCoroutine(target, args.Count > 1 ? args[1].CastToString() ?? "" : ""));
            });
            g["destroyObject"] = Fn(args =>
            {
                var target = args.Count > 0 && args[0].Type == DataType.Table ? args[0].Table.Get("guid").CastToString() : null;
                return target == null ? DynValue.False : DynValue.NewBoolean(Issue(new DeleteCommand { ObjectId = target }));
            });
            g["getSeatedPlayers"] = Fn(args =>
            {
                var colors = new Table(script);
                var i = 1;
                foreach (var seat in Engine.State.Players.Seats)
                    colors[i++] = Capital(seat.Color);
                return DynValue.NewTable(colors);
            });
            g["getObjects"] = Fn(args => List(script, Engine.State.Objects.Where(o => o.Container == null).Select(o => o.Id)));
            g["getAllObjects"] = g["getObjects"];
            g["getObjectsWithAllTags"] = Fn(args =>
            {
                var tags = args.Count > 0 ? Strings(args[0]) : new List<string>();
                return List(script, Engine.State.Objects.Where(o => o.Container == null && tags.Count > 0 && tags.All(o.Tags.Contains)).Select(o => o.Id));
            });
            g["getObjectsWithAnyTags"] = Fn(args =>
            {
                var tags = args.Count > 0 ? Strings(args[0]) : new List<string>();
                return List(script, Engine.State.Objects.Where(o => o.Container == null && tags.Any(o.Tags.Contains)).Select(o => o.Id));
            });

            // Timer: TTS's older timer API, on the same clock as Wait.
            var timers = new Table(script);
            var named = new Dictionary<string, int>();
            timers["create"] = Fn(args =>
            {
                if (args.Count < 1 || args[0].Type != DataType.Table)
                    return DynValue.False;
                var p = args[0].Table;
                var name = p.Get("function_name").CastToString();
                var ownerId = p.Get("function_owner").Type == DataType.Table ? p.Get("function_owner").Table.Get("guid").CastToString() : null;
                var owner = ownerId != null && _objectScripts.TryGetValue(ownerId, out var o) ? o : _global ?? script;
                var function = name != null ? owner.Globals.Get(name) : DynValue.Nil;
                if (function.Type != DataType.Function)
                    return DynValue.False;
                var parameters = p.Get("parameters");
                var delay = p.Get("delay").Type == DataType.Number ? Math.Max(0, p.Get("delay").Number) : 0;
                // TTS: repetitions 0 repeats forever.
                var repetitions = p.Get("repetitions").Type == DataType.Number ? (int)p.Get("repetitions").Number : 1;
                var timer = new Timer { Id = _nextTimer++, Script = owner, Function = function, Arguments = parameters.IsNil() ? null : new[] { parameters }, Due = _now + delay, Interval = delay, Repetitions = repetitions <= 0 ? int.MaxValue : repetitions };
                var identifier = p.Get("identifier").CastToString() ?? timer.Id.ToString();
                if (named.TryGetValue(identifier, out var old) && _timers.Any(t => t.Id == old))
                    return DynValue.False;
                named[identifier] = timer.Id;
                _timers.Add(timer);
                return DynValue.True;
            });
            timers["destroy"] = Fn(args =>
            {
                var identifier = args.Count > 0 ? args[0].CastToString() : null;
                if (identifier == null || !named.TryGetValue(identifier, out var timerId))
                    return DynValue.False;
                named.Remove(identifier);
                return DynValue.NewBoolean(_timers.RemoveAll(t => t.Id == timerId) > 0);
            });
            g["Timer"] = timers;
            g["getObjectsWithTag"] = Fn(args =>
            {
                var tag = args.Count > 0 ? args[0].CastToString() : null;
                return List(script, Engine.State.Objects.Where(o => o.Container == null && tag != null && o.Tags.Contains(tag)).Select(o => o.Id));
            });
            DynValue Say(CallbackArguments args)
            {
                var text = args.Count > 0 ? args[0].CastToString() : null;
                if (!string.IsNullOrEmpty(text))
                    Broadcast?.Invoke(text!);
                return DynValue.Nil;
            }
            g["broadcastToAll"] = Fn(Say);
            g["printToAll"] = Fn(Say);
            g["broadcastToColor"] = Fn(Say);
            g["printToColor"] = Fn(Say);

            var json = new Table(script);
            json["encode"] = Fn(args => args.Count > 0 && args[0].Type == DataType.Table
                ? DynValue.NewString(JsonTableConverter.TableToJson(args[0].Table))
                : DynValue.NewString(args.Count > 0 ? args[0].ToPrintString() : "null"));
            json["decode"] = Fn(args =>
            {
                var text = args.Count > 0 ? args[0].CastToString() : null;
                if (string.IsNullOrEmpty(text))
                    return DynValue.Nil;
                try
                {
                    return DynValue.NewTable(JsonTableConverter.JsonToTable(text!, script));
                }
                catch (Exception e) when (e is SyntaxErrorException || e is ScriptRuntimeException || e is FormatException)
                {
                    return DynValue.Nil;
                }
            });
            g["JSON"] = json;

            var wait = new Table(script);
            wait["time"] = Fn(args => Schedule(script, args, seconds: true));
            wait["frames"] = Fn(args => Schedule(script, args, seconds: false));
            wait["condition"] = Fn(args =>
            {
                if (args.Count < 2 || args[0].Type != DataType.Function || args[1].Type != DataType.Function)
                    return DynValue.Nil;
                var timeout = args.Count > 2 && args[2].Type == DataType.Number ? _now + args[2].Number : 0;
                var timer = new Timer { Id = _nextTimer++, Script = script, Function = args[0], Condition = args[1], Due = _now, Timeout = timeout };
                _timers.Add(timer);
                return DynValue.NewNumber(timer.Id);
            });
            wait["stop"] = Fn(args =>
            {
                var id = args.Count > 0 && args[0].Type == DataType.Number ? (int)args[0].Number : -1;
                _timers.RemoveAll(t => t.Id == id);
                return DynValue.Nil;
            });
            wait["stopAll"] = Fn(args =>
            {
                _timers.RemoveAll(t => t.Script == script);
                return DynValue.Nil;
            });
            g["Wait"] = wait;

            // Global.call / Global.getVar from object scripts; obj.call goes to an object's script.
            var global = new Table(script);
            int GlobalSelf(CallbackArguments args) => args.Count > 0 && args[0].Type == DataType.Table && args[0].Table == global ? 1 : 0;
            global["call"] = Fn(args => CallIn(_global, script, args, GlobalSelf(args)));
            global["getVar"] = Fn(args => args.Count > GlobalSelf(args) && _global != null ? Copy(_global.Globals.Get(args[GlobalSelf(args)].CastToString() ?? ""), script) : DynValue.Nil);
            global["setVar"] = Fn(args =>
            {
                var i = GlobalSelf(args);
                if (args.Count > i + 1 && _global != null && args[i].CastToString() is { } name)
                    _global.Globals[name] = Copy(args[i + 1], _global);
                return DynValue.Nil;
            });
            global["getTable"] = global["getVar"];
            global["setTable"] = global["setVar"];
            g["Global"] = global;
            g["UI"] = UiApi(script, null);
            g["Player"] = PlayerApi(script);
            g["Physics"] = PhysicsApi(script);
            g["log"] = Fn(args =>
            {
                Log?.Invoke("[log] " + string.Join(" ", args.GetArray().Select(a => a.ToPrintString())));
                return DynValue.Nil;
            });
            json["encode_pretty"] = json["encode"];

            // The notebook (G25): TTS getNotebookTabs / addNotebookTab / editNotebookTab / removeNotebookTab.
            g["getNotebookTabs"] = Fn(args =>
            {
                var tabs = new Table(script);
                var i = 1;
                foreach (var page in Engine.State.Notebook)
                {
                    var tab = new Table(script);
                    tab["index"] = i - 1;
                    tab["title"] = page.Title;
                    tab["body"] = page.Body;
                    tab["color"] = page.Color == null ? "Grey" : Capital(page.Color);
                    tabs[i++] = tab;
                }
                return DynValue.NewTable(tabs);
            });
            NoteTab? TabFrom(DynValue value, NoteTab? existing)
            {
                if (value.Type != DataType.Table)
                    return null;
                var p = value.Table;
                var color = p.Get("color").CastToString() ?? existing?.Color ?? "Grey";
                return new NoteTab
                {
                    Id = existing?.Id ?? "",
                    Title = p.Get("title").CastToString() ?? existing?.Title ?? "",
                    Body = p.Get("body").CastToString() ?? existing?.Body ?? "",
                    Color = string.Equals(color, "Grey", StringComparison.OrdinalIgnoreCase) ? null : color.ToLowerInvariant(),
                };
            }
            g["addNotebookTab"] = Fn(args =>
            {
                var tab = args.Count > 0 ? TabFrom(args[0], null) : null;
                if (tab == null)
                    return DynValue.NewNumber(-1);
                var result = _execute(new SetNoteTabCommand { Tab = tab });
                Count(result);
                return DynValue.NewNumber(result.Ok ? Engine.State.Notebook.FindIndex(t => t.Id == result.CreatedId) : -1);
            });
            g["editNotebookTab"] = Fn(args =>
            {
                var index = args.Count > 0 && args[0].Type == DataType.Table && args[0].Table.Get("index").Type == DataType.Number
                    ? (int)args[0].Table.Get("index").Number : -1;
                if (index < 0 || index >= Engine.State.Notebook.Count)
                    return DynValue.False;
                var tab = TabFrom(args[0], Engine.State.Notebook[index]);
                return tab == null ? DynValue.False : DynValue.NewBoolean(Issue(new SetNoteTabCommand { Tab = tab }));
            });
            g["removeNotebookTab"] = Fn(args =>
            {
                var index = args.Count > 0 && args[0].Type == DataType.Number ? (int)args[0].Number : -1;
                return index < 0 || index >= Engine.State.Notebook.Count
                    ? DynValue.False
                    : DynValue.NewBoolean(Issue(new DeleteNoteTabCommand { TabId = Engine.State.Notebook[index].Id }));
            });
        }

        private DynValue Schedule(Script script, CallbackArguments args, bool seconds)
        {
            if (args.Count < 1 || args[0].Type != DataType.Function)
                return DynValue.Nil;
            var amount = args.Count > 1 && args[1].Type == DataType.Number ? Math.Max(0, args[1].Number) : 0;
            var interval = seconds ? amount : amount / 60.0;
            var repetitions = args.Count > 2 && args[2].Type == DataType.Number ? (int)args[2].Number : 1;
            // TTS: -1 repeats forever.
            if (repetitions < 0)
                repetitions = int.MaxValue;
            var timer = new Timer { Id = _nextTimer++, Script = script, Function = args[0], Due = _now + interval, Interval = interval, Repetitions = Math.Max(1, repetitions) };
            _timers.Add(timer);
            return DynValue.NewNumber(timer.Id);
        }

        /// <summary>Calls a function of another script; only plain values (numbers, strings, tables of them) cross over.</summary>
        private DynValue CallIn(Script? target, Script caller, CallbackArguments args, int nameIndex)
        {
            if (target == null || args.Count <= nameIndex)
                return DynValue.Nil;
            var function = target.Globals.Get(args[nameIndex].CastToString() ?? "");
            if (function.Type != DataType.Function)
                return DynValue.Nil;
            var passed = args.Count > nameIndex + 1 ? new[] { Copy(args[nameIndex + 1], target) } : Array.Empty<DynValue>();
            return RunFor(target, function, "call", passed, out var result) ? Copy(result, caller) : DynValue.Nil;
        }

        /// <summary>A plain value rebuilt in <paramref name="into"/> (tables are owned by one script).</summary>
        private DynValue Copy(DynValue value, Script into, int depth = 0)
        {
            switch (value.Type)
            {
                case DataType.Table when depth < 16:
                {
                    var t = new Table(into);
                    foreach (var pair in value.Table.Pairs)
                        t.Set(Copy(pair.Key, into, depth + 1), Copy(pair.Value, into, depth + 1));
                    return DynValue.NewTable(t);
                }
                case DataType.Number:
                case DataType.String:
                case DataType.Boolean:
                case DataType.Nil:
                    return value;
                default:
                    return DynValue.Nil;
            }
        }

        private DynValue List(Script script, IEnumerable<string> ids)
        {
            var t = new Table(script);
            var i = 1;
            foreach (var id in ids)
                t[i++] = Proxy(script, id);
            return DynValue.NewTable(t);
        }

        /// <summary>The object (or zone) <paramref name="id"/> as this script sees it, or nil.</summary>
        private DynValue Proxy(Script script, string? id)
        {
            if (id == null)
                return DynValue.Nil;
            if (_proxies.TryGetValue((script, id), out var known))
                return Engine.Find(id) != null || Engine.State.Zones.Any(z => z.Id == id) ? known : DynValue.Nil;
            var obj = Engine.Find(id);
            var zone = obj == null ? Engine.State.Zones.FirstOrDefault(z => z.Id == id) : null;
            if (obj == null && zone == null)
                return DynValue.Nil;
            var t = new Table(script);
            DynValue Fn(Func<TableObject, CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) =>
                Engine.Find(id) is { } live ? body(live, args) : DynValue.Nil);
            DynValue Do(Command command) => DynValue.NewBoolean(Issue(command));
            DynValue Str(string? s) => s == null ? DynValue.NewString("") : DynValue.NewString(s);
            // TTS scripts call methods both ways: obj.getName() and obj:getName(); a colon passes the object first.
            int Self(CallbackArguments args) => args.Count > 0 && args[0].Type == DataType.Table && args[0].Table == t ? 1 : 0;
            DynValue At(CallbackArguments args, int i) => args.Count > i + Self(args) ? args[i + Self(args)] : DynValue.Nil;
            string? Arg(CallbackArguments args, int i) => At(args, i).Type == DataType.Nil ? null : At(args, i).CastToString();

            t["getGUID"] = DynValue.NewCallback((_, a) => DynValue.NewString(id));
            t["guid"] = id;
            if (obj != null)
            {
                t["UI"] = UiApi(script, id);
                InstallCollisionMethods(t, id);
            }
            if (zone != null)
            {
                t["getName"] = DynValue.NewCallback((_, a) => Str(zone.Name));
                t["type"] = "Scripting";
                t["getObjects"] = DynValue.NewCallback((_, a) => List(script, Engine.ObjectsIn(id).Select(o => o.Id)));
                t["getPosition"] = DynValue.NewCallback((_, a) => DynValue.NewTable(ToTts(script, zone.Transform.Position)));
                t["hasTag"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(zone.Tags.Contains(Arg(a, 0) ?? "")));
                var zoneProxy = DynValue.NewTable(t);
                _proxies[(script, id)] = zoneProxy;
                return zoneProxy;
            }

            // Arguments of a colon call start with self, so values are at index 1.
            t["type"] = obj!.Type;
            t["getName"] = Fn((o, a) => Str(o.Name));
            t["setName"] = Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Name = Arg(a, 0) ?? "" }));
            t["getDescription"] = Fn((o, a) => Str(o.Description));
            t["setDescription"] = Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Description = Arg(a, 0) ?? "" }));
            t["getGMNotes"] = Fn((o, a) => Str(o.GmNotes));
            t["setGMNotes"] = Fn((o, a) =>
            {
                // GM notes are host data; no command carries them yet, so scripts set them directly (host only).
                o.GmNotes = Arg(a, 0);
                return DynValue.True;
            });
            t["getTags"] = Fn((o, a) =>
            {
                var tags = new Table(script);
                for (var i = 0; i < o.Tags.Count; i++)
                    tags[i + 1] = o.Tags[i];
                return DynValue.NewTable(tags);
            });
            t["hasTag"] = Fn((o, a) => DynValue.NewBoolean(o.Tags.Contains(Arg(a, 0) ?? "")));
            t["addTag"] = Fn((o, a) => Arg(a, 0) is { } add && !o.Tags.Contains(add)
                ? Do(new SetPropertiesCommand { ObjectId = id, Tags = o.Tags.Concat(new[] { add }).ToList() })
                : DynValue.NewBoolean(Arg(a, 0) != null));
            t["removeTag"] = Fn((o, a) => Arg(a, 0) is { } remove && o.Tags.Contains(remove)
                ? Do(new SetPropertiesCommand { ObjectId = id, Tags = o.Tags.Where(x => x != remove).ToList() })
                : DynValue.False);
            t["setTags"] = Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Tags = Strings(At(a, 0)) }));
            t["hasAnyTag"] = Fn((o, a) => DynValue.NewBoolean(o.Tags.Count > 0));
            t["getStateId"] = Fn((o, a) =>
            {
                // TTS numbers states from 1; the other states are listed by number, so this one is the number missing.
                if (o.States.Count == 0)
                    return DynValue.NewNumber(-1);
                var n = 1;
                while (o.States.ContainsKey(n.ToString()))
                    n++;
                return DynValue.NewNumber(n);
            });
            t["isDestroyed"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(Engine.Find(id) == null));
            t["clone"] = Fn((o, a) =>
            {
                var position = At(a, 0).Type == DataType.Table ? FromTts(At(a, 0).Table.Get("position"), true) : null;
                var result = _execute(new CloneCommand { ObjectId = id, Position = position });
                Count(result);
                var added = result.Ok ? result.Events.OfType<ObjectAdded>().FirstOrDefault()?.ObjectId : null;
                return added != null ? Proxy(script, added) : DynValue.Nil;
            });
            t["deal"] = Fn((o, a) =>
            {
                // deal(count[, color]): to that seat, or to every seat.
                var count = At(a, 0).Type == DataType.Number ? Math.Max(1, (int)At(a, 0).Number) : 1;
                var seats = Engine.State.Players.Seats.Where(s => Arg(a, 1) is not { } color || string.Equals(s.Color, color, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Id).ToList();
                return seats.Count == 0 ? DynValue.False : Do(new DealCommand { DeckId = id, Count = count, Seats = seats });
            });
            t["setInvisibleTo"] = Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, InvisibleTo = Strings(At(a, 0)) }));
            t["getInvisibleTo"] = Fn((o, a) =>
            {
                var list = new Table(script);
                for (var i = 0; i < o.InvisibleTo.Count; i++)
                    list[i + 1] = Capital(o.InvisibleTo[i]);
                return DynValue.NewTable(list);
            });
            t["getPosition"] = Fn((o, a) => DynValue.NewTable(ToTts(script, o.Transform.Position)));
            t["getRotation"] = Fn((o, a) => DynValue.NewTable(Vector(script, o.Transform.Rotation)));
            t["setPosition"] = Fn((o, a) => FromTts(At(a, 0), true) is { } p
                ? Do(new MoveCommand { ObjectId = id, Position = p, Rotation = o.Transform.Rotation })
                : DynValue.False);
            t["setPositionSmooth"] = t["setPosition"];
            t["setRotation"] = Fn((o, a) => FromTts(At(a, 0), false) is { } r
                ? Do(new MoveCommand { ObjectId = id, Position = o.Transform.Position, Rotation = r })
                : DynValue.False);
            t["setRotationSmooth"] = t["setRotation"];
            t["flip"] = Fn((o, a) => Do(new FlipCommand { ObjectId = id }));
            t["shuffle"] = Fn((o, a) => Do(new ShuffleCommand { ObjectId = id }));
            t["randomize"] = Fn((o, a) => o is Die ? Do(new RollCommand { ObjectId = id }) : Do(new ShuffleCommand { ObjectId = id }));
            t["roll"] = Fn((o, a) => Do(new RollCommand { ObjectId = id }));
            t["lock"] = Fn((o, a) => Do(new LockCommand { ObjectId = id, Locked = true }));
            t["unlock"] = Fn((o, a) => Do(new LockCommand { ObjectId = id, Locked = false }));
            t["getLock"] = Fn((o, a) => DynValue.NewBoolean(o.Locked));
            t["setLock"] = Fn((o, a) => Do(new LockCommand { ObjectId = id, Locked = At(a, 0).CastToBool() }));
            t["is_face_down"] = Fn((o, a) => DynValue.NewBoolean(!o.IsFaceUp));
            t["destruct"] = Fn((o, a) => Do(new DeleteCommand { ObjectId = id }));
            t["getValue"] = Fn((o, a) => o switch
            {
                Counter c => DynValue.NewNumber(c.Value),
                Die d when d.Value is { } v => DynValue.NewNumber(v),
                _ => DynValue.Nil,
            });
            t["setValue"] = Fn((o, a) => o switch
            {
                Counter _ when At(a, 0).Type == DataType.Number => Do(new SetCounterCommand { ObjectId = id, Value = (int)At(a, 0).Number }),
                Die _ when At(a, 0).Type == DataType.Number => Do(new SetDieValueCommand { ObjectId = id, Value = (int)At(a, 0).Number }),
                _ => DynValue.False,
            });
            t["getQuantity"] = Fn((o, a) => DynValue.NewNumber(o switch
            {
                Deck d => d.Cards.Count,
                Bag b => Engine.ContentsOf(b.Id).Count(),
                _ => Stacks.IsStackable(o) ? Stacks.CountOf(o) : -1,
            }));
            t["getObjects"] = Fn((o, a) =>
            {
                // A deck or bag: what it holds, as TTS returns it ({ name, guid, index }).
                var list = new Table(script);
                var i = 1;
                if (o is Deck deck)
                    foreach (var card in deck.Cards)
                        list[i] = Entry(script, card.Name, card.Id, i++ - 1);
                else if (o is Bag bag)
                    foreach (var item in Engine.ContentsOf(bag.Id))
                        list[i] = Entry(script, item.Name, item.Id, i++ - 1);
                return DynValue.NewTable(list);
            });
            t["takeObject"] = Fn((o, a) =>
            {
                var take = new TakeCommand { ContainerId = id };
                if (At(a, 0).Type == DataType.Table)
                {
                    var p = At(a, 0).Table;
                    if (FromTts(p.Get("position"), true) is { } at)
                        take.Position = at;
                    if (FromTts(p.Get("rotation"), false) is { } rot)
                        take.Rotation = rot;
                    if (p.Get("guid").CastToString() is { } guid)
                        take.EntryId = guid;
                    else if (p.Get("index").Type == DataType.Number)
                        take.Index = (int)p.Get("index").Number;
                    if (p.Get("top").Type == DataType.Boolean && !p.Get("top").Boolean)
                        take.FromBottom = true;
                }
                var result = _execute(take);
                Count(result);
                return result.Ok && result.CreatedId != null ? Proxy(script, result.CreatedId) : DynValue.Nil;
            });
            t["putObject"] = Fn((o, a) =>
            {
                var other = At(a, 0).Type == DataType.Table ? At(a, 0).Table.Get("guid").CastToString() : null;
                return other == null ? DynValue.False : Do(new InsertCommand { ObjectId = other, IntoId = id });
            });
            t["call"] = Fn((o, a) => CallIn(_objectScripts.TryGetValue(id, out var s) ? s : null, script, a, Self(a)));
            t["getVar"] = Fn((o, a) => _objectScripts.TryGetValue(id, out var s) ? Copy(s.Globals.Get(Arg(a, 0) ?? ""), script) : DynValue.Nil);
            t["setVar"] = Fn((o, a) =>
            {
                if (_objectScripts.TryGetValue(id, out var s) && Arg(a, 0) is { } name)
                    s.Globals[name] = Copy(At(a, 1), s);
                return DynValue.Nil;
            });
            t["getLuaScript"] = Fn((o, a) => Str(o.Script));
            t["isSmoothMoving"] = DynValue.NewCallback((_, a) => DynValue.False);
            t["reload"] = Fn((o, a) => Proxy(script, id));
            t["setState"] = Fn((o, a) =>
            {
                // TTS returns the object in its new state, which has its own GUID.
                if (At(a, 0).Type != DataType.Number)
                    return DynValue.Nil;
                var result = _execute(new SetStateCommand { ObjectId = id, State = ((int)At(a, 0).Number).ToString() });
                Count(result);
                var added = result.Ok ? result.Events.OfType<ObjectAdded>().FirstOrDefault()?.ObjectId : null;
                return added != null ? Proxy(script, added) : DynValue.Nil;
            });
            t["getStates"] = Fn((o, a) =>
            {
                var states = new Table(script);
                var i = 1;
                foreach (var pair in o.States.OrderBy(p => p.Key))
                    states[i++] = Entry(script, pair.Value.Name, pair.Value.Id, int.TryParse(pair.Key, out var n) ? n : 0);
                return DynValue.NewTable(states);
            });
            t["getCustomObject"] = Fn((o, a) =>
            {
                // The TTS custom fields that map to ours (image, mesh), as asset paths.
                var custom = new Table(script);
                string? Path(string? assetId) => assetId == null ? null : Engine.State.Assets.FirstOrDefault(x => x.Id == assetId)?.Path;
                switch (o)
                {
                    case Card c: custom["face"] = Path(c.FaceAsset); custom["back"] = Path(c.BackAsset); break;
                    case Token tk: custom["image"] = Path(tk.Asset); break;
                    case Tile tl: custom["image"] = Path(tl.Asset); break;
                    case Model m: custom["mesh"] = Path(m.Asset); custom["diffuse"] = Path(m.DiffuseAsset); break;
                    case Die d: custom["image"] = Path(d.Asset); break;
                }
                return DynValue.NewTable(custom);
            });
            t["setLuaScript"] = Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Script = Arg(a, 0) ?? "" }));
            t["script_state"] = DynValue.Nil;
            // getTable / setTable: TTS's way to move a table between scripts (copied, like getVar).
            t["getTable"] = t["getVar"];
            t["setTable"] = t["setVar"];
            t["getColorTint"] = Fn((o, a) =>
            {
                var c = o.Tint ?? new[] { 1f, 1f, 1f };
                var tint = new Table(script);
                tint["r"] = c[0]; tint["g"] = c[1]; tint["b"] = c[2]; tint["a"] = c.Length > 3 ? c[3] : 1f;
                tint[1] = c[0]; tint[2] = c[1]; tint[3] = c[2];
                return DynValue.NewTable(tint);
            });
            t["setColorTint"] = Fn((o, a) =>
            {
                var c = At(a, 0);
                if (c.Type != DataType.Table)
                    return DynValue.False;
                float Part(string key, int index)
                {
                    var v = c.Table.Get(key);
                    if (v.Type != DataType.Number)
                        v = c.Table.Get(index);
                    return v.Type == DataType.Number ? Math.Max(0f, Math.Min(1f, (float)v.Number)) : 1f;
                }
                return Do(new SetPropertiesCommand { ObjectId = id, Tint = new[] { Part("r", 1), Part("g", 2), Part("b", 3) } });
            });
            t["getScale"] = Fn((o, a) => DynValue.NewTable(Vector(script, o.Transform.Scale)));
            t["setScale"] = Fn((o, a) =>
            {
                // Our objects scale uniformly: the x of the requested scale decides.
                var wanted = FromTts(At(a, 0), false);
                var current = o.Transform.Scale.X;
                return wanted is { } w && w.X > 0 && current > 0 ? Do(new ScaleCommand { ObjectId = id, Factor = w.X / current }) : DynValue.False;
            });
            t["getBounds"] = Fn((o, a) => DynValue.NewTable(Bounds(script, o)));
            t["getBoundsNormalized"] = t["getBounds"];
            t["getZones"] = Fn((o, a) => List(script, Engine.ZonesOf(id)));
            t["positionToWorld"] = Fn((o, a) => FromTts(At(a, 0), false) is { } local
                ? DynValue.NewTable(ToTts(script, Geometry.Add(o.Transform.Position, Geometry.Rotate(o.Transform.Rotation,
                    new Vec3(local.X * TtsUnitsToMeters * o.Transform.Scale.X, local.Y * TtsUnitsToMeters * o.Transform.Scale.Y, local.Z * TtsUnitsToMeters * o.Transform.Scale.Z)))))
                : DynValue.Nil);
            t["positionToLocal"] = Fn((o, a) =>
            {
                if (!(FromTts(At(a, 0), true) is { } world))
                    return DynValue.Nil;
                var local = Geometry.InverseRotate(o.Transform.Rotation, Geometry.Subtract(world, o.Transform.Position));
                var s = o.Transform.Scale;
                return DynValue.NewTable(Vector(script, new Vec3(local.X / TtsUnitsToMeters / s.X, local.Y / TtsUnitsToMeters / s.Y, local.Z / TtsUnitsToMeters / s.Z)));
            });
            // Buttons (TTS createButton): parameters in TTS units, kept as table state in metres.
            t["createButton"] = Fn((o, a) =>
            {
                if (At(a, 0).Type != DataType.Table)
                    return DynValue.False;
                var buttons = o.Buttons.ToList();
                buttons.Add(ReadButton(script, At(a, 0).Table, new ButtonDef()));
                return Do(new SetButtonsCommand { ObjectId = id, Buttons = buttons });
            });
            t["editButton"] = Fn((o, a) =>
            {
                if (At(a, 0).Type != DataType.Table)
                    return DynValue.False;
                var p = At(a, 0).Table;
                var index = p.Get("index").Type == DataType.Number ? (int)p.Get("index").Number : 0;
                if (index < 0 || index >= o.Buttons.Count)
                    return DynValue.False;
                var buttons = o.Buttons.ToList();
                buttons[index] = ReadButton(script, p, Copy(buttons[index]));
                return Do(new SetButtonsCommand { ObjectId = id, Buttons = buttons });
            });
            t["removeButton"] = Fn((o, a) =>
            {
                var index = At(a, 0).Type == DataType.Number ? (int)At(a, 0).Number : -1;
                if (index < 0 || index >= o.Buttons.Count)
                    return DynValue.False;
                var buttons = o.Buttons.ToList();
                buttons.RemoveAt(index);
                return Do(new SetButtonsCommand { ObjectId = id, Buttons = buttons });
            });
            t["clearButtons"] = Fn((o, a) => o.Buttons.Count == 0 ? DynValue.True : Do(new SetButtonsCommand { ObjectId = id }));
            t["getButtons"] = Fn((o, a) =>
            {
                if (o.Buttons.Count == 0)
                    return DynValue.Nil;
                var list = new Table(script);
                for (var i = 0; i < o.Buttons.Count; i++)
                    list[i + 1] = DynValue.NewTable(WriteButton(script, o.Buttons[i], i));
                return DynValue.NewTable(list);
            });

            // Effects of TTS asset bundles (M4) and physics velocity are not simulated here: the calls are accepted and do nothing.
            foreach (var noOp in new[] { "playLoopingEffect", "playTriggerEffect", "setVelocity", "addForce", "addTorque", "highlightOn", "highlightOff" })
                t[noOp] = DynValue.NewCallback((_, a) => DynValue.Nil);
            t["getVelocity"] = DynValue.NewCallback((_, a) => DynValue.NewTable(Vector(script, new Vec3(0f, 0f, 0f))));

            var proxy = DynValue.NewTable(t);
            _proxies[(script, id)] = proxy;
            return proxy;
        }

        private const float TtsUnitsToMeters = Core.Tts.TtsUnits.TtsUnitsToMeters;

        /// <summary>A Lua list of strings (or one string) as a list.</summary>
        private static List<string> Strings(DynValue value)
        {
            var list = new List<string>();
            if (value.Type == DataType.String)
                list.Add(value.String);
            else if (value.Type == DataType.Table)
                foreach (var v in value.Table.Values)
                    if (v.CastToString() is { Length: > 0 } s)
                        list.Add(s);
            return list;
        }

        /// <summary>
        /// TTS button sizes (width, height, font_size) count about 500 per local unit: a button 1000 wide spans a
        /// 2-unit tile. To check against TTS (docs/lua-api.md).
        /// </summary>
        public const float ButtonUnitsPerTtsUnit = 500f;

        private static ButtonDef Copy(ButtonDef b) => new ButtonDef
        {
            Label = b.Label, Click = b.Click, Owner = b.Owner, Position = b.Position, Rotation = b.Rotation, Width = b.Width,
            Height = b.Height, FontSize = b.FontSize, Color = b.Color, FontColor = b.FontColor, Tooltip = b.Tooltip,
        };

        /// <summary>Reads TTS button parameters over <paramref name="b"/> (fields left out keep their value).</summary>
        private ButtonDef ReadButton(Script script, Table p, ButtonDef b)
        {
            if (p.Get("label").Type != DataType.Nil)
                b.Label = p.Get("label").CastToString() ?? "";
            if (p.Get("click_function").Type == DataType.String)
                b.Click = p.Get("click_function").String;
            var owner = p.Get("function_owner");
            if (owner.Type == DataType.Table)
                b.Owner = owner.Table.Get("guid").Type == DataType.String ? owner.Table.Get("guid").String : null;
            if (FromTts(p.Get("position"), false) is { } at)
                b.Position = new Vec3(at.X * TtsUnitsToMeters, at.Y * TtsUnitsToMeters, at.Z * TtsUnitsToMeters);
            if (FromTts(p.Get("rotation"), false) is { } rot)
                b.Rotation = rot;
            float? Size(string key) => p.Get(key).Type == DataType.Number
                ? Math.Max(0f, Math.Min(2f, (float)p.Get(key).Number / ButtonUnitsPerTtsUnit * TtsUnitsToMeters))
                : (float?)null;
            b.Width = Size("width") ?? b.Width;
            b.Height = Size("height") ?? b.Height;
            b.FontSize = Size("font_size") is { } font ? Math.Min(1f, font) : b.FontSize;
            b.Color = ReadColor(p.Get("color")) ?? b.Color;
            b.FontColor = ReadColor(p.Get("font_color")) ?? b.FontColor;
            if (p.Get("tooltip").Type != DataType.Nil)
                b.Tooltip = p.Get("tooltip").CastToString();
            return b;
        }

        private Table WriteButton(Script script, ButtonDef b, int index)
        {
            var t = new Table(script);
            t["index"] = index;
            t["label"] = b.Label;
            t["click_function"] = b.Click;
            t["function_owner"] = b.Owner == null ? script.Globals.Get("Global") : Proxy(script, b.Owner);
            t["position"] = Vector(script, new Vec3(b.Position.X / TtsUnitsToMeters, b.Position.Y / TtsUnitsToMeters, b.Position.Z / TtsUnitsToMeters));
            t["rotation"] = Vector(script, b.Rotation);
            t["width"] = Math.Round(b.Width / TtsUnitsToMeters * ButtonUnitsPerTtsUnit);
            t["height"] = Math.Round(b.Height / TtsUnitsToMeters * ButtonUnitsPerTtsUnit);
            t["font_size"] = Math.Round(b.FontSize / TtsUnitsToMeters * ButtonUnitsPerTtsUnit);
            t["tooltip"] = b.Tooltip ?? "";
            return t;
        }

        /// <summary>TTS's player colours by name (Color.Red and friends).</summary>
        private static readonly Dictionary<string, float[]> NamedColors = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["White"] = new[] { 1f, 1f, 1f }, ["Brown"] = new[] { 0.443f, 0.231f, 0.09f }, ["Red"] = new[] { 0.856f, 0.1f, 0.094f },
            ["Orange"] = new[] { 0.956f, 0.392f, 0.113f }, ["Yellow"] = new[] { 0.905f, 0.898f, 0.172f }, ["Green"] = new[] { 0.192f, 0.701f, 0.168f },
            ["Teal"] = new[] { 0.129f, 0.694f, 0.607f }, ["Blue"] = new[] { 0.118f, 0.53f, 1f }, ["Purple"] = new[] { 0.627f, 0.125f, 0.941f },
            ["Pink"] = new[] { 0.96f, 0.439f, 0.807f }, ["Grey"] = new[] { 0.5f, 0.5f, 0.5f }, ["Black"] = new[] { 0.25f, 0.25f, 0.25f },
        };

        /// <summary>A TTS colour: { r, g, b[, a] } or { 1, 1, 1 }, or a colour name; null when it is neither.</summary>
        private static float[]? ReadColor(DynValue value)
        {
            if (value.Type == DataType.String)
                return NamedColors.TryGetValue(value.String, out var named) ? named : null;
            if (value.Type != DataType.Table)
                return null;
            var c = value.Table;
            float? Part(string key, int index)
            {
                var v = c.Get(key);
                if (v.Type != DataType.Number)
                    v = c.Get(index);
                return v.Type == DataType.Number ? Math.Max(0f, Math.Min(1f, (float)v.Number)) : (float?)null;
            }
            if (!(Part("r", 1) is { } r && Part("g", 2) is { } g && Part("b", 3) is { } b))
                return null;
            return Part("a", 4) is { } alpha ? new[] { r, g, b, alpha } : new[] { r, g, b };
        }

        /// <summary>TTS getBounds: { center, size, offset } in TTS units, from the object's footprint.</summary>
        private static Table Bounds(Script script, TableObject o)
        {
            var size = Footprint.Of(o);
            var height = o is Card || o is Deck ? 0.01f : Math.Min(size.X, size.Y);
            var bounds = new Table(script);
            bounds["center"] = ToTts(script, o.Transform.Position);
            bounds["size"] = Vector(script, new Vec3(size.X / TtsUnitsToMeters, height / TtsUnitsToMeters, size.Y / TtsUnitsToMeters));
            bounds["offset"] = Vector(script, new Vec3(0f, 0f, 0f));
            return bounds;
        }

        private static DynValue Entry(Script script, string? name, string? guid, int index)
        {
            var e = new Table(script);
            e["name"] = name ?? "";
            e["guid"] = guid ?? "";
            e["index"] = index;
            return DynValue.NewTable(e);
        }

        /// <summary>Runs a command for a script; true when the table accepted it.</summary>
        private bool Issue(Command command)
        {
            var result = _execute(command);
            Count(result);
            if (!result.Ok)
                Log?.Invoke($"[script] {command.GetType().Name.Replace("Command", "")} refused: {result.Error?.Message}");
            return result.Ok;
        }

        private void Count(CommandResult result)
        {
            if (result.Ok)
                CommandsIssued++;
        }
    }
}
