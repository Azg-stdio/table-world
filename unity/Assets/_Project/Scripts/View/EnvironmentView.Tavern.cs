using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TableWorld.View
{
    /// <summary>
    /// The tavern room (M12, <see cref="Core.Rules.RoomPreset.Tavern"/>): the game night room in stone and wood, with a
    /// chandelier of candles over the table instead of the pendant lamp, candles on the walls, a fire in the hearth,
    /// banners and barrels. Candles and the fire flicker (their lights and flames, see <see cref="StepLights"/>); only the
    /// lamp and the table light cast shadows. Built from primitives and the CC0 textures in
    /// <c>Art/ThirdParty/AquilariusRetroTextures/Resources/Tavern</c>; without them the room keeps plain colours.
    /// </summary>
    public sealed partial class EnvironmentView
    {
        public static readonly Color TavernWall = new Color(0.62f, 0.55f, 0.5f);
        public static readonly Color TavernFloor = new Color(0.55f, 0.45f, 0.38f);
        public static readonly Color TavernAmbient = new Color(0.7f, 0.56f, 0.44f);
        public const float TavernAmbientIntensity = 0.3f;
        public static readonly Color TavernLamp = new Color(1f, 0.72f, 0.45f);
        public const float TavernLampIntensity = 2.8f;
        public static readonly Color TavernTableLight = new Color(1f, 0.95f, 0.88f);
        public const float TavernTableLightIntensity = 2.45f;
        public static readonly Color TavernFelt = new Color(0.3f, 0.07f, 0.07f);
        /// <summary>The tavern's tabletop: dark planks under the pieces (a game's felt colour or image replaces them).</summary>
        public static readonly Color TavernTabletop = new Color(0.7f, 0.52f, 0.38f);
        public static readonly Color TavernFill = new Color(1f, 0.66f, 0.4f);
        public const float TavernFillIntensity = 1.1f;
        public static readonly Color TavernWood = new Color(0.25f, 0.15f, 0.08f);

        public const string TavernTextures = "Tavern/";
        /// <summary>Height of the chandelier's ring over the table, above the eyes of anyone seated (metres).</summary>
        public const float ChandelierHeight = 1.32f;
        public const int ChandelierCandles = 8;
        public const float SconceIntensity = 0.55f;
        public const float FireIntensity = 1.6f;

        private static readonly Color Iron = new Color(0.07f, 0.065f, 0.06f);
        private static readonly Color Wax = new Color(0.86f, 0.8f, 0.66f);
        private static readonly Color CandleLight = new Color(1f, 0.66f, 0.36f);
        private static readonly Color FireLight = new Color(1f, 0.5f, 0.2f);

        private bool _tavern;
        private static Texture2D _flameTexture;
        private Material _flameMaterial;
        private readonly List<Transform> _flames = new List<Transform>();

        /// <summary>The flames on screen (tests count them).</summary>
        public IReadOnlyList<Transform> Flames => _flames;

        private void BuildTavern(Color wall, Color floor)
        {
            _room.name = "Room (tavern)";
            var h = RoomHalfSize;
            var stone = Resources.Load<Texture2D>(TavernTextures + "wall-brick");
            var planks = Resources.Load<Texture2D>(TavernTextures + "planks");
            var beams = Resources.Load<Texture2D>(TavernTextures + "beams");
            var cobbles = Resources.Load<Texture2D>(TavernTextures + "cobbles");

            // The walls, floor and ceiling of the game night room, dressed in stone and wood (a tile about every metre).
            foreach (Transform part in _room.transform)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                // Stone, plaster and old wood hardly shine: a glossy floor mirrored the dark room in blue-grey.
                renderer.sharedMaterial.SetFloat("_Smoothness", 0.04f);
                switch (part.name)
                {
                    case "Floor":
                        Dress(renderer, planks, new Vector2(h.x * 2f / 1.2f, h.z * 2f / 1.2f));
                        break;
                    case "Ceiling":
                        Dress(renderer, beams, new Vector2(h.x * 2f / 1.5f, h.z * 2f / 1.5f));
                        renderer.sharedMaterial.color = new Color(0.32f, 0.26f, 0.22f);
                        break;
                    case "Wall North":
                    case "Wall South":
                        Dress(renderer, stone, new Vector2(h.x * 2f, CeilingY - FloorY));
                        break;
                    case "Wall East":
                    case "Wall West":
                        Dress(renderer, stone, new Vector2(h.z * 2f, CeilingY - FloorY));
                        break;
                    case "Rug":
                        renderer.sharedMaterial.color = new Color(0.2f, 0.07f, 0.05f);
                        break;
                }
            }

            // A wooden wainscot round the room, waist high.
            const float wainscot = 0.95f;
            var wood = new Color(0.45f, 0.34f, 0.26f);
            var wy = FloorY + wainscot / 2f;
            Dress(Box("Wainscot North", new Vector3(0f, wy, h.z - 0.03f), new Vector3(h.x * 2f, wainscot, 0.05f), wood, 0.2f), planks, new Vector2(h.x * 2f, 1f));
            Dress(Box("Wainscot South", new Vector3(0f, wy, -h.z + 0.03f), new Vector3(h.x * 2f, wainscot, 0.05f), wood, 0.2f), planks, new Vector2(h.x * 2f, 1f));
            Dress(Box("Wainscot East", new Vector3(h.x - 0.03f, wy, 0f), new Vector3(0.05f, wainscot, h.z * 2f), wood, 0.2f), planks, new Vector2(h.z * 2f, 1f));
            Dress(Box("Wainscot West", new Vector3(-h.x + 0.03f, wy, 0f), new Vector3(0.05f, wainscot, h.z * 2f), wood, 0.2f), planks, new Vector2(h.z * 2f, 1f));
            Box("Wainscot Rail", new Vector3(0f, FloorY + wainscot + 0.02f, h.z - 0.05f), new Vector3(h.x * 2f, 0.04f, 0.09f), wood * 0.6f, 0.3f);
            Box("Wainscot Rail", new Vector3(0f, FloorY + wainscot + 0.02f, -h.z + 0.05f), new Vector3(h.x * 2f, 0.04f, 0.09f), wood * 0.6f, 0.3f);

            // Beams across the ceiling.
            for (var i = -2; i <= 2; i++)
                Box("Beam", new Vector3(i * h.x * 0.38f, CeilingY - 0.13f, 0f), new Vector3(0.22f, 0.26f, h.z * 2f), new Color(0.16f, 0.1f, 0.06f), 0.15f);

            BuildChandelier();
            foreach (var light in _lights)
                if (light.Kind == LightKind.Fill)
                {
                    light.Color = TavernFill;
                    light.Intensity = TavernFillIntensity;
                    light.Light.range = 9f;
                }

            // Candles on the walls: two on each long wall, one on the wall without the hearth.
            var sconces = new List<(Vector3 at, Vector3 inward)>
            {
                (new Vector3(-h.x * 0.45f, 0.95f, h.z), Vector3.back), (new Vector3(h.x * 0.45f, 0.95f, h.z), Vector3.back),
                (new Vector3(-h.x * 0.45f, 0.95f, -h.z), Vector3.forward), (new Vector3(h.x * 0.45f, 0.95f, -h.z), Vector3.forward),
                (new Vector3(-h.x, 0.95f, 0f), Vector3.right),
            };
            foreach (var (at, inward) in sconces)
                BuildSconce(at, inward);

            BuildHearth(new Vector3(h.x, FloorY, 0f), Vector3.left, cobbles);

            // Banners in deep colours either side of the long walls' middle, and barrels in two corners.
            foreach (var (x, z, color, facing) in new[]
            {
                // A quad shows its -Z side: unturned it faces into the room from the north (+Z) wall.
                (-h.x * 0.15f, h.z - 0.07f, new Color(0.42f, 0.06f, 0.06f), 0f), (h.x * 0.15f, h.z - 0.07f, new Color(0.07f, 0.18f, 0.12f), 0f),
                (-h.x * 0.15f, -h.z + 0.07f, new Color(0.07f, 0.12f, 0.3f), 180f), (h.x * 0.15f, -h.z + 0.07f, new Color(0.42f, 0.06f, 0.06f), 180f),
            })
                BuildBanner(new Vector3(x, 1.25f, z), facing, color);
            foreach (var (x, z) in new[] { (-h.x + 0.45f, h.z - 0.45f), (-h.x + 1.05f, h.z - 0.4f), (-h.x + 0.5f, -h.z + 0.5f) })
                BuildBarrel(new Vector3(x, FloorY, z));
        }

        /// <summary>A texture tiled <paramref name="tiles"/> times over a part; a missing texture leaves its colour.</summary>
        private static GameObject Dress(GameObject part, Texture2D texture, Vector2 tiles)
        {
            Dress(part.GetComponent<Renderer>(), texture, tiles);
            return part;
        }

        private static void Dress(Renderer renderer, Texture2D texture, Vector2 tiles)
        {
            if (texture == null || renderer == null)
                return;
            var material = renderer.sharedMaterial;
            material.mainTexture = texture;
            material.mainTextureScale = tiles;
        }

        private void BuildChandelier()
        {
            var root = new GameObject("Chandelier");
            root.transform.SetParent(_room.transform, false);
            _fixture = root;
            var centre = new Vector3(0f, ChandelierHeight, 0f);
            const float radius = 0.42f;
            const int segments = 16;
            for (var i = 0; i < segments; i++)
            {
                var angle = (i + 0.5f) * 360f / segments;
                var at = centre + Quaternion.Euler(0f, angle, 0f) * new Vector3(radius, 0f, 0f);
                var segment = Box("Chandelier Ring", at, new Vector3(0.025f, 0.03f, 2f * Mathf.PI * radius / segments + 0.01f), Iron, 0.4f);
                segment.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                segment.transform.SetParent(root.transform, true);
            }
            // Three chains up to the ceiling, and a short rod in the middle.
            for (var i = 0; i < 3; i++)
            {
                var foot = centre + Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(radius, 0f, 0f);
                var top = new Vector3(0f, CeilingY - 0.26f, 0f);
                var chain = Box("Chandelier Chain", (foot + top) / 2f, new Vector3(0.012f, 0.012f, Vector3.Distance(foot, top)), Iron, 0.3f);
                chain.transform.rotation = Quaternion.LookRotation(top - foot);
                chain.transform.SetParent(root.transform, true);
            }
            Box("Chandelier Hub", centre + Vector3.down * 0.04f, new Vector3(0.08f, 0.12f, 0.08f), Iron, 0.4f).transform.SetParent(root.transform, true);
            for (var i = 0; i < ChandelierCandles; i++)
            {
                var at = centre + Quaternion.Euler(0f, i * 360f / ChandelierCandles, 0f) * new Vector3(radius, 0f, 0f);
                Candle(at + Vector3.up * 0.015f, 0.11f, root.transform, 0.07f);
            }
            foreach (var part in root.GetComponentsInChildren<Renderer>())
                part.shadowCastingMode = ShadowCastingMode.Off;
            _fixtureParts.AddRange(root.GetComponentsInChildren<Renderer>(true));

            // The lamp is the chandelier's light: warm, flickering a little (all its candles together).
            foreach (var light in _lights)
                if (light.Kind == LightKind.Lamp)
                {
                    light.Flicker = 0.06f;
                    light.Speed = 4f;
                    light.Seed = 3.1f;
                }
        }

        /// <summary>A candle standing on <paramref name="at"/>, with its flame; returns the flame.</summary>
        private Transform Candle(Vector3 at, float height, Transform parent, float flameSize)
        {
            var candle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            candle.name = "Candle";
            Object.Destroy(candle.GetComponent<Collider>());
            candle.transform.SetParent(parent, false);
            candle.transform.position = at + Vector3.up * height / 2f;
            candle.transform.localScale = new Vector3(0.032f, height / 2f, 0.032f);
            var renderer = candle.GetComponent<Renderer>();
            renderer.sharedMaterial = NewMaterial(Wax, 0.35f);
            renderer.renderingLayerMask = RoomLayers;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return Flame(at + Vector3.up * (height + flameSize * 0.42f), flameSize, parent);
        }

        /// <summary>A flame: a soft teardrop that always faces the camera, glowing over a faint halo.</summary>
        private Transform Flame(Vector3 at, float size, Transform parent)
        {
            var flame = new GameObject("Flame").transform;
            flame.SetParent(parent, false);
            flame.position = at;
            foreach (var (scale, alpha) in new[] { (new Vector3(size * 0.55f, size, 1f), 1f), (new Vector3(size * 2.4f, size * 2.4f, 1f), 0.16f) })
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = alpha < 1f ? "Flame Halo" : "Flame Core";
                Object.Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(flame, false);
                quad.transform.localScale = scale;
                var renderer = quad.GetComponent<Renderer>();
                renderer.sharedMaterial = alpha < 1f ? HaloMaterial() : FlameMaterial();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.renderingLayerMask = RoomLayers;
            }
            _flames.Add(flame);
            return flame;
        }

        private Material _haloMaterial;

        private Material FlameMaterial()
        {
            if (_flameMaterial == null)
                // Several times white, so the room's bloom catches it (and nothing else on the table).
                _flameMaterial = Additive(FlameTexture(), new Color(4f, 3.4f, 2.4f, 1f), "Flame");
            return _flameMaterial;
        }

        private Material HaloMaterial()
        {
            if (_haloMaterial == null)
                _haloMaterial = Additive(HaloTexture(), new Color(1.4f, 0.75f, 0.35f, 0.45f), "Flame halo");
            return _haloMaterial;
        }

        /// <summary>An unlit additive material: light added over what is behind it, never darkening it.</summary>
        private static Material Additive(Texture texture, Color color, string name)
        {
            var material = new Material(RequiredShaders.Unlit) { name = name };
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        /// <summary>A flame drawn in code: a teardrop, white-yellow at its heart and orange at its edge, fading out.</summary>
        public static Texture2D FlameTexture()
        {
            if (_flameTexture != null)
                return _flameTexture;
            const int w = 32, hgt = 64;
            var texture = new Texture2D(w, hgt, TextureFormat.RGBA32, false) { name = "Flame", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * hgt];
            for (var y = 0; y < hgt; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (y + 0.5f) / hgt;
                var u = (x + 0.5f) / w * 2f - 1f;
                // Widest a third of the way up, rounded at the bottom, a point at the top.
                var width = v < 0.3f ? Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow((0.3f - v) / 0.3f, 2f))) : Mathf.Pow(1f - (v - 0.3f) / 0.7f, 0.8f);
                width *= 0.95f;
                var d = width <= 0.001f ? 2f : Mathf.Abs(u) / width;
                var a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a) * Mathf.Clamp01(1f - Mathf.Pow(v, 6f));
                var heat = Mathf.Clamp01(1f - d * 1.3f) * Mathf.Clamp01(1.2f - v);
                var colour = Color.Lerp(new Color(1f, 0.42f, 0.08f), new Color(1f, 0.95f, 0.75f), heat);
                pixels[y * w + x] = new Color(colour.r, colour.g, colour.b, a);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _flameTexture = texture;
            return texture;
        }

        private static Texture2D _haloTexture;

        private static Texture2D HaloTexture()
        {
            if (_haloTexture != null)
                return _haloTexture;
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Flame halo", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _haloTexture = texture;
            return texture;
        }

        /// <summary>Flames turn about the vertical to face the camera, so they never show as a thin edge.</summary>
        private void FaceFlames()
        {
            if (_flames.Count == 0 || _camera == null)
                return;
            var eye = _camera.transform.position;
            foreach (var flame in _flames)
            {
                if (flame == null)
                    continue;
                var to = flame.position - eye;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-6f)
                    flame.rotation = Quaternion.LookRotation(to, Vector3.up);
            }
        }

        private void BuildSconce(Vector3 wallPoint, Vector3 inward)
        {
            var root = new GameObject("Sconce").transform;
            root.SetParent(_room.transform, false);
            Box("Sconce Plate", wallPoint + inward * 0.02f, Abs(Quaternion.LookRotation(inward) * new Vector3(0.1f, 0.16f, 0.02f)), Iron, 0.3f).transform.SetParent(root, true);
            Box("Sconce Arm", wallPoint + inward * 0.1f + Vector3.down * 0.05f, Abs(Quaternion.LookRotation(inward) * new Vector3(0.025f, 0.025f, 0.16f)), Iron, 0.3f).transform.SetParent(root, true);
            Box("Sconce Cup", wallPoint + inward * 0.17f + Vector3.down * 0.04f, new Vector3(0.07f, 0.02f, 0.07f), Iron, 0.3f).transform.SetParent(root, true);
            var flame = Candle(wallPoint + inward * 0.17f + Vector3.down * 0.03f, 0.13f, root, 0.075f);

            var lightGo = new GameObject("Sconce Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = flame.position + inward * 0.1f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3.2f;
            light.shadows = LightShadows.None;
            SetLayers(light, RoomLayers);
            var entry = Register(light, LightKind.Flame, true, CandleLight, SconceIntensity);
            entry.Flicker = 0.16f;
            entry.Speed = 7f;
            entry.Seed = wallPoint.x * 1.3f + wallPoint.z * 0.7f;
            entry.Flame = flame;
            entry.FlameScale = flame.localScale;
            foreach (var part in root.GetComponentsInChildren<Renderer>())
                part.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>The hearth on a wall: a stone surround, a dark opening, logs and a fire whose light flickers slowly and deeply.</summary>
        private void BuildHearth(Vector3 foot, Vector3 inward, Texture2D stone)
        {
            var root = new GameObject("Hearth").transform;
            root.SetParent(_room.transform, false);
            var side = Vector3.Cross(Vector3.up, inward);
            var turn = Quaternion.LookRotation(inward);
            Vector3 Size(float across, float up, float deep) => Abs(turn * new Vector3(across, up, deep));
            var stoneColour = new Color(0.6f, 0.56f, 0.52f);
            const float width = 1.6f, height = 1.25f, depth = 0.5f, opening = 0.95f, openingHigh = 0.75f;
            var pillar = (width - opening) / 2f;
            foreach (var s in new[] { -1f, 1f })
                Dress(Box("Hearth Pillar", foot + inward * depth / 2f + side * s * (opening + pillar) / 2f + Vector3.up * height / 2f, Size(pillar, height, depth), stoneColour, 0.1f), stone, new Vector2(1f, 2f)).transform.SetParent(root, true);
            Dress(Box("Hearth Lintel", foot + inward * depth / 2f + Vector3.up * (openingHigh + (height - openingHigh) / 2f), Size(opening, height - openingHigh, depth), stoneColour, 0.1f), stone, new Vector2(2f, 1f)).transform.SetParent(root, true);
            Box("Hearth Mantel", foot + inward * (depth / 2f + 0.05f) + Vector3.up * (height + 0.04f), Size(width + 0.2f, 0.08f, depth + 0.12f), new Color(0.2f, 0.12f, 0.07f), 0.25f).transform.SetParent(root, true);
            Box("Hearth Back", foot + inward * 0.04f + Vector3.up * openingHigh / 2f, Size(opening, openingHigh, 0.05f), new Color(0.03f, 0.02f, 0.02f), 0f).transform.SetParent(root, true);
            Dress(Box("Hearth Floor", foot + inward * (depth / 2f + 0.1f) + Vector3.up * 0.03f, Size(width + 0.3f, 0.06f, depth + 0.3f), stoneColour * 0.8f, 0.1f), stone, new Vector2(2f, 1f)).transform.SetParent(root, true);

            // Logs, crossed, and the fire on them.
            var logColour = new Color(0.16f, 0.09f, 0.05f);
            foreach (var (offset, yaw) in new[] { (-0.12f, 70f), (0.12f, 110f), (0f, 90f) })
            {
                var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = "Hearth Log";
                Object.Destroy(log.GetComponent<Collider>());
                log.transform.SetParent(root, false);
                log.transform.position = foot + inward * (depth / 2f) + side * offset + Vector3.up * (offset == 0f ? 0.16f : 0.1f);
                log.transform.rotation = turn * Quaternion.Euler(0f, yaw, 90f);
                log.transform.localScale = new Vector3(0.09f, 0.28f, 0.09f);
                var renderer = log.GetComponent<Renderer>();
                renderer.sharedMaterial = NewMaterial(logColour, 0.1f);
                renderer.renderingLayerMask = RoomLayers;
            }
            Transform main = null;
            foreach (var (along, size, rise) in new[] { (0f, 0.42f, 0.2f), (-0.18f, 0.3f, 0.16f), (0.17f, 0.32f, 0.16f), (0.06f, 0.24f, 0.15f) })
            {
                var flame = Flame(foot + inward * (depth / 2f) + side * along + Vector3.up * (rise + size * 0.4f), size, root);
                main ??= flame;
            }

            var lightGo = new GameObject("Hearth Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = foot + inward * (depth + 0.35f) + Vector3.up * 0.45f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5.5f;
            light.shadows = LightShadows.None;
            SetLayers(light, RoomLayers);
            var entry = Register(light, LightKind.Flame, true, FireLight, FireIntensity);
            entry.Flicker = 0.24f;
            entry.Speed = 2.6f;
            entry.Seed = 11.7f;
            entry.Flame = main;
            entry.FlameScale = main.localScale;
            foreach (var part in root.GetComponentsInChildren<Renderer>())
                part.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildBanner(Vector3 top, float facing, Color colour)
        {
            var turn = Quaternion.Euler(0f, facing, 0f);
            var cloth = Quad("Banner", top + Vector3.down * 0.55f, turn, new Vector2(0.6f, 1.1f), colour, 0.05f);
            cloth.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Box("Banner Rod", top + Vector3.up * 0.01f + turn * Vector3.back * 0.01f, Abs(turn * new Vector3(0.72f, 0.025f, 0.025f)), new Color(0.25f, 0.16f, 0.07f), 0.3f);
            // A gold stripe across the bottom, in front of the cloth.
            var stripe = Quad("Banner Stripe", top + Vector3.down * 0.92f + turn * Vector3.back * 0.004f, turn, new Vector2(0.6f, 0.06f), new Color(0.72f, 0.55f, 0.2f), 0.4f);
            stripe.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildBarrel(Vector3 foot)
        {
            const float height = 0.8f, radius = 0.27f;
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            Object.Destroy(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(_room.transform, false);
            barrel.transform.position = foot + Vector3.up * height / 2f;
            barrel.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            var renderer = barrel.GetComponent<Renderer>();
            renderer.sharedMaterial = NewMaterial(new Color(0.36f, 0.22f, 0.12f), 0.2f);
            renderer.renderingLayerMask = RoomLayers;
            foreach (var y in new[] { 0.12f, height - 0.12f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                band.name = "Barrel Band";
                Object.Destroy(band.GetComponent<Collider>());
                band.transform.SetParent(barrel.transform.parent, false);
                band.transform.position = foot + Vector3.up * y;
                band.transform.localScale = new Vector3(radius * 2f + 0.012f, 0.02f, radius * 2f + 0.012f);
                var bandRenderer = band.GetComponent<Renderer>();
                bandRenderer.sharedMaterial = NewMaterial(Iron, 0.5f);
                bandRenderer.renderingLayerMask = RoomLayers;
            }
        }
    }
}
