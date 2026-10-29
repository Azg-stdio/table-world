// Unity CLI probe (M12), Play mode only: `unity command script eval_file --file tools/unity-probes/UiAudit.cs`.
// Checks every text the HUD shows right now for contrast against what is behind it and for room from its panel's edges
// (TableWorld.View.Hud.UiAudit), and returns what fails, worst first. Open the screen to check first.
if (!UnityEngine.Application.isPlaying) return "not playing";
var hud = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.Hud.HudController>();
var root = hud.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement;
var problems = TableWorld.View.Hud.UiAudit.Check(root);
return problems.Count == 0 ? "readable: no problems" : problems.Count + " problems\n" + TableWorld.View.Hud.UiAudit.Report(System.Linq.Enumerable.Take(problems, 60));
