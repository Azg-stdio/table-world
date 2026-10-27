// Unity CLI probe (M9 T10.2), Play mode only: shows the interface in LANGUAGE ("es", "en"; "" goes back to the setting)
// with PANEL open for `capture game_view`: "hud" (the table and its bars), "settings" (Escape → Settings), "controls"
// (F1). Does not change the saved setting.
if (!UnityEngine.Application.isPlaying) return "not playing";
const string LANGUAGE = "es";
const string PANEL = "hud";
TableWorld.View.Ui.Override = LANGUAGE == "" ? null : LANGUAGE;
TableWorld.View.Ui.SetLanguage("");
var hud = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.Hud.HudController>();
if (PANEL == "settings")
{
    hud.Escape.Open();
    hud.Escape.OpenSettings();
}
else if (PANEL == "hud")
{
    hud.Escape.CloseSettings();
    hud.Escape.Close();
}
else
{
    // controls, saves, agent: the HUD keeps them private; reached by name for the screenshot only.
    var field = PANEL == "controls" ? "_help" : PANEL == "saves" ? "_savesPanel" : "_agent";
    var panel = typeof(TableWorld.View.Hud.HudController).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(hud);
    panel.GetType().GetMethod("Show", System.Type.EmptyTypes).Invoke(panel, null);
}
return "language " + TableWorld.View.Ui.Language + ", panel " + PANEL;
