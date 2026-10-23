// Unity CLI probe: run with `unity command script eval_file --file <path> --timeout 180`.
// Renders 3 TTS bundle prefabs to a RenderTexture before/after remapping Standard -> URP/Lit,
// counts magenta pixels and writes PNGs to outDir. Edit `dir` and `outDir` before running.

var sb = new System.Text.StringBuilder();
var dir = @"C:\Users\Miguel Arbelaez\Documents\My Games\Tabletop Simulator\Mods\Assetbundles";
var outDir = @"C:\Users\MIGUEL~1\AppData\Local\Temp\claude\f--Users-Migue-Desktop-Proyectos-Personales-table-world\0fadc7db-117d-4c8f-a363-9812ba4a3b51\scratchpad";
var all = System.IO.Directory.GetFiles(dir, "*.unity3d").OrderBy(f => f).ToList();
string[] picks = {
  all.First(f => System.IO.Path.GetFileName(f).StartsWith("httpcloud3steamusercontentcomugc14957127")),   // Staraptor 2019.1
  all.First(f => System.IO.Path.GetFileName(f).StartsWith("httpcloud3steamusercontentcomugc78074479")),   // love_letter table 5.6.5
  all.First(f => System.IO.Path.GetFileName(f).StartsWith("httpcloud3steamusercontentcomugc1021697231137159142")), // Porygon 5.6.2
};
var urpLit = Shader.Find("Universal Render Pipeline/Lit");
sb.AppendLine($"URP/Lit found={urpLit != null} supported={urpLit?.isSupported}");

System.Func<Camera, RenderTexture, string, (int magenta, int lit, int total)> snap = (cam, rt, file) => {
  cam.targetTexture = rt;
  cam.Render();
  var prev = RenderTexture.active; RenderTexture.active = rt;
  var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
  tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
  RenderTexture.active = prev;
  var px = tex.GetPixels32(); int mag = 0, lit = 0;
  foreach (var c in px) { if (c.r > 200 && c.g < 60 && c.b > 200) mag++; if (c.a > 0 && !(c.r == 0 && c.g == 0 && c.b == 0)) lit++; }
  System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, file), tex.EncodeToPNG());
  UnityEngine.Object.DestroyImmediate(tex);
  return (mag, lit, px.Length);
};

System.Func<Material, Material> remap = (src) => {
  var m = new Material(urpLit); m.name = src.name + "_URP";
  if (src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null) m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
  if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
  if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null) { m.SetTexture("_BumpMap", src.GetTexture("_BumpMap")); m.EnableKeyword("_NORMALMAP"); }
  if (src.HasProperty("_MetallicGlossMap") && src.GetTexture("_MetallicGlossMap") != null) { m.SetTexture("_MetallicGlossMap", src.GetTexture("_MetallicGlossMap")); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
  if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
  if (src.HasProperty("_Glossiness")) m.SetFloat("_Smoothness", src.GetFloat("_Glossiness"));
  if (src.HasProperty("_EmissionMap") && src.GetTexture("_EmissionMap") != null) { m.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap")); m.SetColor("_EmissionColor", src.GetColor("_EmissionColor")); m.EnableKeyword("_EMISSION"); }
  return m;
};

var camGo = new GameObject("__probeCam"); var cam = camGo.AddComponent<Camera>();
cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1);
cam.cameraType = CameraType.Game; cam.cullingMask = ~0;
var lightGo = new GameObject("__probeLight"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
var rt = new RenderTexture(512, 512, 24);
int idx = 0;
foreach (var p in picks) {
  idx++;
  AssetBundle ab = null; GameObject inst = null;
  try {
    ab = AssetBundle.LoadFromFile(p);
    var prefab = ab.LoadAllAssets<GameObject>().FirstOrDefault();
    if (prefab == null) { sb.AppendLine("no prefab"); continue; }
    inst = UnityEngine.Object.Instantiate(prefab); inst.name = "__probe_" + prefab.name;
    inst.transform.position = new Vector3(1000, 1000, 1000);
    var rends = inst.GetComponentsInChildren<Renderer>(true);
    var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
    float dist = b.extents.magnitude * 2.2f + 0.1f;
    cam.transform.position = b.center + new Vector3(0.6f, 0.5f, -1f).normalized * dist;
    cam.transform.LookAt(b.center); cam.nearClipPlane = 0.01f; cam.farClipPlane = dist * 10;
    var before = snap(cam, rt, $"probe{idx}_original.png");
    var shaders = string.Join(",", rends.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).Distinct());
    int remapped = 0;
    foreach (var r in rends) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) if (ms[i] != null) { ms[i] = remap(ms[i]); remapped++; } r.sharedMaterials = ms; }
    var after = snap(cam, rt, $"probe{idx}_urp_remap.png");
    sb.AppendLine($"[{idx}] {prefab.name}: bounds={b.size} shaders=[{shaders}] renderers={rends.Length} remapped={remapped}");
    sb.AppendLine($"     ORIGINAL: magenta={before.magenta} nonBg={before.lit}/{before.total}   -> {100f*before.magenta/System.Math.Max(1,before.lit):F0}% of drawn pixels magenta");
    sb.AppendLine($"     URP/Lit : magenta={after.magenta} nonBg={after.lit}/{after.total}   -> {100f*after.magenta/System.Math.Max(1,after.lit):F0}% of drawn pixels magenta");
  } catch (System.Exception e) { sb.AppendLine("  EXCEPTION: " + e); }
  finally { if (inst != null) UnityEngine.Object.DestroyImmediate(inst); if (ab != null) ab.Unload(true); }
}
UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(lightGo); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
return sb.ToString();
