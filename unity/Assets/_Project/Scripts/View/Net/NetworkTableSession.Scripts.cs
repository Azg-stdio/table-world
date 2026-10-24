using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Scripting;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Table scripts (M5, ADR 0003): the host (or a player alone) runs the table's Lua; clients never do. Scripts load
    /// whenever a table is loaded, rewound or replaced, hear every command the host orders with the acting seat's colour,
    /// and their timers advance every frame. What they broadcast goes to everyone's chat; their errors to the log.
    /// </summary>
    public sealed partial class NetworkTableSession
    {
        private ScriptHost _scripts;

        /// <summary>The script host of this machine; null on a client.</summary>
        public ScriptHost Scripts => IsClient ? null : _scripts;

        private void StartScripts()
        {
            _scripts = new ScriptHost(() => _table != null ? _table.Engine : null, command => _table.Execute(command));
            _scripts.Broadcast += text => Announce(text);
            _scripts.Log += text => Debug.Log("[Lua] " + text);
            _scripts.PhysicsCast = Cast;
            _table.Collided += OnCollided;
        }

        /// <summary>A collision a script listens to (its object's onCollisionEnter, or registerCollisions).</summary>
        private void OnCollided(ObjectView view, Collision collision, bool enter)
        {
            if (_scripts == null || IsClient || _scripts.ScriptCount == 0 || !_scripts.ListensToCollisions(view.Id))
                return;
            var other = collision.collider != null ? collision.collider.GetComponentInParent<ObjectView>() : null;
            var contact = collision.contactCount > 0 ? collision.GetContact(0).point : view.transform.position;
            _scripts.OnCollision(view.Id, other != null ? other.Id : null, contact.ToCore(), collision.relativeVelocity.ToCore(), enter);
        }

        /// <summary>TTS Physics.cast with PhysX: rays, spheres and boxes; no distance (or no direction) checks what overlaps.</summary>
        private IReadOnlyList<PhysicsCastHit> Cast(PhysicsCastQuery query)
        {
            var origin = query.Origin.ToUnity();
            var direction = query.Direction.ToUnity();
            var distance = float.IsInfinity(query.MaxDistance) ? 1000f : query.MaxDistance;
            var orientation = Quaternion.Euler(query.Orientation.ToUnity());
            var size = query.Size.ToUnity();
            var hits = new List<PhysicsCastHit>();
            var seen = new HashSet<string>();
            void Add(Collider collider, Vector3 point, Vector3 normal, float at)
            {
                var view = collider != null ? collider.GetComponentInParent<ObjectView>() : null;
                if (view != null && seen.Add(view.Id))
                    hits.Add(new PhysicsCastHit { ObjectId = view.Id, Point = point.ToCore(), Normal = normal.ToCore(), Distance = at });
            }
            const QueryTriggerInteraction solids = QueryTriggerInteraction.Ignore;
            if (query.Type != PhysicsCastQuery.Shape.Ray && (distance <= 0f || direction.sqrMagnitude < 1e-8f))
            {
                var overlaps = query.Type == PhysicsCastQuery.Shape.Sphere
                    ? Physics.OverlapSphere(origin, size.x / 2f, Physics.DefaultRaycastLayers, solids)
                    : Physics.OverlapBox(origin, size / 2f, orientation, Physics.DefaultRaycastLayers, solids);
                foreach (var c in overlaps.OrderBy(c => (c.ClosestPoint(origin) - origin).sqrMagnitude))
                    Add(c, c.ClosestPoint(origin), Vector3.up, Vector3.Distance(c.ClosestPoint(origin), origin));
                return hits;
            }
            if (direction.sqrMagnitude < 1e-8f)
                return hits;
            direction.Normalize();
            var found = query.Type switch
            {
                PhysicsCastQuery.Shape.Sphere => Physics.SphereCastAll(origin, size.x / 2f, direction, distance, Physics.DefaultRaycastLayers, solids),
                PhysicsCastQuery.Shape.Box => Physics.BoxCastAll(origin, size / 2f, direction, orientation, distance, Physics.DefaultRaycastLayers, solids),
                _ => Physics.RaycastAll(origin, direction, distance, Physics.DefaultRaycastLayers, solids),
            };
            foreach (var hit in found.OrderBy(h => h.distance))
                Add(hit.collider, hit.distance == 0f ? origin : hit.point, hit.normal, hit.distance);
            return hits;
        }

        /// <summary>A table arrived (loaded, rewound, received): the host runs its scripts from the start.</summary>
        private void LoadScripts()
        {
            if (_table == null || _table.Engine == null || IsClient)
                return;
            if (_scripts == null)
                StartScripts();
            _scripts.Load(Time.unscaledTimeAsDouble);
        }

        /// <summary>Before a save: the scripts' onSave state goes into the table (TTS script_state).</summary>
        private void SaveScripts()
        {
            if (_scripts != null && !IsClient && _scripts.ScriptCount > 0)
                _scripts.Save();
        }

        private void TickScripts()
        {
            if (_scripts != null && !IsClient && _scripts.ScriptCount > 0)
                _scripts.Tick(Time.unscaledTimeAsDouble);
        }

        /// <summary>Tells the scripts about a command, with the colour of the seat that did it.</summary>
        private void ScriptsHear(Command command, CommandResult result, string seat)
        {
            // Even with no script running: a scripted object coming out of a bag starts its own.
            if (_scripts == null || IsClient)
                return;
            var color = seat == null ? null : _table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color;
            _scripts.OnCommand(command, result, color);
        }
    }
}
