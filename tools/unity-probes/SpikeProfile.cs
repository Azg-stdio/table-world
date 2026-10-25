// Unity CLI probe (M1 T2.8), Play mode, with the Profiler recording (run ProfilerOn.cs first):
// `unity command script eval_file --file tools/unity-probes/SpikeProfile.cs`.
// Finds the frames in the Profiler's history slower than LIMIT ms and lists, for each, the main-thread samples that took
// the most time on their own (self time), merged by name. The history is short (a few hundred frames): call it often.
if (!UnityEngine.Application.isPlaying) return "not playing";
const float LIMIT = 150f;
const int TOP = 8;
var first = UnityEditorInternal.ProfilerDriver.firstFrameIndex;
var last = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
var report = new System.Text.StringBuilder();
report.Append("frames " + first + ".." + last + "\n");
for (var frame = first; frame <= last && frame >= 0; frame++) {
  using (var view = UnityEditorInternal.ProfilerDriver.GetRawFrameDataView(frame, 0)) {
    if (view == null || !view.valid || view.frameTimeMs < LIMIT) continue;
    var self = new System.Collections.Generic.Dictionary<string, double>();
    var count = view.sampleCount;
    // Self time: a sample's time minus its direct children's, children follow their parent in the raw view.
    var stack = new System.Collections.Generic.List<(int Index, int Left, double Children)>();
    for (var i = 0; i < count; i++) {
      var name = view.GetSampleName(i);
      var ms = view.GetSampleTimeMs(i);
      while (stack.Count > 0 && stack[stack.Count - 1].Left == 0) {
        var done = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        var doneName = view.GetSampleName(done.Index);
        self[doneName] = (self.TryGetValue(doneName, out var v) ? v : 0) + view.GetSampleTimeMs(done.Index) - done.Children;
      }
      if (stack.Count > 0) {
        var parent = stack[stack.Count - 1];
        stack[stack.Count - 1] = (parent.Index, parent.Left - 1, parent.Children + ms);
      }
      stack.Add((i, view.GetSampleChildrenCount(i), 0));
    }
    while (stack.Count > 0) {
      var done = stack[stack.Count - 1];
      stack.RemoveAt(stack.Count - 1);
      var doneName = view.GetSampleName(done.Index);
      self[doneName] = (self.TryGetValue(doneName, out var v) ? v : 0) + view.GetSampleTimeMs(done.Index) - done.Children;
    }
    report.Append("frame " + frame + ": " + view.frameTimeMs.ToString("F0") + " ms; ");
    foreach (var pair in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(self, p => p.Value), TOP))
      report.Append(pair.Key + " " + pair.Value.ToString("F0") + "; ");
    report.Append("\n");
  }
}
return report.ToString();
