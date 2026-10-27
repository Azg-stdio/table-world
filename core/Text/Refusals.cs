using System.Linq;
using Core.Rules;

namespace Core.Text
{
    /// <summary>
    /// What a player reads when a move is refused (ADR 0011): the refusal's own text key (<see cref="ValidationError.TextKey"/>)
    /// when the engine gave one, else <c>refusal.&lt;code&gt;</c> when the table has it, else the English message (what
    /// agents read). A game's own reason (a <c>refuse</c> effect's text) has no key, so it reads as the game wrote it.
    /// </summary>
    public static class Refusals
    {
        public static string Text(Strings strings, ValidationError error)
        {
            if (error.TextKey != null && strings.Raw(error.TextKey) != null)
                return strings.Get(error.TextKey, error.TextFills.Select(f => (f.Name, (object?)f.Value)).ToArray());
            // A refuse effect with the game's own reason keeps it.
            if (error.Code == ErrorCodes.Refused)
                return error.Message;
            return strings.Raw("refusal." + error.Code) ?? error.Message;
        }
    }
}
