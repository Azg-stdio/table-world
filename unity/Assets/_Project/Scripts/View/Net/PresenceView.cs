using System.Collections.Generic;
using System.Linq;
using Core.Net;
using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Draws the other players at the table (G1–G3): a pointer marker in each player's seat colour, and the objects they
    /// carry moving live with a frame in their colour under them. Carried objects are kinematic here and glide to the
    /// received pose; when the holder lets go, the object returns to physics (its final pose arrives as a move command).
    /// </summary>
    public sealed class PresenceView : MonoBehaviour
    {
        /// <summary>How fast a remote carried object catches up with its latest pose (≈0.1 s): smooth at 15 frames/s.</summary>
        public const float FollowRate = 18f;

        [SerializeField] private TableView _table;
        [SerializeField] private NetworkTableSession _session;

        private readonly Dictionary<string, GameObject> _pointers = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, (string Seat, Vector3 Position, Quaternion Rotation)> _carried = new Dictionary<string, (string, Vector3, Quaternion)>();
        private readonly Dictionary<string, GameObject> _frames = new Dictionary<string, GameObject>();
        private readonly List<(string Seat, Vector3 Position, Color Color)> _shownPointers = new List<(string, Vector3, Color)>();
        private Material _unlit;

        /// <summary>Another player picked something up (for its sound).</summary>
        public event System.Action<ObjectView> RemotePickedUp;

        /// <summary>Pointers drawn this frame, for the HUD's name labels.</summary>
        public IReadOnlyList<(string Seat, Vector3 Position, Color Color)> Pointers => _shownPointers;

        public void Configure(TableView table, NetworkTableSession session)
        {
            _table = table;
            _session = session;
        }

        private void LateUpdate()
        {
            if (_table == null || _session == null || _table.Engine == null)
                return;
            var others = _session.Others.ToList();

            // Pointers.
            _shownPointers.Clear();
            var seen = new HashSet<string>();
            foreach (var (_, frame) in others)
            {
                if (!(frame.Pointer is { } p))
                    continue;
                seen.Add(frame.Seat);
                var color = ColorOf(frame.Seat);
                var marker = PointerFor(frame.Seat, color);
                var target = new Vector3(p.X, p.Y, p.Z);
                // Glide between frames so a remote pointer moves smoothly instead of stepping 15 times a second.
                marker.transform.position = marker.activeSelf ? Vector3.Lerp(marker.transform.position, target, 1f - Mathf.Exp(-FollowRate * Time.deltaTime)) : target;
                marker.SetActive(true);
                _shownPointers.Add((frame.Seat, marker.transform.position, color));
            }
            foreach (var pair in _pointers)
                if (!seen.Contains(pair.Key))
                    pair.Value.SetActive(false);

            // Carried objects.
            var carriedNow = new Dictionary<string, (string, Vector3, Quaternion)>();
            foreach (var (_, frame) in others)
                foreach (var held in frame.Held)
                    carriedNow[held.Id] = (frame.Seat, new Vector3(held.Position.X, held.Position.Y, held.Position.Z), Quaternion.Euler(held.Rotation.X, held.Rotation.Y, held.Rotation.Z));
            foreach (var id in _carried.Keys.Where(id => !carriedNow.ContainsKey(id)).ToList())
                Release(id);
            _carried.Clear();
            foreach (var pair in carriedNow)
                _carried[pair.Key] = pair.Value;
            var k = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            foreach (var pair in _carried)
            {
                var view = _table.ViewOf(pair.Key);
                // Someone here grabbed it in the same instant: the host's order decides later; leave it to the local hand.
                if (view == null || view.Held)
                    continue;
                if (view.RemoteHolder == null)
                {
                    view.RemoteHolder = pair.Value.Seat;
                    view.Body.isKinematic = true;
                    // The pose is set every frame here; rigidbody interpolation would drag it back.
                    view.Body.interpolation = RigidbodyInterpolation.None;
                    RemotePickedUp?.Invoke(view);
                }
                view.transform.SetPositionAndRotation(
                    Vector3.Lerp(view.transform.position, pair.Value.Position, k),
                    Quaternion.Slerp(view.transform.rotation, pair.Value.Rotation, k));
                var frameMarker = FrameFor(pair.Key, ColorOf(pair.Value.Seat));
                var bounds = view.Renderer.bounds;
                frameMarker.transform.position = new Vector3(bounds.center.x, bounds.min.y - 0.001f, bounds.center.z);
                frameMarker.transform.localScale = new Vector3(bounds.size.x + 0.012f, 1f, bounds.size.z + 0.012f);
                frameMarker.SetActive(true);
            }
        }

        /// <summary>The holder let go: the object falls under this machine's physics until its final pose arrives.</summary>
        private void Release(string id)
        {
            if (_frames.TryGetValue(id, out var marker) && marker != null)
                marker.SetActive(false);
            var view = _table.ViewOf(id);
            if (view == null || view.RemoteHolder == null)
                return;
            view.RemoteHolder = null;
            view.Body.interpolation = RigidbodyInterpolation.Interpolate;
            if (view.Held)
                return;
            view.Body.isKinematic = view.Model.Locked || view.InHand;
            if (!view.Body.isKinematic)
                view.Body.WakeUp();
        }

        private Color ColorOf(string seat) =>
            ZoneViewFactory.SeatColor(_table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color ?? "white");

        private Material Unlit(Color color)
        {
            if (_unlit == null)
                _unlit = new Material(RequiredShaders.Unlit) { name = "Presence" };
            var material = new Material(_unlit);
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// <summary>A small cone pointing down at the table plus a ring where it touches, in the player's colour.</summary>
        private GameObject PointerFor(string seat, Color color)
        {
            if (_pointers.TryGetValue(seat, out var existing) && existing != null)
                return existing;
            var root = new GameObject($"Pointer {seat}");
            root.transform.SetParent(transform, false);
            var material = Unlit(color);
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(cone.GetComponent<Collider>());
            cone.name = "Arrow";
            cone.transform.SetParent(root.transform, false);
            cone.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            cone.transform.localScale = new Vector3(0.012f, 0.02f, 0.012f);
            cone.GetComponent<Renderer>().sharedMaterial = material;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "Spot";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.0008f, 0f);
            ring.transform.localScale = new Vector3(0.022f, 0.0004f, 0.022f);
            ring.GetComponent<Renderer>().sharedMaterial = material;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _pointers[seat] = root;
            return root;
        }

        /// <summary>A thin frame under a carried object, in the holder's colour (TTS outlines what others hold).</summary>
        private GameObject FrameFor(string id, Color color)
        {
            if (_frames.TryGetValue(id, out var existing) && existing != null)
            {
                existing.GetComponentInChildren<Renderer>().sharedMaterial.SetColor("_BaseColor", color);
                return existing;
            }
            var root = new GameObject($"Held by other {id}");
            root.transform.SetParent(transform, false);
            var material = Unlit(color);
            const float w = 0.05f;
            foreach (var (position, size) in new[]
            {
                (new Vector3(0f, 0f, 0.5f - w / 2f), new Vector3(1f, 0.0005f, w)),
                (new Vector3(0f, 0f, -0.5f + w / 2f), new Vector3(1f, 0.0005f, w)),
                (new Vector3(0.5f - w / 2f, 0f, 0f), new Vector3(w, 0.0005f, 1f)),
                (new Vector3(-0.5f + w / 2f, 0f, 0f), new Vector3(w, 0.0005f, 1f)),
            })
            {
                var strip = new GameObject("Strip");
                strip.transform.SetParent(root.transform, false);
                strip.transform.localPosition = position;
                strip.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Box(size);
                var renderer = strip.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _frames[id] = root;
            return root;
        }
    }
}
