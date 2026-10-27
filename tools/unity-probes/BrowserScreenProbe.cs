// Unity CLI probe (M11 T12.9, the web view experiment), Play mode only. STEP:
//   "open"   loads Karaoke night and puts a BrowserScreen on the karaoke screen showing VIDEO
//   "click"  clicks the middle of the page (Chromium's rule for sound needs an interaction)
//   "look"   aims the camera at the screen for a screenshot
//   "state"  the player's state, time and error, the screen's fps and the frame time
//   "seek"   jumps the video to 30 s and plays it
//   "pause"  pauses it
//   "other"  loads another video (an embeddable one)
//   "close"  destroys the screen
if (!UnityEngine.Application.isPlaying) return "not playing";
const string STEP = "open";
const string VIDEO = "M7lc1UVf-VE";
var screen = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.Karaoke.BrowserScreen>();
if (STEP == "open")
{
    if (screen != null) UnityEngine.Object.Destroy(screen.gameObject);
    var entry = System.Linq.Enumerable.FirstOrDefault(TableWorld.View.GameLibrary.List(), e => e.File.EndsWith("karaoke-night.json"));
    var table = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableView>();
    table.Load(System.IO.File.ReadAllText(entry.File), entry.PackageRoot);
    var env = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.EnvironmentView>();
    env.Apply();
    TableWorld.View.Karaoke.BrowserScreen.Create(env.Screen, VIDEO);
    return "opened " + VIDEO;
}
if (screen == null) return "no screen";
if (STEP == "click") { screen.ClickToStart(); return "clicked"; }
if (STEP == "look")
{
    var env2 = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.EnvironmentView>();
    var rig2 = UnityEngine.Object.FindAnyObjectByType<TableWorld.View.TableCamera>();
    if (rig2 != null) rig2.enabled = false;
    var at2 = env2.Screen.transform.position;
    UnityEngine.Camera.main.transform.position = at2 + new UnityEngine.Vector3(0f, -0.1f, -3.2f);
    UnityEngine.Camera.main.transform.LookAt(at2);
    return "looking";
}
if (STEP == "seek") { screen.Seek(30); screen.Play(); return "seek 30"; }
if (STEP == "pause") { screen.Pause(); return "paused"; }
if (STEP == "other") { screen.LoadVideo("aqz-KE-bpKQ", 10); return "other video"; }
if (STEP == "close") { UnityEngine.Object.Destroy(screen.gameObject); return "closed"; }
return "ready " + screen.Ready + ", fps " + screen.Fps + ", state " + screen.State + ", time " + screen.Time.ToString("F2") + "/" + screen.Duration.ToString("F0")
    + ", error " + screen.Error + ", title " + screen.Title + ", refused " + screen.Refused + ", frame " + (UnityEngine.Time.smoothDeltaTime * 1000f).ToString("F1") + " ms";
