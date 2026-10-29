// Unity CLI probe (M12), Edit mode: `unity command script eval_file --file tools/unity-probes/HandRig.cs`.
// Edit mode: the placeholder model's right arm and hand in the bind pose: each bone's offset from the wrist along the
// hand's own (normalised) axes and its length from its parent, in metres of the file.
var prefab = UnityEngine.Resources.Load<UnityEngine.GameObject>("TomLizard/TomLizard");
if (prefab == null) return "no model";
var go = UnityEngine.Object.Instantiate(prefab);
try
{
    var bind = new System.Collections.Generic.Dictionary<UnityEngine.Transform, UnityEngine.Matrix4x4>();
    foreach (var r in go.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>())
    {
        var poses = r.sharedMesh.bindposes;
        for (var i = 0; i < r.bones.Length && i < poses.Length; i++)
            if (r.bones[i] != null) bind[r.bones[i]] = r.transform.localToWorldMatrix * poses[i].inverse;
    }
    UnityEngine.Transform hand = null;
    foreach (var t in go.GetComponentsInChildren<UnityEngine.Transform>(true))
        if (t.name == "hand_r") hand = t;
    if (hand == null) return "no hand_r";
    UnityEngine.Vector3 P(UnityEngine.Transform t) => bind.TryGetValue(t, out var m) ? (UnityEngine.Vector3)m.GetColumn(3) : t.position;
    var hb = bind[hand];
    var ax = hb.MultiplyVector(UnityEngine.Vector3.right).normalized;
    var ay = hb.MultiplyVector(UnityEngine.Vector3.up).normalized;
    var az = hb.MultiplyVector(UnityEngine.Vector3.forward).normalized;
    var sb = new System.Text.StringBuilder();
    sb.Append("model scale (lossy) " + go.transform.lossyScale.ToString("F3") + "; hand axes x " + ax.ToString("F2") + " y " + ay.ToString("F2") + " z " + az.ToString("F2") + "\n");
    foreach (var t in hand.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t == hand || t.name.StartsWith("deform")) continue;
        var d = P(t) - P(hand);
        var len = (P(t) - P(t.parent)).magnitude;
        sb.Append(t.name + " <- " + t.parent.name + ": x " + UnityEngine.Vector3.Dot(d, ax).ToString("F3") + " y " + UnityEngine.Vector3.Dot(d, ay).ToString("F3") + " z " + UnityEngine.Vector3.Dot(d, az).ToString("F3") + " len " + len.ToString("F3") + "\n");
    }
    return sb.ToString();
}
finally
{
    UnityEngine.Object.DestroyImmediate(go);
}
