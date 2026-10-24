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
            g["getObjects"] = Fn(args => List(script, Engine.State.Objects.Where(o => o.Container == null).Select(o => o.Id)));
            g["getAllObjects"] = g["getObjects"];
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
            g["Global"] = global;
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

            var proxy = DynValue.NewTable(t);
            _proxies[(script, id)] = proxy;
            return proxy;
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
