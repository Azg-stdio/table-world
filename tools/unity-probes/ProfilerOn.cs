// Unity CLI probe (M1 T2.8): starts the Editor's Profiler recording Play mode (not the Editor), for SpikeProfile.cs.
// `unity command script eval_file --file tools/unity-probes/ProfilerOn.cs`; stop with `... --code "UnityEditorInternal.ProfilerDriver.enabled = false; return \"off\";"`.
UnityEditorInternal.ProfilerDriver.profileEditor = false;
UnityEditorInternal.ProfilerDriver.ClearAllFrames();
UnityEditorInternal.ProfilerDriver.enabled = true;
UnityEngine.Profiling.Profiler.enabled = true;
return "profiler recording";
