// Unity CLI probe (M1 T2.8), Play mode only: `unity command script eval_file --file tools/unity-probes/FrameReport.cs`.
// Reports the FrameStats that MeasureTable.cs started (frames, main thread, physics, scripts, rendering, garbage, awake
// bodies, memory) and restarts them, so the next report covers only what happens after this one.
if (!UnityEngine.Application.isPlaying) return "not playing";
var stats = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.FrameStats>();
if (stats == null) return "no FrameStats: run MeasureTable.cs first";
var report = stats.Report();
stats.Restart();
return report;
