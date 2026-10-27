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

    /// <summary>
    /// Sets a variable: exactly one of <see cref="Value"/>, <see cref="Delta"/> (a number), or for a list
    /// <see cref="Add"/> / <see cref="Remove"/> (an item, e.g. a seat picked for a team; <c>"value": []</c> empties it).
    /// </summary>
    public sealed class SetVariableEffect : Effect
    {
        public string Name { get; set; } = "";
        public JToken? Value { get; set; }
        public double? Delta { get; set; }
        public JToken? Add { get; set; }
        public JToken? Remove { get; set; }
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

    /// <summary>
    /// Adds a line to the game log. <c>{$ref}</c> in the text is replaced by the reference's value. With <see cref="To"/>
    /// (a seat, <c>team:&lt;id&gt;</c> or <c>role:&lt;id&gt;</c>) only those players read it, and the shared log does not.
    /// </summary>
    public sealed class MessageEffect : Effect
    {
        public string Text { get; set; } = "";
        public string? To { get; set; }
    }

    public sealed class EndTurnEffect : Effect { }

    public sealed class EndPhaseEffect : Effect { }

    /// <summary>Jumps to a phase of the current turn (re-entering it runs its onEnter again).</summary>
    public sealed class GotoPhaseEffect : Effect
    {
        public string Phase { get; set; } = "";
    }

    /// <summary>Ends the game: <see cref="Player"/> wins, or with <see cref="Team"/> every seat of that team (hidden roles).</summary>
    public sealed class WinEffect : Effect
    {
        public string Player { get; set; } = "";
        public string? Team { get; set; }
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

    /// <summary>
    /// Shows the secret choices made so far (of <see cref="Action"/> only, when set) and runs each one's effects, in turn
    /// order from the current seat, with <c>$actor</c> and <c>$param</c> bound to that seat's choice.
    /// </summary>
    public sealed class RevealEffect : Effect
    {
        public string? Action { get; set; }
    }

    /// <summary>
    /// Shows <see cref="Seat"/>'s hidden role to <see cref="To"/>, or only its team with <see cref="TeamOnly"/>: the seer
    /// looks at a card, the wolves learn who the new wolf is, a dead player's role is shown to all. <see cref="To"/> is a
    /// seat, <c>team:&lt;id&gt;</c>, <c>role:&lt;id&gt;</c>, or every seat when left out or <c>all</c>. <see cref="Set"/>
    /// shows a trait of an extra role set instead.
    /// </summary>
    public sealed class RevealRoleEffect : Effect
    {
        public string Seat { get; set; } = "";
        public string? To { get; set; }
        public bool TeamOnly { get; set; }
        public string? Set { get; set; }
    }

    /// <summary>
    /// Gives <see cref="Seat"/> another role (a conversion); its team becomes the new role's. <see cref="Tell"/> lets the
    /// player know. What others learnt of it before stays what they saw.
    /// </summary>
    public sealed class SetRoleEffect : Effect
    {
        public string Seat { get; set; } = "";
        /// <summary>A role id, or a reference that reads one.</summary>
        public string Role { get; set; } = "";
        public string? Set { get; set; }
        public bool Tell { get; set; }
    }

    /// <summary>Swaps two seats' roles (Werewolf's Troublemaker) without either passing through a shared variable.</summary>
    public sealed class SwapRolesEffect : Effect
    {
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public string? Set { get; set; }
        public bool Tell { get; set; }
    }

    /// <summary>
    /// Puts <see cref="Seat"/> out of the game (lynched, executed, eaten): it takes no more actions or votes, and turns
    /// and seat choices pass it by. <see cref="RevealRole"/> shows its role to everyone.
    /// </summary>
    public sealed class EliminateEffect : Effect
    {
        public string Seat { get; set; } = "";
        public bool RevealRole { get; set; }
    }

    /// <summary>Counts a vote now with the votes cast so far (a timer ran out, the host calls it).</summary>
    public sealed class CloseVoteEffect : Effect
    {
        public string Action { get; set; } = "";
    }

    /// <summary>Moves <see cref="Seat"/> to another team, keeping its role (an allegiance that changes).</summary>
    public sealed class SetTeamEffect : Effect
    {
        public string Seat { get; set; } = "";
        public string Team { get; set; } = "";
        public bool Tell { get; set; }
    }

    /// <summary>
    /// Calls the global Lua function <see cref="Call"/> with one table: <c>actor</c>, <c>params</c> (the action's
    /// parameters), <c>it</c> (inside forEach) and <c>args</c> (<see cref="Args"/>). The function changes the game through
    /// the Lua <c>Game</c> API and the table through the usual object API; returning <c>false, "reason"</c> refuses the
    /// action and rolls it back.
    /// </summary>
    public sealed class LuaEffect : Effect
    {
        public string Call { get; set; } = "";
        public JObject? Args { get; set; }
    }

    /// <summary>Refuses what is happening (a move onto a zone, an action) and undoes it; the player reads <see cref="Reason"/>.</summary>
    public sealed class RefuseEffect : Effect
    {
        public string Reason { get; set; } = "";
    }

    /// <summary>Shows a card big on every screen to read it out (a deck shows its top card); nothing moves.</summary>
    public sealed class ShowEffect : Effect
    {
        public string Object { get; set; } = "";
    }

    /// <summary>Starts a blind round (<see cref="On"/>) or reveals it, from the rules: everyone moves in secret, then all is shown.</summary>
    public sealed class BlindRoundEffect : Effect
    {
        public bool On { get; set; }
    }

    /// <summary>
    /// Changes one light: <c>room</c> (the lamp and the room: night falls without darkening the cards) or <c>table</c>
    /// (the felt and the pieces). <see cref="Scale"/> 1 is normal and 0 off; <see cref="Tint"/> is three numbers 0–2.
    /// Leaving both out puts the light back to normal.
    /// </summary>
    public sealed class LightEffect : Effect
    {
        /// <summary><c>room</c> or <c>table</c>.</summary>
        public string Part { get; set; } = "room";
        public float? Scale { get; set; }
        public float[]? Tint { get; set; }
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
            ["reveal"] = typeof(RevealEffect),
            ["revealRole"] = typeof(RevealRoleEffect),
            ["setRole"] = typeof(SetRoleEffect),
            ["swapRoles"] = typeof(SwapRolesEffect),
            ["setTeam"] = typeof(SetTeamEffect),
            ["closeVote"] = typeof(CloseVoteEffect),
            ["eliminate"] = typeof(EliminateEffect),
            ["lua"] = typeof(LuaEffect),
            ["refuse"] = typeof(RefuseEffect),
            ["blindRound"] = typeof(BlindRoundEffect),
            ["show"] = typeof(ShowEffect),
            ["light"] = typeof(LightEffect),
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

    /// <summary>A list (a variable holding one, e.g. a team being picked) contains <see cref="Value"/>.</summary>
    public sealed class InListCondition : Condition
    {
        public JToken? List { get; set; }
        public JToken? Value { get; set; }
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
            ["inList"] = typeof(InListCondition),
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
