// Unity CLI probe (M1 T2.8), Play mode only: `unity command script eval_file --file tools/unity-probes/MeasureTable.cs --timeout 300`.
// Loads a generated table of CARDS loose cards (in piles of PILE, raised DROP metres) and TOKENS tokens over the table, reports the
// load time and memory, and starts FrameStats. Wait, then read the frames with tools/unity-probes/FrameReport.cs.
// Change the constants below for other sizes. Replaces the open table (do not run it on a table you care about).
if (!UnityEngine.Application.isPlaying) return "not playing";
const int CARDS = 2000, PILE = 20, TOKENS = 200;
const double DROP = 0.0;
var inv = System.Globalization.CultureInfo.InvariantCulture;
var table = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableView>();
var sample = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Tables/sample").text;
var root = Newtonsoft.Json.Linq.JObject.Parse(sample);
var objects = new Newtonsoft.Json.Linq.JArray();
var piles = (CARDS + PILE - 1) / PILE;
var columns = (int)System.Math.Ceiling(System.Math.Sqrt(piles * 1.6));
var rows = (piles + columns - 1) / columns;
for (var i = 0; i < CARDS; i++) {
  var pile = i / PILE;
  var x = -0.7 + (pile % columns) * (1.4 / System.Math.Max(1, columns - 1));
  var z = -0.4 + (pile / columns) * (0.68 / System.Math.Max(1, rows - 1));
  var y = DROP + (i % PILE) * 0.0006;
  objects.Add(Newtonsoft.Json.Linq.JObject.Parse("{ \"id\": \"c" + i + "\", \"type\": \"Card\", \"faceAsset\": \"std_deck\", \"faceIndex\": " + (i % 52) +
    ", \"backAsset\": \"std_back\", \"transform\": { \"position\": [" + x.ToString(inv) + ", " + y.ToString(inv) + ", " + z.ToString(inv) + "], \"rotation\": [0, 180, " + (i % 2 == 0 ? "0" : "180") + "] } }"));
}
for (var i = 0; i < TOKENS; i++) {
  var x = -0.74 + (i % 50) * 0.03;
  var z = 0.36 + (i / 50) * 0.03;
  objects.Add(Newtonsoft.Json.Linq.JObject.Parse("{ \"id\": \"t" + i + "\", \"type\": \"Token\", \"shape\": \"round\", \"tint\": [0.8, 0.2, 0.2], \"transform\": { \"position\": [" +
    x.ToString(inv) + ", " + DROP.ToString(inv) + ", " + z.ToString(inv) + "] } }"));
}
root["objects"] = objects;
// No zones: nothing on the table falls in a hand or a hidden area, and the seats keep no hand.
root["zones"] = new Newtonsoft.Json.Linq.JArray();
foreach (var seat in root["players"]["seats"]) ((Newtonsoft.Json.Linq.JObject)seat).Remove("handZone");
var json = root.ToString(Newtonsoft.Json.Formatting.None);
System.GC.Collect();
var before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
var watch = System.Diagnostics.Stopwatch.StartNew();
var ok = table.Load(json, null);
watch.Stop();
var after = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
var stats = table.GetComponent<TableWorld.View.FrameStats>() ?? table.gameObject.AddComponent<TableWorld.View.FrameStats>();
stats.Restart();
return "loaded " + ok + ": " + table.Views.Count + " objects in " + watch.ElapsedMilliseconds + " ms, +" + ((after - before) / 1048576.0).ToString("F0") +
  " MB allocated, json " + (json.Length / 1024) + " KB; frame stats started";
