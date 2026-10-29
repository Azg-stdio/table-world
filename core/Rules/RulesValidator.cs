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
            // A table may have more seats than players (a 3–6 player game has six); the game starts with those seated.
            if (_state.Game != null && (seats < _state.Game.MinPlayers || _state.Game.MaxPlayers < _state.Game.MinPlayers))
                Add("players.seats", ErrorCodes.PlayerCount, $"{seats} seats, but the game takes {_state.Game.MinPlayers}–{_state.Game.MaxPlayers} players.",
                    "Give the table at least minPlayers seats (new_game players = the most players), and keep minPlayers ≤ maxPlayers.");

            var hasRules = _state.Turn != null || _state.Phases.Count > 0 || _state.Actions.Count > 0;
            if (!hasRules)
            {
                CheckUi(new HashSet<string>());
                return;
            }
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
                if (phase.Awake is { } awake && awake != "nobody" && awake != "all")
                {
                    if (References.IsReference(awake))
                    {
                        if (References.Check(awake, $"{path}.awake", _state, null) is { } awakeError)
                            _errors.Add(awakeError);
                    }
                    else
                        Audience(awake, $"{path}.awake");
                }
                if (phase.Raised is { } thumbs)
                {
                    if (References.IsReference(thumbs))
                    {
                        if (References.Check(thumbs, $"{path}.raised", _state, null) is { } thumbsError)
                            _errors.Add(thumbsError);
                    }
                    else
                        Audience(thumbs, $"{path}.raised");
                }
                if (phase.Next is { } nextPhase && !phaseIds.Contains(nextPhase))
                    Add($"{path}.next", ErrorCodes.UnknownPhase, $"Phase '{nextPhase}' does not exist.", Suggest.OneOf(nextPhase, phaseIds, "a phase id"));
                if (phase.Seconds is { } seconds && (seconds < 1 || seconds > 3600))
                    Add($"{path}.seconds", ErrorCodes.InvalidValue, $"A timed phase lasts 1 to 3600 seconds, not {seconds}.");
                if (phase.Voice == PhaseVoice.Team && _state.Roles == null)
                    Add($"{path}.voice", ErrorCodes.UnknownTeam, "Team voice needs teams.", "Add roles with teams (set_roles), or use all, awake or none.");
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

            for (var i = 0; i < _state.Marks.Count; i++)
            {
                var mark = _state.Marks[i];
                if (References.IsReference(mark.Seats))
                {
                    if (References.Check(mark.Seats, $"marks[{i}].seats", _state, null) is { } markError)
                        _errors.Add(markError);
                }
                else
                    Audience(mark.Seats, $"marks[{i}].seats");
                if (mark.Color is { } c && (c.Length != 3 || c.Any(v => v < 0f || v > 1f)))
                    Add($"marks[{i}].color", ErrorCodes.InvalidValue, "A colour is [r, g, b], each 0 to 1.");
            }

            for (var i = 0; i < _state.Actions.Count; i++)
            {
                var action = _state.Actions[i];
                var path = $"actions[{i}]";
                _params = Core.Engine.GameEngine.ParamsOf(action).Select(p => p.Name).ToList();
                if (action.OnPlayer && (action.Params.Count != 1 || action.Params[0].Type != ParamType.Seat || action.Vote != null))
                    Add($"{path}.onPlayer", ErrorCodes.InvalidValue, "An action done by clicking a player has exactly one parameter, of type seat, and is not a vote.");
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
                if (action.Vote is { } vote)
                {
                    if (!vote.Seats && vote.Options.Count < 2)
                        Add($"{path}.vote.options", ErrorCodes.MissingField, "A vote needs at least two options, or \"seats\": true to vote for a player.");
                    if (vote.Who != null && !References.IsReference(vote.Who))
                        Audience(vote.Who, $"{path}.vote.who");
                    else if (vote.Who != null && References.Check(vote.Who, $"{path}.vote.who", _state, _params) is { } whoError)
                        _errors.Add(whoError);
                    if (vote.Otherwise != null && !vote.Seats && !vote.Options.Contains(vote.Otherwise) && vote.Otherwise != "tie")
                        Add($"{path}.vote.otherwise", ErrorCodes.InvalidValue, $"'{vote.Otherwise}' is not one of the vote's options.",
                            Suggest.OneOf(vote.Otherwise, vote.Options.Append("tie"), "an option or tie"));
                    if (vote.Seconds is < 1)
                        Add($"{path}.vote.seconds", ErrorCodes.InvalidValue, "A vote's time is at least 1 second.");
                    if (vote.Colors != null)
                        foreach (var pair in vote.Colors)
                        {
                            if (!vote.Options.Contains(pair.Key))
                                Add($"{path}.vote.colors.{pair.Key}", ErrorCodes.InvalidValue, $"'{pair.Key}' is not one of the vote's options.",
                                    Suggest.OneOf(pair.Key, vote.Options, "an option"));
                            Colour(pair.Value, $"{path}.vote.colors.{pair.Key}");
                        }
                    if (action.Secret || action.Effects.Count > 0)
                        Add($"{path}.vote", ErrorCodes.InvalidValue, "A vote is secret by itself and runs its onResult once counted.",
                            "Drop secret, and move the effects to vote.onResult.");
                    Effects(vote.OnResult, $"{path}.vote.onResult", phaseIds);
                }
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

            for (var i = 0; i < _state.Zones.Count; i++)
            {
                Effects(_state.Zones[i].OnEnter, $"zones[{i}].onEnter", phaseIds);
                Effects(_state.Zones[i].OnLeave, $"zones[{i}].onLeave", phaseIds);
            }

            for (var i = 0; i < _state.WinConditions.Count; i++)
            {
                var win = _state.WinConditions[i];
                if (win.Team != null && (_state.Roles == null || _state.Roles.Teams.All(t => t.Id != win.Team)))
                    Add($"winConditions[{i}].team", ErrorCodes.UnknownTeam, $"Team '{win.Team}' does not exist.",
                        _state.Roles == null ? "Teams come with roles (set_roles)." : Suggest.OneOf(win.Team, _state.Roles.Teams.Select(t => t.Id), "a team id"));
                if (win.When == null)
                    Add($"winConditions[{i}].when", ErrorCodes.MissingField, "A win condition needs 'when'.");
                else
                    Cond(win.When, $"winConditions[{i}].when");
                Seat(win.Winner, $"winConditions[{i}].winner");
            }

            CheckUi(actionIds);
            CheckRoles();
            for (var i = 0; i < _state.Zones.Count; i++)
                if (_state.Zones[i].Catch is { } catcher)
                {
                    if (string.IsNullOrWhiteSpace(catcher.Tag))
                        Add($"zones[{i}].catch.tag", ErrorCodes.MissingField, "A zone that catches pieces names their tag.", "\"catch\": {\"tag\": \"vote\", \"for\": \"seat_1\"}");
                    if (catcher.For != null && catcher.For != "all" && !References.IsReference(catcher.For) && _state.Players.Seats.All(s => s.Id != catcher.For))
                        Audience(catcher.For, $"zones[{i}].catch.for");
                }
            if (_state.Music is { } tableMusic)
            {
                foreach (var pair in tableMusic.Playlists)
                    for (var i = 0; i < pair.Value.Count; i++)
                        if (!MusicTrack.Valid(_state, pair.Value[i].Url))
                            Add($"music.playlists.{pair.Key}[{i}].url", ErrorCodes.InvalidValue, $"'{pair.Value[i].Url}' is not a track.",
                                "Use library:<pack>/<track> (list_music), an audio asset's id, or a web address.");
                if (tableMusic.Start != null && !tableMusic.Playlists.ContainsKey(tableMusic.Start))
                    Add("music.start", ErrorCodes.InvalidValue, $"There is no playlist '{tableMusic.Start}'.", Suggest.OneOf(tableMusic.Start, tableMusic.Playlists.Keys, "a playlist"));
                if (tableMusic.Volume is < 0f or > 1f)
                    Add("music.volume", ErrorCodes.InvalidValue, "The music's volume goes from 0 to 1.");
            }
            if (_state.Game?.Highlights != null)
                foreach (var pair in _state.Game.Highlights)
                    Colour(pair.Value, $"game.highlights.{pair.Key}");
            for (var i = 0; i < _state.SecretVariables.Count; i++)
                if (!_state.Variables.ContainsKey(_state.SecretVariables[i]))
                    Add($"secretVariables[{i}]", ErrorCodes.UnknownVariable, $"Variable '{_state.SecretVariables[i]}' is not declared.",
                        Suggest.OneOf(_state.SecretVariables[i], _state.Variables.Keys, "a declared variable") ?? "Declare it under 'variables'.");

            for (var i = 0; i < _state.Resources.Count; i++)
                if (_state.Resources.Take(i).Any(r => r.Id == _state.Resources[i].Id))
                    Add($"resources[{i}].id", ErrorCodes.DuplicateId, $"Resource '{_state.Resources[i].Id}' is defined twice.");
        }

        /// <summary>A role effect needs roles (and the role set it names); false when the error was reported.</summary>
        private bool NeedsRoles(string path, string? set)
        {
            if (_state.Roles == null)
            {
                Add(path, ErrorCodes.UnknownRole, "This effect works on hidden roles, and the game has none.", "Add a 'roles' block (set_roles).");
                return false;
            }
            if (set != null && _state.Roles.List.All(r => r.Set != set))
            {
                Add($"{path}.set", ErrorCodes.UnknownRole, $"No role belongs to the set '{set}'.",
                    Suggest.OneOf(set, Core.Engine.GameEngine.RoleSets(_state.Roles), "a role set"));
                return false;
            }
            return true;
        }

        /// <summary>A <c>to</c> field: a seat, <c>all</c>, <c>team:&lt;id&gt;</c> or <c>role:&lt;id&gt;</c> of this game.</summary>
        private void Audience(string? to, string path)
        {
            if (to == null || to == "all")
                return;
            if (to.StartsWith("team:") || to.StartsWith("role:"))
            {
                var isTeam = to.StartsWith("team:");
                var id = to.Substring(5);
                var known = isTeam ? _state.Roles?.Teams.Select(t => t.Id) : _state.Roles?.List.Select(r => r.Id);
                if (known == null || !known.Contains(id))
                    Add(path, isTeam ? ErrorCodes.UnknownTeam : ErrorCodes.UnknownRole, $"{(isTeam ? "Team" : "Role")} '{id}' does not exist.",
                        known == null ? "Add a 'roles' block (set_roles)." : Suggest.OneOf(id, known, isTeam ? "a team id" : "a role id"));
                return;
            }
            Seat(to, path);
        }

        /// <summary>Teams and roles that exist, counts that are numbers or fill, and team sizes the roles can fill (ADR 0012).</summary>
        private void CheckRoles()
        {
            if (!(_state.Roles is { } roles))
                return;
            var teams = new HashSet<string>();
            for (var i = 0; i < roles.Teams.Count; i++)
            {
                if (!teams.Add(roles.Teams[i].Id))
                    Add($"roles.teams[{i}].id", ErrorCodes.DuplicateId, $"Team '{roles.Teams[i].Id}' is defined twice.");
                Colour(roles.Teams[i].Color, $"roles.teams[{i}].color");
            }
            var ids = new HashSet<string>();
            for (var i = 0; i < roles.List.Count; i++)
            {
                var role = roles.List[i];
                if (!ids.Add(role.Id))
                    Add($"roles.list[{i}].id", ErrorCodes.DuplicateId, $"Role '{role.Id}' is defined twice.");
                Colour(role.Color, $"roles.list[{i}].color");
                // A role needs a team; a trait of an extra set may have none.
                if ((role.Set == null || role.Team.Length > 0) && !teams.Contains(role.Team))
                    Add($"roles.list[{i}].team", ErrorCodes.UnknownTeam, $"Team '{role.Team}' does not exist.", Suggest.OneOf(role.Team, teams, "a team id") ?? "Declare it under roles.teams.");
                var count = role.Count;
                var valid = count == null || role.Fills || count.Type == JTokenType.Integer && (int)count >= 0;
                if (!valid)
                    Add($"roles.list[{i}].count", ErrorCodes.InvalidValue, $"'{count}' is not a count.", "Use a number of seats, or \"fill\" for the seats left in its team.");
            }
            void Known(string? role, string? team, string path)
            {
                if (role != null && !ids.Contains(role))
                    Add($"{path}.role", ErrorCodes.UnknownRole, $"Role '{role}' does not exist.", Suggest.OneOf(role, ids, "a role id"));
                if (team != null && !teams.Contains(team))
                    Add($"{path}.team", ErrorCodes.UnknownTeam, $"Team '{team}' does not exist.", Suggest.OneOf(team, teams, "a team id"));
            }
            for (var i = 0; i < roles.Knows.Count; i++)
            {
                var rule = roles.Knows[i];
                var path = $"roles.knows[{i}]";
                if ((rule.Role == null) == (rule.Team == null))
                    Add(path, ErrorCodes.InvalidValue, "A knows rule names one role or one team that sees.", "Set role or team, not both.");
                Known(rule.Role, rule.Team, path);
                Known(null, rule.Sees.Team, $"{path}.sees");
                foreach (var seen in rule.Sees.Roles.Concat(rule.Sees.Except))
                    Known(seen, null, $"{path}.sees");
                if (rule.MinPlayers > rule.MaxPlayers)
                    Add(path, ErrorCodes.InvalidValue, $"minPlayers {rule.MinPlayers} is above maxPlayers {rule.MaxPlayers}.");
                if (rule.As == KnowsAs.OneOf && rule.Sees.Roles.Count < 2)
                    Add($"{path}.sees.roles", ErrorCodes.InvalidValue, "oneOf shows a seat as one of several roles: list at least two.");
            }
            if (roles.ByPlayers.Count == 0 && roles.List.Count(r => r.Fills && r.Set == null) > 1)
                Add("roles.list", ErrorCodes.RoleCount, "Without byPlayers only one role can fill the seats left.", "Give team sizes under roles.byPlayers.");
            foreach (var set in Core.Engine.GameEngine.RoleSets(roles).Where(s => roles.List.Count(r => r.Fills && r.Set == s) > 1))
                Add("roles.list", ErrorCodes.RoleCount, $"Only one role of the set '{set}' can fill the seats left.");
            foreach (var pair in roles.ByPlayers)
            {
                if (!int.TryParse(pair.Key, out var players) || players < 1)
                {
                    Add($"roles.byPlayers.{pair.Key}", ErrorCodes.InvalidValue, $"'{pair.Key}' is not a number of players.");
                    continue;
                }
                foreach (var team in pair.Value.Keys.Where(t => !teams.Contains(t)))
                    Add($"roles.byPlayers.{pair.Key}.{team}", ErrorCodes.UnknownTeam, $"Team '{team}' does not exist.", Suggest.OneOf(team, teams, "a team id"));
                // Checked without optional roles; a start with too many of them is refused with the reason (not every mix fits
                // every count: Avalon's five players cannot take all four optional minions).
                if (Core.Engine.GameEngine.RolesFor(roles, players, new List<string>()).Problem is { } problem)
                    Add($"roles.byPlayers.{pair.Key}", ErrorCodes.RoleCount, problem);
            }
        }

        private void CheckUi(HashSet<string> actionIds)
        {
            if (_state.Ui == null)
                return;
            _params = null;
            for (var i = 0; i < _state.Ui.Panels.Count; i++)
            {
                var panel = _state.Ui.Panels[i];
                var path = $"ui.panels[{i}]";
                if (string.IsNullOrEmpty(panel.Id))
                    Add($"{path}.id", ErrorCodes.MissingField, "A panel needs an id.");
                for (var j = 0; j < panel.VisibleTo.Count; j++)
                    Viewer(panel.VisibleTo[j], $"{path}.visibleTo[{j}]");
                for (var j = 0; j < panel.Elements.Count; j++)
                {
                    var e = panel.Elements[j];
                    var ep = $"{path}.elements[{j}]";
                    for (var k = 0; k < e.VisibleTo.Count; k++)
                        Viewer(e.VisibleTo[k], $"{ep}.visibleTo[{k}]");
                    if (e.Text != null)
                        foreach (var reference in References.InTemplate(e.Text))
                            if (References.Check(reference, $"{ep}.text", _state, null) is { } error)
                                _errors.Add(error);
                    switch (e.Type)
                    {
                        case UiElementType.Counter:
                            if (e.Value == null)
                                Add($"{ep}.value", ErrorCodes.MissingField, "A counter needs a value reference, e.g. $viewer.res.score.");
                            else if (!References.IsReference(e.Value))
                                Add($"{ep}.value", ErrorCodes.InvalidReference, "A counter value must be a $ reference.");
                            else if (References.Check(e.Value, $"{ep}.value", _state, null) is { } error)
                                _errors.Add(error);
                            break;
                        case UiElementType.Button:
                            if (e.Action == null)
                                Add($"{ep}.action", ErrorCodes.MissingField, "A button needs an action.");
                            else if (!actionIds.Contains(e.Action))
                                Add($"{ep}.action", ErrorCodes.UnknownAction, $"Action '{e.Action}' does not exist.", Suggest.OneOf(e.Action, actionIds, "an action id"));
                            if (e.Seat != null)
                                Seat(e.Seat, $"{ep}.seat");
                            break;
                        case UiElementType.Image:
                            if (e.Asset == null || _state.Assets.All(a => a.Id != e.Asset))
                                Add($"{ep}.asset", ErrorCodes.UnknownAsset, $"Asset '{e.Asset}' does not exist.",
                                    Suggest.OneOf(e.Asset ?? "", _state.Assets.Select(a => a.Id), "an asset id"));
                            break;
                    }
                }
            }
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
                    if ((set.Value != null ? 1 : 0) + (set.Delta != null ? 1 : 0) + (set.Add != null || set.Remove != null ? 1 : 0) != 1)
                        Add(path, ErrorCodes.InvalidValue, "setVariable needs exactly one of 'value', 'delta', or 'add'/'remove' for a list.");
                    Value(set.Value, $"{path}.value");
                    Value(set.Add, $"{path}.add");
                    Value(set.Remove, $"{path}.remove");
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
                case WinEffect win when win.Team != null:
                    if (_state.Roles == null || _state.Roles.Teams.All(t => t.Id != win.Team))
                        Add($"{path}.team", ErrorCodes.UnknownTeam, $"Team '{win.Team}' does not exist.",
                            _state.Roles == null ? "Teams come with roles (set_roles)." : Suggest.OneOf(win.Team, _state.Roles.Teams.Select(t => t.Id), "a team id"));
                    break;
                case WinEffect win:
                    Seat(win.Player, $"{path}.player");
                    break;
                case ShowEffect show:
                    Target(show.Object, $"{path}.object");
                    break;
                case MusicEffect music:
                {
                    var lists = _state.Music?.Playlists;
                    if (music.Playlist != null && (lists == null || !lists.ContainsKey(music.Playlist)))
                        Add($"{path}.playlist", ErrorCodes.InvalidValue, $"The game has no playlist '{music.Playlist}'.",
                            lists == null || lists.Count == 0 ? "Add playlists under music.playlists." : Suggest.OneOf(music.Playlist, lists.Keys, "a playlist"));
                    if (music.Track != null && !MusicTrack.Valid(_state, music.Track))
                        Add($"{path}.track", ErrorCodes.InvalidValue, $"'{music.Track}' is not a track.", "Use a library track, an audio asset's id or a web address.");
                    if (music.Volume is < 0f or > 1f)
                        Add($"{path}.volume", ErrorCodes.InvalidValue, "The music's volume goes from 0 to 1.");
                    if (music.Crossfade is < 0f or > 20f)
                        Add($"{path}.crossfade", ErrorCodes.InvalidValue, "A crossfade takes 0 to 20 seconds.");
                    var asked = (music.Playlist != null ? 1 : 0) + (music.Track != null ? 1 : 0) + (music.Pause ? 1 : 0) + (music.Resume ? 1 : 0) + (music.Stop ? 1 : 0);
                    if (asked > 1)
                        Add(path, ErrorCodes.InvalidValue, "A music effect does one thing: playlist, track, pause, resume or stop.");
                    break;
                }
                case SoundEffect sound:
                    if (string.IsNullOrWhiteSpace(sound.Play))
                        Add($"{path}.play", ErrorCodes.MissingField, "A sound effect names what to play.", "Use a built-in cue (" + string.Join(", ", SoundCues.Builtin) + ").");
                    else if (!SoundCues.Known(_state, sound.Play))
                        Add($"{path}.play", ErrorCodes.InvalidValue, $"'{sound.Play}' is not a sound.",
                            Suggest.OneOf(sound.Play, SoundCues.Builtin, "a built-in cue") ?? "Use a built-in cue, an audio asset's id, or library:<pack>/<track>.");
                    if (sound.Volume is < 0f or > 1f)
                        Add($"{path}.volume", ErrorCodes.InvalidValue, "A sound's volume goes from 0 to 1.");
                    if (sound.To != null && sound.To != "awake" && sound.To != "all")
                        Audience(sound.To, $"{path}.to");
                    break;
                case LightEffect light:
                    if (light.Part != "room" && light.Part != "table")
                        Add($"{path}.part", ErrorCodes.InvalidValue, $"'{light.Part}' is not a light.", "Use room (the lamp and the room) or table (the felt and the pieces).");
                    if (light.Scale is < 0f or > 8f)
                        Add($"{path}.scale", ErrorCodes.InvalidValue, "A light's scale goes from 0 (off) to 8; 1 is normal.");
                    if (light.Tint != null && (light.Tint.Length != 3 || light.Tint.Any(v => !(v >= 0f && v <= 2f))))
                        Add($"{path}.tint", ErrorCodes.InvalidValue, "A tint is three numbers from 0 to 2.");
                    if (light.Seconds is < 0f or > 30f)
                        Add($"{path}.seconds", ErrorCodes.InvalidValue, "A light change takes 0 to 30 seconds.");
                    break;
                case LuaEffect lua:
                    if (string.IsNullOrWhiteSpace(lua.Call))
                        Add($"{path}.call", ErrorCodes.MissingField, "A lua effect needs the name of a global Lua function to call.");
                    else if (!_state.Scripts.TryGetValue("global", out var global) ||
                             !System.Text.RegularExpressions.Regex.IsMatch(global, $@"(function\s+{System.Text.RegularExpressions.Regex.Escape(lua.Call)}\s*\(|\b{System.Text.RegularExpressions.Regex.Escape(lua.Call)}\s*=\s*function\b)"))
                        Add($"{path}.call", ErrorCodes.ScriptFailed, $"The global script defines no function '{lua.Call}'.",
                            $"Add 'function {lua.Call}(ctx) ... end' to scripts.global (set_script).");
                    break;
                case RevealRoleEffect show:
                    Seat(show.Seat, $"{path}.seat");
                    Audience(show.To, $"{path}.to");
                    NeedsRoles(path, show.Set);
                    break;
                case SetRoleEffect set:
                    Seat(set.Seat, $"{path}.seat");
                    if (NeedsRoles(path, set.Set) && !References.IsReference(set.Role) && _state.Roles!.List.All(r => r.Id != set.Role || r.Set != set.Set))
                        Add($"{path}.role", ErrorCodes.UnknownRole, $"'{set.Role}' is not a {(set.Set == null ? "role" : $"'{set.Set}' role")}.",
                            Suggest.OneOf(set.Role, _state.Roles.List.Where(r => r.Set == set.Set).Select(r => r.Id), "a role id"));
                    break;
                case SwapRolesEffect swap:
                    Seat(swap.A, $"{path}.a");
                    Seat(swap.B, $"{path}.b");
                    NeedsRoles(path, swap.Set);
                    break;
                case SetTeamEffect team:
                    Seat(team.Seat, $"{path}.seat");
                    if (NeedsRoles(path, null) && !References.IsReference(team.Team) && _state.Roles!.Teams.All(t => t.Id != team.Team))
                        Add($"{path}.team", ErrorCodes.UnknownTeam, $"Team '{team.Team}' does not exist.", Suggest.OneOf(team.Team, _state.Roles.Teams.Select(t => t.Id), "a team id"));
                    break;
                case EliminateEffect gone:
                    Seat(gone.Seat, $"{path}.seat");
                    break;
                case CloseVoteEffect close when _state.Actions.All(a => a.Id != close.Action || a.Vote == null):
                    Add($"{path}.action", ErrorCodes.UnknownAction, $"'{close.Action}' is not a vote.",
                        Suggest.OneOf(close.Action, _state.Actions.Where(a => a.Vote != null).Select(a => a.Id), "a vote"));
                    break;
                case CastBallotEffect ballot:
                    if (_state.Actions.All(a => a.Id != ballot.Action || a.Vote == null))
                        Add($"{path}.action", ErrorCodes.UnknownAction, $"'{ballot.Action}' is not a vote.",
                            Suggest.OneOf(ballot.Action, _state.Actions.Where(a => a.Vote != null).Select(a => a.Id), "a vote"));
                    Seat(ballot.Seat, $"{path}.seat");
                    break;
                case MessageEffect message when message.To != null:
                    Audience(message.To, $"{path}.to");
                    break;
                case RevealEffect reveal when reveal.Action != null:
                    var secret = _state.Actions.FirstOrDefault(a => a.Id == reveal.Action);
                    if (secret == null)
                        Add($"{path}.action", ErrorCodes.UnknownAction, $"Action '{reveal.Action}' does not exist.",
                            Suggest.OneOf(reveal.Action, _state.Actions.Where(a => a.Secret).Select(a => a.Id), "a secret action"));
                    else if (!secret.Secret)
                        Add($"{path}.action", ErrorCodes.InvalidValue, $"Action '{reveal.Action}' is not secret, so there is nothing to reveal.",
                            "Set \"secret\": true on the action, or leave out reveal's action to reveal every secret choice.");
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
                case InListCondition il:
                    Value(il.List, $"{path}.list");
                    Value(il.Value, $"{path}.value");
                    break;
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

        /// <summary>A <c>visibleTo</c> entry: a seat id or <c>host</c>.</summary>
        private void Viewer(string who, string path)
        {
            if (who != PanelDef.Host && _state.Players.Seats.All(s => s.Id != who))
                Add(path, ErrorCodes.UnknownSeat, $"'{who}' is neither a seat nor '{PanelDef.Host}'.",
                    Suggest.OneOf(who, _state.Players.Seats.Select(s => s.Id).Append(PanelDef.Host), "a seat id or host"));
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

        /// <summary>A colour players read (a team's, an option's, a highlighted word's): a seat colour name or #rrggbb.</summary>
        private void Colour(string? colour, string path)
        {
            if (colour != null && !Text.Highlights.TryParseColor(colour, out _))
                Add(path, ErrorCodes.InvalidValue, $"'{colour}' is not a colour.",
                    "Use a seat colour (" + string.Join(", ", SeatLayout.Colors) + ") or #rrggbb.");
        }

        private void Add(string path, string code, string message, string? suggestion = null) =>
            _errors.Add(new ValidationError(path, code, message, suggestion));
    }
}
