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
                // An object picked by a player arrives under its table id; the script knows a drawn card by its own (getGUID).
                foreach (var pair in call.Params)
                    parameters[pair.Key] = pair.Value is string id && Engine.Find(id) is { } picked ? TableEngine.OwnId(picked) : pair.Value;
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
            t["getSeats"] = Fn(_ => Texts(script, (Game()?.Playing ?? Engine.State.Players.Seats).Select(s => s.Id)));
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
            // Hidden roles (ADR 0012): scripts run on the host, which knows every seat's.
            // getRole(seat, set): the role, or the trait of an extra role set.
            t["getRole"] = Fn(args => Game() is { } game && Arg(args, 0) is { } seat && game.RoleOf(seat, Arg(args, 1)) is { } role ? DynValue.NewString(role) : DynValue.Nil);
            t["getTeam"] = Fn(args => Game() is { } game && Arg(args, 0) is { } seat && game.TeamOf(seat) is { } team ? DynValue.NewString(team) : DynValue.Nil);
            // Languages (ADR 0011): the session's language, and the game's own strings in it (text("noche", { space = "A1" })).
            t["getLanguage"] = Fn(_ => DynValue.NewString(Core.Rules.Names.Language(Engine.State)));
            // tr("Evento de noche"): a sentence the script writes, in the session's language (keyed by its own words).
            t["tr"] = Fn(args => Arg(args, 0) is { } written ? DynValue.NewString(Core.Text.Translations.Script(Engine.State, written)) : DynValue.Nil);
            t["text"] = Fn(args =>
            {
                if (Arg(args, 0) is not { } key)
                    return DynValue.Nil;
                var text = Core.Text.Translations.Resolve(Engine.State, "@" + key);
                var fills = new List<(string, object?)>();
                if (args.Count > 1 && args[1].Type == DataType.Table)
                    foreach (var pair in args[1].Table.Pairs)
                        fills.Add((pair.Key.CastToString(), pair.Value.CastToString()));
                return DynValue.NewString(Core.Text.Strings.Fill(text, fills.ToArray()));
            });
            t["getLegalActions"] = Fn(args => Game() is { } game && Arg(args, 0) is { } seat
                ? Texts(script, game.LegalActions(seat).Select(a => a.ActionId))
                : Texts(script, Enumerable.Empty<string>()));

            t["setResource"] = Fn(args => Change("setResource", Arg(args, 0), Arg(args, 1), ToJson(args.Count > 2 ? args[2] : DynValue.Nil)));
            t["addResource"] = Fn(args => Change("addResource", Arg(args, 0), Arg(args, 1), ToJson(args.Count > 2 ? args[2] : DynValue.Nil)));
            t["setVariable"] = Fn(args => Change("setVariable", null, Arg(args, 0), ToJson(args.Count > 1 ? args[1] : DynValue.Nil)));
            t["endTurn"] = Fn(_ => Change("endTurn"));
            t["endPhase"] = Fn(_ => Change("endPhase"));
            t["gotoPhase"] = Fn(args => Change("gotoPhase", null, Arg(args, 0)));
            t["win"] = Fn(args => Change("win", Arg(args, 0)));
            // winTeam(team): hidden roles, the whole team wins.
            t["winTeam"] = Fn(args => Change("winTeam", null, Arg(args, 0)));
            t["log"] = Fn(args => Change("log", null, null, new JValue(Arg(args, 0) ?? "")));
            // revealRole(seat, to, teamOnly): to a seat, "team:<id>", "role:<id>", or every seat when to is nil or "all".
            t["revealRole"] = Fn(args => Change("revealRole", Arg(args, 0), Arg(args, 1), new JValue(args.Count > 2 && args[2].CastToBool())));
            // setRole(seat, role, tell) and setTeam(seat, team, tell): a conversion; tell lets the player know.
            t["setRole"] = Fn(args => Change("setRole", Arg(args, 0), Arg(args, 1), new JValue(args.Count > 2 && args[2].CastToBool())));
            t["setTeam"] = Fn(args => Change("setTeam", Arg(args, 0), Arg(args, 1), new JValue(args.Count > 2 && args[2].CastToBool())));
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
