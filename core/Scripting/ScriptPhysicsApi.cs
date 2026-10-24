using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using MoonSharp.Interpreter;

namespace Core.Scripting
{
    /// <summary>A physics query a script asked for (TTS <c>Physics.cast</c>), in metres on our table.</summary>
    public sealed class PhysicsCastQuery
    {
        public enum Shape { Ray = 1, Sphere = 2, Box = 3 }

        public Vec3 Origin;
        public Vec3 Direction;
        public Shape Type = Shape.Ray;
        /// <summary>Sphere: diameter in x; box: full size.</summary>
        public Vec3 Size;
        /// <summary>Box orientation, degrees.</summary>
        public Vec3 Orientation;
        /// <summary>Metres; infinity when the script gave none.</summary>
        public float MaxDistance = float.PositiveInfinity;
    }

    /// <summary>One object a cast hit, nearest first.</summary>
    public sealed class PhysicsCastHit
    {
        public string ObjectId = "";
        public Vec3 Point;
        public Vec3 Normal;
        public float Distance;
    }

    /// <summary>TTS <c>Physics.cast</c> and collision events (M5). The physics is the game's: core only asks and hears.</summary>
    public sealed partial class ScriptHost
    {
        /// <summary>Runs a cast in the game's physics; null when there is none (headless), and casts then hit nothing.</summary>
        public Func<PhysicsCastQuery, IReadOnlyList<PhysicsCastHit>>? PhysicsCast { get; set; }

        /// <summary>Objects whose collisions reach the global <c>onObjectCollisionEnter</c> (TTS <c>registerCollisions</c>).</summary>
        private readonly HashSet<string> _registeredCollisions = new HashSet<string>();

        private DynValue PhysicsApi(Script script)
        {
            var t = new Table(script);
            t["cast"] = DynValue.NewCallback((_, args) =>
            {
                var i = args.Count > 0 && args[0].Type == DataType.Table && args[0].Table == t ? 1 : 0;
                var p = args.Count > i && args[i].Type == DataType.Table ? args[i].Table : new Table(script);
                var query = new PhysicsCastQuery
                {
                    Origin = FromTts(p.Get("origin"), true) ?? new Vec3(0f, 0f, 0f),
                    Direction = FromTts(p.Get("direction"), false) ?? new Vec3(0f, 0f, 0f),
                    Type = p.Get("type").Type == DataType.Number && (int)p.Get("type").Number is var type && type >= 1 && type <= 3
                        ? (PhysicsCastQuery.Shape)type : PhysicsCastQuery.Shape.Ray,
                    Size = FromTts(p.Get("size"), false) is { } size
                        ? new Vec3(size.X * TtsUnitsToMeters, size.Y * TtsUnitsToMeters, size.Z * TtsUnitsToMeters) : new Vec3(0f, 0f, 0f),
                    Orientation = FromTts(p.Get("orientation"), false) ?? new Vec3(0f, 0f, 0f),
                };
                if (p.Get("max_distance").Type == DataType.Number)
                    query.MaxDistance = Math.Max(0f, (float)p.Get("max_distance").Number * TtsUnitsToMeters);
                var hits = PhysicsCast?.Invoke(query) ?? Array.Empty<PhysicsCastHit>();
                var list = new Table(script);
                var n = 1;
                foreach (var hit in hits)
                {
                    var proxy = Proxy(script, hit.ObjectId);
                    if (proxy.IsNil())
                        continue;
                    var entry = new Table(script);
                    entry["hit_object"] = proxy;
                    entry["point"] = ToTts(script, hit.Point);
                    entry["normal"] = Vector(script, hit.Normal);
                    entry["distance"] = Math.Round(hit.Distance / TtsUnitsToMeters, 4);
                    list[n++] = entry;
                }
                return DynValue.NewTable(list);
            });
            // Gravity belongs to the game's physics settings; reading it gives TTS's default.
            t["getGravity"] = DynValue.NewCallback((_, a) => DynValue.NewTable(Vector(script, new Vec3(0f, -25f, 0f))));
            t["setGravity"] = DynValue.NewCallback((_, a) => DynValue.False);
            return DynValue.NewTable(t);
        }

        /// <summary>Does any script want to hear about <paramref name="objectId"/>'s collisions? (The game only reports those.)</summary>
        public bool ListensToCollisions(string objectId) =>
            _registeredCollisions.Contains(objectId) ||
            _objectScripts.TryGetValue(objectId, out var own) &&
            (own.Globals.Get("onCollisionEnter").Type == DataType.Function || own.Globals.Get("onCollisionExit").Type == DataType.Function);

        /// <summary>
        /// The game saw two objects start (<paramref name="enter"/>) or stop touching: the object's own
        /// <c>onCollisionEnter(info)</c> / <c>onCollisionExit(info)</c>, and for registered objects the global
        /// <c>onObjectCollisionEnter(object, info)</c>. <c>info</c> is <c>{ collision_object, contact_points, relative_velocity }</c>.
        /// </summary>
        public void OnCollision(string objectId, string? otherId, Vec3 contact, Vec3 relativeVelocity, bool enter)
        {
            if (Engine.Find(objectId) == null)
                return;
            DynValue Info(Script script)
            {
                var info = new Table(script);
                info["collision_object"] = otherId != null ? Proxy(script, otherId) : DynValue.Nil;
                var points = new Table(script);
                points[1] = ToTts(script, contact);
                info["contact_points"] = points;
                info["relative_velocity"] = Vector(script, new Vec3(relativeVelocity.X / TtsUnitsToMeters, relativeVelocity.Y / TtsUnitsToMeters, relativeVelocity.Z / TtsUnitsToMeters));
                return DynValue.NewTable(info);
            }
            if (_objectScripts.TryGetValue(objectId, out var own))
                CallIfDefined(own, enter ? "onCollisionEnter" : "onCollisionExit", Info(own));
            if (_registeredCollisions.Contains(objectId))
                EverywhereWith(enter ? "onObjectCollisionEnter" : "onObjectCollisionExit", (script, proxy) => new[] { proxy(objectId), Info(script) });
        }

        private void InstallCollisionMethods(Table t, string id)
        {
            t["registerCollisions"] = DynValue.NewCallback((_, a) =>
            {
                _registeredCollisions.Add(id);
                return DynValue.True;
            });
            t["unregisterCollisions"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_registeredCollisions.Remove(id)));
        }
    }
}
