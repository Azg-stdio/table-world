using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Rules
{
    /// <summary>
    /// One problem in a document, written for an LLM reader: a JSON path, a stable code, one sentence, and a
    /// suggestion when one is computable (docs/rules-schema.md, "Validation errors").
    /// </summary>
    public sealed class ValidationError
    {
        public string Path { get; }
        public string Code { get; }
        public string Message { get; }
        public string? Suggestion { get; }

        public ValidationError(string path, string code, string message, string? suggestion = null)
        {
            Path = path;
            Code = code;
            Message = message;
            Suggestion = suggestion;
        }

        public override string ToString() =>
            Suggestion == null ? $"{Path}: [{Code}] {Message}" : $"{Path}: [{Code}] {Message} {Suggestion}";
    }

    /// <summary>Stable error codes. Never rename one: agents and tests match on them.</summary>
    public static class ErrorCodes
    {
        public const string InvalidJson = "invalid_json";
        public const string InvalidValue = "invalid_value";
        public const string UnknownKey = "unknown_key";
        public const string MissingField = "missing_field";
        public const string UnknownType = "unknown_type";
        public const string UnsupportedSchemaVersion = "unsupported_schema_version";
        public const string InvalidId = "invalid_id";
        public const string DuplicateId = "duplicate_id";
        public const string UnknownContainer = "unknown_container";
        public const string InvalidContainer = "invalid_container";
        public const string ContainerCycle = "container_cycle";
        public const string UnknownAsset = "unknown_asset";
        public const string WrongAssetType = "wrong_asset_type";
        public const string IndexOutOfRange = "index_out_of_range";
        public const string InvalidDieFaces = "invalid_die_faces";
        public const string UnknownSeat = "unknown_seat";
        public const string UnknownZone = "unknown_zone";
        public const string UnknownObject = "unknown_object";
        public const string InvalidPath = "invalid_path";

        // Rules (T4.1).
        public const string UnknownEffect = "unknown_effect";
        public const string UnknownCondition = "unknown_condition";
        public const string UnknownPhase = "unknown_phase";
        public const string UnknownAction = "unknown_action";
        public const string UnknownVariable = "unknown_variable";
        public const string UnknownResource = "unknown_resource";
        public const string UnknownEvent = "unknown_event";
        public const string InvalidReference = "invalid_reference";
        public const string InvalidOperator = "invalid_operator";
        public const string PlayerCount = "player_count";

        // Game engine (T4.2).
        public const string NoRules = "no_rules";
        public const string GameAlreadyStarted = "game_already_started";
        public const string GameNotStarted = "game_not_started";
        public const string GameOver = "game_over";
        public const string ActionNotAvailable = "action_not_available";
        public const string NotYourTurn = "not_your_turn";
        public const string LimitReached = "limit_reached";
        public const string PreconditionFailed = "precondition_failed";
        public const string MissingParam = "missing_param";
        public const string InvalidParam = "invalid_param";
        public const string RulesLoop = "rules_loop";
        public const string RuleFailed = "rule_failed";

        // Engine (command) errors.
        public const string UnknownCommand = "unknown_command";
        public const string ObjectLocked = "object_locked";
        public const string NotOnTable = "not_on_table";
        public const string WrongObjectType = "wrong_object_type";
        public const string ContainerEmpty = "container_empty";
        public const string InvalidTarget = "invalid_target";
        public const string NoHandZone = "no_hand_zone";
    }

    internal static class Suggest
    {
        /// <summary>"Did you mean 'x'?" for the closest candidate, otherwise the list of valid values.</summary>
        public static string? OneOf(string actual, IEnumerable<string> candidates, string noun = "one of")
        {
            var list = candidates.ToList();
            if (list.Count == 0)
                return null;

            var best = list
                .Select(c => (Candidate: c, Distance: Distance(actual.ToLowerInvariant(), c.ToLowerInvariant())))
                .OrderBy(x => x.Distance)
                .First();
            var threshold = actual.Length <= 3 ? 1 : Math.Max(2, actual.Length / 3);
            if (best.Distance <= threshold)
                return $"Did you mean '{best.Candidate}'?";
            var prefixed = list.Where(c => c.StartsWith(actual, StringComparison.OrdinalIgnoreCase)).ToList();
            if (actual.Length >= 3 && prefixed.Count == 1)
                return $"Did you mean '{prefixed[0]}'?";

            const int max = 12;
            var shown = string.Join(", ", list.Take(max));
            return list.Count > max ? $"Use {noun}: {shown}, ... ({list.Count} total)." : $"Use {noun}: {shown}.";
        }

        /// <summary>Optimal string alignment distance: Levenshtein plus adjacent transpositions ("nmae" → "name" is 1).</summary>
        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++)
                d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++)
                d[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
