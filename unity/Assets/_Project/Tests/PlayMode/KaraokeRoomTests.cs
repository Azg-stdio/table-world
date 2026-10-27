using System.Collections;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using TableWorld.View.Hud;
using TableWorld.View.Karaoke;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// The karaoke room (T12.6, reworked by T12.10 after Miguel tried it): no table; sofas facing a big screen on the far
    /// wall; you walk in first person, the walls and sofas stop you, you sit on a sofa place and stand up again; a
    /// standing chalker has its feet on the floor; the singer holds a microphone under the stage light; the big screen
    /// bar puts a video on the screen. Another room brings the table and its chairs back.
    /// </summary>
    public class KaraokeRoomTests
    {
        private HudController _hud;
        private ChalkersView _chalkers;
        private TableView _table;
        private EnvironmentView _environment;

        private const string Karaoke = "{\"schemaVersion\":\"0.1\",\"objects\":[],\"environment\":{\"room\":\"karaoke\"},\"players\":{\"seats\":["
            + "{\"id\":\"seat_0\",\"color\":\"white\"},{\"id\":\"seat_1\",\"color\":\"red\"},{\"id\":\"seat_2\",\"color\":\"blue\"},"
            + "{\"id\":\"seat_3\",\"color\":\"green\"},{\"id\":\"seat_4\",\"color\":\"yellow\"},{\"id\":\"seat_5\",\"color\":\"purple\"}]}}";

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _hud = Object.FindAnyObjectByType<HudController>();
            _chalkers = Object.FindAnyObjectByType<ChalkersView>();
            _table = Object.FindAnyObjectByType<TableView>();
            _environment = Object.FindAnyObjectByType<EnvironmentView>();
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(_table.Load(Karaoke, null));
            yield return null;
            yield return null;
            Assert.IsNotNull(_chalkers.Walker, "walking starts in the karaoke room");
            _chalkers.Walker.IgnoreDevices = true;
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _chalkers.Singer = null;
            _hud.Escape.Set(s => s.HearMyself = false);
            yield return null;
        }

        private Walker Walker => _chalkers.Walker;

        [Test]
        public void TheRoom_HasNoTable_SofasFaceABigScreen()
        {
            Assert.IsTrue(TableGeometry.Walk);
            Assert.IsTrue(TableGeometry.Hidden, "no table");
            Assert.AreEqual(0, _chalkers.Chairs.Count, "no chairs");
            var lounge = _environment.Lounge;
            Assert.IsNotNull(lounge);
            Assert.AreEqual(10, lounge.Places.Count);
            var screen = _environment.Screen;
            Assert.Greater(screen.bounds.size.x, 2f, "a big screen");
            Assert.Greater(screen.transform.position.z, EnvironmentView.RoomHalfSize.z - 0.2f, "on the far wall");
            foreach (var (position, facing) in lounge.Places)
            {
                var toScreen = new Vector3(screen.transform.position.x - position.x, 0f, screen.transform.position.z - position.z).normalized;
                Assert.Greater(Vector3.Dot(facing, toScreen), 0.7f, "every place faces the screen");
            }
            Assert.IsFalse(Object.FindAnyObjectByType<TableCamera>().enabled, "the table camera rests");
            Assert.IsFalse(_environment.StageLight.enabled, "no singer, no stage light");
        }

        [UnityTest]
        public IEnumerator Walking_MovesYou_WallsAndSofasStopYou()
        {
            var start = Walker.Feet;
            Walker.Yaw = 0f;
            for (var i = 0; i < 30; i++)
                Walker.Walk(Vector2.up, false, 0.02f);
            yield return null;
            Assert.AreEqual(start.z + Walker.Speed * 0.6f, Walker.Feet.z, 0.01f, "walked forward");
            Assert.AreEqual(EnvironmentView.FloorY, Walker.Feet.y, 1e-4f, "on the floor");
            var camera = Camera.main.transform.position;
            Assert.AreEqual(Walker.Eyes.y, camera.y, 1e-3f, "the camera is at the eyes");

            // Into the back wall: stopped inside it.
            Walker.Yaw = 180f;
            for (var i = 0; i < 400; i++)
                Walker.Walk(Vector2.up, true, 0.02f);
            Assert.Greater(Walker.Feet.z, -EnvironmentView.RoomHalfSize.z + Walker.Radius, "the wall stops you");

            // Straight at the middle sofa's back from behind: never inside it.
            var middle = _environment.Lounge.Blockers[0];
            Walker.Feet = new Vector3(middle.Centre.x, EnvironmentView.FloorY, middle.Centre.z - 1.5f);
            Walker.Yaw = 0f;
            for (var i = 0; i < 200; i++)
            {
                Walker.Walk(Vector2.up, false, 0.02f);
                var closest = middle.Closest(Walker.Feet);
                Assert.GreaterOrEqual(Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(Walker.Feet.x, Walker.Feet.z)), Walker.Radius - 0.01f, "outside the sofa");
            }
        }

        [UnityTest]
        public IEnumerator Sitting_PutsYouOnASofaPlace_WalkingStandsYouUp()
        {
            var lounge = _environment.Lounge;
            var (place, facing) = lounge.Places[1];
            Walker.Feet = new Vector3(place.x, EnvironmentView.FloorY, place.z) + facing * 0.7f;
            Assert.IsTrue(Walker.ToggleSit());
            Assert.AreEqual(1, Walker.Place);
            yield return null;
            Assert.AreEqual(0f, Vector3.Distance(place + Vector3.up * Chalker.EyeHeight, Camera.main.transform.position), 0.01f, "eyes where a seated chalker's are");
            Assert.AreEqual(1, Walker.Body.Place, "others see you sitting there");

            Walker.Walk(Vector2.up, false, 0.02f);
            Assert.IsNull(Walker.Place, "walking stands you up");

            // A place someone else sits on is skipped; out of reach of any place, nothing to sit on.
            Walker.Taken = () => new[] { 1 };
            Walker.Feet = new Vector3(place.x, EnvironmentView.FloorY, place.z) + facing * 0.7f;
            Assert.IsTrue(Walker.ToggleSit());
            Assert.AreNotEqual(1, Walker.Place);
            Walker.ToggleSit();
            Walker.Feet = new Vector3(0f, EnvironmentView.FloorY, EnvironmentView.RoomHalfSize.z - 1f);
            Assert.IsFalse(Walker.ToggleSit(), "no sofa within reach");
        }

        /// <summary>Your own chalker (Hear myself) stands in front of you on its feet; singing, it holds the microphone in the stage light.</summary>
        [UnityTest]
        public IEnumerator AStandingChalker_HasItsFeetOnTheFloor_TheSingerHoldsTheMic()
        {
            _hud.Escape.Set(s => s.HearMyself = true);
            yield return null;
            yield return null;
            var chalker = _chalkers.Shown[ChalkersView.MirrorKey];
            Assert.IsTrue(chalker.Standing);
            var feet = chalker.Root.GetComponentsInChildren<Renderer>().Where(r => r.name == "Foot" && r.gameObject.activeInHierarchy).ToList();
            Assert.AreEqual(2, feet.Count, "standing legs only");
            foreach (var foot in feet)
                Assert.AreEqual(EnvironmentView.FloorY, foot.bounds.min.y, 0.02f, "the feet are on the floor");
            Assert.IsFalse(chalker.Root.GetComponentsInChildren<Renderer>().Any(r => r.name == "Thigh" && r.gameObject.activeInHierarchy), "no seated legs");

            _chalkers.Singer = _table.LocalSeat;
            yield return null;
            yield return null;
            Assert.IsTrue(chalker.Microphone);
            var tip = chalker.Mic.position + chalker.Mic.forward * Chalker.MicLength;
            Assert.Less(Vector3.Distance(tip, chalker.Mouth), 0.07f, "the microphone is at the mouth");
            var stage = _environment.StageLight;
            Assert.IsTrue(stage.enabled);
            var toSinger = chalker.Root.transform.position + Vector3.up * 0.2f - stage.transform.position;
            Assert.Less(Vector3.Angle(stage.transform.forward, toSinger), EnvironmentView.StageLightAngle / 2f, "the singer is in the light");
        }

        /// <summary>T12.13: walking onto the stage in front of the screen makes you the singer; walking off ends it.</summary>
        [UnityTest]
        public IEnumerator StandingOnTheStage_MakesYouTheSinger()
        {
            var lounge = _environment.Lounge;
            Assert.IsFalse(lounge.OnStage(Walker.Feet), "you start off the stage");
            yield return null;
            Assert.IsNull(_chalkers.Singer);
            Assert.IsFalse(_environment.StageLight.enabled);

            Walker.Feet = new Vector3(lounge.Stage.Centre.x, EnvironmentView.FloorY, lounge.Stage.Centre.z);
            yield return null;
            yield return null;
            Assert.AreEqual(new[] { _table.LocalSeat }, _chalkers.StageSingers.ToArray());
            Assert.AreEqual(_table.LocalSeat, _chalkers.Singer, "you sing");
            Assert.IsTrue(_environment.StageLight.enabled, "the stage light is on you");
            Assert.AreEqual(0, _hud.ScreenBar.Delay, "the singer's own screen is not delayed");

            Walker.Feet = new Vector3(0f, EnvironmentView.FloorY, -2f);
            yield return null;
            yield return null;
            Assert.IsNull(_chalkers.Singer, "off the stage, nobody sings");
            Assert.IsFalse(_environment.StageLight.enabled);
        }

        /// <summary>Another player's body from their presence: standing where their feet are, or on their sofa place.</summary>
        [Test]
        public void AnotherPlayersBody_StandsOrSitsWhereTheirPresenceSays()
        {
            var lounge = _environment.Lounge;
            var standing = new Core.Net.AvatarPose { Position = new Core.Rules.Vec3(1f, EnvironmentView.FloorY, -2f), Yaw = 90f };
            var (at, facing) = ChalkersView.BodyPlace(standing, lounge);
            Assert.AreEqual(Chalker.StandingOrigin(new Vector3(1f, EnvironmentView.FloorY, -2f)), at);
            Assert.AreEqual(1f, facing.x, 1e-4f, "facing +X at 90 degrees");
            var seated = new Core.Net.AvatarPose { Position = new Core.Rules.Vec3(0f, 0f, 0f), Place = 5 };
            Assert.AreEqual(lounge.Places[5], ChalkersView.BodyPlace(seated, lounge));
        }

        /// <summary>
        /// The big screen bar (no YouTube needed): shown only in the karaoke room, a link that is not YouTube's is refused
        /// with a reason, a YouTube link puts the browser on the room's screen, and another room takes it away.
        /// </summary>
        [UnityTest]
        public IEnumerator TheScreenBar_PutsAVideoOnTheRoomsScreen()
        {
            var root = _hud.GetComponent<UIDocument>().rootVisualElement;
            yield return null;
            Assert.IsFalse(root.Q("screenBar").ClassListContains("hidden"), "shown in the karaoke room");
            var bar = _hud.ScreenBar;
            Assert.IsFalse(bar.PlayLink("https://example.com/watch?v=M7lc1UVf-VE"));
            Assert.AreEqual("That is not a YouTube link.", root.Q<Label>("screenStatus").text);
            Assert.IsTrue(bar.PlayLink("https://youtu.be/M7lc1UVf-VE"));
            // Shared: the table's screen says what plays, from where, at what host time.
            var shared = _table.Engine.State.Screen;
            Assert.AreEqual("M7lc1UVf-VE", shared.VideoId);
            Assert.IsTrue(shared.Playing);
            Assert.AreEqual(0, shared.Position, 1e-6);
            yield return null;
            Assert.IsNotNull(bar.Screen, "this machine's player follows it");
            Assert.AreEqual("M7lc1UVf-VE", bar.Screen.VideoId);
            Assert.AreEqual(bar.Screen.transform.parent, _environment.Screen.transform, "on the room's screen");

            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(bar.Pause());
            Assert.IsFalse(_table.Engine.State.Screen.Playing, "paused for everyone");
            Assert.Greater(_table.Engine.State.Screen.Position, 0.2, "where it was");
            Assert.IsTrue(bar.PlayLink(""), "an empty link resumes");
            Assert.IsTrue(_table.Engine.State.Screen.Playing);

            Assert.IsTrue(_table.Load(Karaoke.Replace("karaoke", "gameNight"), null));
            yield return null;
            yield return null;
            Assert.IsTrue(root.Q("screenBar").ClassListContains("hidden"), "hidden in another room");
            Assert.IsNull(bar.Screen, "the screen goes with the room");
        }

        /// <summary>
        /// T12.12 (no YouTube needed): queued songs show under the bar; with the screen off the host starts the first one
        /// for everyone; ✕ takes one out; Next skips to the following one.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSongQueue_ShowsWaitingSongs_TheHostStartsTheNext()
        {
            var root = _hud.GetComponent<UIDocument>().rootVisualElement;
            var bar = _hud.ScreenBar;
            foreach (var (id, title) in new[] { ("M7lc1UVf-VE", "First song"), ("aqz-KE-bpKQ", "Second song"), ("dQw4w9WgXcQ", "Third song") })
                Assert.IsTrue(_table.Execute(new Core.Engine.QueueSongCommand { Actor = _table.LocalSeat, VideoId = id, Title = title }).Ok);
            yield return null;
            // The screen was off: the host put the first song on at once.
            Assert.AreEqual("M7lc1UVf-VE", _table.Engine.State.Screen?.VideoId);
            var rows = root.Q("screenSongs").Query<Label>(className: "screen-bar__song-name").ToList();
            Assert.AreEqual(2, rows.Count, "the two still waiting");
            StringAssert.StartsWith("1. Second song", rows[0].text);
            Assert.AreEqual(2, root.Q("screenSongs").Query<Button>().ToList().Count, "you may take out your own");

            Assert.IsTrue(bar.Remove(0));
            Assert.AreEqual("dQw4w9WgXcQ", _table.Engine.State.SongQueue.Single().VideoId);
            Assert.IsTrue(bar.Next());
            Assert.AreEqual("dQw4w9WgXcQ", _table.Engine.State.Screen.VideoId, "Next plays the following song");
            Assert.IsEmpty(_table.Engine.State.SongQueue);
            yield return null;
            Assert.AreEqual(0, root.Q("screenSongs").childCount, "nothing waiting");
        }

        /// <summary>
        /// Miguel's first walk: W A S D and the arrows stepped the HUD's focus from button to button (Unity's UI navigation),
        /// and nobody said how the room works. Now navigation keys leave the focus alone, a guide card explains the room
        /// and closes with Got it, and the link field says what goes in it.
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingKeys_LeaveTheHudAlone_AndAGuideExplainsTheRoom()
        {
            var root = _hud.GetComponent<UIDocument>().rootVisualElement;
            yield return null;
            var host = root.Q<Button>("hostButton");
            host.Focus();
            Assert.AreEqual(host, root.focusController.focusedElement);
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right))
            {
                move.target = host;
                host.SendEvent(move);
            }
            yield return null;
            Assert.AreEqual(host, root.focusController.focusedElement, "a navigation key does not move the focus");
            host.Blur();

            var guide = root.Q("walkGuide");
            Assert.IsFalse(guide.ClassListContains("hidden"), "the guide shows in the karaoke room");
            Assert.AreEqual(5, guide.Q("walkGuideLines").childCount);
            StringAssert.Contains("W", guide.Q("walkGuideLines").Q<Label>().text, "with the keys as bound");
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                var close = root.Q<Button>("walkGuideClose");
                click.target = close;
                close.SendEvent(click);
            }
            // Walking, a submit from the keyboard is the room's: the guide is closed by a click instead.
            yield return null;
            Assert.IsFalse(guide.ClassListContains("hidden"), "Space or Enter does not press a button while walking");
            ClickButton(root.Q<Button>("walkGuideClose"));
            yield return null;
            Assert.IsTrue(guide.ClassListContains("hidden"), "Got it closes it");
            Assert.AreEqual("Paste a YouTube link", root.Q<TextField>("screenLink").textEdition.placeholder);
        }

        /// <summary>T12.15: the song search shows YouTube's results and its consent page; a video is a pick; anything else goes back.</summary>
        [Test]
        public void TheSongSearch_ShowsOnlyYouTubesResults()
        {
            Assert.AreEqual("https://www.youtube.com/results?search_query=queen%20karaoke", SearchBrowser.SearchUrl("queen karaoke"));
            Assert.AreEqual("queen karaoke", SongSearch.Words(" queen "));
            Assert.AreEqual("Tu falta de querer Karaoke", SongSearch.Words("Tu falta de querer Karaoke"), "said once, not twice");
            Assert.AreEqual("karaoke", SongSearch.Words(""));
            Assert.AreEqual(SearchBrowser.Verdict.Show, SearchBrowser.Judge("https://www.youtube.com/results?search_query=a", out _));
            Assert.AreEqual(SearchBrowser.Verdict.Show, SearchBrowser.Judge("https://consent.youtube.com/m?continue=x", out _));
            Assert.AreEqual(SearchBrowser.Verdict.Show, SearchBrowser.Judge("about:blank", out _));
            Assert.AreEqual(SearchBrowser.Verdict.Pick, SearchBrowser.Judge("https://www.youtube.com/watch?v=M7lc1UVf-VE&pp=x", out var id));
            Assert.AreEqual("M7lc1UVf-VE", id);
            Assert.AreEqual(SearchBrowser.Verdict.Pick, SearchBrowser.Judge("https://www.youtube.com/shorts/aqz-KE-bpKQ", out id));
            Assert.AreEqual("aqz-KE-bpKQ", id);
            foreach (var refused in new[]
                     {
                         "https://www.youtube.com/@SingKingKaraoke", "https://www.youtube.com/", "https://accounts.google.com/ServiceLogin",
                         "http://www.youtube.com/results?search_query=a", "https://example.com/watch?v=M7lc1UVf-VE",
                         "https://www.youtube.com.evil.example/results", "https://www.youtube.com/watch?v=nope",
                     })
                Assert.AreEqual(SearchBrowser.Verdict.Refuse, SearchBrowser.Judge(refused, out _), refused);
        }

        /// <summary>T12.15: Search opens the panel over the room (walking waits, the guide goes); Close ends its browser.</summary>
        [UnityTest]
        public IEnumerator TheSearchPanel_OpensOverTheRoom_AndClosesItsBrowser()
        {
            var root = _hud.GetComponent<UIDocument>().rootVisualElement;
            yield return null;
            Assert.IsNotNull(root.Q<Button>("screenSearch"), "the bar has a Search button");
            _hud.SongSearch.Open("bohemian rhapsody");
            yield return null;
            yield return null;
            Assert.IsTrue(_hud.SongSearch.Visible);
            Assert.IsNotNull(_hud.SongSearch.Browser, "its own browser runs while it is open");
            Assert.IsTrue(Walker.Paused, "walking waits while searching");
            Assert.IsTrue(_hud.TypingInUi(), "the HUD's keys wait too");
            Assert.IsTrue(root.Q("walkGuide").ClassListContains("hidden"), "the guide card goes");
            ClickButton(root.Q<Button>("songSearchClose"));
            yield return null;
            yield return null;
            Assert.IsFalse(_hud.SongSearch.Visible);
            Assert.IsNull(_hud.SongSearch.Browser);
            Assert.IsNull(Object.FindAnyObjectByType<SearchBrowser>(), "its browser is gone");
            Assert.IsFalse(Walker.Paused);
        }

        /// <summary>Clicks a button the way the mouse does (a click, not a keyboard submit).</summary>
        private static void ClickButton(Button button)
        {
            var at = button.worldBound.center;
            foreach (var type in new[] { EventType.MouseDown, EventType.MouseUp })
            {
                var imgui = new Event { type = type, mousePosition = at, button = 0, pointerType = UnityEngine.PointerType.Mouse };
                using EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui) : PointerUpEvent.GetPooled(imgui);
                e.target = button;
                button.SendEvent(e);
            }
        }

        [UnityTest]
        public IEnumerator AnotherRoom_BringsTheTableBack()
        {
            Assert.IsTrue(_table.Load(Karaoke.Replace("karaoke", "gameNight"), null));
            yield return null;
            yield return null;
            Assert.IsFalse(TableGeometry.Walk);
            Assert.IsFalse(TableGeometry.Hidden, "the table is back");
            Assert.IsNull(_chalkers.Walker, "walking ends");
            Assert.IsTrue(Object.FindAnyObjectByType<TableCamera>().enabled, "the table camera is back");
            Assert.AreEqual(_table.Engine.State.Players.Seats.Count - 1, _chalkers.Chairs.Count);
            Assert.IsNull(_environment.Screen);
            Assert.IsNull(_environment.Lounge);
        }
    }
}
