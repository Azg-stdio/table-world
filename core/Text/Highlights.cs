using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Core.Engine;
using Core.Rules;

namespace Core.Text
{
    /// <summary>
    /// Colours in what players read (M12): the game's lines (log, role, banners) name seats, teams, roles and vote
    /// options, and each is shown in its colour (a seat's colour, <see cref="TeamDef.Color"/>, <see cref="RoleDef.Color"/>,
    /// <see cref="VoteDef.Colors"/>, and the game's own <see cref="GameInfo.Highlights"/>). The lines themselves stay plain
    /// text: the window colours them as it shows them (<see cref="Colorize(TableState, string, bool)"/>), so logs, tests
    /// and agents read words only.
    /// </summary>
    public static class Highlights
    {
        /// <summary>Every coloured word of the table as players read it in the session's language, with its colour (0–1).</summary>
        public static Dictionary<string, float[]> Terms(TableState state)
        {
            var terms = new Dictionary<string, float[]>(StringComparer.Ordinal);
            void Add(string? text, string? color)
            {
                if (string.IsNullOrWhiteSpace(text) || !TryParseColor(color, out var rgb) || terms.ContainsKey(text!.Trim()))
                    return;
                terms[text.Trim()] = rgb;
            }
            foreach (var seat in state.Players.Seats)
                Add(Names.Of(state, seat), seat.Color);
            if (state.Game?.Highlights != null)
                foreach (var pair in state.Game.Highlights)
                    Add(pair.Key, pair.Value);
            foreach (var action in state.Actions)
                if (action.Vote is { Colors: { } colors } vote)
                    foreach (var pair in colors)
                        Add(VoteLabel(state, action, vote, pair.Key), pair.Value);
            if (state.Roles is { } roles)
            {
                foreach (var role in roles.List)
                    Add(GameEngine.RoleName(state, roles, role.Id), role.Color ?? roles.Teams.FirstOrDefault(t => t.Id == role.Team)?.Color);
                foreach (var team in roles.Teams)
                    Add(GameEngine.TeamName(state, roles, team.Id), team.Color);
            }
            return terms;
        }

        private static string VoteLabel(TableState state, ActionDef action, VoteDef vote, string option) =>
            vote.Labels != null && vote.Labels.TryGetValue(option, out var label) && !string.IsNullOrWhiteSpace(label)
                ? Translations.Text(state, $"votes.{action.Id}.{option}", label)! : Names.Humanize(option);

        private static string? _cachedKey;
        private static Regex? _cachedRegex;
        private static readonly object CacheLock = new object();

        /// <summary>
        /// <paramref name="line"/> with every coloured word wrapped in <c>&lt;color=#rrggbb&gt;</c>, readable on a dark
        /// background (or a light one, <paramref name="lightBackground"/>): too dark a colour is lightened, too light one
        /// darkened. Anything else that looks like a tag is shown as written, never read as one.
        /// </summary>
        public static string Colorize(TableState state, string line, bool lightBackground = false) =>
            Colorize(Terms(state), line, lightBackground);

        public static string Colorize(IReadOnlyDictionary<string, float[]> terms, string line, bool lightBackground = false)
        {
            if (string.IsNullOrEmpty(line))
                return line ?? "";
            var regex = RegexFor(terms);
            var output = new StringBuilder(line.Length + 32);
            var at = 0;
            if (regex != null)
                foreach (Match match in regex.Matches(line))
                {
                    Plain(output, line.Substring(at, match.Index - at));
                    output.Append("<color=").Append(Hex(Readable(terms[match.Value], lightBackground))).Append('>');
                    Plain(output, match.Value);
                    output.Append("</color>");
                    at = match.Index + match.Length;
                }
            Plain(output, line.Substring(at));
            return output.ToString();
        }

        /// <summary>Text shown as written: a <c>&lt;</c> in it would otherwise start a tag.</summary>
        private static void Plain(StringBuilder output, string text)
        {
            if (text.IndexOf('<') < 0)
                output.Append(text);
            else
                output.Append("<noparse>").Append(text.Replace("</noparse>", "</ noparse>")).Append("</noparse>");
        }

        private static Regex? RegexFor(IReadOnlyDictionary<string, float[]> terms)
        {
            if (terms.Count == 0)
                return null;
            var ordered = terms.Keys.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).ToList();
            var key = string.Join("\u0001", ordered);
            lock (CacheLock)
            {
                if (key == _cachedKey)
                    return _cachedRegex;
                // Whole words only: "Red" in "Red voted", never in "Reddish" or "Fred".
                var pattern = @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", ordered.Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])";
                _cachedRegex = new Regex(pattern, RegexOptions.CultureInvariant);
                _cachedKey = key;
                return _cachedRegex;
            }
        }

        /// <summary>The words of a coloured line, without its tags (what a log, a test or an agent reads).</summary>
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            var plain = Regex.Replace(text, @"</?color(=[^>]*)?>|</?noparse>", "");
            return plain.Replace("</ noparse>", "</noparse>");
        }

        /// <summary>A colour as a game writes it: a seat colour name (<c>red</c>, <see cref="SeatLayout.Colors"/>) or <c>#rgb</c> / <c>#rrggbb</c>.</summary>
        public static bool TryParseColor(string? spec, out float[] rgb)
        {
            rgb = new float[3];
            if (string.IsNullOrWhiteSpace(spec))
                return false;
            var value = spec!.Trim();
            if (value.StartsWith("#"))
            {
                var hex = value.Substring(1);
                if (hex.Length == 3)
                    hex = string.Concat(hex.Select(c => new string(c, 2)));
                if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
                    return false;
                rgb = new[] { (n >> 16 & 255) / 255f, (n >> 8 & 255) / 255f, (n & 255) / 255f };
                return true;
            }
            if (!SeatLayout.Colors.Contains(value.ToLowerInvariant()))
                return false;
            rgb = SeatLayout.Rgb(value);
            return true;
        }

        /// <summary>Brightness as the eye sees it (0 black, 1 white).</summary>
        public static float Luminance(float[] rgb) => 0.2126f * rgb[0] + 0.7152f * rgb[1] + 0.0722f * rgb[2];

        public const float MinOnDark = 0.36f;
        public const float MaxOnLight = 0.3f;

        /// <summary>
        /// A colour that reads on the panel: on a dark panel no darker than <see cref="MinOnDark"/> (mixed towards white),
        /// on a light one no lighter than <see cref="MaxOnLight"/> (mixed towards black). Its hue stays.
        /// </summary>
        public static float[] Readable(float[] rgb, bool lightBackground)
        {
            var l = Luminance(rgb);
            if (!lightBackground && l < MinOnDark)
            {
                var t = (MinOnDark - l) / (1f - l);
                return rgb.Select(c => c + (1f - c) * t).ToArray();
            }
            if (lightBackground && l > MaxOnLight)
            {
                var t = MaxOnLight / l;
                return rgb.Select(c => c * t).ToArray();
            }
            return rgb;
        }

        public static string Hex(float[] rgb) =>
            "#" + string.Concat(rgb.Take(3).Select(c => ((int)Math.Round(Math.Max(0f, Math.Min(1f, c)) * 255f)).ToString("x2", CultureInfo.InvariantCulture)));
    }
}
