using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Core.Rules
{
    /// <summary>An area of the table with special meaning. Zones never collide; they detect objects inside their box.</summary>
    /// <summary>
    /// A zone that catches pieces (M12): a piece tagged <see cref="Tag"/> that a player in <see cref="For"/> lets go
    /// anywhere but their own hand lands here, as if dropped here, and its rules run. While they carry it, the spot is
    /// lit for them alone. A vote card goes to its player's spot; a quest card to the quest pile.
    /// </summary>
    public sealed class CatchDef
    {
        public string Tag { get; set; } = "";
        /// <summary>Whose pieces: a seat, a list of seats (<c>$var.team</c>), <c>team:</c>, <c>role:</c>; null or <c>all</c> is everyone.</summary>
        public string? For { get; set; }
    }

    public abstract class Zone
    {
        /// <summary>Pieces this zone catches (M12); null catches nothing.</summary>
        public CatchDef? Catch { get; set; }

        [JsonProperty(Order = -3)]
        public string Id { get; set; } = "";

        [JsonProperty(Order = -2)]
        public string Type => GetType().Name;

        public string? Name { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public TableTransform Transform { get; set; } = new TableTransform();
        public bool ShouldSerializeTransform() => !Transform.IsDefault;

        /// <summary>Box size in meters (width, height, depth).</summary>
        public Vec3 Size { get; set; } = new Vec3(0.1f, 0.1f, 0.1f);

        /// <summary>Object this zone is attached to and moves with (TTS <c>AttachedSnapPoints</c>), or null.</summary>
        public string? Parent { get; set; }

        /// <summary>The zone's own Lua script (TTS scripting triggers have them), run like an object's; <c>self</c> is the zone.</summary>
        public string? Script { get; set; }
        /// <summary>What the zone's script saved with <c>onSave</c>.</summary>
        public string? ScriptState { get; set; }
        /// <summary>UI its script drew at the zone (M5; TTS scripting triggers can carry XmlUI), in the zone's frame.</summary>
        public List<UiNode> ScriptUi { get; set; } = new List<UiNode>();
        public bool ShouldSerializeScriptUi() => ScriptUi.Count > 0;

        /// <summary>
        /// Rules effects run during a game when a piece is put in the zone (<c>$it</c> is the piece, <c>$actor</c> the seat
        /// that moved it): move your pawn to the Shop and the Shop happens. A <c>refuse</c> effect undoes the move. In a
        /// blind round they wait for the reveal.
        /// </summary>
        public List<Effect> OnEnter { get; set; } = new List<Effect>();
        public bool ShouldSerializeOnEnter() => OnEnter.Count > 0;
        /// <summary>
        /// The zone shows the total of the dice lying in it, and each player's share (a die belongs to the seat named by
        /// its <c>owner:&lt;seatId&gt;</c> tag): an arena where everyone throws and the sums are read off the table. Rules
        /// read it as <c>$diceTotal.&lt;zone&gt;</c> and <c>$diceTotal.&lt;zone&gt;.&lt;seat&gt;</c>.
        /// </summary>
        public bool DiceTotal { get; set; }

        /// <summary>Rules effects run when a piece is taken out of the zone (as <see cref="OnEnter"/>).</summary>
        public List<Effect> OnLeave { get; set; } = new List<Effect>();
        public bool ShouldSerializeOnLeave() => OnLeave.Count > 0;
    }

    /// <summary>A seat's hand. Contents are hidden from every other seat.</summary>
    public sealed class HandZone : Zone
    {
        public string Owner { get; set; } = "";
    }

    /// <summary>
    /// Contents are hidden from everyone except <see cref="VisibleTo"/>: seat ids, the role <c>host</c>, or with hidden roles
    /// <c>role:&lt;id&gt;</c> and <c>team:&lt;id&gt;</c> (the defuser's bomb, the game master's screen).
    /// </summary>
    public sealed class HiddenZone : Zone
    {
        public List<string> VisibleTo { get; set; } = new List<string>();

        /// <summary>True when a viewer sitting at <paramref name="seat"/> (and hosting, if <paramref name="host"/>) sees inside.</summary>
        public bool Reveals(string? seat, bool host) =>
            (seat != null && VisibleTo.Contains(seat)) || (host && VisibleTo.Contains("host"));

        /// <summary>As <see cref="Reveals(string, bool)"/>, with <c>role:</c> and <c>team:</c> entries read from <paramref name="state"/>.</summary>
        public bool Reveals(string? seat, bool host, TableState state) =>
            Reveals(seat, host) || state.Names(VisibleTo, seat);
    }

    /// <summary>Invisible trigger volume; fires enter/leave events for rules and scripts.</summary>
    public sealed class ScriptingZone : Zone
    {
    }

    /// <summary>A point dropped objects snap to. <see cref="Zone.Size"/> is ignored.</summary>
    public sealed class SnapPoint : Zone
    {
        public bool RotationSnap { get; set; }

        public bool ShouldSerializeSize() => false;
    }

    public enum LayoutDirection { RightThenDown, DownThenRight, LeftThenDown, DownThenLeft }
    public enum LayoutFacing { Keep, FaceUp, FaceDown }

    /// <summary>
    /// Arranges what is in it in a grid (G18, TTS <c>LayoutZone</c>): from the zone's far-left corner as seen from its
    /// front, in reading order. "Lay out" arranges on demand; unless <see cref="ManualOnly"/>, a drop into it does too.
    /// Locked objects stay where they are.
    /// </summary>
    public sealed class LayoutZone : Zone
    {
        public LayoutDirection Direction { get; set; }
        /// <summary>Objects per row (or column); 0 fits as many as the zone is wide.</summary>
        public int MaxPerRow { get; set; }
        /// <summary>Gap between objects, meters.</summary>
        // [DefaultValue]: table JSON skips defaults, and a spacing of 0 must survive a save.
        [System.ComponentModel.DefaultValue(0.01f)]
        public float Spacing { get; set; } = 0.01f;
        public LayoutFacing Facing { get; set; }
        public bool ManualOnly { get; set; }
    }

    /// <summary>"Randomize" shuffles where the objects in it lie, and shuffles the decks and bags in it (G18, TTS randomize zone).</summary>
    public sealed class RandomizeZone : Zone
    {
    }

    public static class ZoneTypes
    {
        public static readonly IReadOnlyDictionary<string, Type> ByName = TableObjectTypes.Build(
            typeof(HandZone), typeof(HiddenZone), typeof(ScriptingZone), typeof(SnapPoint), typeof(LayoutZone), typeof(RandomizeZone));
    }
}
