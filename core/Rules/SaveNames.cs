using System.Linq;
using System.Text;

namespace Core.Rules
{
    /// <summary>
    /// Save names (G30): what a player typed becomes a file name that works on every system (no path characters, no
    /// Windows device names, at most <see cref="MaxLength"/> characters), and autosaves rotate through a few slots.
    /// </summary>
    public static class SaveNames
    {
        public const int MaxLength = 60;
        public const int AutosaveSlots = 3;
        public const string AutosavePrefix = "autosave-";

        private static readonly string[] Reserved =
        {
            "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

        /// <summary>A file name (without extension) for <paramref name="name"/>; null when nothing usable is left.</summary>
        public static string? ToFileName(string? name)
        {
            if (name == null)
                return null;
            var builder = new StringBuilder();
            foreach (var c in name.Trim())
            {
                if (builder.Length >= MaxLength)
                    break;
                var bad = char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0;
                builder.Append(bad ? '_' : c);
            }
            var file = builder.ToString().Trim().TrimEnd('.', ' ');
            if (file.Length == 0 || file.All(c => c == '_' || c == '.'))
                return null;
            if (Reserved.Contains(file.Split('.')[0].ToLowerInvariant()))
                file = "_" + file;
            return file;
        }

        /// <summary>The autosave slot after <paramref name="last"/> (1-based, wraps around).</summary>
        public static string Autosave(int slot) => AutosavePrefix + (((slot - 1) % AutosaveSlots + AutosaveSlots) % AutosaveSlots + 1);

        public static bool IsAutosave(string fileName) => fileName.StartsWith(AutosavePrefix);
    }
}
