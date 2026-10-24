using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Core.Rules
{
    /// <summary>An area of the table with special meaning. Zones never collide; they detect objects inside their box.</summary>
    public abstract class Zone
    {
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
    }

    /// <summary>A seat's hand. Contents are hidden from every other seat.</summary>
    public sealed class HandZone : Zone
    {
        public string Owner { get; set; } = "";
    }

    /// <summary>Contents are hidden from everyone except <see cref="VisibleTo"/> (seat ids, or the role <c>host</c>).</summary>
    public sealed class HiddenZone : Zone
    {
        public List<string> VisibleTo { get; set; } = new List<string>();

        /// <summary>True when a viewer sitting at <paramref name="seat"/> (and hosting, if <paramref name="host"/>) sees inside.</summary>
        public bool Reveals(string? seat, bool host) =>
            (seat != null && VisibleTo.Contains(seat)) || (host && VisibleTo.Contains("host"));
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
