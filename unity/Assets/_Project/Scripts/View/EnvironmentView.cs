using System.Collections.Generic;
using Core.Rules;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TableWorld.View
{
    /// <summary>
    /// The room around the table, rebuilt whenever a table loads from its <see cref="EnvironmentDef"/> (a game, or a TTS
    /// import, sets it; none means the default room). The default "game night" room is a small dim room with a warm
    /// pendant lamp over the table, dark walls and a wooden floor. The table's felt and wood follow the environment too.
    /// Everything here is built from primitives at runtime, so a game can change it without an Editor.
    /// <para>
    /// Light is split by URP rendering layers: the warm lamp and fill light only the room (walls, floor, rug, legs, the
    /// wooden rim); game pieces and the felt are lit only by a neutral table light, so card art keeps its true colours
    /// whatever mood the room has. Ambient is neutral for the same reason.
    /// </para>
    /// </summary>
    public sealed class EnvironmentView : MonoBehaviour
    {
        // Game night defaults (linear colours, URP non-physical intensities).
        public static readonly Color DefaultWall = new Color(0.16f, 0.12f, 0.1f);
        public static readonly Color DefaultFloor = new Color(0.22f, 0.14f, 0.09f);
        public static readonly Color DefaultAmbient = new Color(0.6f, 0.6f, 0.62f);
        public const float DefaultAmbientIntensity = 0.3f;
        public static readonly Color DefaultTableLight = new Color(1f, 0.99f, 0.97f);
        public const float DefaultTableLightIntensity = 2.6f;
        /// <summary>Low side lights for the pieces' vertical faces (degrees above the table, intensity each).</summary>
        public const float SideLightElevation = 20f;
        public const float SideLightIntensity = 0.45f;

        /// <summary>Rendering layer of game pieces and the felt (Unity's default layer, so every object is on it).</summary>
        public const uint PiecesLayers = 1u << 0;
        /// <summary>Rendering layer of the room and the table's wooden frame.</summary>
        public const uint RoomLayers = 1u << 1;
        public static readonly Color DefaultLamp = new Color(1f, 0.83f, 0.62f);
        public const float DefaultLampIntensity = 3.2f;
        public static readonly Color DefaultFelt = new Color(0.09f, 0.27f, 0.19f);
        public static readonly Color DefaultWood = new Color(0.36f, 0.22f, 0.12f);

        /// <summary>Room size around the table centre: the camera reaches 4 m, so it never leaves the room.</summary>
        /// <summary>The room grows with a big table, keeping walls a few steps away from it.</summary>
        public static Vector3 RoomHalfSize => new Vector3(Mathf.Max(5f, TableGeometry.Size.x / 2f + 2.5f), 0f, Mathf.Max(4.5f, TableGeometry.Size.y / 2f + 2.5f));
        public const float FloorY = -0.76f;
        public const float CeilingY = 2.4f;
        public const float LampHeight = 1.05f;

        [SerializeField] private TableView _table;
        [SerializeField] private Transform _tableRoot;
        [SerializeField] private Light _sun;
        [SerializeField] private Camera _camera;

        private GameObject _room;
        private Light _lamp;
        private Light _tableLight;
        private bool _bigTable;

        /// <summary>
        /// Even light over a big table, as a share of the lamp's intensity. Lower than the lamp's brightness under it: the
        /// lamp's warm pool suits our green felt, but evenly over a whole TTS layout it washed dark art out to grey (a
        /// #202020 background showed as #5A).
        /// </summary>
        private const float DirectionalShare = 0.4f;
        private Shader _lit;

        public Light Lamp => _lamp;

        /// <summary>The pendant lamp's shade and cord: hidden while the camera looks down from above them (top-down view).</summary>
        private GameObject _fixture;
        private readonly List<Renderer> _fixtureParts = new List<Renderer>();

        private void LateUpdate()
        {
            if (_fixture == null || _camera == null)
                return;
            var above = _camera.transform.position.y > LampHeight;
            foreach (var part in _fixtureParts)
                if (part != null && part.forceRenderingOff != above)
                    part.forceRenderingOff = above;
        }
        public Light TableLight => _tableLight;
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
            var env = _table.Engine?.State.Environment;
            if (env?.Table != null && env.Table.FeltAsset == assetId)
                DressTable(env.Table);
            if (env != null && env.SkyAsset == assetId)
                Apply();
        }

        private void Start()
        {
            EnsureLook();
            Apply();
        }

        private GameObject _look;

        public const string LookProfilePath = "Look/LookProfile";

        /// <summary>The look: neutral tone curve, a little more exposure and saturation (shared with the Editor's asset).</summary>
        public static void FillLook(VolumeProfile profile)
        {
            if (!profile.TryGet<Tonemapping>(out var tone))
                tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            if (!profile.TryGet<ColorAdjustments>(out var adjust))
                adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(0.3f);
            adjust.saturation.Override(12f);
        }

        /// <summary>
        /// Tone mapping for the whole view: under the table light, white cards, boards and papers went past full white
        /// and lost all shading. A neutral tone curve keeps their highlights; a little exposure and saturation keep the
        /// table as bright and colourful as before. Built here, so no scene asset is edited.
        /// </summary>
        private void EnsureLook()
        {
            if (_look != null)
                return;
            _look = new GameObject("Look");
            _look.transform.SetParent(transform, false);
            var volume = _look.AddComponent<Volume>();
            volume.isGlobal = true;
            // The project's profile (Resources/Look/LookProfile, made by Apply Project Settings) keeps a build from
            // stripping the tone mapping; built here only if it is missing.
            var profile = Resources.Load<VolumeProfile>(LookProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                FillLook(profile);
            }
            volume.sharedProfile = profile;
            if (_camera != null)
                _camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        /// <summary>Builds the room and dresses the table for the loaded table's environment.</summary>
        public void Apply()
        {
            var env = _table != null && _table.Engine != null ? _table.Engine.State.Environment : null;
            env ??= new EnvironmentDef();
            if (_room != null)
                Destroy(_room);
            _room = null;
            _lamp = null;
            _tableLight = null;
            _sideLights.Clear();
            _fill = null;

            FitTable();
            var wall = ToColor(env.WallColor, DefaultWall);
            var floor = ToColor(env.FloorColor, DefaultFloor);
            var gameNight = env.Room == RoomPreset.GameNight && env.RoomAsset == null;
            if (gameNight)
                BuildGameNight(wall, floor);
            if (_camera != null)
                _camera.backgroundColor = wall * 0.5f;
            ApplySky(env);

            _gameNight = gameNight;
            ApplyLighting();
            // Without a room the old directional light keeps the table readable.
            if (_sun != null)
                _sun.enabled = !gameNight;

            DressTable(env.Table);
        }

        private bool _gameNight;
        private readonly System.Collections.Generic.List<Light> _sideLights = new System.Collections.Generic.List<Light>();
        private Light _fill;
        private const float FillIntensity = 0.7f;
        private static readonly Color FillColor = new Color(1f, 0.8f, 0.6f);

        /// <summary>
        /// Light: the room preset, then the game's overrides, then its scales and tint (TTS <c>Lighting</c>, which a script
        /// may change while playing).
        /// </summary>
        public void ApplyLighting()
        {
            var lighting = (_table != null && _table.Engine != null ? _table.Engine.State.Environment : null)?.Lighting ?? new LightingDef();
            var scale = lighting.LightScale ?? 1f;
            var tint = lighting.LightTint is { Length: 3 } t ? new Color(t[0], t[1], t[2]) : Color.white;
            // Each light alone, on top of the TTS scale and tint: the room (lamp, fill) and the table (felt, pieces).
            Color Tint(float[] c) => c is { Length: 3 } ? new Color(c[0], c[1], c[2]) : Color.white;
            var roomScale = scale * (lighting.RoomScale ?? 1f);
            var roomTint = tint * Tint(lighting.RoomTint);
            var tableScale = scale * (lighting.TableScale ?? 1f);
            var tableTint = tint * Tint(lighting.TableTint);
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Indoors, shiny and metal pieces reflect the room (its probe), not the default sky, which tints them cyan.
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.ambientLight = ToColor(lighting.AmbientColor, DefaultAmbient)
                * (lighting.AmbientIntensity ?? (_gameNight ? DefaultAmbientIntensity : 1f)) * (lighting.AmbientScale ?? 1f);
            if (_lamp != null)
            {
                _lamp.color = ToColor(lighting.LampColor, DefaultLamp) * roomTint;
                _lamp.intensity = (lighting.LampIntensity ?? DefaultLampIntensity) * roomScale;
            }
            if (_tableLight != null)
            {
                _tableLight.color = ToColor(lighting.TableLightColor, DefaultTableLight) * tableTint;
                _tableLight.intensity = (lighting.TableLightIntensity ?? DefaultTableLightIntensity) * (_bigTable ? DirectionalShare : 1f) * tableScale;
            }
            foreach (var side in _sideLights)
                if (side != null)
                {
                    side.color = DefaultTableLight * tableTint;
                    side.intensity = SideLightIntensity * tableScale;
                }
            if (_fill != null)
            {
                _fill.color = FillColor * roomTint;
                _fill.intensity = FillIntensity * roomScale;
            }
        }

        private Material _sky;

        /// <summary>A game's 360° sky (TTS SkyURL) all around the table, or the room's plain background without one.</summary>
        private void ApplySky(EnvironmentDef env)
        {
            var texture = env.SkyAsset != null ? _table.Assets?.Texture(env.SkyAsset) : null;
            var shader = texture != null ? Shader.Find(RequiredShaders.SkyName) : null;
            if (_camera != null)
                _camera.clearFlags = shader != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (shader == null)
            {
                RenderSettings.skybox = null;
                return;
            }
            if (_sky == null || _sky.shader != shader)
                _sky = new Material(shader) { name = "Game sky" };
            _sky.SetTexture("_MainTex", texture);
            // Equirectangular images (TTS's skies) span 360° by 180°.
            _sky.SetFloat("_Mapping", 1f);
            _sky.SetFloat("_ImageType", 0f);
            RenderSettings.skybox = _sky;
        }

        /// <summary>
        /// Captures the room for reflections once it stands. A capture in the first frames after a scene loads can come
        /// out empty (the renderer is not ready), which leaves metal reflecting the default sky in cyan: it is taken
        /// again a little later.
        /// </summary>
        private static System.Collections.IEnumerator RenderWhenBuilt(ReflectionProbe probe)
        {
            foreach (var wait in new[] { 0f, 0.5f, 2f })
            {
                if (wait > 0f)
                    yield return new WaitForSeconds(wait);
                else
                    yield return null;
                if (probe == null)
                    yield break;
                // A room switched off meanwhile (another table loaded, the look changed) has nothing to render.
                if (probe.isActiveAndEnabled)
                    probe.RenderProbe();
            }
        }

        private void BuildGameNight(Color wall, Color floor)
        {
            _room = new GameObject("Room (game night)");
            _room.transform.SetParent(transform, false);
            // What metal and glossy pieces reflect: the room itself, captured once it is built (and again when it changes).
            var probe = new GameObject("Room Reflections").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(_room.transform, false);
            probe.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(RoomHalfSize.x * 2f, CeilingY - FloorY, RoomHalfSize.z * 2f) * 1.05f;
            probe.center = new Vector3(0f, (CeilingY + FloorY) / 2f - probe.transform.localPosition.y, 0f);
            probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = wall * 0.5f;
            StartCoroutine(RenderWhenBuilt(probe));
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
            if (!TableGeometry.Hidden)
                Quad("Rug", new Vector3(0f, FloorY + 0.003f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(TableGeometry.Size.x + 1.4f, TableGeometry.Size.y + 1.2f), new Color(0.25f, 0.08f, 0.07f), 0f);

            // Table legs, under the corners.
            var legTop = -TableGeometry.Thickness;
            var legHeight = legTop - FloorY;
            var lx = TableGeometry.Size.x / 2f - 0.08f;
            var lz = TableGeometry.Size.y / 2f - 0.08f;
            foreach (var (x, z) in TableGeometry.Hidden ? new (float, float)[0] : new[] { (lx, lz), (-lx, lz), (lx, -lz), (-lx, -lz) })
                Box("Leg", new Vector3(x, legTop - legHeight / 2f, z), new Vector3(0.07f, legHeight, 0.07f), DefaultWood * 0.8f, 0.3f);

            // The lamp: a shade over the table and a warm spot that lights the whole table and casts soft shadows.
            var shade = Cone("Lamp Shade", new Vector3(0f, LampHeight + 0.08f, 0f), 0.22f, 0.16f, new Color(0.12f, 0.1f, 0.08f));
            _fixture = shade;
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
            bulb.GetComponent<Renderer>().renderingLayerMask = RoomLayers;
            // Over a big table (an imported TTS game) a pendant lamp a metre up only blocks the view: the light stays,
            // the fixture goes.
            var big = TableGeometry.Size.x > TableGeometry.DefaultSize.x + 0.01f || TableGeometry.Size.y > TableGeometry.DefaultSize.y + 0.01f;
            if (big)
            {
                shade.SetActive(false);
                foreach (Transform part in _room.transform)
                    if (part.name == "Lamp Cord")
                        part.gameObject.SetActive(false);
            }

            _fixtureParts.Clear();
            foreach (Transform part in _room.transform)
                if (part.name == "Lamp Cord" || part.name == "Lamp Shade")
                    _fixtureParts.AddRange(part.GetComponentsInChildren<Renderer>(true));

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
            // The lamp lights the room only, but everything (the table, the pieces) still casts its shadow on the floor.
            SetLayers(_lamp, RoomLayers);
            var lampData = _lamp.GetUniversalAdditionalLightData();
            lampData.customShadowLayers = true;
            lampData.shadowRenderingLayers = uint.MaxValue;

            // The table light: same place, neutral white, lights only game pieces and the felt, and casts their shadows.
            var tableGo = new GameObject("Table Light");
            tableGo.transform.SetParent(_room.transform, false);
            tableGo.transform.SetPositionAndRotation(new Vector3(0f, LampHeight, 0f), Quaternion.Euler(90f, 0f, 0f));
            _tableLight = tableGo.AddComponent<Light>();
            _tableLight.type = LightType.Spot;
            _tableLight.spotAngle = 120f;
            _tableLight.innerSpotAngle = 80f;
            _tableLight.range = 4f;
            _tableLight.shadows = LightShadows.Soft;
            _tableLight.shadowStrength = 0.8f;
            _tableLight.shadowNearPlane = 0.1f;
            SetLayers(_tableLight, PiecesLayers);
            Settings.GraphicsPresets.ApplyLights(_lamp, _tableLight);

            // A big table (an imported TTS game) is lit evenly from above, as TTS lights it: the lamp's spot reaches
            // about 1.8 m, and several overlapping spots made the middle several times too bright.
            var size = TableGeometry.Size;
            _bigTable = size.x > TableGeometry.DefaultSize.x + 0.01f || size.y > TableGeometry.DefaultSize.y + 0.01f;
            if (_bigTable)
            {
                _tableLight.type = LightType.Directional;
                tableGo.transform.rotation = Quaternion.Euler(80f, 20f, 0f);
            }

            // The table light points straight down, so the sides of chips, dice, tiles and models got only the dim ambient
            // and looked black. Two faint, low, shadowless lights from opposite corners light every side of a piece.
            foreach (var yaw in new[] { 35f, 215f })
            {
                var sideGo = new GameObject("Piece Side Light");
                sideGo.transform.SetParent(_room.transform, false);
                sideGo.transform.rotation = Quaternion.Euler(SideLightElevation, yaw, 0f);
                var side = sideGo.AddComponent<Light>();
                side.type = LightType.Directional;
                side.intensity = SideLightIntensity;
                side.color = DefaultTableLight;
                side.shadows = LightShadows.None;
                SetLayers(side, PiecesLayers);
                _sideLights.Add(side);
            }

            // A faint warm fill so the far side of the table and the room are not black.
            var fillGo = new GameObject("Fill Light");
            fillGo.transform.SetParent(_room.transform, false);
            fillGo.transform.position = new Vector3(0f, 1.8f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 7f;
            fill.intensity = FillIntensity;
            fill.color = FillColor;
            fill.shadows = LightShadows.None;
            SetLayers(fill, RoomLayers);
            _fill = fill;
        }

        /// <summary>Felt colour or image, wood colour; per-instance materials so the shared material assets stay untouched.</summary>
        /// <summary>
        /// Shapes the scene's table to the loaded table's size (felt and rims; the legs and rug follow in the room), or
        /// hides it when the game brings its own surface; the camera's reach follows.
        /// </summary>
        private void FitTable()
        {
            if (_tableRoot != null)
            {
                _tableRoot.gameObject.SetActive(!TableGeometry.Hidden);
                var size = TableGeometry.Size;
                var thickness = TableGeometry.Thickness;
                var rimWidth = TableGeometry.RimWidth;
                var halfX = size.x / 2f + rimWidth / 2f;
                var halfZ = size.y / 2f + rimWidth / 2f;
                var rimY = TableGeometry.RimHeight / 2f - thickness / 2f;
                var rimHeight = TableGeometry.RimHeight + thickness;
                foreach (Transform part in _tableRoot)
                {
                    switch (part.name)
                    {
                        case "Surface":
                            part.localPosition = new Vector3(0f, -thickness / 2f, 0f);
                            part.localScale = new Vector3(size.x, thickness, size.y);
                            break;
                        case "RimNorth":
                        case "RimSouth":
                            part.localPosition = new Vector3(0f, rimY, part.name == "RimNorth" ? halfZ : -halfZ);
                            part.localScale = new Vector3(size.x + 2f * rimWidth, rimHeight, rimWidth);
                            break;
                        case "RimEast":
                        case "RimWest":
                            part.localPosition = new Vector3(part.name == "RimEast" ? halfX : -halfX, rimY, 0f);
                            part.localScale = new Vector3(rimWidth, rimHeight, size.y);
                            break;
                    }
                }
            }
            var camera = _camera != null ? _camera.GetComponent<TableCamera>() : null;
            if (camera != null)
                camera.FitTable(TableGeometry.Size);
        }

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
                    // The wooden frame belongs to the room's warm light; the felt is a game surface.
                    renderer.renderingLayerMask = _room != null ? RoomLayers : PiecesLayers;
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
            quad.GetComponent<Renderer>().renderingLayerMask = RoomLayers;
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
            box.GetComponent<Renderer>().renderingLayerMask = RoomLayers;
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
            renderer.renderingLayerMask = RoomLayers;
            // The shade must not block its own light.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return shade;
        }

        private Material NewMaterial(Color color, float smoothness)
        {
            if (_lit == null)
                _lit = RequiredShaders.Lit;
            var material = new Material(_lit) { color = color };
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        /// <summary>The rendering layers a light lights (URP keeps them in its additional light data).</summary>
        public static uint LightingLayersOf(Light light) => light.GetUniversalAdditionalLightData().renderingLayers;

        /// <summary>URP reads a light's layers from its additional light data, which overrides <see cref="Light.renderingLayerMask"/>.</summary>
        private static void SetLayers(Light light, uint layers)
        {
            light.GetUniversalAdditionalLightData().renderingLayers = layers;
            light.renderingLayerMask = (int)layers;
        }

        private static Color ToColor(float[] values, Color fallback) =>
            values != null && values.Length == 3 ? new Color(values[0], values[1], values[2]) : fallback;
    }
}
