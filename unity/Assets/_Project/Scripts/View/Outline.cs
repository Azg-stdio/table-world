using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// A coloured rim around an object's 3D shape (TTS outlines selected and held objects in the player's colour, G46),
    /// drawn over the scene around its silhouette. Each of the object's meshes (its own, a figure's stand, an asset bundle's parts) gets a copy drawn with the
    /// <c>TableWorld/Outline</c> shader. Several reasons can ask for an outline at once (this player's selection, another
    /// player holding it); the latest one shown wins, and the outline goes when none is left.
    /// </summary>
    public sealed class Outline : MonoBehaviour
    {
        public const string ShaderName = "TableWorld/Outline";
        public const float Width = 0.003f;

        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static Material _material;

        private readonly List<(string Reason, Color Color)> _reasons = new List<(string, Color)>();
        private readonly Dictionary<MeshFilter, (GameObject Copy, MeshFilter Filter, MeshRenderer Renderer)> _copies =
            new Dictionary<MeshFilter, (GameObject, MeshFilter, MeshRenderer)>();
        private MaterialPropertyBlock _block;
        private ObjectView _view;

        /// <summary>The colour drawn now, or null when nothing asks for an outline.</summary>
        public Color? Color => _reasons.Count > 0 ? _reasons[_reasons.Count - 1].Color : (Color?)null;

        /// <summary>Outlines <paramref name="view"/> in <paramref name="color"/> for <paramref name="reason"/>, or drops that reason with null.</summary>
        public static void Set(ObjectView view, string reason, Color? color)
        {
            if (view == null)
                return;
            var outline = view.GetComponent<Outline>();
            if (color == null)
            {
                if (outline == null)
                    return;
                outline._reasons.RemoveAll(r => r.Reason == reason);
                if (outline._reasons.Count == 0)
                    Destroy(outline);
                return;
            }
            if (outline == null)
                outline = view.gameObject.AddComponent<Outline>();
            outline._reasons.RemoveAll(r => r.Reason == reason);
            outline._reasons.Add((reason, color.Value));
        }

        public static Color? ColorOf(ObjectView view) => view != null && view.GetComponent<Outline>() is { } outline ? outline.Color : null;

        private void Awake() => _view = GetComponent<ObjectView>();

        /// <summary>The object's meshes now: its own, its stand, an asset bundle's parts (not its snap point markers).</summary>
        private IEnumerable<MeshFilter> Sources()
        {
            if (_view.Filter != null)
                yield return _view.Filter;
            if (_view.Base != null && _view.Base.GetComponent<MeshFilter>() is { } stand)
                yield return stand;
            if (transform.Find("Bundle") is { } bundle)
                foreach (var part in bundle.GetComponentsInChildren<MeshFilter>())
                    yield return part;
        }

        private void LateUpdate()
        {
            if (_view == null || Color is not { } color)
                return;
            if (_material == null)
                _material = new Material(Shader.Find(ShaderName) ?? RequiredShaders.Unlit) { name = "Outline" };
            _block ??= new MaterialPropertyBlock();
            var sources = Sources().Where(s => s != null).ToList();
            foreach (var gone in _copies.Keys.Where(k => k == null || !sources.Contains(k)).ToList())
            {
                if (_copies[gone].Copy != null)
                    Destroy(_copies[gone].Copy);
                _copies.Remove(gone);
            }
            var hidden = _view.Renderer == null || _view.Renderer.forceRenderingOff || !_view.Renderer.enabled;
            foreach (var source in sources)
            {
                if (!_copies.TryGetValue(source, out var copy) || copy.Copy == null)
                {
                    var go = new GameObject("Outline");
                    go.transform.SetParent(source.transform, false);
                    var filter = go.AddComponent<MeshFilter>();
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    copy = (go, filter, renderer);
                    _copies[source] = copy;
                }
                var mesh = source.sharedMesh;
                var sourceRenderer = source.GetComponent<Renderer>();
                var show = mesh != null && !hidden && sourceRenderer != null && sourceRenderer.enabled && !sourceRenderer.forceRenderingOff;
                copy.Copy.SetActive(show);
                if (!show)
                    continue;
                if (copy.Filter.sharedMesh != mesh)
                    copy.Filter.sharedMesh = mesh;
                // One material for every submesh: the rim follows the whole shape.
                var slots = copy.Renderer.sharedMaterials;
                if (slots.Length != mesh.subMeshCount || slots.Any(m => m != _material))
                    copy.Renderer.sharedMaterials = Enumerable.Repeat(_material, mesh.subMeshCount).ToArray();
                _block.SetColor(ColorId, color);
                _block.SetFloat(WidthId, Width);
                _block.SetVector(CenterId, mesh.bounds.center);
                copy.Renderer.SetPropertyBlock(_block);
            }
        }

        private void OnDestroy()
        {
            foreach (var copy in _copies.Values)
                if (copy.Copy != null)
                    Destroy(copy.Copy);
            _copies.Clear();
        }
    }
}
