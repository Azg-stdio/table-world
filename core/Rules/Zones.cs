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
        public Transform Transform { get; set; } = new Transform();
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

    public static class ZoneTypes
    {
        public static readonly IReadOnlyDictionary<string, Type> ByName = TableObjectTypes.Build(
            typeof(HandZone), typeof(HiddenZone), typeof(ScriptingZone), typeof(SnapPoint));
    }
}
