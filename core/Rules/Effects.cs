using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    /// <summary>
    /// A rule effect, written as a single-key object: <c>{ "move": { ... } }</c>. The key is <see cref="Kind"/>.
    /// Fields that name things take an id or a <c>$</c> reference (docs/rules-schema.md, "References").
    /// </summary>
    public abstract class Effect
    {
        [JsonIgnore]
        public string Kind => EffectKinds.NameOf(GetType());
    }

    /// <summary>Moves cards or objects. Source: <see cref="Object"/>, or <see cref="Take"/> from <see cref="From"/>.</summary>
    public sealed class MoveEffect : Effect
    {
        public string? Object { get; set; }
        public string? From { get; set; }
        /// <summary><c>top</c>, <c>bottom</c>, <c>all</c> or <c>allButTop</c>.</summary>
        public string Take { get; set; } = "top";
        public int Count { get; set; } = 1;
        public string To { get; set; } = "";
        /// <summary>Into a deck: <c>top</c> or <c>bottom</c>.</summary>
        public string At { get; set; } = "top";
        public bool? FaceUp { get; set; }
    }

    /// <summary>Deals <see cref="Count"/> cards to each seat, one at a time, into <see cref="ToZone"/> or the object tagged <see cref="ToTag"/>.</summary>
    public sealed class DealEffect : Effect
    {
        public string From { get; set; } = "";
        public int Count { get; set; } = 1;
        /// <summary>Seat references; empty means every seat in order.</summary>
        public List<string> Seats { get; set; } = new List<string>();
        /// <summary>Destination per seat, with <c>$it</c> bound to the seat (e.g. <c>$it.hand</c>).</summary>
        public string? ToZone { get; set; }
        /// <summary>Destination per seat: the deck tagged <c>&lt;tag&gt;:&lt;seat&gt;</c>.</summary>
        public string? ToTag { get; set; }
        /// <summary>Dealt cards land face up unless set (defaults stay false: writing omits default values).</summary>
        public bool FaceDown { get; set; }
    }

    public sealed class ShuffleEffect : Effect
    {
        public string Object { get; set; } = "";
    }

    public sealed class FlipEffect : Effect
    {
        public string Object { get; set; } = "";
    }

    public sealed class SetVariableEffect : Effect
    {
        public string Name { get; set; } = "";
        public JToken? Value { get; set; }
        public double? Delta { get; set; }
    }

    public sealed class SetResourceEffect : Effect
    {
        public string Player { get; set; } = "";
        public string Resource { get; set; } = "";
        public JToken? Value { get; set; }
        public double? Delta { get; set; }
    }

    public sealed class IfEffect : Effect
    {
        public Condition? Condition { get; set; }
        public List<Effect> Then { get; set; } = new List<Effect>();
        public List<Effect> Else { get; set; } = new List<Effect>();
    }

    /// <summary>Runs <see cref="Do"/> once per seat (<see cref="Players"/>) or per object in a zone or deck, binding <c>$it</c>.</summary>
    public sealed class ForEachEffect : Effect
    {
        public bool Players { get; set; }
        public string? ObjectsIn { get; set; }
        public List<Effect> Do { get; set; } = new List<Effect>();
    }

    /// <summary>Adds a line to the game log. <c>{$ref}</c> in the text is replaced by the reference's value.</summary>
    public sealed class MessageEffect : Effect
    {
        public string Text { get; set; } = "";
    }

    public sealed class EndTurnEffect : Effect { }

    public sealed class EndPhaseEffect : Effect { }

    /// <summary>Jumps to a phase of the current turn (re-entering it runs its onEnter again).</summary>
    public sealed class GotoPhaseEffect : Effect
    {
        public string Phase { get; set; } = "";
    }

    public sealed class WinEffect : Effect
    {
        public string Player { get; set; } = "";
    }

    public sealed class LockEffect : Effect
    {
        public string Object { get; set; } = "";
        /// <summary>Null or true locks; false unlocks.</summary>
        public bool? Locked { get; set; }
    }

    public sealed class RollEffect : Effect
    {
        public string Object { get; set; } = "";
        /// <summary>Optional variable that receives the rolled value.</summary>
        public string? Store { get; set; }
    }

    public static class EffectKinds
    {
        public static readonly IReadOnlyDictionary<string, Type> ByName = new Dictionary<string, Type>
        {
            ["move"] = typeof(MoveEffect),
            ["deal"] = typeof(DealEffect),
            ["shuffle"] = typeof(ShuffleEffect),
            ["flip"] = typeof(FlipEffect),
            ["setVariable"] = typeof(SetVariableEffect),
            ["setResource"] = typeof(SetResourceEffect),
            ["if"] = typeof(IfEffect),
            ["forEach"] = typeof(ForEachEffect),
            ["message"] = typeof(MessageEffect),
            ["endTurn"] = typeof(EndTurnEffect),
            ["endPhase"] = typeof(EndPhaseEffect),
            ["gotoPhase"] = typeof(GotoPhaseEffect),
            ["win"] = typeof(WinEffect),
            ["lock"] = typeof(LockEffect),
            ["roll"] = typeof(RollEffect),
        };

        private static readonly Dictionary<Type, string> Names = ByName.ToDictionary(p => p.Value, p => p.Key);
        public static string NameOf(Type type) => Names[type];
    }

    // ---- conditions ----------------------------------------------------------------------------------------------

    /// <summary>A rule condition, written as a single-key object: <c>{ "zoneCount": { ... } }</c>; <c>and</c>/<c>or</c> take an array.</summary>
    public abstract class Condition
    {
        [JsonIgnore]
        public string Kind => ConditionKinds.NameOf(GetType());
    }

    /// <summary>Cards in a deck, or objects on the table inside a zone.</summary>
    public sealed class ZoneCountCondition : Condition
    {
        public string Zone { get; set; } = "";
        public string Op { get; set; } = "==";
        public JToken? Value { get; set; }
    }

    /// <summary>Compares two values; each is a literal or a <c>$</c> reference such as <c>$param.card.meta.rank</c>.</summary>
    public sealed class CompareCondition : Condition
    {
        public JToken? Left { get; set; }
        public string Op { get; set; } = "==";
        public JToken? Right { get; set; }
    }

    public sealed class VariableCondition : Condition
    {
        public string Name { get; set; } = "";
        public string Op { get; set; } = "==";
        public JToken? Value { get; set; }
    }

    public sealed class ResourceCondition : Condition
    {
        public string Player { get; set; } = "";
        public string Resource { get; set; } = "";
        public string Op { get; set; } = "==";
        public JToken? Value { get; set; }
    }

    public sealed class IsCurrentPlayerCondition : Condition
    {
        public string Player { get; set; } = "";
    }

    public sealed class ObjectInCondition : Condition
    {
        public string Object { get; set; } = "";
        public string Zone { get; set; } = "";
    }

    public sealed class HasTagCondition : Condition
    {
        public string Object { get; set; } = "";
        public string Tag { get; set; } = "";
    }

    public sealed class AndCondition : Condition
    {
        public List<Condition> Conditions { get; set; } = new List<Condition>();
    }

    public sealed class OrCondition : Condition
    {
        public List<Condition> Conditions { get; set; } = new List<Condition>();
    }

    public sealed class NotCondition : Condition
    {
        public Condition? Condition { get; set; }
    }

    public static class ConditionKinds
    {
        public static readonly IReadOnlyDictionary<string, Type> ByName = new Dictionary<string, Type>
        {
            ["zoneCount"] = typeof(ZoneCountCondition),
            ["compare"] = typeof(CompareCondition),
            ["variable"] = typeof(VariableCondition),
            ["resource"] = typeof(ResourceCondition),
            ["isCurrentPlayer"] = typeof(IsCurrentPlayerCondition),
            ["objectIn"] = typeof(ObjectInCondition),
            ["hasTag"] = typeof(HasTagCondition),
            ["and"] = typeof(AndCondition),
            ["or"] = typeof(OrCondition),
            ["not"] = typeof(NotCondition),
        };

        public static readonly IReadOnlyList<string> Operators = new[] { "==", "!=", "<", "<=", ">", ">=" };

        private static readonly Dictionary<Type, string> Names = ByName.ToDictionary(p => p.Value, p => p.Key);
        public static string NameOf(Type type) => Names[type];

        /// <summary>The JSON value of <c>and</c>/<c>or</c> is the list itself; of <c>not</c> the inner condition.</summary>
        internal static bool IsListForm(Type type) => type == typeof(AndCondition) || type == typeof(OrCondition);
    }
}
