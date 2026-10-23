using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    /// <summary>Semantic checks of the rules part of a game document (turn, phases, actions, effects, conditions, events, win).</summary>
    internal sealed class RulesValidator
    {
        private readonly TableState _state;
        private readonly List<ValidationError> _errors;
        private readonly HashSet<string> _ids;
        private ICollection<string>? _params;

        private RulesValidator(TableState state, List<ValidationError> errors)
        {
            _state = state;
            _errors = errors;
            // Objects, zones and cards inside decks (by their entry id) can all be named by a plain id.
            _ids = new HashSet<string>(state.Objects.Select(o => o.Id)
                .Concat(state.Zones.Select(z => z.Id))
                .Concat(state.Objects.OfType<Deck>().SelectMany(d => d.Cards).Where(c => c.Id != null).Select(c => c.Id!)));
        }

        public static void Validate(TableState state, List<ValidationError> errors) => new RulesValidator(state, errors).Run();

        private void Run()
        {
            var seats = _state.Players.Seats.Count;
            if (_state.Game != null && (seats < _state.Game.MinPlayers || seats > _state.Game.MaxPlayers))
                Add("players.seats", ErrorCodes.PlayerCount, $"{seats} seats, but the game takes {_state.Game.MinPlayers}–{_state.Game.MaxPlayers} players.",
                    "Add or remove seats, or change game.minPlayers/maxPlayers.");

            var hasRules = _state.Turn != null || _state.Phases.Count > 0 || _state.Actions.Count > 0;
            if (!hasRules)
                return;
            if (_state.Turn == null)
                Add("turn", ErrorCodes.MissingField, "Phases and actions need a 'turn' block.", "Add { \"phases\": [ ... ] } under 'turn'.");

            var phaseIds = new HashSet<string>();
            for (var i = 0; i < _state.Phases.Count; i++)
                if (!phaseIds.Add(_state.Phases[i].Id))
                    Add($"phases[{i}].id", ErrorCodes.DuplicateId, $"Phase '{_state.Phases[i].Id}' is defined twice.");
            var actionIds = new HashSet<string>();
            for (var i = 0; i < _state.Actions.Count; i++)
                if (!actionIds.Add(_state.Actions[i].Id))
                    Add($"actions[{i}].id", ErrorCodes.DuplicateId, $"Action '{_state.Actions[i].Id}' is defined twice.");

            if (_state.Turn != null)
            {
                if (_state.Turn.Phases.Count == 0)
                    Add("turn.phases", ErrorCodes.MissingField, "A turn needs at least one phase.");
                for (var i = 0; i < _state.Turn.Phases.Count; i++)
                    CheckPhaseRef(_state.Turn.Phases[i], $"turn.phases[{i}]", phaseIds);
                var start = _state.Turn.StartingPlayer;
                if (start != "first" && start != "random" && _state.Players.Seats.All(s => s.Id != start))
                    Add("turn.startingPlayer", ErrorCodes.UnknownSeat, $"'{start}' is not a seat, 'first' or 'random'.",
                        Suggest.OneOf(start, _state.Players.Seats.Select(s => s.Id).Concat(new[] { "first", "random" })));
            }

            for (var i = 0; i < _state.Phases.Count; i++)
            {
                var phase = _state.Phases[i];
                var path = $"phases[{i}]";
                for (var j = 0; j < phase.Actions.Count; j++)
                    if (!actionIds.Contains(phase.Actions[j]))
                        Add($"{path}.actions[{j}]", ErrorCodes.UnknownAction, $"Action '{phase.Actions[j]}' does not exist.",
                            Suggest.OneOf(phase.Actions[j], actionIds, "an action id") ?? "Define it under 'actions' first.");
                _params = null;
                Effects(phase.OnEnter, $"{path}.onEnter", phaseIds);
                Effects(phase.OnExit, $"{path}.onExit", phaseIds);
                if (phase.AdvanceWhen is { } advance)
                {
                    var set = (advance.Immediate ? 1 : 0) + (advance.AllPlayersActed ? 1 : 0) + (advance.CurrentPlayerEnds ? 1 : 0) + (advance.Condition != null ? 1 : 0);
                    if (set != 1)
                        Add($"{path}.advanceWhen", ErrorCodes.InvalidValue, $"advanceWhen sets {set} rules; set exactly one.",
                            "Use one of immediate, allPlayersActed, currentPlayerEnds, condition.");
                    if (advance.Condition != null)
                        Cond(advance.Condition, $"{path}.advanceWhen.condition");
                }
            }

            for (var i = 0; i < _state.Actions.Count; i++)
            {
                var action = _state.Actions[i];
                var path = $"actions[{i}]";
                _params = action.Params.Select(p => p.Name).ToList();
                for (var j = 0; j < action.Params.Count; j++)
                {
                    var p = action.Params[j];
                    if (p.Filter?.InZone != null)
                        Target(p.Filter.InZone, $"{path}.params[{j}].filter.inZone");
                    if (p.Type == ParamType.Choice && p.Options.Count == 0)
                        Add($"{path}.params[{j}].options", ErrorCodes.MissingField, "A choice parameter needs options.");
                }
                for (var j = 0; j < action.Preconditions.Count; j++)
                    Cond(action.Preconditions[j], $"{path}.preconditions[{j}]");
                Effects(action.Effects, $"{path}.effects", phaseIds);
                if (action.LimitPerPhase is { } limit && limit < 1)
                    Add($"{path}.limitPerPhase", ErrorCodes.InvalidValue, "limitPerPhase must be at least 1.");
            }
            _params = null;

            foreach (var pair in _state.Events)
            {
                if (!GameEvents.All.Contains(pair.Key))
                    Add($"events.{pair.Key}", ErrorCodes.UnknownEvent, $"Unknown event '{pair.Key}'.", Suggest.OneOf(pair.Key, GameEvents.All));
                Effects(pair.Value, $"events.{pair.Key}", phaseIds);
            }

            for (var i = 0; i < _state.WinConditions.Count; i++)
            {
                var win = _state.WinConditions[i];
                if (win.When == null)
                    Add($"winConditions[{i}].when", ErrorCodes.MissingField, "A win condition needs 'when'.");
                else
                    Cond(win.When, $"winConditions[{i}].when");
                Seat(win.Winner, $"winConditions[{i}].winner");
            }

            for (var i = 0; i < _state.Resources.Count; i++)
                if (_state.Resources.Take(i).Any(r => r.Id == _state.Resources[i].Id))
                    Add($"resources[{i}].id", ErrorCodes.DuplicateId, $"Resource '{_state.Resources[i].Id}' is defined twice.");
        }

        private void Effects(List<Effect> effects, string path, HashSet<string> phaseIds)
        {
            for (var i = 0; i < effects.Count; i++)
                Effect(effects[i], $"{path}[{i}]", phaseIds);
        }

        private void Effect(Effect effect, string path, HashSet<string> phaseIds)
        {
            path = $"{path}.{effect.Kind}";
            switch (effect)
            {
                case MoveEffect move:
                    if (move.Object == null && move.From == null)
                        Add(path, ErrorCodes.MissingField, "move needs 'object' or 'from'.", "Use 'object' for one thing, or 'from' + 'take' for cards off a deck or zone.");
                    if (move.Object != null) Target(move.Object, $"{path}.object");
                    if (move.From != null) Target(move.From, $"{path}.from");
                    Required(move.To, $"{path}.to");
                    if (move.To.Length > 0) Target(move.To, $"{path}.to");
                    OneOf(move.Take, $"{path}.take", "top", "bottom", "all", "allButTop");
                    OneOf(move.At, $"{path}.at", "top", "bottom");
                    if (move.Count < 1) Add($"{path}.count", ErrorCodes.InvalidValue, "count must be at least 1.");
                    break;
                case DealEffect deal:
                    Required(deal.From, $"{path}.from");
                    if (deal.From.Length > 0) Target(deal.From, $"{path}.from");
                    if ((deal.ToZone == null) == (deal.ToTag == null))
                        Add(path, ErrorCodes.InvalidValue, "deal needs exactly one of 'toZone' or 'toTag'.", "e.g. \"toZone\": \"$it.hand\" or \"toTag\": \"deck\".");
                    if (deal.ToZone != null && !deal.ToZone.StartsWith("$it")) Target(deal.ToZone, $"{path}.toZone");
                    for (var i = 0; i < deal.Seats.Count; i++) Seat(deal.Seats[i], $"{path}.seats[{i}]");
                    if (deal.Count < 1) Add($"{path}.count", ErrorCodes.InvalidValue, "count must be at least 1.");
                    break;
                case ShuffleEffect shuffle: Target(shuffle.Object, $"{path}.object"); break;
                case FlipEffect flip: Target(flip.Object, $"{path}.object"); break;
                case LockEffect lockEffect: Target(lockEffect.Object, $"{path}.object"); break;
                case RollEffect roll:
                    Target(roll.Object, $"{path}.object");
                    if (roll.Store != null) Variable(roll.Store, $"{path}.store");
                    break;
                case SetVariableEffect set:
                    Variable(set.Name, $"{path}.name");
                    if ((set.Value == null) == (set.Delta == null))
                        Add(path, ErrorCodes.InvalidValue, "setVariable needs exactly one of 'value' or 'delta'.");
                    Value(set.Value, $"{path}.value");
                    break;
                case SetResourceEffect res:
                    Seat(res.Player, $"{path}.player");
                    Resource(res.Resource, $"{path}.resource");
                    if ((res.Value == null) == (res.Delta == null))
                        Add(path, ErrorCodes.InvalidValue, "setResource needs exactly one of 'value' or 'delta'.");
                    break;
                case IfEffect ifEffect:
                    if (ifEffect.Condition == null) Add($"{path}.condition", ErrorCodes.MissingField, "if needs a condition.");
                    else Cond(ifEffect.Condition, $"{path}.condition");
                    Effects(ifEffect.Then, $"{path}.then", phaseIds);
                    Effects(ifEffect.Else, $"{path}.else", phaseIds);
                    break;
                case ForEachEffect each:
                    if (each.Players == (each.ObjectsIn != null))
                        Add(path, ErrorCodes.InvalidValue, "forEach needs exactly one of 'players': true or 'objectsIn'.");
                    if (each.ObjectsIn != null) Target(each.ObjectsIn, $"{path}.objectsIn");
                    Effects(each.Do, $"{path}.do", phaseIds);
                    break;
                case GotoPhaseEffect go:
                    CheckPhaseRef(go.Phase, $"{path}.phase", phaseIds);
                    break;
                case WinEffect win:
                    Seat(win.Player, $"{path}.player");
                    break;
            }
        }

        private void Cond(Condition condition, string path)
        {
            path = $"{path}.{condition.Kind}";
            switch (condition)
            {
                case ZoneCountCondition zc: Target(zc.Zone, $"{path}.zone"); Op(zc.Op, path); Value(zc.Value, $"{path}.value"); break;
                case CompareCondition cmp: Op(cmp.Op, path); Value(cmp.Left, $"{path}.left"); Value(cmp.Right, $"{path}.right"); break;
                case VariableCondition v: Variable(v.Name, $"{path}.name"); Op(v.Op, path); Value(v.Value, $"{path}.value"); break;
                case ResourceCondition r: Seat(r.Player, $"{path}.player"); Resource(r.Resource, $"{path}.resource"); Op(r.Op, path); break;
                case IsCurrentPlayerCondition p: Seat(p.Player, $"{path}.player"); break;
                case ObjectInCondition oi: Target(oi.Object, $"{path}.object"); Target(oi.Zone, $"{path}.zone"); break;
                case HasTagCondition ht: Target(ht.Object, $"{path}.object"); break;
                case AndCondition and: for (var i = 0; i < and.Conditions.Count; i++) Cond(and.Conditions[i], $"{path}[{i}]"); break;
                case OrCondition or: for (var i = 0; i < or.Conditions.Count; i++) Cond(or.Conditions[i], $"{path}[{i}]"); break;
                case NotCondition not when not.Condition != null: Cond(not.Condition, path); break;
            }
        }

        /// <summary>A field naming an object, deck or zone: a reference or an existing id.</summary>
        private void Target(string text, string path)
        {
            if (References.IsReference(text))
            {
                if (References.Check(text, path, _state, _params) is { } error)
                    _errors.Add(error);
            }
            else if (!_ids.Contains(text))
            {
                Add(path, ErrorCodes.UnknownObject, $"No object or zone '{text}'.",
                    Suggest.OneOf(text, _ids, "an object or zone id") ?? "Create it first (place_object / define_zone).");
            }
        }

        private void Seat(string text, string path)
        {
            if (References.IsReference(text))
            {
                if (References.Check(text, path, _state, _params) is { } error)
                    _errors.Add(error);
            }
            else if (_state.Players.Seats.All(s => s.Id != text))
            {
                Add(path, ErrorCodes.UnknownSeat, $"'{text}' is not a seat or a reference.", Suggest.OneOf(text, _state.Players.Seats.Select(s => s.Id), "a seat id"));
            }
        }

        private void Value(JToken? token, string path)
        {
            if (token is JValue { Type: JTokenType.String } v && References.IsReference((string)v!))
                if (References.Check((string)v!, path, _state, _params) is { } error)
                    _errors.Add(error);
        }

        private void Variable(string name, string path)
        {
            if (!_state.Variables.ContainsKey(name))
                Add(path, ErrorCodes.UnknownVariable, $"Variable '{name}' is not declared.",
                    Suggest.OneOf(name, _state.Variables.Keys, "a declared variable") ?? "Declare it under 'variables' with an initial value.");
        }

        private void Resource(string id, string path)
        {
            if (_state.Resources.All(r => r.Id != id))
                Add(path, ErrorCodes.UnknownResource, $"Resource '{id}' is not defined.",
                    Suggest.OneOf(id, _state.Resources.Select(r => r.Id), "a resource id") ?? "Define it under 'resources'.");
        }

        private void CheckPhaseRef(string id, string path, HashSet<string> phaseIds)
        {
            if (!phaseIds.Contains(id))
                Add(path, ErrorCodes.UnknownPhase, $"Phase '{id}' does not exist.", Suggest.OneOf(id, phaseIds, "a phase id") ?? "Define it under 'phases'.");
        }

        private void Op(string op, string path)
        {
            if (!ConditionKinds.Operators.Contains(op))
                Add($"{path}.op", ErrorCodes.InvalidOperator, $"'{op}' is not an operator.", $"Use one of {string.Join(" ", ConditionKinds.Operators)}.");
        }

        private void OneOf(string value, string path, params string[] allowed)
        {
            if (!allowed.Contains(value))
                Add(path, ErrorCodes.InvalidValue, $"'{value}' is not allowed here.", Suggest.OneOf(value, allowed));
        }

        private void Required(string value, string path)
        {
            if (string.IsNullOrEmpty(value))
                Add(path, ErrorCodes.MissingField, "This field is required.");
        }

        private void Add(string path, string code, string message, string? suggestion = null) =>
            _errors.Add(new ValidationError(path, code, message, suggestion));
    }
}
