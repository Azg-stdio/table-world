using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Tts;
using MoonSharp.Interpreter;

namespace Core.Scripting
{
    /// <summary>
    /// Runs a table's Lua (M5, ADR 0003) on the host only: the global script (<c>scripts["global"]</c>) and each object's
    /// <see cref="TableObject.Script"/>, each in its own MoonSharp sandbox (no files, no OS, no loading code), every call on
    /// a step budget so a runaway loop is stopped, and strings capped. Scripts read the table directly and change it only
    /// through commands (<see cref="Execute"/>), so their changes are validated, logged and sent to every player like a
    /// player's. Clients never run scripts. The API follows TTS names and units (docs/lua-api.md).
    /// </summary>
    public sealed partial class ScriptHost
    {
        public const string GlobalName = "global";
        /// <summary>Instructions a script runs between checks, and at most per call (an event, a timer, a load).</summary>
        public const int Slice = 10_000;
        public const long MaxSteps = 2_000_000;
        /// <summary>Longest string <c>string.rep</c> and concatenation helpers may build.</summary>
        public const int MaxStringLength = 1_000_000;
        /// <summary>Scripts reacting to commands their own commands caused stop this deep.</summary>
        public const int MaxDepth = 4;

        private readonly Func<TableEngine?> _engine;
        private readonly Func<Command, CommandResult> _execute;
        private readonly Dictionary<string, Script> _objectScripts = new Dictionary<string, Script>();
        private Script? _global;
        private int _depth;
        private double _now;
        private readonly List<Timer> _timers = new List<Timer>();
        private int _nextTimer = 1;

        /// <summary>Raised for <c>broadcastToAll</c> and friends: a line every player should read.</summary>
        public event Action<string>? Broadcast;

        /// <summary>Raised for <c>print</c> and for script errors (the host's log).</summary>
        public event Action<string>? Log;

        /// <summary>Commands the scripts issued (tests and the session's log).</summary>
        public int CommandsIssued { get; private set; }

        /// <param name="engine">The table as it is now (it changes when another table loads).</param>
        /// <param name="execute">Runs a command as the host would (the host's route: executed, logged and broadcast).</param>
        public ScriptHost(Func<TableEngine?> engine, Func<Command, CommandResult> execute)
        {
            _engine = engine;
            _execute = execute;
        }

        private TableEngine Engine => _engine() ?? throw new InvalidOperationException("No table.");

        /// <summary>
        /// Loads every script of the current table and runs their top level and <c>onLoad</c> (global first, as TTS).
        /// Earlier scripts and timers are dropped.
        /// </summary>
        public void Load(double now)
        {
            _now = now;
            _objectScripts.Clear();
            _timers.Clear();
            _proxies.Clear();
            _coroutines.Clear();
            _registeredCollisions.Clear();
            _global = null;
            var state = Engine.State;
            // Buttons are made by the scripts' onLoad (as in TTS); the ones a save kept would double up.
            foreach (var withButtons in state.Objects.Where(o => o.Buttons.Count > 0).ToList())
                Issue(new SetButtonsCommand { ObjectId = withButtons.Id });
            foreach (var withInputs in state.Objects.Where(o => o.Inputs.Count > 0).ToList())
                Issue(new SetInputsCommand { ObjectId = withInputs.Id });
            if (state.Scripts.TryGetValue(GlobalName, out var source) && !string.IsNullOrWhiteSpace(source))
                _global = Compile(source, null, "Global");
            foreach (var obj in state.Objects.Where(o => !string.IsNullOrWhiteSpace(o.Script)))
            {
                var script = Compile(obj.Script!, obj.Id, obj.Name ?? obj.Id);
                if (script != null)
                    _objectScripts[obj.Id] = script;
            }
            // onLoad gets what onSave saved (TTS script_state), or an empty string.
            if (_global != null)
                CallIfDefined(_global, "onLoad", DynValue.NewString(state.GlobalScriptState ?? ""));
            foreach (var pair in _objectScripts.ToList())
                CallIfDefined(pair.Value, "onLoad", DynValue.NewString(Engine.Find(pair.Key)?.ScriptState ?? ""));
        }

        /// <summary>
        /// Before the table is saved: asks every script's <c>onSave</c> for its state string and keeps it with the table
        /// (TTS <c>script_state</c>), so a loaded save resumes where it was. Host data: no command, nobody else needs it.
        /// </summary>
        public void Save()
        {
            var state = Engine.State;
            if (_global != null && CallIfDefined(_global, "onSave") is { Type: DataType.String } global)
                state.GlobalScriptState = global.String;
            foreach (var pair in _objectScripts.ToList())
                if (CallIfDefined(pair.Value, "onSave") is { Type: DataType.String } saved && Engine.Find(pair.Key) is { } obj)
                    obj.ScriptState = saved.String;
        }

