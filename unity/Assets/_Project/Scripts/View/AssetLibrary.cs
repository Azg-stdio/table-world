using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                foreach (var key in _cells.Keys.Where(k => k.Source == material).ToList())
                {
                    Object.Destroy(_cells[key]);
                    _cells.Remove(key);
                }
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
                // Package images decode on a worker thread (a TTS sheet of 10 000 px takes a second even as a small
                // file): the table shows at once, plain, and each picture appears when it is ready (Decoded).
                if (LocalCopy(asset) is { } file)
                {
                    if (_decoding.Add(asset.Path))
                        Decode(asset.Path, file);
                    return null;
                }
                if (FetchFromWeb(asset))
                    return null;
                var bytes = ReadPackageFile(asset) ?? _fetch?.Invoke(asset);
                if (bytes != null)
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true) { name = asset.Path, anisoLevel = 4 };
                    if (texture.LoadImage(bytes, markNonReadable: false))
                        texture = Fit(texture);
                    else
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

        /// <summary>Largest side of a loaded image (M4 T5.2); bigger ones are scaled down, keeping their proportions.</summary>
        public const int MaxTextureSize = 4096;

        /// <summary>
        /// Makes a freshly loaded image fit for the GPU: at most <see cref="MaxTextureSize"/> on its longest side (sprite
        /// sheets keep their grid, since the whole sheet scales), block-compressed when its size allows (a quarter of the
        /// memory or less), with mipmaps, and no longer readable from the CPU.
        /// </summary>
        public static Texture2D Fit(Texture2D texture)
        {
            var largest = Mathf.Max(texture.width, texture.height);
            if (largest > MaxTextureSize)
            {
                var scale = (float)MaxTextureSize / largest;
                var width = Mathf.Max(4, Mathf.RoundToInt(texture.width * scale / 4f) * 4);
                var height = Mathf.Max(4, Mathf.RoundToInt(texture.height * scale / 4f) * 4);
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, target);
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var smaller = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true) { name = texture.name, anisoLevel = texture.anisoLevel };
                smaller.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(texture);
                texture = smaller;
                texture.Apply(updateMipmaps: true);
            }
            // Block compression works on 4 × 4 blocks.
            if (texture.width % 4 == 0 && texture.height % 4 == 0)
                texture.Compress(highQuality: false);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
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
            if (LocalCopy(asset) == null && FetchFromWeb(asset))
                return null;
            var bytes = (LocalCopy(asset) is { } local ? File.ReadAllBytes(local) : null) ?? _fetch?.Invoke(asset);
            if (bytes != null)
            {
                if (!asset.Path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase))
                    Debug.LogWarning($"[AssetLibrary] '{asset.Path}': only OBJ models load for now; showing a placeholder.");
                else if (Core.Models.ObjMesh.Parse(System.Text.Encoding.UTF8.GetString(bytes), out var error) is { } data)
                    mesh = ToMesh(data, asset.Path, asset.MeshScale);
                else
                    Debug.LogWarning($"[AssetLibrary] '{asset.Path}' is not a usable OBJ model: {error}");
            }
            else if (_fetch == null)
                Debug.LogWarning($"[AssetLibrary] Missing model '{asset.Path}' (package: {_packageRoot ?? "none"}); showing a placeholder.");
            if (mesh != null || _fetch == null)
                _meshesByPath[asset.Path] = mesh;
            return mesh;
        }

        private static Mesh ToMesh(Core.Models.MeshData data, string name, float scale)
        {
            var mesh = new Mesh { name = "model:" + name };
            if (data.Positions.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Positions.ConvertAll(p => new Vector3(p.X, p.Y, p.Z) * scale));
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
            if (LocalCopy(asset) == null && FetchFromWeb(asset))
                return null;
            var file = LocalCopy(asset) is { } local ? Path.GetFullPath(local)
                : _packageRoot != null && Asset.IsSafePath(asset.Path) ? Path.GetFullPath(Path.Combine(_packageRoot, asset.Path)) : null;
            var key = file ?? "fetched:" + (asset.Hash ?? asset.Path);
            return Bundles.Prefab(key, file, _fetch == null ? null : (System.Func<byte[]>)(() => _fetch(asset)));
        }

        /// <summary>The asset's file in the game package, or null. Paths were validated to stay inside the package.</summary>
        public byte[] ReadPackageFile(Asset asset) => PackageFile(asset) is { } file ? File.ReadAllBytes(file) : null;

        private string PackageFile(Asset asset)
        {
            if (_packageRoot == null || asset.IsBuiltin || !Asset.IsSafePath(asset.Path))
                return null;
            var file = Path.Combine(_packageRoot, asset.Path);
            return File.Exists(file) ? file : null;
        }

        private readonly HashSet<string> _decoding = new HashSet<string>();

        // ---- files named by web address (Asset.Url) ------------------------------------------------------------------

        /// <summary>Where files fetched from the web are kept, by TTS's name for their address.</summary>
        public static string UrlCacheFolder => Path.Combine(Application.persistentDataPath, "Cache", "urls");

        /// <summary>Downloads running at once; more wait their turn.</summary>
        public const int MaxDownloads = 4;

        private static readonly Queue<System.Action> WaitingDownloads = new Queue<System.Action>();
        private static int _downloading;

        private readonly HashSet<string> _fetching = new HashSet<string>();

        /// <summary>Files still coming from the web.</summary>
        public int Fetching => _fetching.Count;

        private static string Extension(Asset asset) => Path.GetExtension(asset.Path) is { Length: > 0 } e ? e : asset.Type == AssetType.Model ? ".obj" : ".png";

        /// <summary>
        /// A file on this computer with the asset's bytes: the package's, a copy of its web address fetched before, or
        /// the player's own Tabletop Simulator cache (TTS games name the same addresses). Null when there is none.
        /// </summary>
        private string LocalCopy(Asset asset)
        {
            if (PackageFile(asset) is { } package)
                return package;
            if (!Asset.IsWebAddress(asset.Url))
                return null;
            var key = Core.Tts.TtsImporter.CacheKey(asset.Url);
            var cached = Path.Combine(UrlCacheFolder, key + Extension(asset));
            if (File.Exists(cached))
                return cached;
            var tts = Core.Tts.TtsPackage.CachedFile(Core.Tts.TtsPackage.DefaultFolder,
                new Core.Tts.ImportedAsset { Type = asset.Type, CacheKey = key, Url = asset.Url });
            return tts;
        }

        /// <summary>
        /// Starts fetching the asset's web address when no copy is here (true: it is on its way, the views redraw when it
        /// lands, through <see cref="Decoded"/>). Each machine fetches for itself, as TTS does.
        /// </summary>
        private bool FetchFromWeb(Asset asset)
        {
            if (!Asset.IsWebAddress(asset.Url))
                return false;
            if (!_fetching.Add(asset.Path))
                return true;
            var target = Path.Combine(UrlCacheFolder, Core.Tts.TtsImporter.CacheKey(asset.Url) + Extension(asset));
            void Start()
            {
                _downloading++;
                Directory.CreateDirectory(UrlCacheFolder);
                var part = target + ".part";
                var request = new UnityEngine.Networking.UnityWebRequest(Asset.FetchAddress(asset.Url), "GET",
                    new UnityEngine.Networking.DownloadHandlerFile(part) { removeFileOnAbort = true }, null) { timeout = 120 };
                request.SendWebRequest().completed += _ =>
                {
                    var ok = request.result == UnityEngine.Networking.UnityWebRequest.Result.Success;
                    if (!ok)
                        Debug.LogWarning($"[AssetLibrary] Could not fetch {asset.Url}: {request.error}");
                    request.Dispose();
                    _downloading--;
                    if (ok)
                    {
                        if (File.Exists(target))
                            File.Delete(target);
                        File.Move(part, target);
                    }
                    _fetching.Remove(asset.Path);
                    if (ok)
                        foreach (var id in _assets.Values.Where(a => a.Url == asset.Url).Select(a => a.Id).ToList())
                            Decoded?.Invoke(id);
                    if (WaitingDownloads.Count > 0 && _downloading < MaxDownloads)
                        WaitingDownloads.Dequeue()();
                };
            }
            if (_downloading < MaxDownloads)
                Start();
            else
                WaitingDownloads.Enqueue(Start);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDownloads()
        {
            WaitingDownloads.Clear();
            _downloading = 0;
            WaitingDecodes.Clear();
            _decodes = 0;
        }

        /// <summary>
        /// An image named by its web address rather than an asset (TTS UI sets <c>image</c> to a URL): fetched and decoded
        /// like an asset's, under a hidden asset of its own; null until it is here.
        /// </summary>
        public Texture2D WebTexture(string url)
        {
            if (!Asset.IsWebAddress(url))
                return null;
            var id = "web_" + Core.Tts.TtsImporter.CacheKey(url).ToLowerInvariant();
            if (Find(id) == null)
            {
                var extension = url.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
                Register(new Asset { Id = id, Type = AssetType.Image, Path = "web/" + Core.Tts.TtsImporter.CacheKey(url) + extension, Url = url });
            }
            return Texture(id);
        }

        /// <summary>An image decoded in the background is ready (asset id): views showing it redraw.</summary>
        public event System.Action<string> Decoded;

        /// <summary>Images still being decoded.</summary>
        public int Decoding => _decoding.Count;

        /// <summary>
        /// Images decoded at once. Each holds its full-size pixels until it is fitted (a 7151 × 3253 TTS panel is 93 MB),
        /// and decoding a heavy table's hundreds of images all together ran the Editor out of memory.
        /// </summary>
        public const int MaxDecodes = 2;

        private static readonly Queue<System.Action> WaitingDecodes = new Queue<System.Action>();
        private static int _decodes;

        private void Decode(string path, string file)
        {
            if (_decodes >= MaxDecodes)
            {
                WaitingDecodes.Enqueue(() => Decode(path, file));
                return;
            }
            _decodes++;
            var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(new System.Uri(file).AbsoluteUri, nonReadable: false);
            request.SendWebRequest().completed += _ =>
            {
                _decodes--;
                if (WaitingDecodes.Count > 0)
                    WaitingDecodes.Dequeue()();
                Texture2D texture = null;
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                else
                    Debug.LogWarning($"[AssetLibrary] '{path}' is not a PNG or JPEG image ({request.error}).");
                request.Dispose();
                _decoding.Remove(path);
                if (texture != null)
                {
                    texture.name = path;
                    texture.anisoLevel = 4;
                    texture = Fit(texture);
                }
                _texturesByPath[path] = texture;
                if (texture != null)
                    foreach (var id in _assets.Values.Where(a => a.Path == path).Select(a => a.Id).ToList())
                        Decoded?.Invoke(id);
            };
        }

        /// <summary>
        /// A see-through copy of <paramref name="source"/> at <paramref name="alpha"/> (a TTS tint with transparency),
        /// shared by every piece drawn with the same material and alpha.
        /// </summary>
        public Material Translucent(Material source, float alpha)
        {
            if (source == null)
                return null;
            var key = (source, Mathf.Round(alpha * 100f) / 100f);
            if (_translucent.TryGetValue(key, out var material) && material != null)
                return material;
            material = new Material(source) { name = source.name + " (alpha)" };
            if (material.HasProperty(BaseColor))
            {
                var color = material.GetColor(BaseColor);
                color.a = alpha;
                material.SetColor(BaseColor, color);
            }
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _translucent[key] = material;
            return material;
        }

        private readonly Dictionary<(Material, float), Material> _translucent = new Dictionary<(Material, float), Material>();

        /// <summary>
        /// <paramref name="source"/> showing one cell of its image (<c>_BaseMap_ST</c> = <paramref name="scaleOffset"/>),
        /// shared by every piece showing that cell. A material per cell, not a property block per renderer: property
        /// blocks take renderers out of URP's SRP Batcher, and 2 200 cards cost 11 700 draw calls and 2 000 SetPass
        /// calls a frame (T2.8).
        /// </summary>
        public Material Cell(Material source, Vector4 scaleOffset)
        {
            if (source == null || scaleOffset == new Vector4(1f, 1f, 0f, 0f))
                return source;
            var key = (source, scaleOffset);
            if (_cells.TryGetValue(key, out var material) && material != null)
                return material;
            material = new Material(source) { name = source.name + " (cell)" };
            material.SetVector(BaseMapSt, scaleOffset);
            _cells[key] = material;
            return material;
        }

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private readonly Dictionary<(Material Source, Vector4 ScaleOffset), Material> _cells = new Dictionary<(Material, Vector4), Material>();

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
