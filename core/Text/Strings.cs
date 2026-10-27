using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Text
{
    /// <summary>
    /// A string table (M9, ADR 0011): key → text in one language, with <c>{name}</c> placeholders filled in at use. A key
    /// this language lacks reads from English, then shows as the key itself, so a missing text is readable while testing
    /// and <see cref="Check"/> lists it. Table World's own tables (<c>core/Text/strings/&lt;language&gt;.json</c>) are built
    /// into Core.dll; a folder of tables (<see cref="LoadFolder"/>) adds languages or overrides texts without a rebuild.
    /// </summary>
    public sealed class Strings
    {
        /// <summary>The language every table falls back to, and the one every other table is checked against.</summary>
        public const string Fallback = "en";

        private const string ResourcePrefix = "Core.Text.strings.";

        private readonly IReadOnlyDictionary<string, string> _texts;
        private readonly Strings? _fallback;

        /// <summary>The language code, e.g. <c>en</c>, <c>es</c>.</summary>
        public string Language { get; }

        public IReadOnlyDictionary<string, string> Texts => _texts;

        public Strings(string language, IReadOnlyDictionary<string, string> texts, Strings? fallback = null)
        {
            Language = language;
            _texts = texts;
            _fallback = fallback;
        }

        /// <summary>The text for <paramref name="key"/> with its placeholders filled; English, then the key, when missing.</summary>
        public string Get(string key, params (string Name, object? Value)[] fills) => Fill(Raw(key) ?? key, fills);

        /// <summary>The unfilled text for <paramref name="key"/>, from this table or its fallback; null when neither has it.</summary>
        public string? Raw(string key) => _texts.TryGetValue(key, out var text) ? text : _fallback?.Raw(key);

        public bool Has(string key) => _texts.ContainsKey(key);

        /// <summary>
        /// Replaces each <c>{name}</c> in <paramref name="text"/> with its value; a placeholder without a value stays as
        /// written, and <c>{{</c> / <c>}}</c> write a literal brace.
        /// </summary>
        public static string Fill(string text, params (string Name, object? Value)[] fills)
        {
            if (text.IndexOf('{') < 0 && text.IndexOf('}') < 0)
                return text;
            var result = new StringBuilder(text.Length + 16);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
                {
                    result.Append(c);
                    i++;
                    continue;
                }
                var end = c == '{' ? text.IndexOf('}', i + 1) : -1;
                if (end < 0)
                {
                    result.Append(c);
                    continue;
                }
                var name = text.Substring(i + 1, end - i - 1);
                var found = false;
                foreach (var fill in fills)
                    if (fill.Name == name)
                    {
                        result.Append(Convert.ToString(fill.Value, System.Globalization.CultureInfo.InvariantCulture));
                        found = true;
                        break;
                    }
                if (!found)
                    result.Append(text, i, end - i + 1);
                i = end;
            }
            return result.ToString();
        }

        /// <summary>The placeholder names in <paramref name="text"/> (<c>{seat}</c> → <c>seat</c>), escaped braces skipped.</summary>
        public static IReadOnlyList<string> Placeholders(string text)
        {
            var names = new List<string>();
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '{')
                    continue;
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i++;
                    continue;
                }
                var end = text.IndexOf('}', i + 1);
                if (end < 0)
                    break;
                names.Add(text.Substring(i + 1, end - i - 1));
                i = end;
            }
            return names;
        }

        /// <summary>Reads a table: one JSON object of key → text. Anything else (a nested object, a number) is refused.</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            var token = JToken.Parse(json);
            if (!(token is JObject table))
                throw new FormatException("A string table is one JSON object of key → text.");
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in table.Properties())
            {
                if (property.Value.Type != JTokenType.String)
                    throw new FormatException($"'{property.Name}' is not a text: every value in a string table is a string.");
                texts[property.Name] = (string)property.Value!;
            }
            return texts;
        }

        /// <summary>The languages built into Core.dll.</summary>
        public static IReadOnlyList<string> BuiltIn() => typeof(Strings).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n.Substring(ResourcePrefix.Length, n.Length - ResourcePrefix.Length - ".json".Length))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        /// <summary>A built-in table's texts; null for a language not built in.</summary>
        public static Dictionary<string, string>? BuiltInTexts(string language)
        {
            using var stream = typeof(Strings).Assembly.GetManifestResourceStream(ResourcePrefix + language + ".json");
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>
        /// The table for <paramref name="language"/>: the built-in one with <paramref name="extra"/>'s texts on top (a
        /// folder's, <see cref="LoadFolder"/>), falling back to English the same way. An unknown language reads English.
        /// </summary>
        public static Strings For(string language, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? extra = null)
        {
            Dictionary<string, string> Merged(string code)
            {
                var texts = BuiltInTexts(code) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                if (extra != null && extra.TryGetValue(code, out var more))
                    foreach (var (key, text) in more)
                        texts[key] = text;
                return texts;
            }
            var english = new Strings(Fallback, Merged(Fallback));
            return language == Fallback ? english : new Strings(language, Merged(language), english);
        }

        /// <summary>
        /// Every <c>&lt;language&gt;.json</c> in <paramref name="folder"/> (a player's own translations); a file that does not
        /// parse is skipped and named in <paramref name="problems"/>.
        /// </summary>
        public static Dictionary<string, IReadOnlyDictionary<string, string>> LoadFolder(string folder, List<string>? problems = null)
        {
            var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (!Directory.Exists(folder))
                return tables;
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    tables[Path.GetFileNameWithoutExtension(file)] = Parse(File.ReadAllText(file, Encoding.UTF8));
                }
                catch (Exception e) when (e is FormatException || e is JsonException || e is IOException)
                {
                    problems?.Add($"{Path.GetFileName(file)}: {e.Message}");
                }
            }
            return tables;
        }

        /// <summary>
        /// What <paramref name="other"/> gets wrong against <paramref name="reference"/> (English): keys it lacks, keys
        /// English does not have, and texts whose placeholders differ (a translation must fill the same names).
        /// </summary>
        public static (IReadOnlyList<string> Missing, IReadOnlyList<string> Extra, IReadOnlyList<string> Placeholders) Check(
            IReadOnlyDictionary<string, string> reference, IReadOnlyDictionary<string, string> other)
        {
            var missing = reference.Keys.Where(k => !other.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = other.Keys.Where(k => !reference.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var placeholders = reference.Keys.Where(other.ContainsKey)
                .Where(k => !new HashSet<string>(Placeholders(reference[k])).SetEquals(Placeholders(other[k])))
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            return (missing, extra, placeholders);
        }
    }
}
