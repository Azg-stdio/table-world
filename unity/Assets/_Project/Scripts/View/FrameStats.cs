using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Frame timing for performance checks (T2.8): frame times, the main thread's time, physics, scripts, rendering and
    /// garbage per frame, and how many bodies are awake. Added by a probe (<c>tools/unity-probes/MeasureTable.cs</c>);
    /// <see cref="Report"/> summarises since <see cref="Restart"/>.
    /// </summary>
    public sealed class FrameStats : MonoBehaviour
    {
        private readonly List<float> _frames = new List<float>();
        private readonly List<int> _awake = new List<int>();
        private readonly Dictionary<string, (ProfilerRecorder Recorder, List<double> Values, double Scale)> _recorders =
            new Dictionary<string, (ProfilerRecorder, List<double>, double)>();
        private float _startedAt;
        /// <summary>Frames slower than <see cref="SpikeMs"/>: when, how long, and the recorders that frame.</summary>
        private readonly List<string> _spikes = new List<string>();
        public const float SpikeMs = 100f;

        private void OnEnable()
        {
            // Milliseconds for timings (recorded in nanoseconds), kilobytes for garbage.
            Record("main", ProfilerCategory.Internal, "Main Thread", 1e-6);
            Record("physics", ProfilerCategory.Physics, "Physics.Simulate", 1e-6);
            Record("scripts", ProfilerCategory.Scripts, "BehaviourUpdate", 1e-6);
            Record("late scripts", ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate", 1e-6);
            Record("render", ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering", 1e-6);
            Record("wait gpu", ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread", 1e-6);
            Record("draw calls", ProfilerCategory.Render, "Draw Calls Count", 1);
            Record("setpass", ProfilerCategory.Render, "SetPass Calls Count", 1);
            Record("batches", ProfilerCategory.Render, "Batches Count", 1);
            Record("shadow casters", ProfilerCategory.Render, "Shadow Casters Count", 1);
            Record("gc KB", ProfilerCategory.Memory, "GC Allocated In Frame", 1.0 / 1024);
            Record("gc collect", ProfilerCategory.Scripts, "GC.Collect", 1e-6);
            Record("coroutines", ProfilerCategory.Scripts, "CoroutinesDelayedCalls", 1e-6);
            Record("fixed scripts", ProfilerCategory.Scripts, "FixedBehaviourUpdate", 1e-6);
            Record("async upload", ProfilerCategory.Loading, "Initialization.AsyncUploadTimeSlicedUpdate", 1e-6);
            Restart();
        }

        private void Record(string name, ProfilerCategory category, string marker, double scale) =>
            _recorders[name] = (ProfilerRecorder.StartNew(category, marker, 1), new List<double>(), scale);

        private void OnDisable()
        {
            foreach (var r in _recorders.Values)
                r.Recorder.Dispose();
            _recorders.Clear();
        }

        public void Restart()
        {
            _frames.Clear();
            _awake.Clear();
            _spikes.Clear();
            foreach (var r in _recorders.Values)
                r.Values.Clear();
            _startedAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            var ms = Time.unscaledDeltaTime * 1000f;
            _frames.Add(ms);
            foreach (var r in _recorders.Values)
                if (r.Recorder.Valid && r.Recorder.Count > 0)
                    r.Values.Add(r.Recorder.LastValue * r.Scale);
            // The recorders hold the previous frame, the one this delta measured.
            if (ms > SpikeMs && _spikes.Count < 200)
                _spikes.Add($"t={Time.realtimeSinceStartup - _startedAt:F1}s {ms:F0} ms: " + string.Join(", ",
                    _recorders.Where(p => p.Value.Recorder.Valid && p.Value.Recorder.Count > 0 && !p.Key.Contains("calls") && p.Key != "setpass" && p.Key != "batches" && p.Key != "shadow casters")
                        .Select(p => $"{p.Key} {p.Value.Recorder.LastValue * p.Value.Scale:F0}")));
            if (_frames.Count % 10 == 0)
                _awake.Add(FindObjectsByType<Rigidbody>().Count(b => !b.isKinematic && !b.IsSleeping()));
        }

        public string Report()
        {
            if (_frames.Count == 0)
                return "no frames";
            var sorted = _frames.OrderBy(f => f).ToList();
            var seconds = Time.realtimeSinceStartup - _startedAt;
            var lines = new List<string>
            {
                $"{_frames.Count} frames in {seconds:F1} s: {_frames.Count / seconds:F1} fps; frame ms avg {_frames.Average():F2}, " +
                $"median {sorted[sorted.Count / 2]:F2}, p95 {sorted[(int)(sorted.Count * 0.95)]:F2}, p99 {sorted[(int)(sorted.Count * 0.99)]:F2}, max {sorted.Last():F2}",
            };
            foreach (var pair in _recorders)
                lines.Add(pair.Value.Values.Count == 0
                    ? $"{pair.Key}: (no samples)"
                    : $"{pair.Key}: avg {pair.Value.Values.Average():F2}, max {pair.Value.Values.Max():F2}");
            if (_awake.Count > 0)
                lines.Add($"awake bodies: avg {_awake.Average():F0}, max {_awake.Max()}, last {_awake.Last()}");
            lines.Add($"spikes over {SpikeMs:F0} ms: {_spikes.Count}");
            lines.AddRange(_spikes.OrderByDescending(x => float.Parse(x.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)).Take(8));
            lines.Add($"memory: {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB allocated, mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB");
            return string.Join("\n", lines);
        }
    }
}
