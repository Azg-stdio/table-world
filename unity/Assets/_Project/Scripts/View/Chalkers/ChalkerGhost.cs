using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// Your own chalker seen from the lifted camera (docs/dev/camera.md): where it stands between you and the table it
    /// fades to <see cref="SeeThrough"/>, and a rim in your seat's colour stays at full strength around it so you still
    /// know which one is you. Only your own, only on your screen. Lit materials are drawn with <c>TableWorld/Ghost</c>
    /// (depth first, then Lit made transparent: no inner parts showing through), others are made transparent as they are.
    /// </summary>
    public sealed class ChalkerGhost
    {
        public const string ShaderName = "TableWorld/Ghost";
        /// <summary>How much of the chalker still shows where it covers the table.</summary>
        public const float SeeThrough = 0.3f;
        /// <summary>How fast it fades in and out, in alpha per second.</summary>
        public const float FadeRate = 4f;
        /// <summary>The rim's width, in metres.</summary>
        public const float RimWidth = 0.004f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int CenterId = Shader.PropertyToID("_Center");

        private readonly GameObject _root;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly Dictionary<Renderer, Material[]> _originals = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> _ghosts = new Dictionary<Material, Material>();
        private readonly Dictionary<Renderer, (GameObject Copy, MeshFilter Filter, MeshRenderer Renderer, Mesh Baked)> _rims =
            new Dictionary<Renderer, (GameObject, MeshFilter, MeshRenderer, Mesh)>();
        private Material _rim;
        private MaterialPropertyBlock _block;
        private float _alpha = 1f;

        public ChalkerGhost(GameObject root) => _root = root;

        /// <summary>How much of the chalker shows now: 1 drawn as usual, down to <see cref="SeeThrough"/>.</summary>
        public float Alpha => _alpha;

        /// <summary>Fades toward see-through (<paramref name="covering"/>) or solid, the rim in <paramref name="color"/> while faded.</summary>
        public void Update(bool covering, Color color, float dt)
        {
            _alpha = Mathf.MoveTowards(_alpha, covering ? SeeThrough : 1f, FadeRate * dt);
            if (_alpha >= 1f)
            {
                Restore();
                return;
            }
            _root.GetComponentsInChildren(false, _renderers);
            foreach (var renderer in _renderers)
            {
                if (renderer.GetComponent<RimCopy>() != null || renderer is ParticleSystemRenderer)
                    continue;
                if (!_originals.TryGetValue(renderer, out var originals))
                {
                    originals = renderer.sharedMaterials;
                    _originals[renderer] = originals;
                    var ghosts = new Material[originals.Length];
                    for (var i = 0; i < originals.Length; i++)
                        ghosts[i] = Ghost(originals[i]);
                    renderer.sharedMaterials = ghosts;
                }
                foreach (var original in originals)
                    if (original != null && _ghosts.TryGetValue(original, out var ghost))
                        Follow(original, ghost);
            }
            Rims(color);
        }

        /// <summary>Back to the opaque materials, without a rim.</summary>
        public void Restore()
        {
            _alpha = 1f;
            foreach (var pair in _originals)
                if (pair.Key != null)
                    pair.Key.sharedMaterials = pair.Value;
            _originals.Clear();
            foreach (var rim in _rims.Values)
            {
                if (rim.Copy != null)
                    Object.Destroy(rim.Copy);
                if (rim.Baked != null)
                    Object.Destroy(rim.Baked);
            }
            _rims.Clear();
        }

        public void Destroy()
        {
            Restore();
            foreach (var ghost in _ghosts.Values)
                Object.Destroy(ghost);
            _ghosts.Clear();
            if (_rim != null)
                Object.Destroy(_rim);
        }

        private Material Ghost(Material original)
        {
            if (original == null)
                return null;
            if (_ghosts.TryGetValue(original, out var ghost))
                return ghost;
            var shader = original.shader != null && original.shader.name == RequiredShaders.LitName ? Shader.Find(ShaderName) : null;
            ghost = shader != null ? new Material(shader) : new Material(original);
            ghost.name = original.name + " (ghost)";
            ghost.shaderKeywords = original.shaderKeywords;
            _ghosts[original] = ghost;
            return ghost;
        }

        /// <summary>The ghost takes the original's look this frame (a recoloured skin included), made transparent at <see cref="Alpha"/>.</summary>
        private void Follow(Material original, Material ghost)
        {
            ghost.CopyMatchingPropertiesFromMaterial(original);
            var color = original.HasProperty(BaseColorId) ? original.GetColor(BaseColorId) : Color.white;
            color.a *= _alpha;
            ghost.SetColor(BaseColorId, color);
            ghost.SetFloat("_Surface", 1f);
            ghost.SetFloat("_Blend", 0f);
            ghost.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            ghost.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ghost.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            ghost.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            ghost.SetFloat("_ZWrite", 0f);
            ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghost.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            ghost.renderQueue = (int)RenderQueue.Transparent;
            ghost.SetOverrideTag("RenderType", "Transparent");
        }

        /// <summary>
        /// The rim around every visible part, drawn with the selection outline's shader on a copy of its mesh (a skinned
        /// model's baked each frame, as it moves).
        /// </summary>
        private void Rims(Color color)
        {
            if (_rim == null)
            {
                _rim = new Material(Shader.Find(Outline.ShaderName) ?? RequiredShaders.Unlit) { name = "Ghost rim" };
                // Behind what the chalker holds, unlike a selection's outline.
                _rim.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            }
            _block ??= new MaterialPropertyBlock();
            foreach (var renderer in _renderers)
            {
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer) || renderer.GetComponent<RimCopy>() != null)
                    continue;
                var shown = renderer.enabled && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
                if (!_rims.TryGetValue(renderer, out var rim) || rim.Copy == null)
                {
                    var go = new GameObject("Ghost rim");
                    go.AddComponent<RimCopy>();
                    go.transform.SetParent(renderer.transform, false);
                    var filter = go.AddComponent<MeshFilter>();
                    var copy = go.AddComponent<MeshRenderer>();
                    copy.shadowCastingMode = ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                    rim = (go, filter, copy, renderer is SkinnedMeshRenderer ? new Mesh { name = "Ghost rim" } : null);
                    _rims[renderer] = rim;
                }
                rim.Copy.SetActive(shown);
                if (!shown)
                    continue;
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // Baked as it is in the world's units: the copy undoes its renderer's scale.
                    skinned.BakeMesh(rim.Baked);
                    mesh = rim.Baked;
                    var scale = renderer.transform.lossyScale;
                    rim.Copy.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
                }
                else
                    mesh = renderer.TryGetComponent(out MeshFilter own) ? own.sharedMesh : null;
                if (mesh == null)
                {
                    rim.Copy.SetActive(false);
                    continue;
                }
                rim.Filter.sharedMesh = mesh;
                var current = rim.Renderer.sharedMaterials;
                if (current.Length != mesh.subMeshCount || System.Array.Exists(current, m => m != _rim))
                {
                    var slots = new Material[mesh.subMeshCount];
                    for (var i = 0; i < slots.Length; i++)
                        slots[i] = _rim;
                    rim.Renderer.sharedMaterials = slots;
                }
                _block.SetColor(BaseColorId, color);
                _block.SetFloat(WidthId, RimWidth);
                _block.SetVector(CenterId, mesh.bounds.center);
                rim.Renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>Marks a rim's own renderer, so it is neither faded nor given a rim of its own.</summary>
        private sealed class RimCopy : MonoBehaviour
        {
        }
    }
}
