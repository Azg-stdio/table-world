using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Core.Text;

namespace Core.Tests.Text
{
    /// <summary>
    /// T10.2: Table World's own interface reads texts by their English words (<c>Ui.T("...")</c> in the game's code, the
    /// UXML's texts). Every one has a Spanish entry (<c>ui:&lt;English&gt;</c>) with the same placeholders, Spanish keeps no
    /// entry nothing uses, and the HUD writes no bare English to a text or tooltip.
    /// </summary>
    public class InterfaceStringsTests
    {
        private static string Root()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "TableWorld.sln")))
                folder = folder.Parent;
            return folder?.FullName ?? throw new InvalidOperationException("TableWorld.sln not found above the test folder.");
        }

        private static string Project => Path.Combine(Root(), "unity", "Assets", "_Project");

        /// <summary>Texts the interface looks up but cannot be found in the code as literals (enum names shown through Ui.T).</summary>
        private static readonly string[] Dynamic =
            { "Cheerful", "Calm", "Grumpy", "Shy", "Cool", "Curious", "Sleepy", "off", "centers", "corners", "both" };

        private static readonly Regex Literal = new Regex(@"""((?:\\.|[^""\\])*)""", RegexOptions.Compiled);

        /// <summary>Every text the interface asks for: the literals in Ui.T's first argument, and the UXML's texts.</summary>
        public static HashSet<string> Keys()
        {
            var keys = new HashSet<string>(Dynamic, StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(Path.Combine(Project, "Scripts", "View"), "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                var at = 0;
                while ((at = source.IndexOf("Ui.T(", at, StringComparison.Ordinal)) >= 0)
                {
                    at += 5;
                    var end = FirstArgumentEnd(source, at);
                    foreach (Match m in Literal.Matches(source.Substring(at, end - at)))
                        if (Regex.IsMatch(m.Groups[1].Value, "[A-Za-z]"))
                            keys.Add(Regex.Unescape(m.Groups[1].Value));
                    at = end;
                }
            }
            foreach (var file in Directory.GetFiles(Path.Combine(Project, "UI"), "*.uxml", SearchOption.AllDirectories).Where(f => !f.Contains("ChalkerLab")))
            {
                var source = File.ReadAllText(file);
                foreach (var attribute in new[] { "text", "tooltip", "label" })
                    foreach (Match m in Regex.Matches(source, $@"\b{attribute}=""([^""]*)"""))
                        if (Regex.IsMatch(m.Groups[1].Value, "[A-Za-z]"))
                            keys.Add(WebUtility.HtmlDecode(m.Groups[1].Value));
            }
            return keys;
        }

        private static int FirstArgumentEnd(string source, int at)
        {
            var depth = 0;
            for (var i = at; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '"')
                {
                    i++;
                    while (i < source.Length && source[i] != '"')
                        i += source[i] == '\\' ? 2 : 1;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{')
                    depth++;
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0)
                        return i;
                    depth--;
                }
                else if (c == ',' && depth == 0)
                    return i;
            }
            return source.Length;
        }

        [Fact]
        public void EveryInterfaceText_HasItsSpanish_AndSpanishKeepsNoStaleOnes()
        {
            var keys = Keys();
            Assert.True(keys.Count > 400, $"only {keys.Count} interface texts found: the scan is broken");
            var spanish = Strings.BuiltInTexts("es")!;
            var translated = spanish.Keys.Where(k => k.StartsWith(Ui, StringComparison.Ordinal)).Select(k => k.Substring(Ui.Length)).ToHashSet();
            var missing = keys.Where(k => !translated.Contains(k)).OrderBy(k => k).ToList();
            var stale = translated.Where(k => !keys.Contains(k)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, "es.json lacks: " + string.Join(" | ", missing));
            Assert.True(stale.Count == 0, "es.json translates texts nothing uses: " + string.Join(" | ", stale));
            var placeholders = translated.Where(k => !new HashSet<string>(Strings.Placeholders(k)).SetEquals(Strings.Placeholders(spanish[Ui + k]))).ToList();
            Assert.True(placeholders.Count == 0, "other placeholders in: " + string.Join(" | ", placeholders));
        }

        private const string Ui = "ui:";

        /// <summary>No bare English is written to what players read in the HUD: texts, tooltips, labels, status lines.</summary>
        [Fact]
        public void TheHud_WritesNoBareEnglish()
        {
            var assigned = new Regex(@"(\btext\s*=|\btooltip\s*=|new (Label|Button)\(|ShowStatus\(|Section\(|Group\(|Note\()\s*\$?""([^""]*)""", RegexOptions.Compiled);
            var found = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(Project, "Scripts", "View", "Hud"), "*.cs"))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    // Words outside interpolation holes ({text} is code, not English).
                    if (assigned.Match(lines[i]) is { Success: true } m && Regex.IsMatch(Regex.Replace(m.Groups[3].Value, @"\{[^{}]*\}", ""), "[A-Za-z]{2,}")
                        && !lines[i].TrimStart().StartsWith("//"))
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
            Assert.True(found.Count == 0, "bare English in the HUD (wrap it in Ui.T):\n" + string.Join("\n", found));
        }
    }
}