        /// <summary>Recompiles one object's script after <c>setLuaScript</c> (or a player edit) and runs its onLoad.</summary>
        private void ReloadObject(string id)
        {
            if (Engine.Find(id) is { Buttons: { Count: > 0 } })
                Issue(new SetButtonsCommand { ObjectId = id });
            if (Engine.Find(id) is { Inputs: { Count: > 0 } })
                Issue(new SetInputsCommand { ObjectId = id });
            _objectScripts.Remove(id);
            foreach (var key in _proxies.Keys.Where(k => k.Item2 == id).ToList())
                _proxies.Remove(key);
            var obj = Engine.Find(id);
            if (obj == null || string.IsNullOrWhiteSpace(obj.Script))
                return;
            var script = Compile(obj.Script!, id, obj.Name ?? id);
            if (script == null)
                return;
            _objectScripts[id] = script;
            CallIfDefined(script, "onLoad", DynValue.NewString(obj.ScriptState ?? ""));
        }

        /// <summary>Scripts loaded right now (the global one counts).</summary>
        public int ScriptCount => (_global != null ? 1 : 0) + _objectScripts.Count;

        private Script? Compile(string source, string? selfId, string label)
        {
            var script = new Script(CoreModules.Preset_SoftSandbox);
            script.Options.DebugPrint = text => Log?.Invoke($"[{label}] {text}");
            InstallApi(script, selfId);
            try
            {
                script.DoString(LuaPrelude.Source, null, "prelude");
                var chunk = script.LoadString(source, null, label);
                return Run(script, chunk, label, Array.Empty<DynValue>()) ? script : null;
            }
            catch (InterpreterException e)
            {
                Log?.Invoke($"[{label}] does not load: {e.DecoratedMessage ?? e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Runs <paramref name="function"/> as a coroutine that yields every <see cref="Slice"/> instructions, giving up
        /// after <see cref="MaxSteps"/>; false (logged) when it failed or ran too long.
        /// </summary>
        private bool Run(Script script, DynValue function, string label, DynValue[] args) => RunFor(script, function, label, args, out _);

        private bool RunFor(Script script, DynValue function, string label, DynValue[] args, out DynValue result)
        {
            result = DynValue.Nil;
            if (_depth >= MaxDepth)
            {
                Log?.Invoke($"[{label}] skipped: scripts reacting to their own changes went {MaxDepth} deep.");
                return false;
            }
            _depth++;
            try
            {
                var thread = script.CreateCoroutine(function).Coroutine;
                thread.AutoYieldCounter = Slice;
                result = thread.Resume(args);
                long steps = Slice;
                while (thread.State == CoroutineState.ForceSuspended)
                {
                    steps += Slice;
                    if (steps > MaxSteps)
                    {
                        Log?.Invoke($"[{label}] stopped: it ran more than {MaxSteps:N0} steps (an endless loop?).");
                        return false;
                    }
                    result = thread.Resume();
                }
                return true;
            }
            catch (InterpreterException e)
            {
                Log?.Invoke($"[{label}] error: {e.DecoratedMessage ?? e.Message}");
                return false;
            }
            catch (LimitException e)
            {
                Log?.Invoke($"[{label}] stopped: {e.Message}");
                return false;
            }
            finally
            {
                _depth--;
            }
        }

        private DynValue? CallIfDefined(Script script, string name, params DynValue[] args)
        {
            var function = script.Globals.Get(name);
            if (function.Type != DataType.Function)
                return null;
            return RunFor(script, function, name, args, out var result) ? result : null;
        }

        /// <summary>Calls <paramref name="name"/> in the global script and every object script that defines it.</summary>
        private void Everywhere(string name, params DynValue[] args)
        {
            if (_global != null)
                CallIfDefined(_global, name, args);
            foreach (var script in _objectScripts.Values.ToList())
                CallIfDefined(script, name, args);
        }

        // ---- events -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Tells the scripts what a command did (the host calls this after every executed command, the scripts' own too):
        /// <c>onObjectDrop</c> / <c>onDrop</c>, <c>onObjectEnterZone</c> / <c>onObjectLeaveZone</c>, <c>onObjectFlip</c>,
        /// <c>onObjectRandomize</c>, <c>onPlayerTurn</c>. <paramref name="color"/> is the acting seat's colour, or null.
        /// </summary>
        public void OnCommand(Command command, CommandResult result, string? color)
        {
            if (!result.Ok || ScriptCount == 0)
                return;
            var who = color == null ? DynValue.Nil : DynValue.NewString(Capital(color));
            switch (command)
            {
                case DropCommand drop:
                    ForObject("onObjectDrop", "onDrop", drop.ObjectId, who);
                    break;
                case PickUpCommand pick:
                    ForObject("onObjectPickUp", "onPickUp", pick.ObjectId, who);
                    break;
                case SetPropertiesCommand { Script: { } } edit:
                    ReloadObject(edit.ObjectId);
                    break;
                case FlipCommand flip:
                    ForObject("onObjectFlip", "onFlip", flip.ObjectId, who);
                    break;
                case ClickButtonCommand click:
                    Click(click, who);
                    break;
                case TypeInputCommand typed:
                    Typed(typed, who);
                    break;
                case UiEventCommand ui:
                    UiUsed(ui, color);
                    break;
                case EndTurnCommand _:
                case SetTurnOrderCommand { Enabled: true }:
                    if (Engine.State.FreeTurns?.Current is { } seat && Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat) is { } current)
                        Everywhere("onPlayerTurn", DynValue.NewString(Capital(current.Color)));
                    break;
            }
            foreach (var e in result.Events)
            {
                switch (e)
                {
                    // TTS: onObjectRandomize(object, player_color), object first.
                    case Shuffled shuffled:
                        ForObject("onObjectRandomize", "onRandomize", shuffled.ObjectId, who, objectFirst: true);
                        break;
                    case DieRolled rolled:
                        ForObject("onObjectRandomize", "onRandomize", rolled.ObjectId, who, objectFirst: true);
                        break;
                    // TTS: onObjectEnterContainer(container, object) / onObjectLeaveContainer(container, object).
                    case ContainerChanged moved when moved.From != null && Engine.Find(moved.From) != null:
                        EverywhereWith("onObjectLeaveContainer", (s, p) => new[] { p(moved.From), p(moved.ObjectId) });
                        if (moved.To != null)
                            EverywhereWith("onObjectEnterContainer", (s, p) => new[] { p(moved.To), p(moved.ObjectId) });
                        break;
                    case ContainerChanged moved when moved.To != null:
                        EverywhereWith("onObjectEnterContainer", (s, p) => new[] { p(moved.To), p(moved.ObjectId) });
                        break;
                    case ZoneEntered entered when ProxyFor(entered.ZoneId) != null && ProxyFor(entered.ObjectId) != null:
                        EverywhereWith("onObjectEnterZone", (s, p) => new[] { p(entered.ZoneId), p(entered.ObjectId) });
                        // Older TTS scripts use the scripting-zone names for the same thing.
                        if (Engine.State.Zones.FirstOrDefault(z => z.Id == entered.ZoneId) is ScriptingZone)
                            EverywhereWith("onObjectEnterScriptingZone", (s, p) => new[] { p(entered.ZoneId), p(entered.ObjectId) });
                        break;
                    case ZoneLeft left when Engine.State.Zones.Any(z => z.Id == left.ZoneId):
                        EverywhereWith("onObjectLeaveZone", (s, p) => new[] { p(left.ZoneId), p(left.ObjectId) });
                        if (Engine.State.Zones.FirstOrDefault(z => z.Id == left.ZoneId) is ScriptingZone)
                            EverywhereWith("onObjectLeaveScriptingZone", (s, p) => new[] { p(left.ZoneId), p(left.ObjectId) });
                        break;
                }
            }
        }

        /// <summary>
        /// A player typed into an input: <c>input_function(obj, color, value, selected)</c> in the owner's script, as TTS
        /// calls it. A string it returns becomes the input's text.
        /// </summary>
        private void Typed(TypeInputCommand typed, DynValue who)
        {
            if (!(Engine.Find(typed.ObjectId) is { } obj) || typed.Index >= obj.Inputs.Count)
                return;
            var input = obj.Inputs[typed.Index];
            if (string.IsNullOrEmpty(input.InputFunction))
                return;
            var script = input.Owner == null ? _global : _objectScripts.TryGetValue(input.Owner, out var own) ? own : null;
            if (script == null)
                return;
            var answer = CallIfDefined(script, input.InputFunction, Proxy(script, obj.Id), who, DynValue.NewString(typed.Value), DynValue.NewBoolean(typed.Selected));
            if (answer is { Type: DataType.String } text && text.String != typed.Value && Engine.Find(typed.ObjectId) is { } still && typed.Index < still.Inputs.Count)
            {
                var inputs = still.Inputs.Select(CopyInput).ToList();
                inputs[typed.Index].Value = text.String;
                Issue(new SetInputsCommand { ObjectId = still.Id, Inputs = inputs });
            }
        }

        /// <summary>A button click: its function, in the owner's script, gets (object, colour, right click) like in TTS.</summary>
        private void Click(ClickButtonCommand click, DynValue who)
        {
            if (!(Engine.Find(click.ObjectId) is { } obj) || click.Index >= obj.Buttons.Count)
                return;
            var button = obj.Buttons[click.Index];
            if (string.IsNullOrEmpty(button.Click))
                return;
            var script = button.Owner == null ? _global : _objectScripts.TryGetValue(button.Owner, out var own) ? own : null;
            if (script == null)
                return;
            CallIfDefined(script, button.Click, Proxy(script, obj.Id), who, DynValue.NewBoolean(click.Alt));
        }

        /// <summary>
        /// A player said something in chat (TTS <c>onChat(message, player)</c>): every script hears it; false when one
        /// returned false, meaning the line is not shown.
        /// </summary>
        public bool OnChat(string message, string? color)
        {
            if (ScriptCount == 0)
                return true;
            var shown = true;
            void Hear(Script script)
            {
                if (CallIfDefined(script, "onChat", DynValue.NewString(message), color != null ? PlayerTable(script, color) : DynValue.Nil) is { Type: DataType.Boolean, Boolean: false })
                    shown = false;
            }
            if (_global != null)
                Hear(_global);
            foreach (var script in _objectScripts.Values.ToList())
                Hear(script);
            return shown;
        }

        /// <summary>Global event with the object, and the object's own event on its script (TTS onObjectDrop / onDrop).</summary>
        private void ForObject(string globalEvent, string selfEvent, string objectId, DynValue color, bool objectFirst = false)
        {
            EverywhereWith(globalEvent, (script, proxy) => objectFirst ? new[] { proxy(objectId), color } : new[] { color, proxy(objectId) });
            if (_objectScripts.TryGetValue(objectId, out var own))
                CallIfDefined(own, selfEvent, color);
        }

        /// <summary>Calls an event everywhere with arguments built per script (proxies belong to one script).</summary>
        private void EverywhereWith(string name, Func<Script, Func<string, DynValue>, DynValue[]> args)
        {
            void Call(Script script)
            {
                var function = script.Globals.Get(name);
                if (function.Type == DataType.Function)
                    RunFor(script, function, name, args(script, id => Proxy(script, id)), out _);
            }
            if (_global != null)
                Call(_global);
            foreach (var script in _objectScripts.Values.ToList())
                Call(script);
        }

        private object? ProxyFor(string id) =>
            Engine.Find(id) as object ?? Engine.State.Zones.FirstOrDefault(z => z.Id == id);

        /// <summary>Advances time: runs the <c>Wait</c> callbacks that are due and every running coroutine's next frame (the host calls this every frame).</summary>
        public void Tick(double now)
        {
            _now = now;
            ResumeCoroutines();
            foreach (var timer in _timers.Where(t => t.Due <= now).OrderBy(t => t.Due).ToList())
            {
                if (!_timers.Contains(timer))
                    continue;
                if (timer.Condition != null)
                {
                    var ok = RunFor(timer.Script, timer.Condition, "Wait.condition", Array.Empty<DynValue>(), out var met);
                    if (!ok || (!met.CastToBool() && (timer.Timeout <= 0 || now < timer.Timeout)))
                    {
                        timer.Due = now + 1.0 / 60.0;
                        if (!ok)
                            _timers.Remove(timer);
                        continue;
                    }
                }
                if (timer.Repetitions > 1)
                {
                    timer.Repetitions--;
                    timer.Due = now + timer.Interval;
                }
                else
                    _timers.Remove(timer);
                Run(timer.Script, timer.Function, "Wait", timer.Arguments ?? Array.Empty<DynValue>());
            }
        }

        private readonly List<(Script Script, MoonSharp.Interpreter.Coroutine Thread, string Label)> _coroutines = new List<(Script, MoonSharp.Interpreter.Coroutine, string)>();

        /// <summary>
        /// TTS <c>startLuaCoroutine(owner, "name")</c>: runs the function a frame at a time; each <c>coroutine.yield(0)</c>
        /// waits for the next frame. Each frame's slice is on the step budget; a coroutine that never yields is stopped.
        /// </summary>
        private bool StartCoroutine(Script script, string name)
        {
            var function = script.Globals.Get(name);
            if (function.Type != DataType.Function)
                return false;
            var thread = script.CreateCoroutine(function).Coroutine;
            _coroutines.Add((script, thread, name));
            return true;
        }

        private void ResumeCoroutines()
        {
            foreach (var (script, thread, label) in _coroutines.ToList())
            {
                _coroutines.RemoveAll(c => c.Thread == thread);
                if (thread.State == CoroutineState.Dead)
                    continue;
                try
                {
                    thread.AutoYieldCounter = Slice;
                    thread.Resume();
                    long steps = Slice;
                    while (thread.State == CoroutineState.ForceSuspended)
                    {
                        steps += Slice;
                        if (steps > MaxSteps)
                        {
                            Log?.Invoke($"[{label}] stopped: a coroutine ran more than {MaxSteps:N0} steps in one frame.");
                            break;
                        }
                        thread.Resume();
                    }
                    if (thread.State == CoroutineState.Suspended)
                        _coroutines.Add((script, thread, label));
                }
                catch (InterpreterException e)
                {
                    Log?.Invoke($"[{label}] error: {e.DecoratedMessage ?? e.Message}");
                }
            }
        }

        /// <summary>Coroutines still running (tests).</summary>
        public int CoroutineCount => _coroutines.Count;

        private sealed class Timer
        {
            public int Id;
            public Script Script = null!;
            public DynValue Function = null!;
            public DynValue? Condition;
            public DynValue[]? Arguments;
            public double Due;
            public double Interval;
            public int Repetitions = 1;
            public double Timeout;
        }

        /// <summary>Thrown by the API when a script asks for too much (a giant string); stops that call.</summary>
        private sealed class LimitException : Exception
        {
            public LimitException(string message) : base(message) { }
        }

        private static string Capital(string color) => color.Length == 0 ? color : char.ToUpperInvariant(color[0]) + color.Substring(1);

        // ---- units: TTS units on the TTS table, our metres on ours ---------------------------------------------------

        private static Table ToTts(Script script, Vec3 metres)
        {
            var t = new Table(script);
            // Rounded, so a card at 0.3 m reads 10 and not 10.0000003.
            var x = Math.Round(metres.X / TtsUnits.TtsUnitsToMeters, 4);
            var y = Math.Round(metres.Y / TtsUnits.TtsUnitsToMeters + TtsUnits.TableSurfaceY, 4);
            var z = Math.Round(metres.Z / TtsUnits.TtsUnitsToMeters, 4);
            t["x"] = x; t["y"] = y; t["z"] = z;
            return AsVector(script, t, x, y, z);
        }

        /// <summary>A TTS Vector (the prelude's metatable gives v[1] and the methods); a plain table with both keys without it.</summary>
        private static Table AsVector(Script script, Table t, double x, double y, double z)
        {
            if (script.Globals.Get(LuaPrelude.VectorMeta) is { Type: DataType.Table } meta)
                t.MetaTable = meta.Table;
            else
            {
                t[1] = x; t[2] = y; t[3] = z;
            }
            return t;
        }

        private static Vec3? FromTts(DynValue value, bool isPosition)
        {
            if (value.Type != DataType.Table)
                return null;
            var t = value.Table;
            double? Read(string key, int index)
            {
                var v = t.Get(key);
                if (v.Type != DataType.Number)
                    v = t.Get(index);
                return v.Type == DataType.Number ? v.Number : (double?)null;
            }
            if (!(Read("x", 1) is { } x && Read("y", 2) is { } y && Read("z", 3) is { } z))
                return null;
            return isPosition
                ? new Vec3((float)(x * TtsUnits.TtsUnitsToMeters), (float)((y - TtsUnits.TableSurfaceY) * TtsUnits.TtsUnitsToMeters), (float)(z * TtsUnits.TtsUnitsToMeters))
                : new Vec3((float)x, (float)y, (float)z);
        }

        private static Table Vector(Script script, Vec3 v)
        {
            var t = new Table(script);
            double x = Math.Round(v.X, 4), y = Math.Round(v.Y, 4), z = Math.Round(v.Z, 4);
            t["x"] = x; t["y"] = y; t["z"] = z;
            return AsVector(script, t, x, y, z);
        }
    }
}
