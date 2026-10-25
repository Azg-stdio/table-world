// Unity CLI probe (M1 T2.8), Play mode only: `unity command script eval_file --file tools/unity-probes/MeasureGame.cs --timeout 300`.
// Opens the first game in the Games library whose name contains GAME (an imported TTS game, say), as the Games panel
// does, reports the load time, objects and memory, and starts FrameStats. Read the frames later with FrameReport.cs.
if (!UnityEngine.Application.isPlaying) return "not playing";
const string GAME = "Sinnoh";
var entry = System.Linq.Enumerable.FirstOrDefault(TableWorld.View.GameLibrary.List(), e => e.Name.Contains(GAME));
if (entry == null) return "no game named like " + GAME;
var table = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableView>();
var json = System.IO.File.ReadAllText(entry.File);
System.GC.Collect();
var before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
var watch = System.Diagnostics.Stopwatch.StartNew();
var ok = table.Load(json, entry.PackageRoot);
watch.Stop();
var after = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
var stats = table.GetComponent<TableWorld.View.FrameStats>() ?? table.gameObject.AddComponent<TableWorld.View.FrameStats>();
stats.Restart();
return entry.Name + " loaded " + ok + ": " + table.Views.Count + " objects in " + watch.ElapsedMilliseconds + " ms, +" +
  ((after - before) / 1048576.0).ToString("F0") + " MB allocated, table " + (json.Length / 1024) + " KB; frame stats started";
