using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization.Json;
using Newtonsoft.Json.Linq;

namespace Core.Scripting
{
    /// <summary>
    /// The rules side of the Lua API (docs/lua-api.md, "Game"): Lua reads the game in play (seats, phase, resources,
    /// variables) and changes it through <see cref="ScriptRulesCommand"/>s, and <c>lua</c> rule effects call global
    /// functions (<see cref="CallRule"/>). TTS has no rules engine, so these names are ours.
    /// </summary>
    public sealed partial class ScriptHost : IRulesScripts
    {
        /// <summary>The game whose rules scripts read; null when the table has no rules engine at hand.</summary>
        public Func<GameEngine?>? RulesEngine { get; set; }

        private readonly Random _dice = new Random();

        public string? CallRule(string function, RuleCall call)
        {
            if (_global == null)
                return "The table has no global script (scripts.global) to call.";
            var fn = _global.Globals.Get(function);
            if (fn.Type != DataType.Function)
                return $"The global script has no function '{function}'.";
            var errors = new List<string>();
            void Collect(string line) => errors.Add(line);
            var outer = _execute;
            // The function's changes join the rules action in progress instead of going out as commands of their own.
            _execute = command =>
            {
                command.ByScript = true;
                return call.Execute(command);
            };
            Log += Collect;
            try
            {
                var context = new Table(_global);
                if (call.Actor != null)
                    context["actor"] = call.Actor;
                if (call.It != null)
                    context["it"] = call.It;
                var parameters = new Table(_global);
                foreach (var pair in call.Params)
                    parameters[pair.Key] = pair.Value;
                context["params"] = parameters;
                context["args"] = call.Args != null ? ToLua(call.Args, _global) : DynValue.NewTable(new Table(_global));
                if (!RunFor(_global, fn, function, new[] { DynValue.NewTable(context) }, out var result))
                    return errors.LastOrDefault() ?? $"Lua function '{function}' failed.";
                // Returning false, or a reason, refuses the action.
                if (result.Type == DataType.Boolean && !result.Boolean)
                    return $"'{function}' refused.";
                if (result.Type == DataType.String)
                    return result.String;
                return null;
            }
            finally
            {
                Log -= Collect;
                _execute = outer;
            }
        }

        /// <summary>
        /// Runs a piece of Lua in the global script's environment (its functions and variables), under the usual budget:
        /// for trying a script out. Null when it ran, with what it returned as JSON in <paramref name="value"/>; else the error.
        /// </summary>
        public string? RunChunk(string code, out string? value)
        {
            value = null;
            _global ??= Compile("", null, "Global");
            if (_global == null)
                return "The Lua sandbox could not start.";
            DynValue function;
            try
            {
                function = _global.LoadString(code, null, "run_lua");
            }
            catch (SyntaxErrorException e)
            {
                return e.DecoratedMessage ?? e.Message;
            }
            var errors = new List<string>();
            void Collect(string line) => errors.Add(line);
            Log += Collect;
            try
            {
                if (!RunFor(_global, function, "run_lua", Array.Empty<DynValue>(), out var result))
                    return errors.LastOrDefault(l => l.Contains("error") || l.Contains("stopped")) ?? "The Lua failed.";
                value = result.IsNil() ? null : ToJson(result)?.ToString(Newtonsoft.Json.Formatting.None) ?? result.ToPrintString();
                return null;
            }
            finally
            {
                Log -= Collect;
            }
        }

        /// <summary>The first syntax error in <paramref name="code"/>, or null when it parses.</summary>
        public static string? CheckSyntax(string code)
        {
            try
            {
                new Script(CoreModules.None).LoadString(code, null, "script");
                return null;
            }
            catch (SyntaxErrorException e)
            {
                return e.DecoratedMessage ?? e.Message;
            }
        }

