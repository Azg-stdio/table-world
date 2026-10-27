using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Text;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Table World's own interface in the player's language (ADR 0011, T10.2). Every text is written in English in the
    /// code and looked up by its English words: <c>Ui.T("Start game ({count} players)", ("count", n))</c> reads
    /// <c>ui:Start game ({count} players)</c> in the language's table (<c>core/Text/strings/&lt;language&gt;.json</c>, or a
    /// player's own in <c>&lt;persistentDataPath&gt;/Strings</c>), else the English. The language is the player's setting,
    /// else the system's when Table World speaks it, else English.
    /// </summary>
    public static class Ui
    {
        public const string Prefix = "ui:";

        private static Strings _strings;
        private static string _language;

        /// <summary>A language every lookup uses whatever the setting says (tests pin English).</summary>
        public static string Override { get; set; }

        /// <summary>Raised after the language changed: what the interface shows is drawn again.</summary>
        public static event Action Changed;

        public static string Language => _language ?? Strings.Fallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _strings = null;
            _language = null;
            Changed = null;
        }

        /// <summary>The player's own tables (a translation they made or fixed), on top of the built-in ones.</summary>
        public static string PlayerFolder => Path.Combine(Application.persistentDataPath, "Strings");

        /// <summary>Every language the interface can be read in: the built-in ones and any in the player's folder.</summary>
        public static IReadOnlyList<string> Languages =>
            Strings.BuiltIn().Concat(Strings.LoadFolder(PlayerFolder).Keys).Distinct().OrderBy(l => l == Strings.Fallback ? "" : l).ToList();

        /// <summary>A language as its own speakers name it ("Español").</summary>
        public static string NameOf(string language) =>
            Strings.For(language, Strings.LoadFolder(PlayerFolder)).Raw("language.name") is { } name && (language == Strings.Fallback || name != "English") ? name : language;

        /// <summary>The system's language when Table World speaks it, else English.</summary>
        public static string SystemLanguage()
        {
            var code = Application.systemLanguage switch
            {
                UnityEngine.SystemLanguage.Spanish => "es",
                UnityEngine.SystemLanguage.French => "fr",
                UnityEngine.SystemLanguage.German => "de",
                UnityEngine.SystemLanguage.Portuguese => "pt",
                UnityEngine.SystemLanguage.Italian => "it",
                _ => Strings.Fallback,
            };
            return Languages.Contains(code) ? code : Strings.Fallback;
        }

        /// <summary>Uses <paramref name="setting"/> (a language code, or empty for the system's); raises <see cref="Changed"/> when it differs.</summary>
        public static void SetLanguage(string setting)
        {
            var code = Override ?? (string.IsNullOrEmpty(setting) || !Languages.Contains(setting) ? SystemLanguage() : setting);
            if (code == _language && _strings != null)
                return;
            _language = code;
            _strings = Strings.For(code, Strings.LoadFolder(PlayerFolder));
            Changed?.Invoke();
        }

        /// <summary>
        /// <paramref name="english"/> in the interface's language with its <c>{name}</c> placeholders filled; the English
        /// when the language has no text for it.
        /// </summary>
        public static string T(string english, params (string Name, object Value)[] fills)
        {
            if (_strings == null)
                SetLanguage("");
            var text = english;
            if (_language != Strings.Fallback && _strings.Raw(Prefix + english) is { Length: > 0 } translated)
                text = translated;
            return fills.Length == 0 && text.IndexOf('{') < 0 ? text : Strings.Fill(text, fills);
        }
    }
}
