using Core.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace TableWorld.View
{
    /// <summary>
    /// The room around the table, rebuilt whenever a table loads from its <see cref="EnvironmentDef"/> (a game, or a TTS
    /// import, sets it; none means the default room). The default "game night" room is a small dim room with a warm
    /// pendant lamp over the table, dark walls and a wooden floor. The table's felt and wood follow the environment too.
    /// Everything here is built from primitives at runtime, so a game can change it without an Editor.
    /// </summary>
    public sealed class EnvironmentView : MonoBehaviour
    {
        // Game night defaults (linear colours, URP non-physical intensities).
        public static readonly Color DefaultWall = new Color(0.16f, 0.12f, 0.1f);
        public static readonly Color DefaultFloor = new Color(0.22f, 0.14f, 0.09f);
        public static readonly Color DefaultAmbient = new Color(0.55f, 0.45f, 0.38f);
        public const float DefaultAmbientIntensity = 0.45f;
        public static readonly Color DefaultLamp = new Color(1f, 0.83f, 0.62f);
        public const float DefaultLampIntensity = 3.2f;
        public static readonly Color DefaultFelt = new Color(0.09f, 0.27f, 0.19f);
        public static readonly Color DefaultWood = new Color(0.36f, 0.22f, 0.12f);

        /// <summary>Room size around the table centre: the camera reaches 4 m, so it never leaves the room.</summary>
        public static readonly Vector3 RoomHalfSize = new Vector3(5f, 0f, 4.5f);
        public const float FloorY = -0.76f;
        public const float CeilingY = 2.4f;
        public const float LampHeight = 1.05f;

        [SerializeField] private TableView _table;
        [SerializeField] private Transform _tableRoot;
        [SerializeField] private Light _sun;
        [SerializeField] private Camera _camera;

        private GameObject _room;
        private Light _lamp;
        private Shader _lit;

        public Light Lamp => _lamp;
        public GameObject Room => _room;

        public void Configure(TableView table, Transform tableRoot, Light sun, Camera camera)
        {
            _table = table;
            _tableRoot = tableRoot;
            _sun = sun;
            _camera = camera;
        }

        private void OnEnable()
        {
            if (_table == null)
                return;
            _table.Loaded += Apply;
            _table.AssetRefreshed += OnAssetRefreshed;
        }

        private void OnDisable()
        {
            if (_table == null)
                return;
            _table.Loaded -= Apply;
            _table.AssetRefreshed -= OnAssetRefreshed;
        }

        /// <summary>A joining player receives the felt image after the table: dress the table again when it lands.</summary>
        private void OnAssetRefreshed(string assetId)
        {
            var look = _table.Engine?.State.Environment?.Table;
            if (look != null && look.FeltAsset == assetId)
                DressTable(look);
        }

        private void Start() => Apply();

        /// <summary>Builds the room and dresses the table for the loaded table's environment.</summary>
        public void Apply()
        {
            var env = _table != null && _table.Engine != null ? _table.Engine.State.Environment : null;
            env ??= new EnvironmentDef();
            if (_room != null)
                Destroy(_room);
            _room = null;
            _lamp = null;

            var wall = ToColor(env.WallColor, DefaultWall);
            var floor = ToColor(env.FloorColor, DefaultFloor);
            var gameNight = env.Room == RoomPreset.GameNight && env.RoomAsset == null;
            if (gameNight)
                BuildGameNight(wall, floor);
            if (_camera != null)
                _camera.backgroundColor = wall * 0.5f;

            // Light: the room preset, then the game's overrides.
            var lighting = env.Lighting;
            RenderSettings.ambientMode = AmbientMode.Flat;
            // The default sky's reflections tint an indoor room blue-grey; indoors there is no sky to reflect.
            RenderSettings.reflectionIntensity = gameNight ? 0.15f : 1f;
            RenderSettings.ambientLight = ToColor(lighting.AmbientColor, DefaultAmbient) * (lighting.AmbientIntensity ?? (gameNight ? DefaultAmbientIntensity : 1f));
            if (_lamp != null)
            {
                _lamp.color = ToColor(lighting.LampColor, DefaultLamp);
                _lamp.intensity = lighting.LampIntensity ?? DefaultLampIntensity;
            }
            // Without a room the old directional light keeps the table readable.
            if (_sun != null)
                _sun.enabled = !gameNight;

            DressTable(env.Table);
        }

        private void BuildGameNight(Color wall, Color floor)
        {
            _room = new GameObject("Room (game night)");
            _room.transform.SetParent(transform, false);
            var h = RoomHalfSize;
            var wallHeight = CeilingY - FloorY;
            var wallMid = (CeilingY + FloorY) / 2f;
            Quad("Floor", new Vector3(0f, FloorY, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(h.x * 2f, h.z * 2f), floor, 0.25f);
            Quad("Ceiling", new Vector3(0f, CeilingY, 0f), Quaternion.Euler(-90f, 0f, 0f), new Vector2(h.x * 2f, h.z * 2f), wall * 0.6f, 0f);
            // Walls face inward, so a camera that strays outside sees through them instead of being blocked.
            Quad("Wall North", new Vector3(0f, wallMid, h.z), Quaternion.Euler(0f, 0f, 0f), new Vector2(h.x * 2f, wallHeight), wall, 0.1f);
            Quad("Wall South", new Vector3(0f, wallMid, -h.z), Quaternion.Euler(0f, 180f, 0f), new Vector2(h.x * 2f, wallHeight), wall, 0.1f);
            Quad("Wall East", new Vector3(h.x, wallMid, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector2(h.z * 2f, wallHeight), wall, 0.1f);
            Quad("Wall West", new Vector3(-h.x, wallMid, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector2(h.z * 2f, wallHeight), wall, 0.1f);
            // A dark rug under the table grounds it.
            Quad("Rug", new Vector3(0f, FloorY + 0.003f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(TableGeometry.Size.x + 1.4f, TableGeometry.Size.y + 1.2f), new Color(0.25f, 0.08f, 0.07f), 0f);

            // Table legs, under the corners.
            var legTop = -TableGeometry.Thickness;
            var legHeight = legTop - FloorY;
            var lx = TableGeometry.Size.x / 2f - 0.08f;
            var lz = TableGeometry.Size.y / 2f - 0.08f;
            foreach (var (x, z) in new[] { (lx, lz), (-lx, lz), (lx, -lz), (-lx, -lz) })
                Box("Leg", new Vector3(x, legTop - legHeight / 2f, z), new Vector3(0.07f, legHeight, 0.07f), DefaultWood * 0.8f, 0.3f);

            // The lamp: a shade over the table and a warm spot that lights the whole table and casts soft shadows.
            var shade = Cone("Lamp Shade", new Vector3(0f, LampHeight + 0.08f, 0f), 0.22f, 0.16f, new Color(0.12f, 0.1f, 0.08f));
            Box("Lamp Cord", new Vector3(0f, (LampHeight + 0.16f + CeilingY) / 2f, 0f), new Vector3(0.008f, CeilingY - LampHeight - 0.16f, 0.008f), new Color(0.05f, 0.05f, 0.05f), 0f);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            Object.Destroy(bulb.GetComponent<Collider>());
            bulb.transform.SetParent(shade.transform, false);
            bulb.transform.localPosition = new Vector3(0f, -0.06f, 0f);
            bulb.transform.localScale = Vector3.one * 0.06f;
            var glow = NewMaterial(new Color(1f, 0.9f, 0.7f), 0f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.6f) * 3f);
            bulb.GetComponent<Renderer>().sharedMaterial = glow;
            bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var lampGo = new GameObject("Lamp Light");
            lampGo.transform.SetParent(_room.transform, false);
            lampGo.transform.SetPositionAndRotation(new Vector3(0f, LampHeight, 0f), Quaternion.Euler(90f, 0f, 0f));
            _lamp = lampGo.AddComponent<Light>();
            _lamp.type = LightType.Spot;
            _lamp.spotAngle = 120f;
            _lamp.innerSpotAngle = 70f;
            _lamp.range = 4f;
            _lamp.shadows = LightShadows.Soft;
            _lamp.shadowStrength = 0.85f;
            _lamp.shadowNearPlane = 0.1f;

            // A faint warm fill so the far side of the table and the room are not black.
            var fillGo = new GameObject("Fill Light");
            fillGo.transform.SetParent(_room.transform, false);
            fillGo.transform.position = new Vector3(0f, 1.8f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 7f;
            fill.intensity = 0.7f;
            fill.color = new Color(1f, 0.8f, 0.6f);
            fill.shadows = LightShadows.None;
        }

        /// <summary>Felt colour or image, wood colour; per-instance materials so the shared material assets stay untouched.</summary>
        private void DressTable(TableLookDef look)
        {
            if (_tableRoot == null)
                return;
            foreach (var renderer in _tableRoot.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name == "Surface")
                {
                    var felt = renderer.material;
                    felt.color = ToColor(look.FeltColor, look.FeltAsset != null ? Color.white : DefaultFelt);
                    felt.mainTexture = look.FeltAsset != null ? _table.Assets?.Texture(look.FeltAsset) : null;
                    felt.mainTextureScale = Vector2.one;
                }
                else if (renderer.name.StartsWith("Rim"))
                {
                    renderer.material.color = ToColor(look.WoodColor, DefaultWood);
                }
            }
        }

        private GameObject Quad(string name, Vector3 position, Quaternion rotation, Vector2 size, Color color, float smoothness)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_room.transform, false);
            // Unity's quad is visible from its -Z side: each rotation above points that side into the room.
            quad.transform.SetPositionAndRotation(position, rotation);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = NewMaterial(color, smoothness);
            return quad;
        }

        private GameObject Box(string name, Vector3 position, Vector3 size, Color color, float smoothness)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Object.Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(_room.transform, false);
            box.transform.position = position;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = NewMaterial(color, smoothness);
            return box;
        }

        /// <summary>A lamp shade: an open cylinder narrowing upward, approximated by a scaled, squashed cylinder.</summary>
        private GameObject Cone(string name, Vector3 position, float radius, float height, Color color)
        {
            var shade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shade.name = name;
            Object.Destroy(shade.GetComponent<Collider>());
            shade.transform.SetParent(_room.transform, false);
            shade.transform.position = position;
            shade.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            var renderer = shade.GetComponent<Renderer>();
            renderer.sharedMaterial = NewMaterial(color, 0.4f);
            // The shade must not block its own light.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return shade;
        }

        private Material NewMaterial(Color color, float smoothness)
        {
            if (_lit == null)
                _lit = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(_lit) { color = color };
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static Color ToColor(float[] values, Color fallback) =>
            values != null && values.Length == 3 ? new Color(values[0], values[1], values[2]) : fallback;
    }
}
