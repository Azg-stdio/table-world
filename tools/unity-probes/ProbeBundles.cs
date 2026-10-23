// Unity CLI probe: run with `unity command script eval_file --file <path> --timeout 180`.
// Runs as a method body (no `using` directives; fully-qualified names; ends with `return`).
// Loads a sample of TTS AssetBundles and reports assets, materials and shaders. Read-only.


var sb = new System.Text.StringBuilder();
var dir = @"C:\Users\Miguel Arbelaez\Documents\My Games\Tabletop Simulator\Mods\Assetbundles";
var all = System.IO.Directory.GetFiles(dir, "*.unity3d").OrderBy(f => f).ToList();

// Representative sample: the 2019.1 bundle, the 5.3.4 bundle, the two biggest 5.6.x bundles and one small 5.6.2
string[] picks = {
  all.First(f => System.IO.Path.GetFileName(f).StartsWith("httpcloud3steamusercontentcomugc14957127")),
  all.First(f => System.IO.Path.GetFileName(f).StartsWith("httpssteamusercontentaakamaihdnetugc8718")),
  all.OrderByDescending(f => new System.IO.FileInfo(f).Length).ElementAt(0),
  all.OrderByDescending(f => new System.IO.FileInfo(f).Length).ElementAt(1),
  all.First(f => System.IO.Path.GetFileName(f).StartsWith("httpcloud3steamusercontentcomugc1021697231137159142")),
};
sb.AppendLine($"Unity {Application.unityVersion} | QualitySettings.renderPipeline={QualitySettings.renderPipeline?.GetType().Name} | GraphicsSettings.default={UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline?.GetType().Name} | {SystemInfo.graphicsDeviceType}");
sb.AppendLine($"Total bundles in cache: {all.Count()}");
foreach (var p in picks) {
  sb.AppendLine("\n=== " + System.IO.Path.GetFileName(p).Substring(0, 50) + "... (" + new System.IO.FileInfo(p).Length / 1024 + " KB)");
  AssetBundle ab = null;
  try {
    ab = AssetBundle.LoadFromFile(p);
    if (ab == null) { sb.AppendLine("  LOAD FAILED: LoadFromFile returned null (see console)"); continue; }
    var names = ab.GetAllAssetNames();
    sb.AppendLine($"  loaded OK. isStreamedSceneAssetBundle={ab.isStreamedSceneAssetBundle} assets={names.Length}");
    foreach (var n in names.Take(6)) sb.AppendLine("   - " + n);
    var objs = ab.LoadAllAssets();
    var byType = objs.GroupBy(o => o.GetType().Name).Select(g => $"{g.Key}x{g.Count()}");
    sb.AppendLine("  types: " + string.Join(", ", byType));
    int mats = 0, pink = 0;
    foreach (var m in objs.OfType<Material>()) {
      mats++;
      var sh = m.shader;
      bool bad = sh == null || !sh.isSupported || sh.name == "Hidden/InternalErrorShader";
      if (bad) pink++;
      sb.AppendLine($"   mat '{m.name}' shader='{sh?.name}' supported={sh?.isSupported} mainTex={(m.HasProperty("_MainTex") ? (m.mainTexture?.name ?? "null") : "n/a")}{(bad ? "  <-- PINK" : "")}");
    }
    foreach (var go in objs.OfType<GameObject>().Take(3)) {
      var rends = go.GetComponentsInChildren<Renderer>(true);
      var mf = go.GetComponentsInChildren<MeshFilter>(true);
      var anim = go.GetComponentsInChildren<Animator>(true).Length + go.GetComponentsInChildren<Animation>(true).Length;
      var scripts = go.GetComponentsInChildren<Component>(true).Count(c => c == null);
      sb.AppendLine($"   prefab '{go.name}': renderers={rends.Length} meshes={mf.Count(m => m.sharedMesh != null)} verts={mf.Sum(m => m.sharedMesh?.vertexCount ?? 0)} animators={anim} missingScripts={scripts} tris={mf.Sum(m => m.sharedMesh != null ? m.sharedMesh.triangles.Length/3 : 0)}");
      foreach (var r in rends.Take(4)) sb.AppendLine($"      renderer '{r.name}': " + string.Join(" | ", r.sharedMaterials.Select(m => m == null ? "NULL" : $"{m.name}->{m.shader?.name}{(m.shader != null && m.shader.isSupported ? "" : " (UNSUPPORTED)")}")));
    }
    foreach (var t in objs.OfType<Texture2D>().Take(3)) sb.AppendLine($"   tex '{t.name}' {t.width}x{t.height} {t.format} mips={t.mipmapCount}");
    foreach (var sh in objs.OfType<Shader>()) sb.AppendLine($"   shader asset '{sh.name}' supported={sh.isSupported}");
    sb.AppendLine($"  materials={mats} pink={pink}");
  } catch (System.Exception e) { sb.AppendLine("  EXCEPTION: " + e.GetType().Name + ": " + e.Message); }
  finally { if (ab != null) ab.Unload(true); }
}
return sb.ToString();
