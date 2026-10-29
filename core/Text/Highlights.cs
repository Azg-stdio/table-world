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
            Colorize(Terms(state), line, lightBackground ? LightPanel : DarkPanel);

        /// <summary><paramref name="line"/> coloured for a panel of colour <paramref name="background"/> (sRGB 0–1).</summary>
        public static string Colorize(TableState state, string line, float[] background) => Colorize(Terms(state), line, background);

        public static string Colorize(IReadOnlyDictionary<string, float[]> terms, string line, bool lightBackground = false) =>
            Colorize(terms, line, lightBackground ? LightPanel : DarkPanel);

        /// <summary>
        /// The panels coloured words are made readable on when the caller does not know its own: the lightest dark glass
        /// and the darkest parchment they may be drawn on (a translucent strip over a lit table), so they read on any.
        /// </summary>
        public static readonly float[] DarkPanel = { 0.16f, 0.15f, 0.14f };
        public static readonly float[] LightPanel = { 0.78f, 0.72f, 0.58f };

        public static string Colorize(IReadOnlyDictionary<string, float[]> terms, string line, float[] background)
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
                    output.Append("<color=").Append(Hex(Readable(terms[match.Value], background))).Append('>');
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

        private static float Channel(float c) => c <= 0.03928f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>WCAG relative luminance of an sRGB colour (0 black, 1 white).</summary>
        public static float Luminance(float[] rgb) => 0.2126f * Channel(rgb[0]) + 0.7152f * Channel(rgb[1]) + 0.0722f * Channel(rgb[2]);

        /// <summary>WCAG contrast ratio of two sRGB colours (1 to 21).</summary>
        public static float Contrast(float[] a, float[] b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05f) / (Math.Min(la, lb) + 0.05f);
        }

        /// <summary>The contrast a coloured word keeps with its panel: WCAG AA for body text, with a little to spare.</summary>
        public const float MinContrast = 4.7f;

        /// <summary>
        /// A colour that reads on <paramref name="background"/>: mixed towards white on a dark panel, towards black on a
        /// light one, just enough to reach <see cref="MinContrast"/>. Its hue stays.
        /// </summary>
        public static float[] Readable(float[] rgb, float[] background)
        {
            if (Contrast(rgb, background) >= MinContrast)
                return rgb;
            var light = Luminance(background) > 0.18f;
            var target = light ? 0f : 1f;
            var colour = rgb;
            for (var step = 1; step <= 20; step++)
            {
                var t = step / 20f;
                colour = rgb.Select(c => c + (target - c) * t).ToArray();
                if (Contrast(colour, background) >= MinContrast)
                    break;
            }
            return colour;
        }

        public static float[] Readable(float[] rgb, bool lightBackground) => Readable(rgb, lightBackground ? LightPanel : DarkPanel);

        public static string Hex(float[] rgb) =>
            "#" + string.Concat(rgb.Take(3).Select(c => ((int)Math.Round(Math.Max(0f, Math.Min(1f, c)) * 255f)).ToString("x2", CultureInfo.InvariantCulture)));
    }
}
