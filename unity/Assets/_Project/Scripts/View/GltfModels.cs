using System.Collections.Generic;
using System.Linq;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// glTF and GLB models (M4 T5.1, glTFast): each file is loaded once per session, in the background, into a hidden
    /// template that <see cref="Attach"/> copies onto every object showing it. A file over <see cref="MaxBytes"/> or
    /// <see cref="MaxTriangles"/>, or one that does not load, keeps the object's placeholder. Materials are plain URP Lit
    /// (or Unlit) built by <see cref="LitMaterials"/>, not glTFast's shader graphs, so a build needs no extra shaders.
    /// </summary>
    public static class GltfModels
    {
        /// <summary>The largest file loaded, in bytes.</summary>
        public const long MaxBytes = 64L * 1024 * 1024;
        /// <summary>The most triangles one model may have (a detailed figure is 20–100 thousand).</summary>
        public const int MaxTriangles = 500_000;
        /// <summary>
        /// Up to this many parts, each part gets its own convex collider; a model with more (a whole scene in one file)
        /// gets one box around it instead, so physics stays cheap.
        /// </summary>
        public const int MaxColliders = 24;

        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> Loading = new HashSet<string>();
        private static readonly List<GltfImport> Imports = new List<GltfImport>();
        private static GameObject _holder;
        /// <summary>Counts play sessions, so a load finishing after the session ended is dropped.</summary>
        private static int _session;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _session++;
            foreach (var import in Imports)
                import.Dispose();
            Imports.Clear();
            Templates.Clear();
            Loading.Clear();
            _holder = null;
        }

        /// <summary>True for the file names this loads (.glb, .gltf).</summary>
        public static bool Handles(string path) =>
            path != null && (path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The model's template once loaded; null while it loads, when it is missing, or when it did not load. The first
        /// call starts loading <paramref name="file"/> (or else <paramref name="bytes"/>); <paramref name="loaded"/> runs
        /// when the template is ready, to redraw the objects that show it. <paramref name="key"/> names the file.
        /// </summary>
        public static GameObject Template(string key, string file, System.Func<byte[]> bytes, System.Action loaded)
        {
            if (Templates.TryGetValue(key, out var known))
                return known;
            if (Loading.Contains(key))
                return null;
            var local = file != null && System.IO.File.Exists(file);
            byte[] data = null;
            if (!local)
            {
                data = bytes?.Invoke();
                if (data == null)
                {
                    // A client still downloading it tries again when it arrives; without anyone to ask, it is missing.
                    if (bytes == null)
                    {
                        Debug.LogWarning($"[GltfModels] Missing model '{key}'; showing a placeholder.");
                        Templates[key] = null;
                    }
                    return null;
                }
            }
            var size = local ? new System.IO.FileInfo(file).Length : data.LongLength;
            if (size > MaxBytes)
            {
                Debug.LogWarning($"[GltfModels] '{key}' is {size / (1024 * 1024)} MB, over the {MaxBytes / (1024 * 1024)} MB a model may be; showing a placeholder.");
                Templates[key] = null;
                return null;
            }
            Loading.Add(key);
            Load(key, local ? file : null, data, loaded);
            return null;
        }

        private static async void Load(string key, string file, byte[] data, System.Action loaded)
        {
            var session = _session;
            var import = new GltfImport(materialGenerator: new LitMaterials(), logger: new ConsoleLogger());
            var settings = new ImportSettings { AnimationMethod = AnimationMethod.None, GenerateMipMaps = true, AnisotropicFilterLevel = 4 };
            GameObject template = null;
            try
            {
                // A .gltf names its buffers and images by relative paths: loading it from its file finds them.
                var ok = file != null
                    ? await import.Load(new System.Uri(System.IO.Path.GetFullPath(file)), settings)
                    : await import.Load(data, null, settings);
                if (session != _session)
                {
                    import.Dispose();
                    return;
                }
                if (ok)
                {
                    if (_holder == null)
                    {
                        _holder = new GameObject("glTF templates");
                        _holder.SetActive(false);
                        Object.DontDestroyOnLoad(_holder);
                    }
                    var root = new GameObject("glTF " + System.IO.Path.GetFileName(key));
                    root.transform.SetParent(_holder.transform, false);
                    if (await import.InstantiateMainSceneAsync(root.transform) && session == _session)
                        template = Prepare(key, root);
                    else if (root != null)
                        Object.Destroy(root);
                }
                else
                    Debug.LogWarning($"[GltfModels] '{key}' does not load as glTF; showing a placeholder.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GltfModels] '{key}' does not load ({e.Message}); showing a placeholder.");
            }
            if (session != _session)
                return;
            Loading.Remove(key);
            Templates[key] = template;
            if (template != null)
                Imports.Add(import);
            else
                import.Dispose();
            loaded?.Invoke();
        }

        /// <summary>Checks the model's size, gives it colliders, and returns it as the template (or null when refused).</summary>
        private static GameObject Prepare(string key, GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToList();
            var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).ToList();
            long triangles = 0;
            foreach (var mesh in filters.Select(f => f.sharedMesh).Concat(skinned.Select(r => r.sharedMesh)))
                for (var i = 0; i < mesh.subMeshCount; i++)
                    triangles += mesh.GetIndexCount(i) / 3;
            if (triangles > MaxTriangles)
            {
                Debug.LogWarning($"[GltfModels] '{key}' has {triangles:N0} triangles, over the {MaxTriangles:N0} a model may have; showing a placeholder.");
                Object.Destroy(root);
                return null;
            }
            if (filters.Count == 0 && skinned.Count == 0)
            {
                Debug.LogWarning($"[GltfModels] '{key}' has no mesh to show; showing a placeholder.");
                Object.Destroy(root);
                return null;
            }
            // Each part is part of the object's one rigidbody: convex, as PhysX needs for a moving body. Skinned parts and
            // models with many parts get the box the object adds around a model without colliders.
            if (skinned.Count == 0 && filters.Count <= MaxColliders)
                foreach (var filter in filters)
                    filter.gameObject.AddComponent<MeshCollider>().convex = true;
            return root;
        }

        /// <summary>
        /// Puts a copy of <paramref name="template"/> on the object, <paramref name="scale"/> metres per unit of the file
        /// (glTF is in metres: 1). Returns the copy, named "Bundle" like an asset bundle's so refreshes keep it.
        /// </summary>
        public static GameObject Attach(Transform parent, GameObject template, float scale)
        {
            var copy = Object.Instantiate(template, parent, false);
            copy.name = "Bundle";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one * scale;
            copy.SetActive(true);
            return copy;
        }

        /// <summary>
        /// glTF materials as URP Lit (Unlit for KHR_materials_unlit): base colour and texture, normal map, metallic and
        /// roughness factors, emission, cutout and blended alpha, double sides.
        /// </summary>
        private sealed class LitMaterials : IMaterialGenerator
        {
            private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
            private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
            private Material _default;

            public Material GetDefaultMaterial(bool pointsSupport = false) =>
                _default != null ? _default : _default = new Material(RequiredShaders.Lit) { name = "glTF default" };

            public void SetLogger(ICodeLogger logger) { }

            public Material GenerateMaterial(GLTFast.Schema.MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
            {
                var unlit = gltfMaterial.Extensions?.KHR_materials_unlit != null;
                var material = new Material(unlit ? RequiredShaders.Unlit : RequiredShaders.Lit) { name = gltfMaterial.name ?? "glTF" };
                var pbr = gltfMaterial.PbrMetallicRoughness;
                material.SetColor(BaseColor, pbr?.BaseColor ?? Color.white);
                if (Texture(gltf, pbr?.BaseColorTexture, out var flip) is { } baseMap)
                {
                    material.SetTexture(BaseMap, baseMap);
                    if (flip)
                    {
                        material.SetTextureScale(BaseMap, new Vector2(1f, -1f));
                        material.SetTextureOffset(BaseMap, new Vector2(0f, 1f));
                    }
                }
                if (!unlit)
                {
                    material.SetFloat("_Metallic", pbr?.metallicFactor ?? 0f);
                    material.SetFloat("_Smoothness", 1f - (pbr?.roughnessFactor ?? 1f));
                    if (Texture(gltf, gltfMaterial.NormalTexture, out _) is { } normal)
                    {
                        material.SetTexture("_BumpMap", normal);
                        material.EnableKeyword("_NORMALMAP");
                    }
                    var emissive = gltfMaterial.Emissive;
                    var emissionMap = Texture(gltf, gltfMaterial.EmissiveTexture, out _);
                    if (emissive.maxColorComponent > 0f)
                    {
                        material.SetColor("_EmissionColor", emissive);
                        if (emissionMap != null)
                            material.SetTexture("_EmissionMap", emissionMap);
                        material.EnableKeyword("_EMISSION");
                    }
                }
                // Glass (KHR_materials_transmission) has no refraction in URP Lit: drawn see-through and glossy instead.
                var transmission = gltfMaterial.Extensions?.KHR_materials_transmission?.transmissionFactor ?? 0f;
                var mode = gltfMaterial.GetAlphaMode();
                if (transmission > 0f && !unlit)
                {
                    mode = GLTFast.Schema.MaterialBase.AlphaMode.Blend;
                    var tint = material.GetColor(BaseColor);
                    tint.a *= 1f - 0.75f * transmission;
                    material.SetColor(BaseColor, tint);
                    material.SetFloat("_Smoothness", Mathf.Max(material.GetFloat("_Smoothness"), 0.9f));
                }
                switch (mode)
                {
                    case GLTFast.Schema.MaterialBase.AlphaMode.Mask:
                        material.SetFloat("_AlphaClip", 1f);
                        material.SetFloat("_Cutoff", gltfMaterial.alphaCutoff);
                        material.EnableKeyword("_ALPHATEST_ON");
                        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                        break;
                    case GLTFast.Schema.MaterialBase.AlphaMode.Blend:
                        material.SetFloat("_Surface", 1f);
                        material.SetOverrideTag("RenderType", "Transparent");
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_ZWrite", 0f);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        break;
                }
                if (gltfMaterial.doubleSided)
                    material.SetFloat("_Cull", 0f);
                return material;
            }

            private static Texture2D Texture(IGltfReadable gltf, GLTFast.Schema.TextureInfoBase info, out bool flip)
            {
                flip = false;
                if (info == null || info.index < 0 || gltf.GetSourceTexture(info.index) == null)
                    return null;
                flip = gltf.IsTextureYFlipped(info.index);
                return gltf.GetTexture(info.index);
            }
        }
    }
}
