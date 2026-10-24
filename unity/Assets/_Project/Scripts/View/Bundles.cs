using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// TTS AssetBundles (M4 T5.4, ADR 0001): loaded once per file for the whole session (Unity refuses to load the same
    /// bundle twice), their prefab instantiated on the object, and their built-in-pipeline materials remapped to URP so
    /// nothing renders pink. A bundle that does not load leaves the object's placeholder.
    /// </summary>
    public static class Bundles
    {
        /// <summary>TTS bundles are authored in TTS units; ours are metres.</summary>
        public const float Scale = Core.Tts.TtsUnits.TtsUnitsToMeters;

        private static readonly Dictionary<string, AssetBundle> Loaded = new Dictionary<string, AssetBundle>();
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<Material, Material> Remapped = new Dictionary<Material, Material>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>Fast Enter Play Mode keeps statics: start each play session with nothing loaded.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var bundle in Loaded.Values)
                if (bundle != null)
                    bundle.Unload(true);
            Loaded.Clear();
            Prefabs.Clear();
            Remapped.Clear();
            Reported.Clear();
            Pending.Clear();
        }

        /// <summary>
        /// The bundle's prefab (its first root GameObject), loaded from <paramref name="file"/> or else from
        /// <paramref name="bytes"/>; null when it does not load. <paramref name="key"/> names the bundle (its asset path).
        /// </summary>
        public static GameObject Prefab(string key, string file, System.Func<byte[]> bytes)
        {
            if (Prefabs.TryGetValue(key, out var known))
                return known;
            var local = file != null && System.IO.File.Exists(file);
            var data = local ? null : bytes?.Invoke();
            if (!local && data == null)
            {
                // A client still downloading it tries again when it arrives; without anyone to ask, it is missing.
                if (bytes == null)
                {
                    Debug.LogWarning($"[Bundles] Missing bundle '{key}'; showing a placeholder.");
                    Prefabs[key] = null;
                }
                return null;
            }
            // TTS bundles name TTS's own components and use Unity 5's file format: Unity warns about both on every load
            // (native warnings, which no log filter stops). Expected for TTS content; tools/unity/playmode_tests.py counts them apart.
            GameObject prefab = null;
            try
            {
                var bundle = local ? AssetBundle.LoadFromFile(file) : AssetBundle.LoadFromMemory(data);
                if (bundle != null)
                {
                    Loaded[key] = bundle;
                    prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault(g => g.transform.parent == null);
                    ResolvePending();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Bundles] '{key}' does not load ({e.Message}); showing a placeholder.");
            }
            if (prefab == null)
                Debug.LogWarning($"[Bundles] '{key}' has no model to show; showing a placeholder.");
            Prefabs[key] = prefab;
            return prefab;
        }

        /// <summary>
        /// Puts a copy of <paramref name="prefab"/> on the object: scaled to metres, materials remapped, its colliders
        /// made convex (the object is one rigidbody), its own rigidbodies removed. Returns the copy.
        /// </summary>
        public static GameObject Attach(Transform parent, GameObject prefab)
        {
            var copy = Object.Instantiate(prefab, parent, false);
            copy.name = "Bundle";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = prefab.transform.localScale * Scale;
            foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true))
                Object.Destroy(body);
            foreach (var mesh in copy.GetComponentsInChildren<MeshCollider>(true))
                mesh.convex = true;
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                // Materials can live in another bundle (TTS mods split models and materials): until that bundle loads
                // they are missing and would draw magenta, so the renderer waits hidden and is finished when it arrives.
                if (renderer.sharedMaterials.Any(m => m == null))
                {
                    renderer.enabled = false;
                    Pending.Add(renderer);
                    continue;
                }
                renderer.sharedMaterials = renderer.sharedMaterials.Select(Remap).ToArray();
            }
            return copy;
        }

        /// <summary>Renderers waiting for materials that live in a bundle not loaded yet.</summary>
        private static readonly List<Renderer> Pending = new List<Renderer>();

        /// <summary>A bundle arrived: renderers whose materials it holds are remapped and shown.</summary>
        private static void ResolvePending()
        {
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                var renderer = Pending[i];
                if (renderer == null)
                {
                    Pending.RemoveAt(i);
                    continue;
                }
                if (renderer.sharedMaterials.Any(m => m == null))
                    continue;
                renderer.sharedMaterials = renderer.sharedMaterials.Select(Remap).ToArray();
                renderer.enabled = true;
                Pending.RemoveAt(i);
            }
        }

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// A URP copy of a built-in-pipeline material: Standard and Legacy become URP/Lit, Unlit becomes URP/Unlit,
        /// particles URP/Particles/Unlit; texture, colour, normal map, emission, smoothness and transparency carried over.
        /// URP materials are kept. Shaders we do not know are remapped as Lit and reported once.
        /// </summary>
        public static Material Remap(Material source)
        {
            if (source == null)
                return null;
            var shader = source.shader;
            var name = shader != null ? shader.name : "";
            if (shader != null && shader.isSupported && name.StartsWith("Universal Render Pipeline"))
                return source;
            if (Remapped.TryGetValue(source, out var done))
                return done;

            var particles = name.Contains("Particle");
            var unlit = !particles && (name.StartsWith("Unlit") || name.Contains("/Unlit") || name.Contains("Unlit/"));
            if (!particles && !unlit && !(name.StartsWith("Standard") || name.StartsWith("Legacy Shaders") || name.StartsWith("Mobile")) && Reported.Add(name))
                Debug.Log($"[Bundles] Shader '{name}' is not one TTS ships; drawn as URP Lit.");
            var target = particles ? RequiredShaders.Particles : unlit ? RequiredShaders.Unlit : RequiredShaders.Lit;
            var material = new Material(target) { name = source.name + " (URP)" };

            var main = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            if (main != null)
            {
                material.SetTexture(BaseMap, main);
                material.SetTextureScale(BaseMap, source.GetTextureScale("_MainTex"));
                material.SetTextureOffset(BaseMap, source.GetTextureOffset("_MainTex"));
            }
            var color = source.HasProperty("_Color") ? source.GetColor("_Color")
                : source.HasProperty("_TintColor") ? source.GetColor("_TintColor") * 2f : Color.white;
            material.SetColor(BaseColor, color);
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") is { } normal && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            if (source.HasProperty("_Glossiness") && material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            if (source.HasProperty("_Metallic") && material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            if (source.IsKeywordEnabled("_EMISSION") && source.HasProperty("_EmissionColor") && material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                if (source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") is { } emission)
                    material.SetTexture("_EmissionMap", emission);
                material.EnableKeyword("_EMISSION");
            }

            // Standard's rendering mode: 1 cutout, 2 fade, 3 transparent; legacy shaders say it in their name.
            var mode = source.HasProperty("_Mode") ? Mathf.RoundToInt(source.GetFloat("_Mode")) : 0;
            var transparent = mode >= 2 || particles || name.Contains("Transparent") && !name.Contains("Cutout") || source.renderQueue >= 3000;
            var cutout = mode == 1 || name.Contains("Cutout");
            if (transparent)
            {
                var additive = name.Contains("Additive");
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", additive ? 2f : 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else if (cutout)
            {
                material.SetFloat("_AlphaClip", 1f);
                if (source.HasProperty("_Cutoff"))
                    material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            Remapped[source] = material;
            return material;
        }
    }
}
