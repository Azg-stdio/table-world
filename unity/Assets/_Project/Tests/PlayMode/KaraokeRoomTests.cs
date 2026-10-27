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
            Assert.IsNotNull(bar.Screen);
            Assert.AreEqual("M7lc1UVf-VE", bar.Screen.VideoId);
            Assert.AreEqual(bar.Screen.transform.parent, _environment.Screen.transform, "on the room's screen");

            Assert.IsTrue(_table.Load(Karaoke.Replace("karaoke", "gameNight"), null));
            yield return null;
            yield return null;
            Assert.IsTrue(root.Q("screenBar").ClassListContains("hidden"), "hidden in another room");
            Assert.IsNull(bar.Screen, "the screen goes with the room");
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
