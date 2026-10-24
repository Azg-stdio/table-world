using System.Collections.Generic;
using System.IO;
using Core.Rules;
using TableWorld.View.Builtin;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Resolves the table's asset ids to textures and shared materials. <c>builtin:</c> paths are generated
    /// (<see cref="BuiltinTextures"/>); other paths are image files relative to the game package folder, or bytes from
    /// <c>fetch</c> (a network client's asset cache, T3.4). M4 adds URLs and models. Materials are shared per asset;
    /// per-object cells are set with property blocks.
    /// </summary>
    public sealed class AssetLibrary
    {
        public const string BuiltinPrefix = "builtin:";
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        private readonly Dictionary<string, Asset> _assets = new Dictionary<string, Asset>();
        private readonly Dictionary<string, Texture2D> _texturesByPath = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Mesh> _meshesByPath = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly string _packageRoot;
        private readonly System.Func<Asset, byte[]> _fetch;
        private Shader _lit;

        public AssetLibrary(IEnumerable<Asset> assets, string packageRoot, System.Func<Asset, byte[]> fetch = null)
        {
            foreach (var asset in assets)
                _assets[asset.Id] = asset;
            _packageRoot = packageRoot;
            _fetch = fetch;
        }

        /// <summary>Forgets the asset's texture so the next lookup loads it again (its bytes just arrived).</summary>
        public void Forget(string assetId)
        {
            var asset = Find(assetId);
            if (asset == null)
                return;
            if (_texturesByPath.TryGetValue(asset.Path, out var texture) && texture != null)
                Object.Destroy(texture);
            _texturesByPath.Remove(asset.Path);
            if (_meshesByPath.TryGetValue(asset.Path, out var mesh) && mesh != null)
                Object.Destroy(mesh);
            _meshesByPath.Remove(asset.Path);
            if (_materials.TryGetValue("img:" + assetId, out var material))
            {
                Object.Destroy(material);
                _materials.Remove("img:" + assetId);
            }
        }

        /// <summary>Adds an asset the table does not list (e.g. the builtin dice atlas).</summary>
        public void Register(Asset asset) => _assets[asset.Id] = asset;

        public Asset Find(string assetId) => assetId != null && _assets.TryGetValue(assetId, out var a) ? a : null;

        /// <summary>Material showing the whole image of <paramref name="assetId"/>, or a plain material when it has none.</summary>
        public Material ImageMaterial(string assetId, Color fallback)
        {
            var texture = Texture(assetId);
            if (texture == null)
                return Plain(fallback);
            var key = "img:" + assetId;
            if (!_materials.TryGetValue(key, out var material))
            {
                material = NewMaterial(key, Color.white, 0.25f);
                material.SetTexture(BaseMap, texture);
                _materials[key] = material;
            }
            return material;
        }

        /// <summary>
        /// The side of a counted stack (G14): <paramref name="color"/> with a thin dark groove at the bottom of the texture.
        /// The renderer tiles it once per piece, so a stack of chips shows each chip's edge.
        /// </summary>
        public Material Grooved(Color color)
        {
            var key = $"grooved:{ColorUtility.ToHtmlStringRGBA(color)}";
            if (!_materials.TryGetValue(key, out var material))
            {
                if (_groove == null)
                {
                    _groove = new Texture2D(1, 16, TextureFormat.RGBA32, false) { name = "Stack groove", wrapMode = TextureWrapMode.Repeat };
                    for (var y = 0; y < 16; y++)
                        _groove.SetPixel(0, y, y < 2 ? new Color(0.35f, 0.35f, 0.35f) : y == 15 ? new Color(1f, 1f, 1f) : new Color(0.9f, 0.9f, 0.9f));
                    _groove.Apply();
                }
                material = NewMaterial(key, color, 0.3f);
                material.SetTexture(BaseMap, _groove);
                _materials[key] = material;
            }
            return material;
        }

        private Texture2D _groove;

        public Material Plain(Color color, float smoothness = 0.3f)
        {
            var key = $"plain:{ColorUtility.ToHtmlStringRGBA(color)}:{smoothness:F2}";
            if (!_materials.TryGetValue(key, out var material))
            {
                material = NewMaterial(key, color, smoothness);
                _materials[key] = material;
            }
            return material;
        }

        /// <summary>Texture tiling/offset (<c>_BaseMap_ST</c>) selecting cell <paramref name="index"/> of the asset's grid.</summary>
        public Vector4 CellScaleOffset(string assetId, int index)
        {
            var asset = Find(assetId);
            var columns = Mathf.Max(1, asset?.Columns ?? 1);
            var rows = Mathf.Max(1, asset?.Rows ?? 1);
            index = Mathf.Clamp(index, 0, columns * rows - 1);
            var column = index % columns;
            var row = index / columns;
            return new Vector4(1f / columns, 1f / rows, (float)column / columns, 1f - (float)(row + 1) / rows);
        }

        public Texture2D Texture(string assetId)
        {
            var asset = Find(assetId);
            if (asset == null || asset.Type != AssetType.Image)
                return null;
            if (_texturesByPath.TryGetValue(asset.Path, out var cached))
                return cached;

            Texture2D texture = null;
            if (asset.IsBuiltin)
            {
                texture = BuiltinTextures.Create(asset.Path.Substring(BuiltinPrefix.Length));
                if (texture == null)
                    Debug.LogWarning($"[AssetLibrary] Unknown builtin asset '{asset.Path}'.");
            }
            else
            {
                var bytes = ReadPackageFile(asset) ?? _fetch?.Invoke(asset);
                if (bytes != null)
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true) { name = asset.Path, anisoLevel = 4 };
                    if (!texture.LoadImage(bytes, markNonReadable: true))
                        Debug.LogWarning($"[AssetLibrary] '{asset.Path}' is not a PNG or JPEG image.");
                }
                else if (_fetch == null)
                {
                    Debug.LogWarning($"[AssetLibrary] Missing image '{asset.Path}' (package: {_packageRoot ?? "none"}).");
                }
            }
            // A network client does not cache a miss: the bytes may still be on their way.
            if (texture != null || _fetch == null)
                _texturesByPath[asset.Path] = texture;
            return texture;
        }

        /// <summary>
        /// A model asset's mesh (OBJ, TTS Custom_Model; M4/G38), or null: builtin placeholders, other formats (glTF comes
        /// with T5.1), unreadable files, and files still downloading. Cached per path.
        /// </summary>
        public Mesh Mesh(string assetId)
        {
            var asset = Find(assetId);
            if (asset == null || asset.Type != AssetType.Model)
                return null;
            // TTS's primitive pieces are built in (G38); builtin:placeholder stays the grey box.
            if (asset.IsBuiltin)
                return Builtin.BuiltinModels.Get(asset.Path.Substring(BuiltinPrefix.Length));
            if (_meshesByPath.TryGetValue(asset.Path, out var cached))
                return cached;
            Mesh mesh = null;
            var bytes = ReadPackageFile(asset) ?? _fetch?.Invoke(asset);
            if (bytes != null)
            {
                if (!asset.Path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase))
                    Debug.LogWarning($"[AssetLibrary] '{asset.Path}': only OBJ models load for now; showing a placeholder.");
                else if (Core.Models.ObjMesh.Parse(System.Text.Encoding.UTF8.GetString(bytes), out var error) is { } data)
                    mesh = ToMesh(data, asset.Path);
                else
                    Debug.LogWarning($"[AssetLibrary] '{asset.Path}' is not a usable OBJ model: {error}");
            }
            else if (_fetch == null)
                Debug.LogWarning($"[AssetLibrary] Missing model '{asset.Path}' (package: {_packageRoot ?? "none"}); showing a placeholder.");
            if (mesh != null || _fetch == null)
                _meshesByPath[asset.Path] = mesh;
            return mesh;
        }

        private static Mesh ToMesh(Core.Models.MeshData data, string name)
        {
            var mesh = new Mesh { name = "model:" + name };
            if (data.Positions.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Positions.ConvertAll(p => new Vector3(p.X, p.Y, p.Z)));
            mesh.SetUVs(0, data.Uvs.ConvertAll(u => new Vector2(u.X, u.Y)));
            mesh.SetTriangles(data.Triangles, 0);
            if (data.Normals.Count == data.Positions.Count)
                mesh.SetNormals(data.Normals.ConvertAll(n => new Vector3(n.X, n.Y, n.Z)));
            else
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// An AssetBundle asset's prefab (TTS Custom_Assetbundle, M4 T5.4), or null while it is missing or when it does
        /// not load. Loaded once per file for the session.
        /// </summary>
        public GameObject Bundle(string assetId)
        {
            var asset = Find(assetId);
            if (asset == null || asset.Type != AssetType.AssetBundle || asset.IsBuiltin)
                return null;
            var file = _packageRoot != null && Asset.IsSafePath(asset.Path) ? Path.GetFullPath(Path.Combine(_packageRoot, asset.Path)) : null;
            var key = file ?? "fetched:" + (asset.Hash ?? asset.Path);
            return Bundles.Prefab(key, file, _fetch == null ? null : (System.Func<byte[]>)(() => _fetch(asset)));
        }

        /// <summary>The asset's file in the game package, or null. Paths were validated to stay inside the package.</summary>
        public byte[] ReadPackageFile(Asset asset)
        {
            if (_packageRoot == null || asset.IsBuiltin || !Asset.IsSafePath(asset.Path))
                return null;
            var file = Path.Combine(_packageRoot, asset.Path);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }

        private Material NewMaterial(string name, Color color, float smoothness)
        {
            if (_lit == null)
                _lit = RequiredShaders.Lit;
            var material = new Material(_lit) { name = name, enableInstancing = true };
            material.SetColor(BaseColor, color);
            material.SetFloat(Smoothness, smoothness);
            return material;
        }
    }
}