        private DynValue GameApi(Script script)
        {
            var t = new Table(script);
            GameEngine? Game() => RulesEngine?.Invoke();
            DynValue Fn(Func<CallbackArguments, DynValue> body) => DynValue.NewCallback((_, args) => body(args));
            string? Arg(CallbackArguments args, int i) => args.Count > i && !args[i].IsNil() ? args[i].CastToString() : null;
            DynValue Change(string op, string? seat = null, string? name = null, JToken? value = null) =>
                DynValue.NewBoolean(Issue(new ScriptRulesCommand { Op = op, Seat = seat, Name = name, Value = value }));

            t["isStarted"] = Fn(_ => DynValue.NewBoolean(Game()?.Session is { Started: true, Over: false }));
            t["isOver"] = Fn(_ => DynValue.NewBoolean(Game()?.Session?.Over == true));
            t["getWinner"] = Fn(_ => Game()?.Session?.Winner is { } w ? DynValue.NewString(w) : DynValue.Nil);
            t["getCurrentSeat"] = Fn(_ => Game()?.Session?.CurrentSeat is { } s ? DynValue.NewString(s) : DynValue.Nil);
            t["getPhase"] = Fn(_ => Game()?.Session?.Phase is { } p ? DynValue.NewString(p) : DynValue.Nil);
            t["getTurn"] = Fn(_ => DynValue.NewNumber(Game()?.Session?.Turn ?? 0));
            // The seats in the game (those it started with), in turn order; the empty ones are left out.
            t["getSeats"] = Fn(_ => List(script, (Game()?.Playing ?? Engine.State.Players.Seats).Select(s => s.Id)));
            t["getSeatColor"] = Fn(args => Engine.State.Players.Seats.FirstOrDefault(s => s.Id == Arg(args, 0))?.Color is { } c ? DynValue.NewString(c) : DynValue.Nil);
            t["getResource"] = Fn(args =>
                Game()?.Session?.Resources is { } all && Arg(args, 0) is { } seat && all.TryGetValue(seat, out var values) &&
                Arg(args, 1) is { } id && values.TryGetValue(id, out var value)
                    ? DynValue.NewNumber(value)
                    : DynValue.Nil);
            t["getVariable"] = Fn(args =>
                Game()?.Session?.Variables is { } vars && Arg(args, 0) is { } name && vars.TryGetValue(name, out var value)
                    ? ToLua(value, script)
                    : DynValue.Nil);
            // The dice in a zone: the total, or one seat's dice (tagged owner:<seat>).
            t["getDiceTotal"] = Fn(args =>
            {
                if (Arg(args, 0) is not { } zone)
                    return DynValue.Nil;
                var (total, _, bySeat) = Engine.DiceIn(zone);
                return DynValue.NewNumber(Arg(args, 1) is { } seat ? (bySeat.TryGetValue(seat, out var mine) ? mine : 0) : total);
            });
            t["getLegalActions"] = Fn(args => Game() is { } game && Arg(args, 0) is { } seat
                ? List(script, game.LegalActions(seat).Select(a => a.ActionId))
                : List(script, Enumerable.Empty<string>()));

            t["setResource"] = Fn(args => Change("setResource", Arg(args, 0), Arg(args, 1), ToJson(args.Count > 2 ? args[2] : DynValue.Nil)));
            t["addResource"] = Fn(args => Change("addResource", Arg(args, 0), Arg(args, 1), ToJson(args.Count > 2 ? args[2] : DynValue.Nil)));
            t["setVariable"] = Fn(args => Change("setVariable", null, Arg(args, 0), ToJson(args.Count > 1 ? args[1] : DynValue.Nil)));
            t["endTurn"] = Fn(_ => Change("endTurn"));
            t["endPhase"] = Fn(_ => Change("endPhase"));
            t["gotoPhase"] = Fn(args => Change("gotoPhase", null, Arg(args, 0)));
            t["win"] = Fn(args => Change("win", Arg(args, 0)));
            t["log"] = Fn(args => Change("log", null, null, new JValue(Arg(args, 0) ?? "")));
            t["perform"] = Fn(args =>
            {
                var command = new PerformActionCommand { Actor = Arg(args, 0), ActionId = Arg(args, 1) ?? "" };
                if (args.Count > 2 && args[2].Type == DataType.Table)
                    foreach (var pair in args[2].Table.Pairs)
                        command.Params[pair.Key.CastToString() ?? ""] = pair.Value.CastToString() ?? "";
                return DynValue.NewBoolean(Issue(command));
            });
            // Dice for a script's own reckoning (combat totals): Lua's dice, not the table's, so they never shift a shuffle.
            t["roll"] = Fn(args =>
            {
                var count = args.Count > 0 && args[0].Type == DataType.Number ? Math.Clamp((int)args[0].Number, 1, 100) : 1;
                var sides = args.Count > 1 && args[1].Type == DataType.Number ? Math.Clamp((int)args[1].Number, 2, 1000) : 6;
                var rolls = new Table(script);
                for (var i = 1; i <= count; i++)
                    rolls[i] = _dice.Next(1, sides + 1);
                return DynValue.NewTable(rolls);
            });
            return DynValue.NewTable(t);
        }

        private static DynValue ToLua(JToken token, Script script)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return DynValue.NewNumber((double)token);
                case JTokenType.String:
                    return DynValue.NewString((string)token!);
                case JTokenType.Boolean:
                    return DynValue.NewBoolean((bool)token);
                case JTokenType.Object:
                case JTokenType.Array:
                    return DynValue.NewTable(JsonTableConverter.JsonToTable(token.ToString(Newtonsoft.Json.Formatting.None), script));
                default:
                    return DynValue.Nil;
            }
        }

        private static JToken? ToJson(DynValue value)
        {
            switch (value.Type)
            {
                // Lua has one number type: whole numbers go out as integers (3, not 3.0).
                case DataType.Number:
                    return Math.Abs(value.Number) < 1e15 && value.Number == Math.Floor(value.Number) ? new JValue((long)value.Number) : new JValue(value.Number);
                case DataType.String: return new JValue(value.String);
                case DataType.Boolean: return new JValue(value.Boolean);
                case DataType.Table: return JToken.Parse(JsonTableConverter.TableToJson(value.Table));
                default: return null;
            }
        }
    }
}
