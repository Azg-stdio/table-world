using Core.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace TableWorld.View
{
    /// <summary>
    /// While this player carries a piece a zone catches (M12, <see cref="Zone.Catch"/>), a beam of light stands on that
    /// zone and a soft pool of light lies on it, pulsing: let go anywhere but your hand and the piece goes there. Seen by
    /// this player alone (it is the view's, not the table's).
    /// </summary>
    public sealed class CatchGuide : MonoBehaviour
    {
        public const float BeamHeight = 0.9f;
        public static readonly Color Glow = new Color(1f, 0.82f, 0.45f);

        private TableView _table;
        private TableInteraction _interaction;
        private GameObject _guide;
        private Transform _beam;
        private Light _light;
        private Material _beamMaterial, _poolMaterial;

        /// <summary>The zone lit now, or null (tests read it).</summary>
        public string ShownZone { get; private set; }

        private void Awake()
        {
            _table = GetComponent<TableView>();
            _interaction = GetComponent<TableInteraction>();
        }

        private void LateUpdate()
        {
            var held = _interaction != null ? _interaction.Held : null;
            var zone = held != null && _table.Game != null && !_table.Spectating ? _table.Game.CatchingZone(held.Id, _table.LocalSeat) : null;
            if (zone == null)
            {
                if (_guide != null && _guide.activeSelf)
                    _guide.SetActive(false);
                ShownZone = null;
                return;
            }
            if (_guide == null)
                Build();
            if (ShownZone != zone.Id)
                Place(zone);
            _guide.SetActive(true);
            ShownZone = zone.Id;
            // A slow breath of light, so the eye finds it without it shouting.
            var pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3.2f);
            _light.intensity = 2.2f * pulse;
            _beamMaterial.SetColor("_BaseColor", Glow * (0.55f * pulse));
            _poolMaterial.SetColor("_BaseColor", Glow * (0.9f * pulse));
        }

        private void Place(Zone zone)
        {
            var p = zone.Transform.Position;
            var size = Mathf.Max(zone.Size.X * zone.Transform.Scale.X, zone.Size.Z * zone.Transform.Scale.Z);
            var parent = _table.ObjectsRoot != null ? _table.ObjectsRoot : _table.transform;
            _guide.transform.SetParent(parent, false);
            _guide.transform.localPosition = new Vector3(p.X, 0f, p.Z);
            _beam.localScale = new Vector3(size * 0.9f, BeamHeight / 2f, size * 0.9f);
            _beam.localPosition = new Vector3(0f, BeamHeight / 2f, 0f);
            _pool.localScale = new Vector3(size * 1.5f, size * 1.5f, 1f);
            _light.spotAngle = Mathf.Clamp(Mathf.Atan2(size, 0.6f) * Mathf.Rad2Deg * 2.4f, 12f, 60f);
        }

        private Transform _pool;

        private void Build()
        {
            _guide = new GameObject("Catch Guide");
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(_guide.transform, false);
            _beam = beam.transform;
            _beamMaterial = Additive(BeamTexture(), "Catch beam");
            var beamRenderer = beam.GetComponent<Renderer>();
            beamRenderer.sharedMaterial = _beamMaterial;
            beamRenderer.shadowCastingMode = ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;

            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pool.name = "Pool";
            Destroy(pool.GetComponent<Collider>());
            pool.transform.SetParent(_guide.transform, false);
            pool.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _pool = pool.transform;
            _poolMaterial = Additive(PoolTexture(), "Catch pool");
            var poolRenderer = pool.GetComponent<Renderer>();
            poolRenderer.sharedMaterial = _poolMaterial;
            poolRenderer.shadowCastingMode = ShadowCastingMode.Off;
            poolRenderer.receiveShadows = false;

            var lightGo = new GameObject("Catch Light");
            lightGo.transform.SetParent(_guide.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.color = Glow;
            _light.range = 1.2f;
            _light.shadows = LightShadows.None;
            // Onto the pieces and the felt, like the table light.
            var data = UnityEngine.Rendering.Universal.LightExtensions.GetUniversalAdditionalLightData(_light);
            data.renderingLayers = EnvironmentView.PiecesLayers;
        }

        private static Material Additive(Texture texture, string name)
        {
            var material = new Material(RequiredShaders.Unlit) { name = name };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        /// <summary>The beam's skin: bright at the foot, fading to nothing at the top (a cylinder's UV runs up its side).</summary>
        private static Texture2D BeamTexture()
        {
            const int h = 64;
            var texture = new Texture2D(4, h, TextureFormat.RGBA32, false) { name = "Catch beam", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < h; y++)
            {
                var v = y / (float)(h - 1);
                var a = Mathf.Pow(1f - v, 1.8f);
                for (var x = 0; x < 4; x++)
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A soft round pool of light with a brighter ring at its edge.</summary>
        private static Texture2D PoolTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Catch pool", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                var fill = Mathf.Clamp01(1f - r) * 0.5f;
                var ring = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.08f, 2f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(fill + ring) * (r < 1f ? 1f : 0f)));
            }
            texture.Apply(false, true);
            return texture;
        }
    }
}
