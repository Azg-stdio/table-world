// Unity CLI probe (M10 T11.10), Play mode only: opens the first game in the Games library whose name or path contains GAME
// (empty: keep the table on screen), then parks the main camera high over a corner so `capture game_view` shows every
// seat's chair. Reports where each chair went. TableCamera stays disabled until Play mode ends.
if (!UnityEngine.Application.isPlaying) return "not playing";
const string GAME = "";
var table = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableView>();
var loaded = "the table on screen";
if (GAME != "")
{
    var entry = System.Linq.Enumerable.FirstOrDefault(TableWorld.View.GameLibrary.List(), e => e.Name.Contains(GAME) || e.File.Contains(GAME));
    if (entry == null) return "no game named like " + GAME;
    if (!table.Load(System.IO.File.ReadAllText(entry.File), entry.PackageRoot)) return "could not load " + entry.Name;
    loaded = entry.Name;
}
var rig = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableCamera>();
if (rig != null) rig.enabled = false;
var size = TableWorld.View.TableGeometry.Size;
var reach = UnityEngine.Mathf.Max(size.x, size.y);
var camera = UnityEngine.Camera.main;
camera.transform.position = new UnityEngine.Vector3(reach * 0.55f, reach * 1.35f + 0.6f, -reach * 1.05f - 0.6f);
camera.transform.LookAt(new UnityEngine.Vector3(0f, -0.25f, 0f));
var view = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.Chalkers.ChalkersView>();
var places = System.Linq.Enumerable.Select(view.Chairs, p => p.Key.Replace("\u0001", "") + " " + p.Value.Root.transform.position.ToString("F2"));
return loaded + ", table " + size.ToString("F2") + ", local seat " + table.LocalSeat + "; chairs: " + string.Join("; ", places);
