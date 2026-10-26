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

            // As in TTS, an object inside a bag is not on the table: nil (Pokémon Master Trainer takes its figure out of
            // the bag only when this says so; it clones it otherwise).
            g["getObjectFromGUID"] = Fn(args => args.Count > 0 && args[0].CastToString() is { } guid && Engine.Find(guid) is { Container: { } }
                ? DynValue.Nil
                : Proxy(script, args.Count > 0 ? args[0].CastToString() : null));
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
            g["spawnObject"] = Fn(args => args.Count > 0 && args[0].Type == DataType.Table ? SpawnObject(script, args[0].Table) : DynValue.Nil);
            g["spawnObjectData"] = Fn(args => args.Count > 0 && args[0].Type == DataType.Table ? SpawnObjectData(script, args[0].Table) : DynValue.Nil);
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
                return DynValue.NewBoolean(StopTimers(t => t.Id == timerId) > 0);
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
                var timer = new Timer { Id = _nextTimer++, Script = script, Function = args[0], Condition = args[1], Due = _now, Timeout = timeout,
                    OnTimeout = args.Count > 3 && args[3].Type is DataType.Function or DataType.ClrFunction ? args[3] : null };
                _timers.Add(timer);
                return DynValue.NewNumber(timer.Id);
            });
            wait["stop"] = Fn(args =>
            {
                var id = args.Count > 0 && args[0].Type == DataType.Number ? (int)args[0].Number : -1;
                StopTimers(t => t.Id == id);
                return DynValue.Nil;
            });
            wait["stopAll"] = Fn(args =>
            {
                StopTimers(t => t.Script == script);
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
            // TTS's older capitalised name, still used by mods.
            global["Call"] = global["call"];
            g["Global"] = global;
            g["UI"] = UiApi(script, null);
            // Global.UI is the screen UI; in the Global script self is Global (self.UI, self.call).
            global["UI"] = g["UI"];
            global["getGUID"] = Fn(args => DynValue.NewString("-1"));
            if (selfId == null)
                g["self"] = global;
            g["Player"] = PlayerApi(script);
            g["Physics"] = PhysicsApi(script);
            g["Turns"] = TurnsApi(script);
            g["Game"] = GameApi(script);
            g["Grid"] = GridApi(script);
            g["MusicPlayer"] = MusicApi(script);
            g["Lighting"] = LightingApi(script);
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
            // TTS also has them under Notes (Notes.addNotebookTab …).
            var notes = new Table(script);
            foreach (var name in new[] { "getNotebookTabs", "addNotebookTab", "editNotebookTab", "removeNotebookTab" })
                notes[name] = g[name];
            g["Notes"] = notes;
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
            ProxiesMade++;
            var t = new Table(script);
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
                t["type"] = TtsTag(zone);
                t["tag"] = TtsTag(zone);
                t["getObjects"] = DynValue.NewCallback((_, a) => List(script, Engine.ObjectsIn(id).Select(o => o.Id)));
                t["getPosition"] = DynValue.NewCallback((_, a) => DynValue.NewTable(ToTts(script, zone.Transform.Position)));
                t["hasTag"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(zone.Tags.Contains(Arg(a, 0) ?? "")));
                t["getTags"] = DynValue.NewCallback((_, a) =>
                {
                    var tags = new Table(script);
                    for (var i = 0; i < zone.Tags.Count; i++)
                        tags[i + 1] = zone.Tags[i];
                    return DynValue.NewTable(tags);
                });
                t["getRotation"] = DynValue.NewCallback((_, a) => DynValue.NewTable(Vector(script, zone.Transform.Rotation)));
                // Scripts move their triggers (to follow a board, to park them); the host's command moves the zone.
                DynValue MoveZone(Vec3? position, Vec3? rotation, Vec3? size) =>
                    DynValue.NewBoolean(Issue(new MoveZoneCommand { ZoneId = id, Position = position, Rotation = rotation, Size = size }));
                t["setPosition"] = DynValue.NewCallback((_, a) => FromTts(At(a, 0), true) is { } p ? MoveZone(p, null, null) : DynValue.False);
                t["setPositionSmooth"] = t["setPosition"];
                t["setRotation"] = DynValue.NewCallback((_, a) => FromTts(At(a, 0), false) is { } r ? MoveZone(null, r, null) : DynValue.False);
                t["setRotationSmooth"] = t["setRotation"];
                t["setScale"] = DynValue.NewCallback((_, a) => FromTts(At(a, 0), false) is { } s
                    ? MoveZone(null, null, new Vec3(Math.Abs(s.X) * TtsUnitsToMeters, Math.Abs(s.Y) * TtsUnitsToMeters, Math.Abs(s.Z) * TtsUnitsToMeters))
                    : DynValue.False);
                t["destruct"] = DynValue.False;
                // TTS reaches a layout zone's layout through its component: zone.LayoutZone.layout().
                if (zone is LayoutZone)
                {
                    var layout = new Table(script);
                    layout["layout"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(Issue(new ArrangeZoneCommand { ZoneId = id })));
                    t["LayoutZone"] = layout;
                }
                // A trigger's scale is its box in TTS units.
                t["getScale"] = DynValue.NewCallback((_, a) => DynValue.NewTable(Vector(script,
                    new Vec3(zone.Size.X / TtsUnitsToMeters, zone.Size.Y / TtsUnitsToMeters, zone.Size.Z / TtsUnitsToMeters))));
                t["getBounds"] = DynValue.NewCallback((_, a) =>
                {
                    var bounds = new Table(script);
                    bounds["center"] = ToTts(script, zone.Transform.Position);
                    bounds["size"] = Vector(script, new Vec3(zone.Size.X / TtsUnitsToMeters, zone.Size.Y / TtsUnitsToMeters, zone.Size.Z / TtsUnitsToMeters));
                    bounds["offset"] = Vector(script, new Vec3(0f, 0f, 0f));
                    return DynValue.NewTable(bounds);
                });
                t["getBoundsNormalized"] = t["getBounds"];
                t["getLuaScript"] = DynValue.NewCallback((_, a) => Str(zone.Script));
                t["getVar"] = DynValue.NewCallback((_, a) => _objectScripts.TryGetValue(id, out var s) ? Copy(s.Globals.Get(Arg(a, 0) ?? ""), script) : DynValue.Nil);
                t["call"] = DynValue.NewCallback((_, a) => CallIn(_objectScripts.TryGetValue(id, out var s) ? s : null, script, a, Self(a)));
                t["UI"] = UiApi(script, id);
                var zoneProxy = DynValue.NewTable(t);
                _proxies[(script, id)] = zoneProxy;
                return zoneProxy;
            }

            // The methods come from the metatable, made when a script first uses them.
            t["type"] = obj!.Type;
            // TTS's category of the object, which scripts test (obj.tag == "Card", zone.tag == "Scripting").
            t["tag"] = TtsTag(obj);
            t["script_state"] = DynValue.Nil;
            t.MetaTable = ProxyMethods(script);
            var proxy = DynValue.NewTable(t);
            _proxies[(script, id)] = proxy;
            return proxy;
        }

        /// <summary>Per script: the metatable that gives object proxies their methods, each made on first use.</summary>
        private Table ProxyMethods(Script script)
        {
            if (_proxyMethods.TryGetValue(script, out var meta))
                return meta;
            meta = new Table(script);
            meta["__index"] = DynValue.NewCallback((_, args) =>
            {
                if (args.Count < 2 || args[0].Type != DataType.Table || args[1].Type != DataType.String)
                    return DynValue.Nil;
                var t = args[0].Table;
                if (!(t.RawGet("guid") is { Type: DataType.String } guid))
                    return DynValue.Nil;
                // TTS member variables (obj.resting, obj.is_face_down) are read each time, never kept on the proxy.
                if (LiveProperty(script, guid.String, args[1].String) is { } property)
                    return property;
                var method = ObjectMethod(script, guid.String, t, args[1].String);
                if (!method.IsNil())
                    t.Set(args[1], method);
                return method;
            });
            _proxyMethods[script] = meta;
            return meta;
        }

        private readonly Dictionary<Script, Table> _proxyMethods = new Dictionary<Script, Table>();

        /// <summary>Objects a player is holding, by their colour (picked up and not yet dropped).</summary>
        private readonly Dictionary<string, string> _heldBy = new Dictionary<string, string>();

        /// <summary>
        /// The TTS member variables the local mods read, as they are now; null for a name that is not one. Physics runs
        /// on the view, so an object rests unless a player holds it, and nothing is ever still spawning or loading here
        /// (a spawn is done when the command returns). Scripts poll these in <c>Wait.condition</c> every frame.
        /// </summary>
        private DynValue? LiveProperty(Script script, string id, string name)
        {
            switch (name)
            {
                case "resting": case "is_face_down": case "spawning": case "loading_custom": case "interactable":
                case "held_by_color": case "tooltip": case "mass":
                    break;
                default:
                    return null;
            }
            if (!(Engine.Find(id) is { } live))
                return DynValue.Nil;
            return name switch
            {
                "resting" => DynValue.NewBoolean(!_heldBy.ContainsKey(id) && IsMoving?.Invoke(id) != true),
                "is_face_down" => DynValue.NewBoolean(!live.IsFaceUp),
                "spawning" => DynValue.False,
                "loading_custom" => DynValue.False,
                "interactable" => DynValue.True,
                "held_by_color" => _heldBy.TryGetValue(id, out var color) ? DynValue.NewString(color) : DynValue.Nil,
                "tooltip" => DynValue.NewBoolean(live.Tooltip),
                _ => DynValue.NewNumber(1),
            };
        }

        /// <summary>
        /// The method <paramref name="name"/> of the object proxy <paramref name="t"/> (object <paramref name="id"/>), or nil.
        /// Proxies are made for every object a script touches (Gloomhaven's scripts walk thousands), so their methods are
        /// built only when a script uses them.
        /// </summary>
        private DynValue ObjectMethod(Script script, string id, Table t, string name)
        {
            DynValue Fn(Func<TableObject, CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) =>
                Engine.Find(id) is { } live ? body(live, args) : DynValue.Nil);
            DynValue Do(Command command) => DynValue.NewBoolean(Issue(command));
            DynValue Str(string? s) => s == null ? DynValue.NewString("") : DynValue.NewString(s);
            // TTS scripts call methods both ways: obj.getName() and obj:getName(); a colon passes the object first.
            int Self(CallbackArguments args) => args.Count > 0 && args[0].Type == DataType.Table && args[0].Table == t ? 1 : 0;
            DynValue At(CallbackArguments args, int i) => args.Count > i + Self(args) ? args[i + Self(args)] : DynValue.Nil;
            string? Arg(CallbackArguments args, int i) => At(args, i).Type == DataType.Nil ? null : At(args, i).CastToString();

            switch (name)
            {
                case "UI":
                    return UiApi(script, id);
                case "Book":
                {
                    // TTS's PDF component: getPage(offset) and setPage(page, offset); with offset true, page numbers count from the PDF's page offset.
                    if (!(Engine.Find(id) is Pdf))
                        return DynValue.Nil;
                    var book = new Table(script);
                    int Skip(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table && a[0].Table == book ? 1 : 0;
                    DynValue BookAt(CallbackArguments a, int i) => a.Count > i + Skip(a) ? a[i + Skip(a)] : DynValue.Nil;
                    book["getPage"] = DynValue.NewCallback((_, a) => Engine.Find(id) is Pdf pdf
                        ? DynValue.NewNumber(pdf.Page + (BookAt(a, 0).CastToBool() ? pdf.PageOffset : 0))
                        : DynValue.Nil);
                    book["setPage"] = DynValue.NewCallback((_, a) =>
                    {
                        if (!(Engine.Find(id) is Pdf pdf) || BookAt(a, 0).Type != DataType.Number)
                            return DynValue.False;
                        var page = (int)BookAt(a, 0).Number - (BookAt(a, 1).CastToBool() ? pdf.PageOffset : 0);
                        return DynValue.NewBoolean(Issue(new SetPageCommand { ObjectId = id, Page = Math.Max(0, page) }));
                    });
                    return DynValue.NewTable(book);
                }
                case "registerCollisions":
                    return CollisionMethod(id, register: true);
                case "unregisterCollisions":
                    return CollisionMethod(id, register: false);
                case "getName":
                    return Fn((o, a) => Str(o.Name));
                case "setName":
                    return Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Name = Arg(a, 0) ?? "" }));
                case "getDescription":
                    return Fn((o, a) => Str(o.Description));
                case "setDescription":
                    return Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Description = Arg(a, 0) ?? "" }));
                case "getGMNotes":
                    return Fn((o, a) => Str(o.GmNotes));
                case "setGMNotes":
                    return Fn((o, a) =>
                    {
                        // GM notes are host data; no command carries them yet, so scripts set them directly (host only).
                        o.GmNotes = Arg(a, 0);
                        return DynValue.True;
                    });
                case "getTags":
                    return Fn((o, a) =>
                    {
                        var tags = new Table(script);
                        for (var i = 0; i < o.Tags.Count; i++)
                            tags[i + 1] = o.Tags[i];
                        return DynValue.NewTable(tags);
                    });
                case "hasTag":
                    return Fn((o, a) => DynValue.NewBoolean(o.Tags.Contains(Arg(a, 0) ?? "")));
                case "addTag":
                    return Fn((o, a) => Arg(a, 0) is { } add && !o.Tags.Contains(add)
                        ? Do(new SetPropertiesCommand { ObjectId = id, Tags = o.Tags.Concat(new[] { add }).ToList() })
                        : DynValue.NewBoolean(Arg(a, 0) != null));
                case "removeTag":
                    return Fn((o, a) => Arg(a, 0) is { } remove && o.Tags.Contains(remove)
                        ? Do(new SetPropertiesCommand { ObjectId = id, Tags = o.Tags.Where(x => x != remove).ToList() })
                        : DynValue.False);
                case "setTags":
                    return Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Tags = Strings(At(a, 0)) }));
                case "hasAnyTag":
                    return Fn((o, a) => DynValue.NewBoolean(o.Tags.Count > 0));
                case "getStateId":
                    return Fn((o, a) =>
                    {
                        // TTS numbers states from 1; the other states are listed by number, so this one is the number missing.
                        if (o.States.Count == 0)
                            return DynValue.NewNumber(-1);
                        var n = 1;
                        while (o.States.ContainsKey(n.ToString()))
                            n++;
                        return DynValue.NewNumber(n);
                    });
                case "jointTo":
                    // TTS jointTo(other, { type, collision, ... }); jointTo() with nothing frees it.
                    return Fn((o, a) =>
                    {
                        if (At(a, 0).Type != DataType.Table)
                            return Do(new SetJointCommand { ObjectId = id });
                        var other = At(a, 0).Table.Get("guid").CastToString();
                        var p = At(a, 1).Type == DataType.Table ? At(a, 1).Table : null;
                        var type = (p?.Get("type").CastToString() ?? "Fixed") switch { "Hinge" => JointType.Hinge, "Spring" => JointType.Spring, _ => JointType.Fixed };
                        return other == null ? DynValue.False : Do(new SetJointCommand { ObjectId = id, To = other, Type = type, Collision = p?.Get("collision").CastToBool() ?? false });
                    });
                case "getJoints":
                    return Fn((o, a) =>
                    {
                        var list = new Table(script);
                        if (o.Joint is { } joint)
                        {
                            var entry = new Table(script);
                            entry["type"] = joint.Type.ToString();
                            entry["joint_object_guid"] = joint.To;
                            entry["collision"] = joint.Collision;
                            list[1] = entry;
                        }
                        return DynValue.NewTable(list);
                    });
                case "addAttachment":
                    return Fn((o, a) =>
                    {
                        var other = At(a, 0).Type == DataType.Table ? At(a, 0).Table.Get("guid").CastToString() : null;
                        return other == null ? DynValue.False : Do(new SetJointCommand { ObjectId = other, To = id, Attached = true });
                    });
                case "getAttachments":
                    return Fn((o, a) => List(script, Engine.State.Objects.Where(x => x.Joint is { Attached: true } j && j.To == id).Select(x => x.Id)));
                case "removeAttachments":
                    // TTS gives back the objects it let go of.
                    return Fn((o, a) =>
                    {
                        var freed = Engine.State.Objects.Where(x => x.Joint is { Attached: true } j && j.To == id).Select(x => x.Id).ToList();
                        foreach (var other in freed)
                            Issue(new SetJointCommand { ObjectId = other });
                        return List(script, freed);
                    });
                case "getData":
                    // TTS object data: our own object JSON, with the TTS names scripts read and edit.
                    return Fn((o, a) => DynValue.NewTable(DataOf(script, o)));
                case "reset":
                    // TTS: resetting a bag (or infinite bag) clears what it holds.
                    return Fn((o, a) =>
                    {
                        return o is Bag ? Do(new DeleteCommand { ObjectId = id, Contents = true }) : DynValue.False;
                    });
                case "isDestroyed":
                    return DynValue.NewCallback((_, a) => DynValue.NewBoolean(Engine.Find(id) == null));
                case "clone":
                    return Fn((o, a) =>
                    {
                        var position = At(a, 0).Type == DataType.Table ? FromTts(At(a, 0).Table.Get("position"), true) : null;
                        var result = _execute(new CloneCommand { ObjectId = id, Position = position });
                        Count(result);
                        var added = result.Ok ? result.Events.OfType<ObjectAdded>().FirstOrDefault()?.ObjectId : null;
                        return added != null ? Proxy(script, added) : DynValue.Nil;
                    });
                case "deal":
                    return Fn((o, a) =>
                    {
                        // deal(count[, color]): to that seat, or to every seat.
                        var count = At(a, 0).Type == DataType.Number ? Math.Max(1, (int)At(a, 0).Number) : 1;
                        var seats = Engine.State.Players.Seats.Where(s => Arg(a, 1) is not { } color || string.Equals(s.Color, color, StringComparison.OrdinalIgnoreCase))
                            .Select(s => s.Id).ToList();
                        return seats.Count == 0 ? DynValue.False : Do(new DealCommand { DeckId = id, Count = count, Seats = seats });
                    });
                case "setInvisibleTo":
                    return Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, InvisibleTo = Strings(At(a, 0)) }));
                case "getInvisibleTo":
                    return Fn((o, a) =>
                    {
                        var list = new Table(script);
                        for (var i = 0; i < o.InvisibleTo.Count; i++)
                            list[i + 1] = Capital(o.InvisibleTo[i]);
                        return DynValue.NewTable(list);
                    });
                case "getPosition":
                    return Fn((o, a) => DynValue.NewTable(ToTts(script, o.Transform.Position)));
                case "getRotation":
                    return Fn((o, a) => DynValue.NewTable(Vector(script, o.Transform.Rotation)));
                // The object's local axes in the world, unit length (TTS and our axes agree: y up, z forward).
                case "getTransformUp":
                    return Fn((o, a) => DynValue.NewTable(Vector(script, Geometry.Rotate(o.Transform.Rotation, new Vec3(0, 1, 0)))));
                case "getTransformForward":
                    return Fn((o, a) => DynValue.NewTable(Vector(script, Geometry.Rotate(o.Transform.Rotation, new Vec3(0, 0, 1)))));
                case "getTransformRight":
                    return Fn((o, a) => DynValue.NewTable(Vector(script, Geometry.Rotate(o.Transform.Rotation, new Vec3(1, 0, 0)))));
                case "setPosition": case "setPositionSmooth":
                    return Fn((o, a) => FromTts(At(a, 0), true) is { } p
                        ? Do(new MoveCommand { ObjectId = id, Position = p, Rotation = o.Transform.Rotation, EvenLocked = true })
                        : DynValue.False);
                case "setRotation": case "setRotationSmooth":
                    return Fn((o, a) => FromTts(At(a, 0), false) is { } r
                        ? Do(new MoveCommand { ObjectId = id, Position = o.Transform.Position, Rotation = r, EvenLocked = true })
                        : DynValue.False);
                case "flip":
                    return Fn((o, a) => Do(new FlipCommand { ObjectId = id }));
                case "shuffle":
                    return Fn((o, a) => Do(new ShuffleCommand { ObjectId = id }));
                case "randomize":
                    return Fn((o, a) => o is Die ? Do(new RollCommand { ObjectId = id }) : Do(new ShuffleCommand { ObjectId = id }));
                case "roll":
                    return Fn((o, a) => Do(new RollCommand { ObjectId = id }));
                case "lock":
                    return Fn((o, a) => Do(new LockCommand { ObjectId = id, Locked = true }));
                case "unlock":
                    return Fn((o, a) => Do(new LockCommand { ObjectId = id, Locked = false }));
                case "getLock":
                    return Fn((o, a) => DynValue.NewBoolean(o.Locked));
                case "setLock":
                    return Fn((o, a) => Do(new LockCommand { ObjectId = id, Locked = At(a, 0).CastToBool() }));
                case "destruct": case "destroyObject":
                    return Fn((o, a) => Do(new DeleteCommand { ObjectId = id }));
                case "getValue":
                    return Fn((o, a) => o switch
                    {
                        Counter c => DynValue.NewNumber(c.Value),
                        Die d when d.Value is { } v => DynValue.NewNumber(v),
                        _ => DynValue.Nil,
                    });
                case "setValue":
                    return Fn((o, a) => o switch
                    {
                        Counter _ when At(a, 0).Type == DataType.Number => Do(new SetCounterCommand { ObjectId = id, Value = (int)At(a, 0).Number }),
                        Die _ when At(a, 0).Type == DataType.Number => Do(new SetDieValueCommand { ObjectId = id, Value = (int)At(a, 0).Number }),
                        _ => DynValue.False,
                    });
                case "getQuantity":
                    return Fn((o, a) => DynValue.NewNumber(o switch
                    {
                        Deck d => d.Cards.Count,
                        Bag b => Engine.ContentsOf(b.Id).Count(),
                        _ => Stacks.IsStackable(o) ? Stacks.CountOf(o) : -1,
                    }));
                case "getObjects":
                    return Fn((o, a) =>
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
                case "takeObject":
                    return Fn((o, a) =>
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
                        if (!result.Ok || result.CreatedId == null)
                            return DynValue.Nil;
                        var taken = Proxy(script, result.CreatedId);
                        // TTS calls callback_function(taken) once the object is out (Pokémon Master Trainer spawns its figures so).
                        var callback = At(a, 0).Type == DataType.Table ? At(a, 0).Table.Get("callback_function") : DynValue.Nil;
                        if (callback.Type == DataType.Function || callback.Type == DataType.ClrFunction)
                            _timers.Add(new Timer { Id = _nextTimer++, Script = script, Function = callback, Arguments = new[] { taken }, Due = _now });
                        return taken;
                    });
                case "putObject":
                    return Fn((o, a) =>
                    {
                        var other = At(a, 0).Type == DataType.Table ? At(a, 0).Table.Get("guid").CastToString() : null;
                        return other == null ? DynValue.False : Do(new InsertCommand { ObjectId = other, IntoId = id, EvenLocked = true });
                    });
                case "call":
                    return Fn((o, a) => CallIn(_objectScripts.TryGetValue(id, out var s) ? s : null, script, a, Self(a)));
                case "getVar": case "getTable":
                    return Fn((o, a) => _objectScripts.TryGetValue(id, out var s) ? Copy(s.Globals.Get(Arg(a, 0) ?? ""), script) : DynValue.Nil);
                case "setVar": case "setTable":
                    return Fn((o, a) =>
                    {
                        if (_objectScripts.TryGetValue(id, out var s) && Arg(a, 0) is { } name)
                            s.Globals[name] = Copy(At(a, 1), s);
                        return DynValue.Nil;
                    });
                case "getLuaScript":
                    return Fn((o, a) => Str(o.Script));
                case "isSmoothMoving":
                    return DynValue.NewCallback((_, a) => DynValue.False);
                case "reload":
                    return Fn((o, a) => Proxy(script, id));
                case "setState":
                    return Fn((o, a) =>
                    {
                        // TTS returns the object in its new state, which has its own GUID.
                        if (At(a, 0).Type != DataType.Number)
                            return DynValue.Nil;
                        var result = _execute(new SetStateCommand { ObjectId = id, State = ((int)At(a, 0).Number).ToString() });
                        Count(result);
                        var added = result.Ok ? result.Events.OfType<ObjectAdded>().FirstOrDefault()?.ObjectId : null;
                        return added != null ? Proxy(script, added) : DynValue.Nil;
                    });
                case "getStates":
                    return Fn((o, a) =>
                    {
                        var states = new Table(script);
                        var i = 1;
                        foreach (var pair in o.States.OrderBy(p => p.Key))
                            states[i++] = Entry(script, pair.Value.Name, pair.Value.Id, int.TryParse(pair.Key, out var n) ? n : 0);
                        return DynValue.NewTable(states);
                    });
                case "getCustomObject":
                    return Fn((o, a) =>
                    {
                        // The TTS custom fields that map to ours (image, mesh): their web address, as TTS gives, or their path.
                        var custom = new Table(script);
                        string? Path(string? assetId) => assetId == null ? null
                            : Engine.State.Assets.FirstOrDefault(x => x.Id == assetId) is { } asset ? asset.Url ?? asset.Path : null;
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
                case "setCustomObject":
                    return Fn((o, a) =>
                    {
                        // TTS setCustomObject({ image, face, back, mesh, diffuse, ... }) with web addresses; reload() is not needed here.
                        if (At(a, 0).Type != DataType.Table)
                            return DynValue.False;
                        var p = At(a, 0).Table;
                        var added = new List<Asset>();
                        string? Image(string key) => UrlAsset(p.Get(key).CastToString(), AssetType.Image, added)?.Id;
                        var command = new SetCustomCommand
                        {
                            ObjectId = id,
                            Image = Image("image"),
                            Face = Image("face"),
                            Back = Image("back"),
                            Mesh = UrlAsset(p.Get("mesh").CastToString(), AssetType.Model, added)?.Id,
                            Diffuse = Image("diffuse"),
                            Assets = added,
                        };
                        return Do(command);
                    });
                case "setLuaScript":
                    return Fn((o, a) => Do(new SetPropertiesCommand { ObjectId = id, Script = Arg(a, 0) ?? "" }));
                case "getColorTint":
                    return Fn((o, a) =>
                    {
                        var c = o.Tint ?? new[] { 1f, 1f, 1f };
                        var tint = new Table(script);
                        tint["r"] = c[0]; tint["g"] = c[1]; tint["b"] = c[2]; tint["a"] = c.Length > 3 ? c[3] : 1f;
                        if (script.Globals.Get(LuaPrelude.ColorMeta) is { Type: DataType.Table } meta)
                            tint.MetaTable = meta.Table;
                        else
                        {
                            tint[1] = c[0]; tint[2] = c[1]; tint[3] = c[2];
                        }
                        return DynValue.NewTable(tint);
                    });
                case "setColorTint":
                    return Fn((o, a) =>
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
                        // Alpha too: scripts fade pieces or hide them with a transparent tint.
                        var alpha = Part("a", 4);
                        var rgb = new[] { Part("r", 1), Part("g", 2), Part("b", 3) };
                        return Do(new SetPropertiesCommand { ObjectId = id, Tint = alpha < 0.999f ? rgb.Append(alpha).ToArray() : rgb });
                    });
                case "getScale":
                    return Fn((o, a) => DynValue.NewTable(Vector(script, o.Transform.Scale)));
                case "setScale":
                    return Fn((o, a) =>
                    {
                        // Our objects scale uniformly: the x of the requested scale decides.
                        var wanted = FromTts(At(a, 0), false);
                        var current = o.Transform.Scale.X;
                        return wanted is { } w && w.X > 0 && current > 0 ? Do(new ScaleCommand { ObjectId = id, Factor = w.X / current, EvenLocked = true }) : DynValue.False;
                    });
                // TTS scale(n) or scale({x, y, z}) multiplies the current scale (Pokémon Master Trainer grows its figures so).
                case "scale":
                    return Fn((o, a) =>
                    {
                        var factor = At(a, 0).Type == DataType.Number ? (float)At(a, 0).Number : FromTts(At(a, 0), false)?.X ?? 0f;
                        return factor > 0 ? Do(new ScaleCommand { ObjectId = id, Factor = factor, EvenLocked = true }) : DynValue.False;
                    });
                case "getBounds":
                    return Fn((o, a) => DynValue.NewTable(Bounds(script, o, normalized: false)));
                case "getBoundsNormalized":
                    return Fn((o, a) => DynValue.NewTable(Bounds(script, o, normalized: true)));
                case "getZones":
                    return Fn((o, a) => List(script, Engine.ZonesOf(id)));
                case "positionToWorld":
                    return Fn((o, a) => FromTts(At(a, 0), false) is { } local
                        ? DynValue.NewTable(ToTts(script, Geometry.Add(o.Transform.Position, Geometry.Rotate(o.Transform.Rotation,
                            new Vec3(local.X * TtsUnitsToMeters * o.Transform.Scale.X, local.Y * TtsUnitsToMeters * o.Transform.Scale.Y, local.Z * TtsUnitsToMeters * o.Transform.Scale.Z)))))
                        : DynValue.Nil);
                case "positionToLocal":
                    return Fn((o, a) =>
                    {
                        if (!(FromTts(At(a, 0), true) is { } world))
                            return DynValue.Nil;
                        var local = Geometry.InverseRotate(o.Transform.Rotation, Geometry.Subtract(world, o.Transform.Position));
                        var s = o.Transform.Scale;
                        return DynValue.NewTable(Vector(script, new Vec3(local.X / TtsUnitsToMeters / s.X, local.Y / TtsUnitsToMeters / s.Y, local.Z / TtsUnitsToMeters / s.Z)));
                    });
                // Buttons (TTS createButton): parameters in TTS units, kept as table state in metres.
                case "createButton":
                    return Fn((o, a) =>
                    {
                        if (At(a, 0).Type != DataType.Table)
                            return DynValue.False;
                        var buttons = o.Buttons.ToList();
                        buttons.Add(ReadButton(script, At(a, 0).Table, new ButtonDef()));
                        return Do(new SetButtonsCommand { ObjectId = id, Buttons = buttons });
                    });
                case "editButton":
                    return Fn((o, a) =>
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
                case "removeButton":
                    return Fn((o, a) =>
                    {
                        var index = At(a, 0).Type == DataType.Number ? (int)At(a, 0).Number : -1;
                        if (index < 0 || index >= o.Buttons.Count)
                            return DynValue.False;
                        var buttons = o.Buttons.ToList();
                        buttons.RemoveAt(index);
                        return Do(new SetButtonsCommand { ObjectId = id, Buttons = buttons });
                    });
                case "clearButtons":
                    return Fn((o, a) => o.Buttons.Count == 0 ? DynValue.True : Do(new SetButtonsCommand { ObjectId = id }));
                case "createInput":
                    return Fn((o, a) =>
                    {
                        if (At(a, 0).Type != DataType.Table)
                            return DynValue.False;
                        var inputs = o.Inputs.ToList();
                        inputs.Add(ReadInput(At(a, 0).Table, new InputDef()));
                        return Do(new SetInputsCommand { ObjectId = id, Inputs = inputs });
                    });
                case "editInput":
                    return Fn((o, a) =>
                    {
                        if (At(a, 0).Type != DataType.Table)
                            return DynValue.False;
                        var p = At(a, 0).Table;
                        var index = p.Get("index").Type == DataType.Number ? (int)p.Get("index").Number : 0;
                        if (index < 0 || index >= o.Inputs.Count)
                            return DynValue.False;
                        var inputs = o.Inputs.ToList();
                        inputs[index] = ReadInput(p, CopyInput(inputs[index]));
                        return Do(new SetInputsCommand { ObjectId = id, Inputs = inputs });
                    });
                case "removeInput":
                    return Fn((o, a) =>
                    {
                        var index = At(a, 0).Type == DataType.Number ? (int)At(a, 0).Number : -1;
                        if (index < 0 || index >= o.Inputs.Count)
                            return DynValue.False;
                        var inputs = o.Inputs.ToList();
                        inputs.RemoveAt(index);
                        return Do(new SetInputsCommand { ObjectId = id, Inputs = inputs });
                    });
                case "clearInputs":
                    return Fn((o, a) => o.Inputs.Count == 0 ? DynValue.True : Do(new SetInputsCommand { ObjectId = id }));
                case "getInputs":
                    return Fn((o, a) =>
                    {
                        if (o.Inputs.Count == 0)
                            return DynValue.Nil;
                        var list = new Table(script);
                        for (var i = 0; i < o.Inputs.Count; i++)
                        {
                            var input = o.Inputs[i];
                            var e = WriteButton(script, new ButtonDef
                            {
                                Label = input.Label, Click = input.InputFunction, Owner = input.Owner, Position = input.Position, Rotation = input.Rotation,
                                Width = input.Width, Height = input.Height, FontSize = input.FontSize, Tooltip = input.Tooltip,
                            }, i);
                            e["input_function"] = input.InputFunction;
                            e["click_function"] = DynValue.Nil;
                            e["value"] = input.Value;
                            e["alignment"] = input.Alignment;
                            e["validation"] = input.Validation;
                            list[i + 1] = e;
                        }
                        return DynValue.NewTable(list);
                    });
                case "getButtons":
                    return Fn((o, a) =>
                    {
                        if (o.Buttons.Count == 0)
                            return DynValue.Nil;
                        var list = new Table(script);
                        for (var i = 0; i < o.Buttons.Count; i++)
                            list[i + 1] = DynValue.NewTable(WriteButton(script, o.Buttons[i], i));
                        return DynValue.NewTable(list);
                    });
                // Asset-bundle effects (ADR 0008), also reachable straight on the object as older scripts call them.
                case "playLoopingEffect":
                    return Fn((o, a) => At(a, 0).Type == DataType.Number ? Do(new PlayEffectCommand { ObjectId = id, Index = (int)At(a, 0).Number }) : DynValue.False);
                case "playTriggerEffect":
                    return Fn((o, a) => At(a, 0).Type == DataType.Number ? Do(new PlayEffectCommand { ObjectId = id, Index = (int)At(a, 0).Number, Looping = false }) : DynValue.False);
                case "getLoopingEffects":
                    return Fn((o, a) => Effects(script, id, looping: true));
                case "getTriggerEffects":
                    return Fn((o, a) => Effects(script, id, looping: false));
                // Physics velocity is not simulated here: the calls are accepted and do nothing.
                case "setVelocity":
                    return DynValue.NewCallback((_, a) => DynValue.Nil);
                case "setAngularVelocity":
                    return DynValue.NewCallback((_, a) => DynValue.Nil);
                case "addForce":
                    return DynValue.NewCallback((_, a) => DynValue.Nil);
                case "addTorque":
                    return DynValue.NewCallback((_, a) => DynValue.Nil);
                case "highlightOn":
                    return DynValue.NewCallback((_, a) => DynValue.Nil);
                case "highlightOff":
                    return DynValue.NewCallback((_, a) => DynValue.Nil);
                case "getVelocity":
                    return DynValue.NewCallback((_, a) => DynValue.NewTable(Vector(script, new Vec3(0f, 0f, 0f))));
                // Asset-bundle effects (M4) and Unity components are not scripted yet: accepted, without effect.
                case "AssetBundle":
                    return DynValue.NewTable(EffectsOf(script, id));
                case "getComponent":
                    return DynValue.NewCallback((_, a) =>
                    {
                        var component = new Table(script);
                        var name = Arg(a, 0) ?? "";
                        // The renderer's "enabled" shows or hides the object's shape; other components are not scripted.
                        var renderer = name.EndsWith("Renderer", StringComparison.Ordinal);
                        int At2(CallbackArguments b) => b.Count > 0 && b[0].Type == DataType.Table && b[0].Table == component ? 1 : 0;
                        component["get"] = DynValue.NewCallback((__, b) =>
                            renderer && b.Count > At2(b) && b[At2(b)].CastToString() == "enabled" && Engine.Find(id) is { } live ? DynValue.NewBoolean(!live.MeshHidden) : DynValue.Nil);
                        component["set"] = DynValue.NewCallback((__, b) =>
                        {
                            var i = At2(b);
                            if (!renderer || b.Count < i + 2 || b[i].CastToString() != "enabled")
                                return DynValue.False;
                            return Do(new SetPropertiesCommand { ObjectId = id, MeshHidden = !b[i + 1].CastToBool() });
                        });
                        component["getVars"] = DynValue.NewCallback((__, b) => DynValue.NewTable(new Table(script)));
                        return DynValue.NewTable(component);
                    });
                case "getComponents":
                    return DynValue.NewCallback((_, a) => DynValue.NewTable(new Table(script)));
                case "getChildren":
                    return DynValue.NewCallback((_, a) => DynValue.NewTable(new Table(script)));
                default:
                    return DynValue.Nil;
            }
        }

        private const float TtsUnitsToMeters = Core.Tts.TtsUnits.TtsUnitsToMeters;

        /// <summary>
        /// The effect names of an asset-bundle object, from the views (only they open bundles): (looping or trigger) →
        /// names in order. Null, or no names, when the bundle is not loaded or has none.
        /// </summary>
        public Func<string, bool, IReadOnlyList<string>?>? EffectNames { get; set; }

        /// <summary>TTS's effect list: <c>{ { index = 0, name = "Idle" }, ... }</c>.</summary>
        private DynValue Effects(Script script, string id, bool looping)
        {
            var list = new Table(script);
            var names = EffectNames?.Invoke(id, looping) ?? Array.Empty<string>();
            for (var i = 0; i < names.Count; i++)
            {
                var entry = new Table(script);
                entry["index"] = i;
                entry["name"] = names[i];
                list[i + 1] = entry;
            }
            return DynValue.NewTable(list);
        }

        /// <summary>An object's <c>AssetBundle</c> member (TTS): its effects, played for everyone (ADR 0008).</summary>
        private Table EffectsOf(Script script, string id)
        {
            var bundle = new Table(script);
            int Skip(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table && a[0].Table == bundle ? 1 : 0;
            DynValue Arg0(CallbackArguments a) => a.Count > Skip(a) ? a[Skip(a)] : DynValue.Nil;
            bundle["playLoopingEffect"] = DynValue.NewCallback((_, a) => Arg0(a).Type == DataType.Number
                ? DynValue.NewBoolean(Issue(new PlayEffectCommand { ObjectId = id, Index = (int)Arg0(a).Number })) : DynValue.False);
            bundle["playTriggerEffect"] = DynValue.NewCallback((_, a) => Arg0(a).Type == DataType.Number
                ? DynValue.NewBoolean(Issue(new PlayEffectCommand { ObjectId = id, Index = (int)Arg0(a).Number, Looping = false })) : DynValue.False);
            bundle["getLoopingEffects"] = DynValue.NewCallback((_, a) => Effects(script, id, looping: true));
            bundle["getTriggerEffects"] = DynValue.NewCallback((_, a) => Effects(script, id, looping: false));
            bundle["getLoopingEffectIndex"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Engine.Find(id)?.LoopingEffect ?? 0));
            return bundle;
        }

        /// <summary>The TTS names <see cref="DataOf"/> adds beside our own object JSON, and the fields they stand for.</summary>
        private static readonly (string Tts, string Ours)[] DataAliases = { ("Nickname", "name"), ("Description", "description"), ("GMNotes", "gmNotes") };

        /// <summary>
        /// TTS <c>getData()</c>: the object as our own JSON (what <see cref="SpawnObjectData"/> reads back), plus the TTS
        /// names scripts read and edit: <c>GUID</c>, <c>Nickname</c>, <c>Description</c>, <c>GMNotes</c>. A bag's
        /// contents are not included.
        /// </summary>
        private Table DataOf(Script script, TableObject obj)
        {
            var document = Newtonsoft.Json.Linq.JObject.Parse(TableJson.Serialize(new TableState { Objects = { TableJson.CloneObject(obj) } }, indented: false));
            var json = (Newtonsoft.Json.Linq.JObject)document["objects"]![0]!;
            json.Remove("container");
            var data = JsonTableConverter.JsonToTable(json.ToString(Newtonsoft.Json.Formatting.None), script);
            data["GUID"] = obj.Id;
            data["Nickname"] = obj.Name ?? "";
            data["Description"] = obj.Description ?? "";
            data["GMNotes"] = obj.GmNotes ?? "";
            return data;
        }

        /// <summary>
        /// TTS <c>spawnObjectData({ data, position, rotation, scale, callback_function })</c> for data from
        /// <c>getData()</c> (edited or not): a new object with a new id. Hand-made TTS data (<c>Name = 'Custom_Model'</c>
        /// ...) is not read yet and spawns nothing.
        /// </summary>
        private DynValue SpawnObjectData(Script script, Table p)
        {
            var data = p.Get("data");
            if (data.Type != DataType.Table)
                return DynValue.Nil;
            Newtonsoft.Json.Linq.JObject json;
            try
            {
                json = Newtonsoft.Json.Linq.JObject.Parse(JsonTableConverter.TableToJson(data.Table));
            }
            catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is ScriptRuntimeException)
            {
                return DynValue.Nil;
            }
            if (json["type"]?.Type != Newtonsoft.Json.Linq.JTokenType.String)
            {
                Log?.Invoke("[script] spawnObjectData: only data from getData() can be spawned yet.");
                return DynValue.Nil;
            }
            foreach (var (tts, ours) in DataAliases)
            {
                if (json[tts]?.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    json[ours] = (string)json[tts]!;
                json.Remove(tts);
            }
            json.Remove("GUID");
            var transform = json["transform"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            if (FromTts(p.Get("position"), true) is { } at)
                transform["position"] = new Newtonsoft.Json.Linq.JArray(at.X, at.Y, at.Z);
            if (FromTts(p.Get("rotation"), false) is { } rot)
                transform["rotation"] = new Newtonsoft.Json.Linq.JArray(rot.X, rot.Y, rot.Z);
            if (FromTts(p.Get("scale"), false) is { } scale)
                transform["scale"] = new Newtonsoft.Json.Linq.JArray(scale.X, scale.Y, scale.Z);
            json["transform"] = transform;
            return Spawned(script, p, _execute(new SpawnCommand { ObjectJson = json.ToString(Newtonsoft.Json.Formatting.None) }), "spawnObjectData");
        }

        /// <summary>The spawned object's proxy; its callback runs on the next tick, as TTS calls it once the object has loaded.</summary>
        private DynValue Spawned(Script script, Table p, CommandResult result, string what)
        {
            Count(result);
            if (!result.Ok)
            {
                Log?.Invoke($"[script] {what} refused: {result.Error?.Message}");
                return DynValue.Nil;
            }
            var proxy = Proxy(script, result.CreatedId);
            var callback = p.Get("callback_function");
            if (callback.Type == DataType.Function || callback.Type == DataType.ClrFunction)
                _timers.Add(new Timer { Id = _nextTimer++, Script = script, Function = callback, Arguments = new[] { proxy }, Due = _now });
            return proxy;
        }

        /// <summary>
        /// TTS <c>spawnObject({ type, position, rotation, scale, callback_function })</c> for the types that need no
        /// custom files: dice, tokens, tiles, boards, bags, notecards, counters and the built-in blocks, checkers and
        /// pawn. The callback gets the object on the next frame, as TTS calls it once the object has loaded.
        /// </summary>
        private DynValue SpawnObject(Script script, Table p)
        {
            var type = p.Get("type").CastToString() ?? "";
            var json = new Newtonsoft.Json.Linq.JObject();
            var assets = new List<Asset>();
            void Builtin(string name)
            {
                json["type"] = "Model";
                json["asset"] = "builtin_" + name;
                assets.Add(new Asset { Id = "builtin_" + name, Type = AssetType.Model, Path = Asset.BuiltinPrefix + name });
            }
            if (type.StartsWith("Die_", StringComparison.Ordinal) && int.TryParse(new string(type.Substring(4).TakeWhile(char.IsDigit).ToArray()), out var faces)
                && Die.SupportedFaces.Contains(faces))
            {
                json["type"] = "Die";
                json["faces"] = faces;
            }
            else
                switch (type)
                {
                    case "Custom_Token": case "Chip_10": case "Chip_50": case "Chip_100": case "Chip_500": case "Chip_1000":
                        json["type"] = "Token";
                        if (type.StartsWith("Chip", StringComparison.Ordinal))
                            json["shape"] = "round";
                        break;
                    case "Custom_Tile": json["type"] = "Tile"; break;
                    case "Custom_Board": json["type"] = "Board"; break;
                    case "Card":
                    case "CardCustom":
                        // A blank card; scripts give it its face and back with setCustomObject.
                        json["type"] = "Card";
                        json["faceAsset"] = "builtin_standard_deck";
                        json["faceIndex"] = 0;
                        json["backAsset"] = "builtin_standard_back";
                        // A TTS card's size, like the cards of imported tables.
                        json["size"] = new Newtonsoft.Json.Linq.JArray(Core.Tts.TtsUnits.CardSize.X, Core.Tts.TtsUnits.CardSize.Y);
                        assets.Add(new Asset { Id = "builtin_standard_deck", Type = AssetType.Image, Path = Asset.BuiltinPrefix + "standard_deck", Columns = 10, Rows = 7 });
                        assets.Add(new Asset { Id = "builtin_standard_back", Type = AssetType.Image, Path = Asset.BuiltinPrefix + "standard_back" });
                        break;
                    case "Custom_Model":
                        json["type"] = "Model";
                        json["asset"] = "builtin_placeholder";
                        assets.Add(new Asset { Id = "builtin_placeholder", Type = AssetType.Model, Path = Asset.BuiltinPrefix + "placeholder" });
                        break;
                    case "Bag": case "Custom_Model_Bag": json["type"] = "Bag"; break;
                    case "Infinite_Bag": case "Custom_Model_Infinite_Bag": json["type"] = "InfiniteBag"; break;
                    case "Notecard": json["type"] = "Notecard"; break;
                    case "Counter": json["type"] = "Counter"; break;
                    case "BlockSquare": Builtin("block_square"); break;
                    case "BlockRectangle": Builtin("block_rectangle"); break;
                    case "Checker_white": case "Checker_black": case "Checker_red": Builtin("checker"); break;
                    case "PlayerPawn": Builtin("pawn"); break;
                    default:
                        Log?.Invoke($"[script] spawnObject: type '{type}' is not supported yet.");
                        return DynValue.Nil;
                }
            var transform = new Newtonsoft.Json.Linq.JObject();
            if (FromTts(p.Get("position"), true) is { } at)
                transform["position"] = new Newtonsoft.Json.Linq.JArray(at.X, at.Y, at.Z);
            if (FromTts(p.Get("rotation"), false) is { } rot)
                transform["rotation"] = new Newtonsoft.Json.Linq.JArray(rot.X, rot.Y, rot.Z);
            if (FromTts(p.Get("scale"), false) is { } scale)
                transform["scale"] = new Newtonsoft.Json.Linq.JArray(scale.X, scale.Y, scale.Z);
            if (transform.Count > 0)
                json["transform"] = transform;
            return Spawned(script, p, _execute(new SpawnCommand { ObjectJson = json.ToString(Newtonsoft.Json.Formatting.None), Assets = assets }), "spawnObject");
        }

        /// <summary>
        /// The asset for a web address a script names (TTS <c>setCustomObject</c>): one per address, fetched by each
        /// machine (<see cref="Asset.Url"/>); null when it is not an http(s) address.
        /// </summary>
        private Asset? UrlAsset(string? url, AssetType type, List<Asset> added)
        {
            if (!Asset.IsWebAddress(url))
                return null;
            var key = Core.Tts.TtsImporter.CacheKey(url!);
            var existing = Engine.State.Assets.FirstOrDefault(a => a.Url == url && a.Type == type) ?? added.FirstOrDefault(a => a.Url == url && a.Type == type);
            if (existing != null)
                return existing;
            var extension = type == AssetType.Model ? ".obj" : url!.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
            var asset = new Asset
            {
                Id = "url_" + Math.Abs(key.GetHashCode()).ToString("x8") + (type == AssetType.Model ? "_m" : ""),
                Type = type,
                Path = $"urls/{key}{extension}",
                Url = url,
                MeshScale = type == AssetType.Model ? Core.Tts.TtsUnits.TtsUnitsToMeters : 1f,
            };
            added.Add(asset);
            return asset;
        }

        /// <summary>
        /// TTS's <c>Grid</c>: <c>type</c> (1 squares, 2 and 3 hexes), <c>show_lines</c>, <c>snapping</c> (1 off, 2 lines,
        /// 3 centres, 4 both), <c>sizeX</c>/<c>sizeY</c> and <c>offsetX</c>/<c>offsetY</c> in TTS units, <c>color</c>,
        /// <c>opacity</c>. Reading gives the table's grid; setting a field sets the grid (<c>SetGrid</c>), turning it on.
        /// </summary>
        private DynValue GridApi(Script script)
        {
            var t = new Table(script);
            GridDef Current() => Engine.State.Grid ?? new GridDef { Lines = false };
            var meta = new Table(script);
            meta["__index"] = DynValue.NewCallback((_, a) =>
            {
                var grid = Engine.State.Grid;
                var g = Current();
                switch (a.Count > 1 ? a[1].CastToString() : null)
                {
                    case "type": return DynValue.NewNumber(g.Shape == GridShape.Hex ? 2 : 1);
                    case "show_lines": return DynValue.NewBoolean(grid != null && g.Lines);
                    case "snapping": return DynValue.NewNumber(grid == null ? 1 : g.Snapping switch { GridSnapping.Corners => 2, GridSnapping.Centers => 3, GridSnapping.Both => 4, _ => 1 });
                    case "sizeX": return DynValue.NewNumber(g.SizeX / TtsUnitsToMeters);
                    case "sizeY": return DynValue.NewNumber(g.SizeZ / TtsUnitsToMeters);
                    case "offsetX": return DynValue.NewNumber(g.OffsetX / TtsUnitsToMeters);
                    case "offsetY": return DynValue.NewNumber(g.OffsetZ / TtsUnitsToMeters);
                    case "opacity": return DynValue.NewNumber(g.Opacity);
                    case "thick_lines": return DynValue.False;
                    case "color":
                        var c = new Table(script);
                        c["r"] = g.Color[0]; c["g"] = g.Color[1]; c["b"] = g.Color[2];
                        c[1] = g.Color[0]; c[2] = g.Color[1]; c[3] = g.Color[2];
                        return DynValue.NewTable(c);
                    default: return DynValue.Nil;
                }
            });
            meta["__newindex"] = DynValue.NewCallback((_, a) =>
            {
                var key = a.Count > 1 ? a[1].CastToString() : null;
                var value = a.Count > 2 ? a[2] : DynValue.Nil;
                var g = Current();
                var grid = new GridDef
                {
                    Shape = g.Shape, Lines = g.Lines, Snapping = g.Snapping, SizeX = g.SizeX, SizeZ = g.SizeZ,
                    OffsetX = g.OffsetX, OffsetZ = g.OffsetZ, Color = (float[])g.Color.Clone(), Opacity = g.Opacity,
                };
                float Units(float min, float max) => Math.Max(min, Math.Min(max, (float)value.CastToNumber().GetValueOrDefault() * TtsUnitsToMeters));
                switch (key)
                {
                    case "type": grid.Shape = value.CastToNumber() is { } type && type >= 2 ? GridShape.Hex : GridShape.Square; break;
                    case "show_lines": grid.Lines = value.CastToBool(); break;
                    case "snapping":
                        grid.Snapping = (int)(value.CastToNumber() ?? 1) switch { 2 => GridSnapping.Corners, 3 => GridSnapping.Centers, 4 => GridSnapping.Both, _ => GridSnapping.Off };
                        break;
                    case "sizeX": grid.SizeX = Units(GridDef.MinSize, GridDef.MaxSize); break;
                    case "sizeY": grid.SizeZ = Units(GridDef.MinSize, GridDef.MaxSize); break;
                    case "offsetX": grid.OffsetX = Units(-100f, 100f); break;
                    case "offsetY": grid.OffsetZ = Units(-100f, 100f); break;
                    case "opacity": grid.Opacity = Math.Max(0f, Math.Min(1f, (float)(value.CastToNumber() ?? 1))); break;
                    case "color" when value.Type == DataType.Table:
                        float Channel(string name, int index) => (float)((value.Table.Get(name).CastToNumber() ?? value.Table.Get(index).CastToNumber()) ?? 0);
                        grid.Color = new[] { Channel("r", 1), Channel("g", 2), Channel("b", 3) };
                        break;
                    default: return DynValue.Nil;
                }
                Issue(new SetGridCommand { Grid = grid });
                return DynValue.Nil;
            });
            t.MetaTable = meta;
            return DynValue.NewTable(t);
        }

        /// <summary>
        /// TTS's <c>MusicPlayer</c> (G27) over the table's music: <c>play</c>, <c>pause</c>, <c>skip</c>/<c>back</c>,
        /// <c>getPlaylist</c>/<c>setPlaylist</c>, <c>getCurrentAudioclip</c>/<c>setCurrentAudioclip</c> (clips are
        /// <c>{url, title}</c>), and the members <c>player_status</c>, <c>playlist_index</c> (also <c>playlistIndex</c>),
        /// <c>repeat_track</c>, <c>shuffle</c>, <c>loaded</c>. Changes go through the host's <c>SetMusic</c>.
        /// </summary>
        private DynValue MusicApi(Script script)
        {
            var t = new Table(script);
            MusicState Copy() => Engine.State.Music?.Clone() ?? new MusicState();
            DynValue Set(MusicState music) => DynValue.NewBoolean(Issue(new SetMusicCommand { Music = music }));
            // MusicPlayer.play() and MusicPlayer:play() both work: a colon passes the player first.
            DynValue At(CallbackArguments a, int i)
            {
                var k = a.Count > 0 && a[0].Type == DataType.Table && a[0].Table == t ? i + 1 : i;
                return a.Count > k ? a[k] : DynValue.Nil;
            }
            DynValue Callback(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, a) => body(a));
            Table Clip(MusicTrack track)
            {
                var clip = new Table(script);
                clip["url"] = track.Url;
                clip["title"] = track.Title;
                return clip;
            }
            MusicTrack? ReadClip(DynValue value) =>
                value.Type == DataType.Table && value.Table.Get("url").CastToString() is { } url && Rules.Asset.IsWebAddress(url)
                    ? new MusicTrack { Url = url, Title = value.Table.Get("title").CastToString() ?? "" }
                    : null;
            MusicState Load(MusicState music, int index)
            {
                music.Index = index;
                music.Current = music.Playlist[index].Clone();
                music.Starts++;
                return music;
            }
            var random = new Random();

            t["play"] = Callback(a =>
            {
                var music = Copy();
                if (music.Current == null && music.Playlist.Count > 0)
                    Load(music, 0);
                if (music.Current == null)
                    return DynValue.False;
                music.Playing = true;
                return Set(music);
            });
            t["pause"] = Callback(a =>
            {
                var music = Copy();
                music.Playing = false;
                return Set(music);
            });
            DynValue Skip(int step)
            {
                var music = Copy();
                if (music.Playlist.Count == 0)
                    return DynValue.False;
                var repeat = music.Repeat;
                music.Repeat = false;
                var next = music.Next(random, step);
                next.Repeat = repeat;
                return Set(next);
            }
            t["skip"] = Callback(a => Skip(1));
            t["skipForward"] = t["skip"];
            t["back"] = Callback(a => Skip(-1));
            t["skipBack"] = t["back"];
            t["getPlaylist"] = Callback(a =>
            {
                var list = new Table(script);
                var music = Engine.State.Music;
                for (var i = 0; music != null && i < music.Playlist.Count; i++)
                    list[i + 1] = Clip(music.Playlist[i]);
                return DynValue.NewTable(list);
            });
            t["setPlaylist"] = Callback(a =>
            {
                var music = Copy();
                var given = At(a, 0);
                music.Playlist = given.Type == DataType.Table
                    ? given.Table.Values.Select(ReadClip).Where(c => c != null).Select(c => c!).ToList()
                    : new List<MusicTrack>();
                music.Index = music.Current == null ? -1 : music.Playlist.FindIndex(c => c.Url == music.Current.Url);
                return Set(music);
            });
            t["getCurrentAudioclip"] = Callback(a => Engine.State.Music?.Current is { } current ? DynValue.NewTable(Clip(current)) : DynValue.Nil);
            t["setCurrentAudioclip"] = Callback(a =>
            {
                if (!(ReadClip(At(a, 0)) is { } clip))
                    return DynValue.False;
                var music = Copy();
                music.Current = clip;
                music.Index = music.Playlist.FindIndex(c => c.Url == clip.Url);
                music.Playing = true;
                music.Starts++;
                return Set(music);
            });
            var meta = new Table(script);
            meta["__index"] = DynValue.NewCallback((_, a) =>
            {
                var music = Engine.State.Music;
                switch (a.Count > 1 ? a[1].CastToString() : null)
                {
                    case "player_status": return DynValue.NewString(music?.Current == null ? "Ready" : music.Playing ? "Play" : "Pause");
                    case "playlist_index": case "playlistIndex": return DynValue.NewNumber(music?.Index ?? -1);
                    case "repeat_track": return DynValue.NewBoolean(music?.Repeat ?? false);
                    case "shuffle": return DynValue.NewBoolean(music?.Shuffle ?? false);
                    case "loaded": return DynValue.NewBoolean(music?.Current != null);
                    default: return DynValue.Nil;
                }
            });
            meta["__newindex"] = DynValue.NewCallback((_, a) =>
            {
                var key = a.Count > 1 ? a[1].CastToString() : null;
                var value = a.Count > 2 ? a[2] : DynValue.Nil;
                var music = Copy();
                switch (key)
                {
                    case "repeat_track": music.Repeat = value.CastToBool(); break;
                    case "shuffle": music.Shuffle = value.CastToBool(); break;
                    case "playlist_index": case "playlistIndex":
                        var index = (int)(value.CastToNumber() ?? -1);
                        if (index < 0 || index >= music.Playlist.Count)
                            return DynValue.Nil;
                        Load(music, index);
                        break;
                    default: return DynValue.Nil;
                }
                Set(music);
                return DynValue.Nil;
            });
            t.MetaTable = meta;
            return DynValue.NewTable(t);
        }

        /// <summary>TTS's <c>tag</c> for an object or zone.</summary>
        private static string TtsTag(object thing) => thing switch
        {
            Card _ => "Card",
            Deck _ => "Deck",
            InfiniteBag _ => "Infinite",
            Bag _ => "Bag",
            Die _ => "Dice",
            Tile _ => "Tile",
            Board _ => "Board",
            Figure _ => "Figurine",
            Counter _ => "Counter",
            Clock _ => "Clock",
            Notecard _ => "Notecard",
            ScriptingZone _ => "Scripting",
            HandZone _ => "Hand",
            HiddenZone _ => "Fog",
            LayoutZone _ => "Layout",
            RandomizeZone _ => "Randomize",
            _ => "Generic",
        };

        /// <summary>
        /// TTS's <c>Turns</c>: <c>enable</c>, <c>order</c>, <c>turn_color</c>, <c>reverse_order</c>,
        /// <c>skip_empty_hands</c> read the table's free-play turns and setting them changes them (<c>SetTurnOrder</c>);
        /// <c>getNextTurnColor()</c> / <c>getPreviousTurnColor()</c>.
        /// </summary>
        private DynValue TurnsApi(Script script)
        {
            var t = new Table(script);
            string? ColorOf(string? seat) => seat == null ? null : Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color is { } c ? Capital(c) : null;
            string? SeatOf(string? color) => color == null ? null : Engine.State.Players.Seats.FirstOrDefault(s => string.Equals(s.Color, color, StringComparison.OrdinalIgnoreCase))?.Id;
            FreeTurns Current() => Engine.State.FreeTurns ?? new FreeTurns();
            List<string> Order(FreeTurns turns) => turns.Seats.Count > 0 ? turns.Seats : Engine.State.Players.Seats.Select(s => s.Id).ToList();
            void Set(Action<SetTurnOrderCommand> change)
            {
                var turns = Current();
                var command = new SetTurnOrderCommand
                {
                    Enabled = turns.Enabled, Seats = Order(turns).ToList(), Reverse = turns.Reverse, SkipEmptyHands = turns.SkipEmptyHands, First = turns.Current,
                };
                change(command);
                Issue(command);
            }
            string? Step(int by)
            {
                var turns = Current();
                var order = Order(turns);
                if (order.Count == 0)
                    return null;
                var at = Math.Max(0, order.IndexOf(turns.Current ?? order[0]));
                var direction = turns.Reverse ? -by : by;
                return ColorOf(order[((at + direction) % order.Count + order.Count) % order.Count]);
            }
            t["getNextTurnColor"] = DynValue.NewCallback((_, a) => Step(1) is { } c ? DynValue.NewString(c) : DynValue.Nil);
            t["getPreviousTurnColor"] = DynValue.NewCallback((_, a) => Step(-1) is { } c ? DynValue.NewString(c) : DynValue.Nil);
            var meta = new Table(script);
            meta["__index"] = DynValue.NewCallback((_, a) =>
            {
                var turns = Current();
                switch (a.Count > 1 ? a[1].CastToString() : null)
                {
                    case "enable": return DynValue.NewBoolean(turns.Enabled);
                    case "reverse_order": return DynValue.NewBoolean(turns.Reverse);
                    case "skip_empty_hands": return DynValue.NewBoolean(turns.SkipEmptyHands);
                    case "turn_color": return ColorOf(turns.Current) is { } c ? DynValue.NewString(c) : DynValue.NewString("");
                    case "type": return DynValue.NewNumber(turns.Seats.Count > 0 ? 2 : 1);
                    case "pass_turns": return DynValue.True;
                    case "disable_interactations": return DynValue.False;
                    case "order":
                        var list = new Table(script);
                        var i = 1;
                        foreach (var seat in Order(turns))
                            if (ColorOf(seat) is { } color)
                                list[i++] = color;
                        return DynValue.NewTable(list);
                    default: return DynValue.Nil;
                }
            });
            meta["__newindex"] = DynValue.NewCallback((_, a) =>
            {
                var key = a.Count > 1 ? a[1].CastToString() : null;
                var value = a.Count > 2 ? a[2] : DynValue.Nil;
                switch (key)
                {
                    case "enable": Set(c => c.Enabled = value.CastToBool()); break;
                    case "reverse_order": Set(c => c.Reverse = value.CastToBool()); break;
                    case "skip_empty_hands": Set(c => c.SkipEmptyHands = value.CastToBool()); break;
                    case "turn_color": Set(c => c.First = SeatOf(value.CastToString())); break;
                    case "order" when value.Type == DataType.Table:
                        var seats = value.Table.Values.Select(v => SeatOf(v.CastToString())).Where(s => s != null).Select(s => s!).ToList();
                        Set(c =>
                        {
                            c.Seats = seats;
                            if (c.First == null || !seats.Contains(c.First))
                                c.First = seats.FirstOrDefault();
                        });
                        break;
                }
                return DynValue.Nil;
            });
            t.MetaTable = meta;
            return DynValue.NewTable(t);
        }

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

        private static InputDef CopyInput(InputDef b) => new InputDef
        {
            Label = b.Label, Value = b.Value, InputFunction = b.InputFunction, Owner = b.Owner, Position = b.Position, Rotation = b.Rotation,
            Width = b.Width, Height = b.Height, FontSize = b.FontSize, Color = b.Color, FontColor = b.FontColor, Tooltip = b.Tooltip,
            Alignment = b.Alignment, Validation = b.Validation,
        };

        /// <summary>Reads TTS input parameters over <paramref name="b"/>: the button ones, then value, input_function, alignment, validation.</summary>
        private InputDef ReadInput(Table p, InputDef b)
        {
            var shape = ReadButton(null!, p, new ButtonDef
            {
                Label = b.Label, Owner = b.Owner, Position = b.Position, Rotation = b.Rotation, Width = b.Width, Height = b.Height,
                FontSize = b.FontSize, Color = b.Color, FontColor = b.FontColor, Tooltip = b.Tooltip,
            });
            b.Label = shape.Label;
            b.Owner = shape.Owner;
            b.Position = shape.Position;
            b.Rotation = shape.Rotation;
            b.Width = shape.Width;
            b.Height = shape.Height;
            b.FontSize = shape.FontSize;
            b.Color = shape.Color;
            b.FontColor = shape.FontColor;
            b.Tooltip = shape.Tooltip;
            if (p.Get("input_function").Type == DataType.String)
                b.InputFunction = p.Get("input_function").String;
            if (p.Get("value").Type != DataType.Nil)
                b.Value = p.Get("value").CastToString() ?? "";
            if (p.Get("alignment").Type == DataType.Number)
                b.Alignment = Math.Max(1, Math.Min(5, (int)p.Get("alignment").Number));
            if (p.Get("validation").Type == DataType.Number)
                b.Validation = Math.Max(1, Math.Min(6, (int)p.Get("validation").Number));
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
        /// <summary>
        /// The bounds of an object's colliders in metres, from the views (only they know the shapes): (id, normalized) →
        /// (center, size), or null. Normalized is as if the object were not rotated (TTS <c>getBoundsNormalized</c>).
        /// </summary>
        public Func<string, bool, (Vec3 Center, Vec3 Size)?>? MeasureBounds { get; set; }

        /// <summary>
        /// Whether an object is still moving (falling, sliding, rolling), from the views: TTS <c>resting</c> is false then.
        /// Without views, a piece no one holds is at rest.
        /// </summary>
        public Func<string, bool>? IsMoving { get; set; }

        /// <summary>TTS <c>getBounds</c>: <c>{ center, size, offset }</c> in TTS units; offset is center minus position.</summary>
        private Table Bounds(Script script, TableObject o, bool normalized)
        {
            Vec3 center, size;
            if (MeasureBounds?.Invoke(o.Id, normalized) is { } measured)
                (center, size) = measured;
            else
            {
                // Without views (headless): the footprint, and a height guessed from it.
                var footprint = Footprint.Of(o);
                center = o.Transform.Position;
                size = new Vec3(footprint.X, o is Card || o is Deck ? 0.01f : Math.Min(footprint.X, footprint.Y), footprint.Y);
            }
            var bounds = new Table(script);
            bounds["center"] = ToTts(script, center);
            bounds["size"] = Vector(script, new Vec3(size.X / TtsUnitsToMeters, size.Y / TtsUnitsToMeters, size.Z / TtsUnitsToMeters));
            var offset = Geometry.Subtract(center, o.Transform.Position);
            bounds["offset"] = Vector(script, new Vec3(offset.X / TtsUnitsToMeters, offset.Y / TtsUnitsToMeters, offset.Z / TtsUnitsToMeters));
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
