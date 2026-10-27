using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Text
{
    /// <summary>
    /// A game in several languages (ADR 0011). The game's texts as written are in <see cref="GameInfo.Language"/>;
    /// <see cref="TableState.Translations"/> holds the others by path key (<see cref="Texts"/> lists every one), plus the
    /// game's own free texts (<c>strings.&lt;key&gt;</c>, read where a text says <c>@key</c>). The session's language
    /// (<see cref="Session.Language"/>) picks what players read; a text a translation lacks reads as written.
    /// </summary>
    public static class Translations
    {
        public const string StringsPrefix = "strings.";

        /// <summary>
        /// A sentence a script writes, translated by its own words (<c>Game.tr("Evento de noche")</c>): the key is this
        /// prefix and the text as written, so wrapping a literal is all a script needs.
        /// </summary>
        public const string ScriptPrefix = "tr:";

        /// <summary>The fields a text card prints, as their path keys name them (<c>.text.body</c>).</summary>
        public static readonly string[] CardTextFields = { "title", "kind", "body", "footer", "corner" };

        /// <summary>The language the game is written in; null for a game in one language.</summary>
        public static string? BaseLanguage(TableState state) => string.IsNullOrWhiteSpace(state.Game?.Language) ? null : state.Game!.Language;

        /// <summary>The languages the game can be played in: its own first, then <see cref="GameInfo.Languages"/> or every translation.</summary>
        public static IReadOnlyList<string> Languages(TableState state)
        {
            var languages = new List<string>();
            if (BaseLanguage(state) is { } own)
                languages.Add(own);
            var declared = state.Game?.Languages is { Count: > 0 } list ? list : state.Translations.Keys.ToList();
            foreach (var language in declared)
                if (!languages.Contains(language))
                    languages.Add(language);
            return languages;
        }

        /// <summary>The language players read now: the session's when the game has it, else the game's own.</summary>
        public static string? Current(TableState state)
        {
            var session = state.Session?.Language;
            return !string.IsNullOrWhiteSpace(session) && (session == BaseLanguage(state) || state.Translations.ContainsKey(session!)) ? session : BaseLanguage(state);
        }

        /// <summary>
        /// Every text of the game that a translation can replace: path key → the text as written. Objects
        /// (<c>objects.&lt;id&gt;.name</c>, <c>.description</c>, a text card's <c>.text.&lt;field&gt;</c>), cards in decks
        /// (<c>cards.&lt;id&gt;...</c>), seats, resources, actions, choice options (<c>params.&lt;action&gt;.&lt;param&gt;.&lt;option&gt;</c>),
        /// vote options (<c>votes.&lt;action&gt;.&lt;option&gt;</c>), phases (<c>.name</c>, <c>.sleepText</c>), panels
        /// (<c>ui.&lt;panel&gt;.title</c>, <c>ui.&lt;panel&gt;.&lt;index&gt;.text</c>), the game's messages, notebook pages, teams and roles,
        /// and the game's own strings.
        /// </summary>
        public static Dictionary<string, string> Texts(TableState state)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string key, string? text)
            {
                if (!string.IsNullOrWhiteSpace(text))
                    texts[key] = text!;
            }
            void AddCardText(string prefix, CardText? text)
            {
                if (text == null)
                    return;
                Add(prefix + ".text.title", text.Title);
                Add(prefix + ".text.kind", text.Kind);
                Add(prefix + ".text.body", text.Body);
                Add(prefix + ".text.footer", text.Footer);
                Add(prefix + ".text.corner", text.Corner);
            }

            if (state.Game != null)
            {
                Add("game.name", state.Game.Name);
                Add("game.description", state.Game.Description);
                foreach (var (key, text) in state.Game.Messages ?? new Dictionary<string, string>())
                    Add("messages." + key, text);
            }
            foreach (var obj in state.Objects)
            {
                Add($"objects.{obj.Id}.name", obj.Name);
                Add($"objects.{obj.Id}.description", obj.Description);
                if (obj is Card card)
                    AddCardText($"objects.{obj.Id}", card.Text);
                if (obj is Deck deck)
                    foreach (var inDeck in deck.Cards.Where(c => !string.IsNullOrEmpty(c.Id)))
                    {
                        Add($"cards.{inDeck.Id}.name", inDeck.Name);
                        Add($"cards.{inDeck.Id}.description", inDeck.Description);
                        AddCardText($"cards.{inDeck.Id}", inDeck.Text);
                    }
            }
            foreach (var seat in state.Players.Seats)
                Add($"seats.{seat.Id}.name", seat.Name);
            foreach (var zone in state.Zones)
                Add($"zones.{zone.Id}.name", zone.Name);
            foreach (var resource in state.Resources)
                Add($"resources.{resource.Id}.name", resource.Name);
            foreach (var action in state.Actions)
            {
                Add($"actions.{action.Id}.name", action.Name);
                foreach (var param in action.Params.Where(p => p.Type == ParamType.Choice))
                    foreach (var option in param.Options)
                        Add($"params.{action.Id}.{param.Name}.{option}", param.Labels != null && param.Labels.TryGetValue(option, out var label) ? label : option);
                if (action.Vote is { Seats: false } vote)
                    foreach (var option in vote.Options)
                        Add($"votes.{action.Id}.{option}", vote.Labels != null && vote.Labels.TryGetValue(option, out var label) ? label : option);
            }
            foreach (var phase in state.Phases)
            {
                Add($"phases.{phase.Id}.name", phase.Name);
                Add($"phases.{phase.Id}.sleepText", phase.SleepText);
            }
            foreach (var panel in state.Ui?.Panels ?? new List<PanelDef>())
            {
                Add($"ui.{panel.Id}.title", panel.Title);
                for (var i = 0; i < panel.Elements.Count; i++)
                    Add($"ui.{panel.Id}.{i}.text", panel.Elements[i].Text);
            }
            foreach (var tab in state.Notebook)
            {
                Add($"notebook.{tab.Id}.title", tab.Title);
                Add($"notebook.{tab.Id}.body", tab.Body);
            }
            if (state.Roles != null)
            {
                foreach (var team in state.Roles.Teams)
                    Add($"teams.{team.Id}.name", team.Name);
                foreach (var role in state.Roles.List)
                {
                    Add($"roles.{role.Id}.name", role.Name);
                    Add($"roles.{role.Id}.description", role.Description);
                }
            }
            if (BaseLanguage(state) is { } own && state.Translations.TryGetValue(own, out var mine))
                foreach (var (key, text) in mine.Where(p => p.Key.StartsWith(StringsPrefix, StringComparison.Ordinal)))
                    Add(key, text);
            foreach (var script in state.Scripts.Values)
                foreach (var text in ScriptTexts(script))
                    Add(ScriptPrefix + text, text);
            foreach (var obj in state.Objects.Where(o => !string.IsNullOrEmpty(o.Script)))
                foreach (var text in ScriptTexts(obj.Script!))
                    Add(ScriptPrefix + text, text);
            return texts;
        }

        /// <summary>
        /// The literal texts a Lua script wraps in <c>tr(...)</c> (<c>Game.tr("...")</c>, or a local <c>tr</c>): what
        /// translate_game lists for it. Texts built at run time are not found; wrap their fixed parts.
        /// </summary>
        public static IEnumerable<string> ScriptTexts(string script)
        {
            foreach (System.Text.RegularExpressions.Match match in ScriptCall.Matches(script))
            {
                var raw = match.Groups[2].Value;
                yield return raw.Replace("\\" + match.Groups[1].Value, match.Groups[1].Value).Replace("\\n", "\n").Replace("\\\\", "\\");
            }
        }

        private static readonly System.Text.RegularExpressions.Regex ScriptCall = new System.Text.RegularExpressions.Regex(
            @"\btr\s*\(\s*([""'])((?:\\.|(?!\1).)*)\1", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>A sentence a script wrote with <c>Game.tr</c>, in the session's language.</summary>
        public static string Script(TableState state, string text) => Text(state, ScriptPrefix + text, text) ?? text;

        /// <summary>
        /// What players read for the text at <paramref name="key"/>, written as <paramref name="written"/>: the translation in
        /// the session's language when it has one, else as written. A game without translations always reads as written.
        /// </summary>
        public static string? Text(TableState state, string key, string? written)
        {
            if (state.Translations.Count == 0 || !(Current(state) is { } language) || language == BaseLanguage(state))
                return written;
            return state.Translations.TryGetValue(language, out var table) && table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text) ? text : written;
        }

        /// <summary>
        /// A text that may name one of the game's strings: <c>@noche</c> reads <c>strings.noche</c> in the session's language,
        /// else in the game's own; <c>@@</c> writes a literal <c>@</c>. Any other text is returned as it is.
        /// </summary>
        public static string Resolve(TableState state, string text)
        {
            if (text.Length < 2 || text[0] != '@')
                return text;
            if (text[1] == '@')
                return text.Substring(1);
            var key = StringsPrefix + text.Substring(1);
            string? From(string? language) =>
                language != null && state.Translations.TryGetValue(language, out var table) && table.TryGetValue(key, out var found) ? found : null;
            return From(Current(state)) ?? From(BaseLanguage(state)) ?? text;
        }

        /// <summary>The file for <paramref name="asset"/> in the session's language: its localized path when it has one, else its own.</summary>
        public static string AssetPath(TableState state, Asset asset) =>
            asset.Localized.Count > 0 && Current(state) is { } language && asset.Localized.TryGetValue(language, out var path) && !string.IsNullOrWhiteSpace(path)
                ? path
                : asset.Path;

        /// <summary>
        /// Errors in the game's translations: translations without a language of its own, a language the game does not
        /// list, a key that names no text, an <c>@key</c> the game's own strings lack, a localized file for an unknown
        /// language.
        /// </summary>
        public static List<ValidationError> Validate(TableState state)
        {
            var errors = new List<ValidationError>();
            // A game in one language has nothing to check (and its texts may start with @ for its own reasons).
            if (!(BaseLanguage(state) is { } own))
            {
                if (state.Translations.Count > 0 || state.Assets.Any(a => a.Localized.Count > 0))
                    errors.Add(new ValidationError("game.language", ErrorCodes.MissingField, "A game with translations says which language it is written in.",
                        "Set game.language, e.g. \"es\"."));
                return errors;
            }
            var languages = Languages(state);
            var texts = Texts(state);
            foreach (var (language, table) in state.Translations)
            {
                if (!languages.Contains(language))
                    errors.Add(new ValidationError($"translations.{language}", ErrorCodes.InvalidValue, $"'{language}' is not one of the game's languages ({string.Join(", ", languages)}).",
                        "Add it to game.languages."));
                foreach (var key in table.Keys)
                {
                    if (key.StartsWith(StringsPrefix, StringComparison.Ordinal))
                    {
                        if (language != own && !texts.ContainsKey(key))
                            errors.Add(new ValidationError($"translations.{language}.{key}", ErrorCodes.UnknownKey,
                                $"'{key}' is not one of the game's strings in its own language ({own}).", $"Add it to translations.{own} first."));
                        continue;
                    }
                    if (language == own && key.StartsWith(ScriptPrefix, StringComparison.Ordinal))
                        errors.Add(new ValidationError($"translations.{language}.{key}", ErrorCodes.UnknownKey,
                            "A script's own words are written in the script; they need no entry in the game's own language.", "Remove it."));
                    else if (language == own)
                        errors.Add(new ValidationError($"translations.{language}.{key}", ErrorCodes.UnknownKey,
                            $"The game's own language holds only its strings; '{key}' is written in the game itself.", "Change the text where it is, or use a strings.<key>."));
                    else if (!texts.ContainsKey(key))
                        errors.Add(new ValidationError($"translations.{language}.{key}", ErrorCodes.UnknownKey, $"'{key}' names no text of the game.",
                            Suggest(key, texts.Keys)));
                }
            }
            foreach (var asset in state.Assets)
                foreach (var language in asset.Localized.Keys.Where(l => !languages.Contains(l)))
                    errors.Add(new ValidationError($"assets.{asset.Id}.localized.{language}", ErrorCodes.InvalidValue,
                        $"'{language}' is not one of the game's languages ({string.Join(", ", languages)})."));
            foreach (var name in AtReferences(state).Where(n => !texts.ContainsKey(StringsPrefix + n)).Distinct())
                errors.Add(new ValidationError($"translations.{own}.{StringsPrefix}{name}", ErrorCodes.MissingField,
                    $"A text says @{name}, but the game has no string '{name}' in its own language ({own}).", $"Add \"{StringsPrefix}{name}\" to translations.{own}."));
            return errors;
        }

        /// <summary>
        /// Each language's missing texts: path keys the game has and that language's translation lacks (a warning, so a
        /// translation that is not done yet still plays, its missing texts read as written).
        /// </summary>
        public static Dictionary<string, List<string>> Missing(TableState state)
        {
            var missing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var own = BaseLanguage(state);
            if (own == null)
                return missing;
            var texts = Texts(state);
            foreach (var language in Languages(state).Where(l => l != own))
            {
                state.Translations.TryGetValue(language, out var table);
                missing[language] = texts.Keys.Where(k => table == null || !table.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            }
            return missing;
        }

        /// <summary>The names every <c>@name</c> text of the game refers to (anywhere in the document: effects, panels, names).</summary>
        private static IEnumerable<string> AtReferences(TableState state)
        {
            var document = JToken.Parse(TableJson.Serialize(state));
            foreach (var value in document.SelectTokens("$..*").OfType<JValue>())
                if (value.Type == JTokenType.String && value.Value is string s && s.Length > 1 && s[0] == '@' && s[1] != '@'
                    && !(value.Parent is JProperty { Name: "id" }) && IsName(s.Substring(1)))
                    yield return s.Substring(1);
        }

        private static bool IsName(string name) => name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.');

        private static string? Suggest(string key, IEnumerable<string> keys)
        {
            var prefix = key.Contains('.') ? key.Substring(0, key.IndexOf('.') + 1) : key;
            var near = keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Take(5).ToList();
            return near.Count > 0 ? $"Keys like it: {string.Join(", ", near)}." : null;
        }
    }
}
