using System.Collections.Generic;
using System.Linq;

namespace Core.Rules
{
    /// <summary>
    /// <c>$root.segment.segment</c> references used in rule fields. Roots:
    /// <list type="bullet">
    /// <item><c>$actor</c> (seat performing the action), <c>$current</c> (seat whose turn it is), <c>$player</c> (seat being
    /// checked by a win condition), <c>$other</c> (seat after <c>$player</c>, or after the actor), <c>$it</c> (forEach item),
    /// <c>$seat.&lt;id&gt;</c>: seats. A seat's <c>.hand</c> is its HandZone; <c>.&lt;tag&gt;</c> is the object or zone tagged
    /// <c>&lt;tag&gt;:&lt;seatId&gt;</c>; <c>.id</c>, <c>.color</c>, <c>.res.&lt;resource&gt;</c>.</item>
    /// <item><c>$param.&lt;name&gt;</c>: an action parameter; <c>$var.&lt;name&gt;</c>: a variable; <c>$obj.&lt;id&gt;</c>: an object or zone by id.</item>
    /// <item>On objects, decks and zones: <c>.count</c>, <c>.top</c>, <c>.bottom</c>, <c>.meta.&lt;key&gt;</c>, <c>.name</c>, <c>.id</c>,
    /// <c>.faceUp</c>, <c>.value</c> (die).</item>
    /// </list>
    /// A field that names a thing accepts a reference or a plain id.
    /// </summary>
    public static class References
    {
        /// <summary><c>$viewer</c> is the seat looking at a UI panel (only meaningful in <c>ui</c> texts and values).</summary>
        public static readonly IReadOnlyList<string> Roots = new[] { "actor", "current", "player", "other", "it", "param", "var", "seat", "obj", "viewer" };

        public static bool IsReference(string? text) => text != null && text.StartsWith("$");

        /// <summary>The <c>{$ref}</c> parts of a template text.</summary>
        public static IEnumerable<string> InTemplate(string text)
        {
            var start = text.IndexOf("{$", System.StringComparison.Ordinal);
            while (start >= 0)
            {
                var end = text.IndexOf('}', start);
                if (end < 0)
                    yield break;
                yield return text.Substring(start + 1, end - start - 1);
                start = text.IndexOf("{$", end, System.StringComparison.Ordinal);
            }
        }

        /// <summary>Splits <c>$a.b.c</c> into <c>[a, b, c]</c>.</summary>
        public static string[] Parse(string reference) => reference.Substring(1).Split('.');

        /// <summary>Static check: known root, declared variable, existing seat/object. Returns null when fine.</summary>
        internal static ValidationError? Check(string reference, string path, TableState state, ICollection<string>? paramNames)
        {
            var parts = Parse(reference);
            if (parts.Length == 0 || parts.Any(p => p.Length == 0))
                return new ValidationError(path, ErrorCodes.InvalidReference, $"'{reference}' is not a valid reference.", "Use $root.part, e.g. $actor.hand.");
            var root = parts[0];
            if (!Roots.Contains(root))
                return new ValidationError(path, ErrorCodes.InvalidReference, $"Unknown reference root '${root}'.", Suggest.OneOf(root, Roots.Select(r => r), "one of"));
            var needsName = root == "param" || root == "var" || root == "seat" || root == "obj";
            if (needsName && parts.Length < 2)
                return new ValidationError(path, ErrorCodes.InvalidReference, $"'${root}' needs a name: ${root}.<name>.");
            switch (root)
            {
                case "var" when !state.Variables.ContainsKey(parts[1]):
                    return new ValidationError(path, ErrorCodes.UnknownVariable, $"Variable '{parts[1]}' is not declared.",
                        Suggest.OneOf(parts[1], state.Variables.Keys, "a declared variable") ?? "Declare it under 'variables'.");
                case "seat" when state.Players.Seats.All(s => s.Id != parts[1]):
                    return new ValidationError(path, ErrorCodes.UnknownSeat, $"Seat '{parts[1]}' does not exist.",
                        Suggest.OneOf(parts[1], state.Players.Seats.Select(s => s.Id), "a seat id"));
                case "obj" when state.Objects.All(o => o.Id != parts[1]) && state.Zones.All(z => z.Id != parts[1]):
                    return new ValidationError(path, ErrorCodes.UnknownObject, $"No object or zone '{parts[1]}'.",
                        Suggest.OneOf(parts[1], state.Objects.Select(o => o.Id).Concat(state.Zones.Select(z => z.Id)), "an id"));
                case "param" when paramNames != null && !paramNames.Contains(parts[1]):
                    return new ValidationError(path, ErrorCodes.InvalidReference, $"This action has no parameter '{parts[1]}'.",
                        paramNames.Count == 0 ? "Declare it under the action's 'params'." : Suggest.OneOf(parts[1], paramNames, "a parameter"));
            }
            return null;
        }
    }
}
