using System.Collections.Generic;
using TableWorld.View.Chalkers;
using UnityEngine;

namespace TableWorld.View.Karaoke
{
    /// <summary>
    /// The karaoke room's furniture where people walk (ADR 0013 §5, T12.10): sofas facing the big screen, each with places
    /// anyone may sit on, and the footprints a walker cannot pass through. Built from <see cref="ChalkerSofa"/>s, so a
    /// seated chalker fits them as at a table. Places are numbered across the sofas, the same on every machine (presence
    /// carries the number).
    /// </summary>
    public sealed class Lounge
    {
        /// <summary>A rectangle on the floor seen from above: centre, half sizes along its own right and forward, and its forward.</summary>
        public readonly struct Footprint
        {
            public readonly Vector3 Centre;
            public readonly Vector3 Forward;
            public readonly Vector2 Half;

            public Footprint(Vector3 centre, Vector3 forward, Vector2 half)
            {
                Centre = new Vector3(centre.x, 0f, centre.z);
                Forward = new Vector3(forward.x, 0f, forward.z).normalized;
                Half = half;
            }

            public Vector3 Right => new Vector3(Forward.z, 0f, -Forward.x);

            /// <summary>The nearest point of the rectangle to <paramref name="point"/> (floor plane).</summary>
            public Vector3 Closest(Vector3 point)
            {
                var d = new Vector3(point.x, 0f, point.z) - Centre;
                var x = Mathf.Clamp(Vector3.Dot(d, Right), -Half.x, Half.x);
                var z = Mathf.Clamp(Vector3.Dot(d, Forward), -Half.y, Half.y);
                return Centre + Right * x + Forward * z;
            }
        }

        private readonly List<ChalkerSofa> _sofas = new List<ChalkerSofa>();
        private readonly List<(Vector3 Position, Vector3 Facing)> _places = new List<(Vector3, Vector3)>();
        private readonly List<Footprint> _blockers = new List<Footprint>();

        /// <summary>Every sofa place: the sitter's origin (table-top height, as a seated chalker's) and which way they face.</summary>
        public IReadOnlyList<(Vector3 Position, Vector3 Facing)> Places => _places;

        /// <summary>What a walker cannot walk through.</summary>
        public IReadOnlyList<Footprint> Blockers => _blockers;

        public IReadOnlyList<ChalkerSofa> Sofas => _sofas;

        /// <summary>The stage: the floor in front of the screen. Whoever stands on it sings (ADR 0013 §5).</summary>
        public Footprint Stage { get; }

        /// <summary>True when feet at <paramref name="feet"/> stand on the stage.</summary>
        public bool OnStage(Vector3 feet)
        {
            var closest = Stage.Closest(feet);
            return Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(feet.x, feet.z)) < 1e-3f;
        }

        /// <summary>
        /// Sofas in the room facing <paramref name="screen"/> (a point on the far wall): one long sofa in the middle, two at
        /// the sides turned towards the screen, an open floor in front of the screen for whoever sings.
        /// </summary>
        public Lounge(Transform parent, Vector3 screen)
        {
            var target = new Vector3(screen.x, 0f, screen.z);
            // The stage: from a step off the screen's wall, 3.6 m wide and 2 m deep, facing back into the room.
            Stage = new Footprint(new Vector3(screen.x, 0f, screen.z - 1.4f), Vector3.back, new Vector2(1.8f, 1.0f));
            BuildStageFloor(parent);
            Add(parent, "middle", new Vector3(0f, 0f, -0.8f), target, 4);
            Add(parent, "left", new Vector3(-3.1f, 0f, 0.9f), target, 3);
            Add(parent, "right", new Vector3(3.1f, 0f, 0.9f), target, 3);
        }

        private GameObject _stageFloor;
        private Material _stageMaterial;

        /// <summary>The stage drawn on the floor: a warm carpet with a lit edge, so people see where to stand to sing.</summary>
        private void BuildStageFloor(Transform parent)
        {
            _stageFloor = new GameObject("Stage");
            _stageFloor.transform.SetParent(parent, false);
            _stageMaterial = new Material(RequiredShaders.Lit) { name = "Stage" };
            _stageMaterial.SetColor("_BaseColor", new Color(0.34f, 0.12f, 0.26f));
            _stageMaterial.EnableKeyword("_EMISSION");
            _stageMaterial.SetColor("_EmissionColor", new Color(0.25f, 0.05f, 0.2f));
            var carpet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(carpet.GetComponent<Collider>());
            carpet.name = "Stage carpet";
            carpet.transform.SetParent(_stageFloor.transform, false);
            carpet.transform.position = new Vector3(Stage.Centre.x, EnvironmentView.FloorY + 0.01f, Stage.Centre.z);
            carpet.transform.localScale = new Vector3(Stage.Half.x * 2f, 0.02f, Stage.Half.y * 2f);
            carpet.GetComponent<Renderer>().sharedMaterial = _stageMaterial;
        }

        private void Add(Transform parent, string name, Vector3 at, Vector3 lookAt, int places)
        {
            var facing = lookAt - at;
            facing.y = 0f;
            facing.Normalize();
            var sofa = new ChalkerSofa(name, parent, places);
            sofa.Place(at, facing);
            _sofas.Add(sofa);
            var right = Vector3.Cross(Vector3.up, facing);
            var inner = places * ChalkerSofa.PlaceWidth;
            for (var i = 0; i < places; i++)
                _places.Add((at + right * (-inner / 2f + ChalkerSofa.PlaceWidth * (i + 0.5f)), facing));
            // From the front of the cushions to the back of the backrest, arm to arm.
            var front = ChalkerSofa.Front;
            var back = ChalkerSofa.Front - ChalkerSofa.SeatDepth - ChalkerSofa.BackThickness;
            _blockers.Add(new Footprint(at + facing * ((front + back) / 2f), facing, new Vector2(sofa.Length / 2f, (front - back) / 2f)));
        }

        public void Destroy()
        {
            foreach (var sofa in _sofas)
                sofa.Destroy();
            _sofas.Clear();
            if (_stageMaterial != null)
                Object.Destroy(_stageMaterial);
            if (_stageFloor != null)
                Object.Destroy(_stageFloor);
        }
    }
}
