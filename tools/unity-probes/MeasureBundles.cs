// Unity CLI probe (M4 T5.5), Play mode only: `unity command script eval_file --file tools/unity-probes/MeasureBundles.cs --timeout 300`.
// Copies 20 bundles of the local TTS cache (spread over the size range) into a temporary package, loads them as one
// table and reports load time, memory and materials left unconverted. Reads the TTS cache; never writes to it.
if (!UnityEngine.Application.isPlaying) return "not playing";
var cache = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "My Games", "Tabletop Simulator", "Mods", "Assetbundles");
var all = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(System.IO.Directory.GetFiles(cache, "*.unity3d"), f => new System.IO.FileInfo(f).Length));
var package = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "bundle-measure");
System.IO.Directory.CreateDirectory(package);
var assets = ""; var objects = ""; long bytes = 0;
var count = System.Math.Min(20, all.Count);
for (var i = 0; i < count; i++) {
  var source = all[i * (all.Count - 1) / System.Math.Max(1, count - 1)];
  System.IO.File.Copy(source, System.IO.Path.Combine(package, "b" + i + ".unity3d"), true);
  bytes += new System.IO.FileInfo(source).Length;
  var x = ((i % 5) * 0.15 - 0.3).ToString(System.Globalization.CultureInfo.InvariantCulture);
  var z = ((i / 5) * 0.15 - 0.2).ToString(System.Globalization.CultureInfo.InvariantCulture);
  assets += (i > 0 ? "," : "") + "{ \"id\": \"b" + i + "\", \"type\": \"assetBundle\", \"path\": \"b" + i + ".unity3d\" }";
  objects += (i > 0 ? "," : "") + "{ \"id\": \"m" + i + "\", \"type\": \"Model\", \"asset\": \"b" + i + "\", \"transform\": { \"position\": [" + x + ", 0.05, " + z + "] } }";
}
var json = "{ \"schemaVersion\": \"0.1\", \"assets\": [" + assets + "], \"objects\": [" + objects + "] }";
var table = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableView>();
System.GC.Collect();
var before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
var watch = System.Diagnostics.Stopwatch.StartNew();
var ok = table.Load(json, package);
watch.Stop();
var after = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
int shown = 0, renderers = 0, notUrp = 0;
for (var i = 0; i < count; i++) {
  var b = table.ViewOf("m" + i).transform.Find("Bundle");
  if (b == null) continue;
  shown++;
  foreach (var r in b.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
    renderers++;
    foreach (var m in r.sharedMaterials) if (m != null && !(m.shader.isSupported && m.shader.name.StartsWith("Universal"))) notUrp++;
  }
}
return "loaded " + ok + ": " + shown + "/" + count + " bundles shown, " + (bytes / 1048576.0).ToString("F1") + " MB of files, "
  + watch.ElapsedMilliseconds + " ms, +" + ((after - before) / 1048576.0).ToString("F0") + " MB allocated, " + renderers + " renderers, " + notUrp + " materials not URP";
