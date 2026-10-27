// Unity CLI probe (M11 T12.6), Play mode only: loads a six-seat table in the karaoke room, turns on Hear myself so your
// own chalker sits across the table, makes you the singer (microphone, stage light) and parks the main camera for
// `capture game_view`. SHOT "room": from behind the near sofa towards the screen; "singer": close on the singer;
// "off": Hear myself off, no singer, the camera back to normal. TableCamera stays disabled until "off".
if (!UnityEngine.Application.isPlaying) return "not playing";
const string SHOT = "room";
var table = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableView>();
var view = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.Chalkers.ChalkersView>();
var hud = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.Hud.HudController>();
var rig = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableCamera>();
if (SHOT == "off")
{
    view.Singer = null;
    hud.Escape.Set(s => s.HearMyself = false);
    if (rig != null) rig.enabled = true;
    return "off";
}
if (!view.Karaoke)
{
    var seats = "{\"id\":\"seat_0\",\"color\":\"white\"},{\"id\":\"seat_1\",\"color\":\"red\"},{\"id\":\"seat_2\",\"color\":\"blue\"},"
        + "{\"id\":\"seat_3\",\"color\":\"green\"},{\"id\":\"seat_4\",\"color\":\"yellow\"},{\"id\":\"seat_5\",\"color\":\"purple\"}";
    if (!table.Load("{\"schemaVersion\":\"0.1\",\"objects\":[],\"environment\":{\"room\":\"karaoke\"},\"players\":{\"seats\":[" + seats + "]}}", null))
        return "could not load the karaoke table";
}
hud.Escape.Set(s => s.HearMyself = true);
view.Singer = table.LocalSeat;
if (rig != null) rig.enabled = false;
var camera = UnityEngine.Camera.main;
if (SHOT == "singer" && view.Shown.TryGetValue(TableWorld.View.Chalkers.ChalkersView.MirrorKey, out var singer))
{
    var at = singer.Root.transform.position;
    var forward = singer.Root.transform.forward;
    camera.transform.position = at + forward * 1.1f + UnityEngine.Vector3.up * 0.35f + singer.Root.transform.right * 0.35f;
    camera.transform.LookAt(at + UnityEngine.Vector3.up * 0.25f);
}
else
{
    var half = TableWorld.View.TableGeometry.Size / 2f;
    camera.transform.position = new UnityEngine.Vector3(0.6f, 1.25f, -half.y - 1.7f);
    camera.transform.LookAt(new UnityEngine.Vector3(0f, 0.35f, 1.6f));
}
var stage = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.EnvironmentView>().StageLight;
return "sofas " + view.Sofas.Count + ", stage light " + (stage != null && stage.enabled) + ", local seat " + table.LocalSeat;
