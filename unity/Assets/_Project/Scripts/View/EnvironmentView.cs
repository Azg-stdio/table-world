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
    /// <para>
    /// The karaoke room (T12.6) is the same room in party colours, with a big screen on the far wall, speakers either side
    /// of it, two coloured accent lights and a stage light that follows the singer (<see cref="AimStage"/>); sofas
    /// replace the chairs (<see cref="Chalkers.ChalkersView"/>).
    /// </para>
    /// </summary>
    public sealed partial class EnvironmentView : MonoBehaviour
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
        public static readonly Color KaraokeWall = new Color(0.09f, 0.07f, 0.15f);
        public static readonly Color KaraokeFloor = new Color(0.13f, 0.07f, 0.14f);
        /// <summary>The karaoke screen: as wide as the far wall allows, up to this (metres), 16:9.</summary>
        public const float ScreenWidth = 3.2f;
        public const float ScreenCentreY = 1.15f;
        public const float StageLightIntensity = 6f;
        public const float StageLightAngle = 34f;

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
            FaceFlames();
            if (_fixture == null || _camera == null)
                return;
            var above = _camera.transform.position.y > LampHeight;
            foreach (var part in _fixtureParts)
                if (part != null && part.forceRenderingOff != above)
                    part.forceRenderingOff = above;
        }
        public Light TableLight => _tableLight;
        public GameObject Room => _room;

        private Renderer _screen;
        private Light _stage;
        private readonly List<Light> _accents = new List<Light>();

        /// <summary>The karaoke room's big screen (null in other rooms): a shared video will play on it (T12.4).</summary>
        public Renderer Screen => _screen;

        /// <summary>The karaoke room's stage light, on the singer while someone sings (null in other rooms).</summary>
        public Light StageLight => _stage;

        private Karaoke.Lounge _lounge;

        /// <summary>The sofas of a room people walk in (the karaoke room), or null.</summary>
        public Karaoke.Lounge Lounge => _lounge;

        /// <summary>
        /// Points the stage light down at whoever sits at <paramref name="seat"/> facing <paramref name="facing"/>, from the
        /// ceiling in front of them; null turns it off. Only the karaoke room has one.
        /// </summary>
        public void AimStage(Vector3? seat, Vector3? facing)
        {
            if (_stage == null)
                return;
            _stage.enabled = seat != null;
            if (!(seat is { } at))
                return;
            var forward = facing is { } f && f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            var target = at + Vector3.up * (Chalkers.Chalker.EyeHeight * 0.6f);
            var from = at + forward * 0.9f + Vector3.up * (CeilingY - 0.15f);
            _stage.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from, Vector3.up));
        }

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
            _lights.Clear();
            _flames.Clear();
            _flickers = false;
            _screen = null;
            _stage = null;
            _accents.Clear();
            _lounge?.Destroy();
            _lounge = null;

            FitTable();
            var karaoke = env.Room == RoomPreset.Karaoke && env.RoomAsset == null;
            var wall = ToColor(env.WallColor, karaoke ? KaraokeWall : DefaultWall);
            var floor = ToColor(env.FloorColor, karaoke ? KaraokeFloor : DefaultFloor);
            var tavern = env.Room == RoomPreset.Tavern && env.RoomAsset == null;
            if (tavern)
            {
                wall = ToColor(env.WallColor, TavernWall);
                floor = ToColor(env.FloorColor, TavernFloor);
            }
            var gameNight = (env.Room == RoomPreset.GameNight || karaoke || tavern) && env.RoomAsset == null;
            UsePresetLight(tavern);
            _tavern = tavern;
            if (gameNight)
                BuildGameNight(wall, floor, tavern);
            if (karaoke)
                BuildKaraoke();
            if (tavern)
                BuildTavern(wall, floor);
            foreach (var light in _lights)
                _flickers |= light.Flicker > 0f;
            if (_camera != null)
                _camera.backgroundColor = wall * 0.5f;
            ApplySky(env);

            _gameNight = gameNight;
            _roomNow = _tableNow = new Vector4(1, 1, 1, 1);
            ApplyLighting(true);
            // Without a room the old directional light keeps the table readable.
            if (_sun != null)
                _sun.enabled = !gameNight;

            DressTable(env.Table);
            ApplyLook(env.Look, tavern);
        }

        // ---- the room's post-processing (M12, T13.12) ------------------------------------------------------------------

        /// <summary>The tavern's look: soft dark edges and flames that glow; no fog (it dimmed the flames and the hearth, M12 record).</summary>
        public const float TavernFog = 0f, TavernVignette = 0.5f, TavernBloom = 0.5f;
        public static readonly Color TavernFogColor = new Color(0.06f, 0.04f, 0.03f);

        /// <summary>Fog density (exponential squared) at fog 1: the table, a metre or two away, loses 3 % at 0.5; the walls a third.</summary>
        public const float MaxFogDensity = 0.22f;
        /// <summary>Bloom starts far above white: only flames and lamps (HDR, several times white) glow, never a lit card.</summary>
        public const float BloomThreshold = 2.5f;

        private Volume _roomLook;
        private VolumeProfile _roomProfile;

        /// <summary>The look in force (tests read it): fog density, vignette and bloom intensities.</summary>
        public (float Fog, float Vignette, float Bloom) Look { get; private set; }

        private void ApplyLook(LookDef look, bool tavern)
        {
            var fog = Mathf.Clamp01(look?.Fog ?? (tavern ? TavernFog : 0f));
            var vignette = Mathf.Clamp01(look?.Vignette ?? (tavern ? TavernVignette : 0f));
            var bloom = Mathf.Clamp01(look?.Bloom ?? (tavern ? TavernBloom : 0f));
            RenderSettings.fog = fog > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fog * MaxFogDensity;
            RenderSettings.fogColor = ToColor(look?.FogColor, tavern ? TavernFogColor : _camera != null ? _camera.backgroundColor : Color.black);
            if (_roomLook == null)
            {
                var go = new GameObject("Room Look");
                go.transform.SetParent(transform, false);
                _roomLook = go.AddComponent<Volume>();
                _roomLook.isGlobal = true;
                _roomLook.priority = 1f;
                _roomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _roomProfile.name = "Room look";
                _roomLook.sharedProfile = _roomProfile;
            }
            if (!_roomProfile.TryGet<Vignette>(out var edges))
                edges = _roomProfile.Add<Vignette>(true);
            edges.intensity.Override(0.32f * vignette);
            edges.smoothness.Override(0.45f);
            edges.color.Override(Color.black);
            if (!_roomProfile.TryGet<Bloom>(out var glow))
                glow = _roomProfile.Add<Bloom>(true);
            glow.threshold.Override(BloomThreshold);
            glow.intensity.Override(1.2f * bloom);
            glow.scatter.Override(0.65f);
            _roomLook.enabled = vignette > 0f || bloom > 0f;
            Look = (RenderSettings.fogDensity, edges.intensity.value, glow.intensity.value);
        }

        private bool _gameNight;
        private const float FillIntensity = 0.7f;
        private static readonly Color FillColor = new Color(1f, 0.8f, 0.6f);

        // The preset's own light, before a game's overrides (game night's, or the tavern's).
        private Color _ambient, _lampColor, _tableLightColor;
        private float _ambientIntensity, _lampIntensity, _tableLightIntensity;

        private void UsePresetLight(bool tavern)
        {
            _ambient = tavern ? TavernAmbient : DefaultAmbient;
            _ambientIntensity = tavern ? TavernAmbientIntensity : DefaultAmbientIntensity;
            _lampColor = tavern ? TavernLamp : DefaultLamp;
            _lampIntensity = tavern ? TavernLampIntensity : DefaultLampIntensity;
            _tableLightColor = tavern ? TavernTableLight : DefaultTableLight;
            _tableLightIntensity = tavern ? TavernTableLightIntensity : DefaultTableLightIntensity;
        }

        /// <summary>What a room light is, for its base colour and intensity and whether a room tint colours it.</summary>
        private enum LightKind { Lamp, TableLight, Side, Fill, Accent, Stage, Flame }

        /// <summary>
        /// A light the room drives: its base colour and intensity (the preset's, or the game's for the lamp and the table
        /// light), whether it belongs to the room or the table, and how it flickers (candles, a fire). Every frame its
        /// light shows base × the current scale and tint of its part × its flicker.
        /// </summary>
        private sealed class RoomLight
        {
            public Light Light;
            public LightKind Kind;
            public Color Color;
            public float Intensity;
            public bool Room;
            public bool Tinted = true;
            /// <summary>How deep the flicker goes (0 none, 0.12 a candle) and how fast (noise per second).</summary>
            public float Flicker;
            public float Speed = 6f;
            public float Seed;
            /// <summary>Flame sprites that grow and dim with this light's flicker (null for none).</summary>
            public Transform Flame;
            public Vector3 FlameScale;
        }

        private readonly List<RoomLight> _lights = new List<RoomLight>();

        /// <summary>The current and target scale (w) and tint (rgb) of the room's lights and of the table's.</summary>
        private Vector4 _roomNow = new Vector4(1, 1, 1, 1), _roomFrom = new Vector4(1, 1, 1, 1), _roomTarget = new Vector4(1, 1, 1, 1);
        private Vector4 _tableNow = new Vector4(1, 1, 1, 1), _tableFrom = new Vector4(1, 1, 1, 1), _tableTarget = new Vector4(1, 1, 1, 1);
        private float _fadeSeconds;
        private float _fadeTime = 1f;

        /// <summary>True while a light change is still fading in (tests read it).</summary>
        public bool Fading => _fadeTime < 1f;

        private RoomLight Register(Light light, LightKind kind, bool room, Color color, float intensity)
        {
            var entry = new RoomLight { Light = light, Kind = kind, Room = room, Color = color, Intensity = intensity };
            _lights.Add(entry);
            return entry;
        }

        /// <summary>
        /// Light: the room preset, then the game's overrides, then its scales and tint (TTS <c>Lighting</c>, which a script
        /// may change while playing). A change while playing fades over <see cref="LightingDef.Fade"/> seconds; a table that
        /// loads (<paramref name="instant"/>) shows its light at once.
        /// </summary>
        public void ApplyLighting() => ApplyLighting(false);

        public void ApplyLighting(bool instant)
        {
            var lighting = (_table != null && _table.Engine != null ? _table.Engine.State.Environment : null)?.Lighting ?? new LightingDef();
            var scale = lighting.LightScale ?? 1f;
            var tint = lighting.LightTint is { Length: 3 } t ? new Color(t[0], t[1], t[2]) : Color.white;
            // Each light alone, on top of the TTS scale and tint: the room (lamp, fill) and the table (felt, pieces).
            Color Tint(float[] c) => c is { Length: 3 } ? new Color(c[0], c[1], c[2]) : Color.white;
            var roomTint = tint * Tint(lighting.RoomTint);
            var tableTint = tint * Tint(lighting.TableTint);
            var roomTarget = new Vector4(roomTint.r, roomTint.g, roomTint.b, scale * (lighting.RoomScale ?? 1f));
            var tableTarget = new Vector4(tableTint.r, tableTint.g, tableTint.b, scale * (lighting.TableScale ?? 1f));
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Indoors, shiny and metal pieces reflect the room (its probe), not the default sky, which tints them cyan.
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.ambientLight = ToColor(lighting.AmbientColor, _ambient)
                * (lighting.AmbientIntensity ?? (_gameNight ? _ambientIntensity : 1f)) * (lighting.AmbientScale ?? 1f);
            foreach (var light in _lights)
                switch (light.Kind)
                {
                    case LightKind.Lamp:
                        light.Color = ToColor(lighting.LampColor, _lampColor);
                        light.Intensity = lighting.LampIntensity ?? _lampIntensity;
                        break;
                    case LightKind.TableLight:
                        light.Color = ToColor(lighting.TableLightColor, _tableLightColor);
                        light.Intensity = (lighting.TableLightIntensity ?? _tableLightIntensity) * (_bigTable ? DirectionalShare : 1f);
                        break;
                    case LightKind.Side:
                        light.Color = ToColor(lighting.TableLightColor, _tableLightColor);
                        break;
                }
            _fadeSeconds = instant ? 0f : Mathf.Clamp(lighting.Fade ?? LightingDef.DefaultFade, 0f, 30f);
            _roomFrom = _roomNow;
            _tableFrom = _tableNow;
            _roomTarget = roomTarget;
            _tableTarget = tableTarget;
            _fadeTime = 0f;
            StepLights(0f);
        }

        private void Update() => StepLights(Time.deltaTime);

        /// <summary>Moves the fade on by <paramref name="delta"/> seconds and writes every light (and flame) of the room.</summary>
        private void StepLights(float delta)
        {
            if (_fadeTime < 1f)
            {
                _fadeTime = _fadeSeconds <= 0f ? 1f : Mathf.Min(1f, _fadeTime + delta / _fadeSeconds);
                var k = Mathf.SmoothStep(0f, 1f, _fadeTime);
                _roomNow = Vector4.Lerp(_roomFrom, _roomTarget, k);
                _tableNow = Vector4.Lerp(_tableFrom, _tableTarget, k);
            }
            else if (!_flickers)
                return;
            var time = Time.time;
            foreach (var light in _lights)
            {
                if (light.Light == null)
                    continue;
                var part = light.Room ? _roomNow : _tableNow;
                var flicker = 1f;
                if (light.Flicker > 0f)
                    flicker = 1f + light.Flicker * (Mathf.PerlinNoise(time * light.Speed + light.Seed, light.Seed * 1.7f) * 2f - 1f);
                light.Light.intensity = light.Intensity * part.w * flicker;
                light.Light.color = light.Tinted ? light.Color * new Color(part.x, part.y, part.z) : light.Color;
                if (light.Flame != null)
                {
                    // A flame stays lit in the dark (a candle burns whatever the room), a little smaller.
                    var size = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(part.w)) * (0.85f + 0.15f * flicker);
                    light.Flame.localScale = light.FlameScale * size;
                }
            }
        }

        /// <summary>Some light flickers (a candle, a fire): the lights are written every frame, not only while fading.</summary>
        private bool _flickers;

        private const float AccentIntensity = 1.4f;

        /// <summary>
        /// The karaoke room on top of the game night one: the screen on the far wall (dark until a song plays) in a black
        /// bezel, a speaker either side, a magenta and a blue light low on the side walls, and the stage light (off until
        /// someone sings). The pendant lamp stays: the table is still for playing.
        /// </summary>
        private void BuildKaraoke()
        {
            _room.name = "Room (karaoke)";
            var h = RoomHalfSize;
            var width = Mathf.Min(ScreenWidth, h.x * 2f - 2f);
            var height = width * 9f / 16f;
            var wallZ = h.z - 0.01f;
            Box("Screen Bezel", new Vector3(0f, ScreenCentreY, wallZ - 0.03f), new Vector3(width + 0.08f, height + 0.08f, 0.05f), new Color(0.02f, 0.02f, 0.02f), 0.6f);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            Object.Destroy(screen.GetComponent<Collider>());
            screen.transform.SetParent(_room.transform, false);
            // A quad shows its -Z side: it faces into the room from the far (+Z) wall unturned.
            screen.transform.SetPositionAndRotation(new Vector3(0f, ScreenCentreY, wallZ - 0.06f), Quaternion.identity);
            screen.transform.localScale = new Vector3(width, height, 1f);
            var picture = new Material(RequiredShaders.Unlit) { name = "Karaoke screen" };
            picture.SetColor("_BaseColor", new Color(0.06f, 0.08f, 0.17f));
            _screen = screen.GetComponent<Renderer>();
            _screen.sharedMaterial = picture;
            _screen.shadowCastingMode = ShadowCastingMode.Off;
            _screen.renderingLayerMask = RoomLayers;

            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * (width / 2f + 0.45f);
                Box("Speaker", new Vector3(x, FloorY + 0.5f, wallZ - 0.2f), new Vector3(0.36f, 1f, 0.3f), new Color(0.05f, 0.05f, 0.06f), 0.3f);
                foreach (var (y, size) in new[] { (0.72f, 0.24f), (0.28f, 0.12f) })
                {
                    var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    cone.name = "Speaker Cone";
                    Object.Destroy(cone.GetComponent<Collider>());
                    cone.transform.SetParent(_room.transform, false);
                    cone.transform.SetPositionAndRotation(new Vector3(x, FloorY + y, wallZ - 0.355f), Quaternion.Euler(90f, 0f, 0f));
                    cone.transform.localScale = new Vector3(size, 0.005f, size);
                    var renderer = cone.GetComponent<Renderer>();
                    renderer.sharedMaterial = NewMaterial(new Color(0.12f, 0.12f, 0.13f), 0.5f);
                    renderer.renderingLayerMask = RoomLayers;
                }
            }

            // Either side of the screen, washing the far wall in colour.
            foreach (var (x, color) in new[] { (-(width / 2f + 1.1f), new Color(0.9f, 0.2f, 0.75f)), (width / 2f + 1.1f, new Color(0.25f, 0.4f, 1f)) })
            {
                var accentGo = new GameObject("Accent Light");
                accentGo.transform.SetParent(_room.transform, false);
                accentGo.transform.position = new Vector3(x, 1.5f, h.z - 0.7f);
                var accent = accentGo.AddComponent<Light>();
                accent.type = LightType.Point;
                accent.range = 4.5f;
                accent.color = color;
                accent.intensity = AccentIntensity;
                accent.shadows = LightShadows.None;
                SetLayers(accent, RoomLayers);
                _accents.Add(accent);
                Register(accent, LightKind.Accent, true, color, AccentIntensity).Tinted = false;
            }

            // The stage light lights the singer (a piece-layer chalker) and their sofa (also on the pieces layer).
            var stageGo = new GameObject("Stage Light");
            stageGo.transform.SetParent(_room.transform, false);
            _stage = stageGo.AddComponent<Light>();
            _stage.type = LightType.Spot;
            _stage.spotAngle = StageLightAngle;
            _stage.innerSpotAngle = StageLightAngle * 0.6f;
            _stage.range = 5f;
            _stage.color = new Color(1f, 0.93f, 0.85f);
            _stage.intensity = StageLightIntensity;
            _stage.shadows = LightShadows.None;
            SetLayers(_stage, PiecesLayers | RoomLayers);
            _stage.enabled = false;
            Register(_stage, LightKind.Stage, true, _stage.color, StageLightIntensity).Tinted = false;

            // Where people walk: sofas facing the screen instead of a table.
            if (TableGeometry.Walk)
                _lounge = new Karaoke.Lounge(_room.transform, screen.transform.position);
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

        private void BuildGameNight(Color wall, Color floor, bool tavern = false)
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
            if (!TableGeometry.Hidden && !TableGeometry.Round)
                Quad("Rug", new Vector3(0f, FloorY + 0.003f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(TableGeometry.Size.x + 1.4f, TableGeometry.Size.y + 1.2f), new Color(0.25f, 0.08f, 0.07f), 0f);
            else if (!TableGeometry.Hidden)
                Disc("Rug", new Vector3(0f, FloorY + 0.003f, 0f), TableGeometry.Radius + 0.75f, 0.004f, new Color(0.25f, 0.08f, 0.07f));

            // Table legs, under the corners; a round table stands on one pedestal and a cross of feet.
            var legTop = -TableGeometry.Thickness;
            var legHeight = legTop - FloorY;
            var lx = TableGeometry.Size.x / 2f - 0.08f;
            var lz = TableGeometry.Size.y / 2f - 0.08f;
            foreach (var (x, z) in TableGeometry.Hidden || TableGeometry.Round ? new (float, float)[0] : new[] { (lx, lz), (-lx, lz), (lx, -lz), (-lx, -lz) })
                Box("Leg", new Vector3(x, legTop - legHeight / 2f, z), new Vector3(0.07f, legHeight, 0.07f), DefaultWood * 0.8f, 0.3f);
            if (!TableGeometry.Hidden && TableGeometry.Round)
            {
                Disc("Leg", new Vector3(0f, FloorY + legHeight / 2f, 0f), 0.09f, legHeight, DefaultWood * 0.8f);
                Disc("Leg", new Vector3(0f, legTop - 0.03f, 0f), TableGeometry.Radius * 0.35f, 0.06f, DefaultWood * 0.7f);
                var reach = Mathf.Min(0.9f, TableGeometry.Radius * 0.7f);
                foreach (var yaw in new[] { 45f, 135f })
                {
                    var foot = Box("Leg", new Vector3(0f, FloorY + 0.04f, 0f), new Vector3(2f * reach, 0.08f, 0.1f), DefaultWood * 0.8f, 0.3f);
                    foot.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }

            // The lamp: a shade over the table and a warm spot that lights the whole table and casts soft shadows. The tavern
            // hangs a chandelier of candles instead (BuildTavern), with the same light.
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

            if (tavern)
            {
                // Renamed first: Destroy waits for the frame's end, and the fixture parts are gathered by name below.
                foreach (Transform part in _room.transform)
                    if (part.name == "Lamp Cord" || part.name == "Lamp Shade")
                    {
                        part.name = "Removed";
                        Object.Destroy(part.gameObject);
                    }
                _fixture = null;
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
            Register(_lamp, LightKind.Lamp, true, _lampColor, _lampIntensity);
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
            Register(_tableLight, LightKind.TableLight, false, _tableLightColor, _tableLightIntensity);
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
                Register(side, LightKind.Side, false, DefaultTableLight, SideLightIntensity);
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
            Register(fill, LightKind.Fill, true, FillColor, FillIntensity);
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
                FitRound();
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

        /// <summary>
        /// A round table (M12) swaps the scene's box top and four rims for a disc and a ring made for its size, with the
        /// same materials, layer and physics; a square one swaps them back.
        /// </summary>
        private void FitRound()
        {
            var round = TableGeometry.Round;
            Transform surface = null, rim = null, disc = null, ring = null;
            foreach (Transform part in _tableRoot)
            {
                if (part.name == "Surface")
                    surface = part;
                else if (part.name == "RimNorth")
                    rim = part;
                else if (part.name == RoundSurfaceName)
                    disc = part;
                else if (part.name == RoundRimName)
                    ring = part;
            }
            foreach (Transform part in _tableRoot)
            {
                if (part.name == "Surface" || part.name.StartsWith("Rim") && part.name != RoundRimName)
                    part.gameObject.SetActive(!round);
            }
            if (!round)
            {
                if (disc != null)
                    disc.gameObject.SetActive(false);
                if (ring != null)
                    ring.gameObject.SetActive(false);
                return;
            }
            var radius = TableGeometry.Radius;
            disc = RoundPart(disc, RoundSurfaceName, surface, RoundTableMesh.Disc(radius, TableGeometry.Thickness), true);
            ring = RoundPart(ring, RoundRimName, rim, RoundTableMesh.Ring(radius, TableGeometry.RimWidth, TableGeometry.RimHeight, TableGeometry.Thickness), false);
        }

        public const string RoundSurfaceName = "SurfaceRound";
        public const string RoundRimName = "RimRound";

        /// <summary>A round part like <paramref name="like"/> (its material, layer, physics material), with this mesh.</summary>
        private Transform RoundPart(Transform part, string name, Transform like, Mesh mesh, bool convex)
        {
            if (part == null)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.transform.SetParent(_tableRoot, false);
                part = go.transform;
                if (like != null)
                {
                    go.layer = like.gameObject.layer;
                    go.tag = like.gameObject.tag;
                    go.isStatic = true;
                    var source = like.GetComponent<Renderer>();
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = source.sharedMaterial;
                    renderer.shadowCastingMode = source.shadowCastingMode;
                    renderer.receiveShadows = source.receiveShadows;
                    renderer.renderingLayerMask = source.renderingLayerMask;
                    go.GetComponent<MeshCollider>().sharedMaterial = like.GetComponent<Collider>()?.sharedMaterial;
                }
            }
            part.gameObject.SetActive(true);
            part.localPosition = Vector3.zero;
            part.localRotation = Quaternion.identity;
            part.localScale = Vector3.one;
            var filter = part.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null)
                Destroy(filter.sharedMesh);
            filter.sharedMesh = mesh;
            var collider = part.GetComponent<MeshCollider>();
            collider.convex = convex;
            collider.sharedMesh = mesh;
            return part;
        }

        private void DressTable(TableLookDef look)
        {
            if (_tableRoot == null)
                return;
            foreach (var renderer in _tableRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "Surface" || renderer.name == RoundSurfaceName)
                {
                    var felt = renderer.material;
                    // The tavern's table is bare planks unless the game asks for a cloth (a colour or an image).
                    var planks = _tavern && look.FeltAsset == null && look.FeltColor == null ? Resources.Load<Texture2D>(TavernTextures + "planks") : null;
                    felt.color = ToColor(look.FeltColor, look.FeltAsset != null ? Color.white : planks != null ? TavernTabletop : _tavern ? TavernFelt : DefaultFelt);
                    felt.mainTexture = look.FeltAsset != null ? _table.Assets?.Texture(look.FeltAsset) : planks;
                    felt.mainTextureScale = planks != null ? new Vector2(TableGeometry.Size.x / 0.6f, TableGeometry.Size.y / 0.6f) : Vector2.one;
                }
                else if (renderer.name.StartsWith("Rim"))
                {
                    renderer.material.color = ToColor(look.WoodColor, _tavern ? TavernWood : DefaultWood);
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

        /// <summary>A flat or tall round part of the room (a rug, a pedestal): a cylinder <paramref name="height"/> tall, centred on <paramref name="centre"/>.</summary>
        private GameObject Disc(string name, Vector3 centre, float radius, float height, Color color)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(_room.transform, false);
            disc.transform.position = centre;
            // Unity's cylinder is 2 units tall and 1 across.
            disc.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            disc.GetComponent<Renderer>().sharedMaterial = NewMaterial(color, 0.25f);
            disc.GetComponent<Renderer>().renderingLayerMask = RoomLayers;
            return disc;
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
