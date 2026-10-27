using System.Collections;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T12.6: the karaoke room has sofas instead of chairs with every seat on one, a big screen on the far wall, and the
    /// singer holds a microphone under the stage light. Another room puts the chairs back.
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
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _chalkers.Singer = null;
            _hud.Escape.Set(s => s.HearMyself = false);
            yield return null;
        }

        [Test]
        public void EverySeat_SitsOnASofa_NoChairs()
        {
            Assert.IsTrue(_chalkers.Karaoke);
            Assert.AreEqual(0, _chalkers.Chairs.Count, "sofas instead of chairs");
            Assert.Greater(_chalkers.Sofas.Count, 0);
            var seats = _table.Engine.State.Players.Seats.Select(s => s.Id).ToList();
            Assert.AreEqual(seats.Count, _chalkers.Sofas.Values.Sum(s => s.Places), "a place on a sofa for every seat, yours included");
            var places = seats.Select(s => _chalkers.Place(s).Value).ToList();
            var cushions = _chalkers.Sofas.Values.SelectMany(s => Enumerable.Range(0, s.Places).Select(s.Seat)).ToList();
            var half = TableGeometry.Size / 2f;
            foreach (var (place, seat) in places.Zip(seats, (p, s) => (p, s)))
            {
                var at = place.Position;
                Assert.IsTrue(Mathf.Abs(at.x) > half.x || Mathf.Abs(at.z) > half.y, $"{seat} sits outside the table");
                Assert.Greater(Vector3.Dot(place.Facing, new Vector3(-at.x, 0f, -at.z).normalized), 0.3f, $"{seat} faces the table");
                var cushion = cushions.OrderBy(c => Vector3.Distance(Flat(c), Flat(at))).First();
                Assert.Less(Vector3.Distance(Flat(cushion), Flat(at)), 0.1f, $"{seat} is on a cushion");
                Assert.AreEqual(ChalkerChair.SeatTop, cushion.y, 0.01f, "cushions are at chair height");
            }
            for (var i = 0; i < places.Count; i++)
                for (var j = i + 1; j < places.Count; j++)
                    Assert.GreaterOrEqual(Vector3.Distance(places[i].Position, places[j].Position), ChalkerSofa.PlaceWidth - 0.01f, "nobody shares a place");
            // The screen is on the far wall: with room on the sides, nobody sits with their back to it.
            Assert.IsFalse(places.Any(p => p.Position.z > half.y), "nobody on the far side, facing away from the screen");
            Assert.IsFalse(_chalkers.Sofas.ContainsKey(2));
        }

        [Test]
        public void TheRoom_HasABigScreen_FacingIn()
        {
            Assert.AreEqual("Room (karaoke)", _environment.Room.name);
            var screen = _environment.Screen;
            Assert.IsNotNull(screen);
            Assert.Greater(screen.bounds.size.x, 2f, "a big screen");
            Assert.Greater(screen.transform.position.z, EnvironmentView.RoomHalfSize.z - 0.2f, "on the far wall");
            // A quad shows its -Z side.
            Assert.Less(Vector3.Dot(screen.transform.forward, -screen.transform.position.normalized), 0f, "it faces into the room");
            Assert.IsFalse(_environment.StageLight.enabled, "no singer, no stage light");
        }

        [UnityTest]
        public IEnumerator TheSinger_HoldsTheMic_UnderTheStageLight()
        {
            _hud.Escape.Set(s => s.HearMyself = true);
            yield return null;
            _chalkers.Singer = _table.LocalSeat;
            yield return null;
            yield return null;
            var chalker = _chalkers.Shown[ChalkersView.MirrorKey];
            Assert.IsTrue(chalker.Microphone);
            Assert.IsNotNull(chalker.Mic);
            Assert.IsTrue(chalker.Mic.gameObject.activeInHierarchy);
            var tip = chalker.Mic.position + chalker.Mic.forward * Chalker.MicLength;
            Assert.Less(Vector3.Distance(tip, chalker.Mouth), 0.07f, "the microphone is at the mouth");
            Assert.Less(Vector3.Distance(chalker.Mic.position, chalker.RightHand.position), 0.01f, "held in the right hand");
            // On a sofa: the thighs rest on a cushion.
            var thigh = chalker.Root.GetComponentsInChildren<Renderer>().First(r => r.name == "Thigh").bounds;
            Assert.AreEqual(ChalkerChair.SeatTop, thigh.min.y, 0.02f, "sitting on the cushion");

            var stage = _environment.StageLight;
            Assert.IsTrue(stage.enabled);
            var toSinger = chalker.Root.transform.position + Vector3.up * 0.2f - stage.transform.position;
            Assert.Less(Vector3.Angle(stage.transform.forward, toSinger), EnvironmentView.StageLightAngle / 2f, "the singer is in the light");

            _chalkers.Singer = null;
            yield return null;
            Assert.IsFalse(chalker.Mic.gameObject.activeInHierarchy, "the microphone goes away");
            Assert.IsFalse(stage.enabled);
        }

        /// <summary>The sample game: in a game in the karaoke room, whoever's turn it is sings, and passing the microphone moves it.</summary>
        [UnityTest]
        public IEnumerator InAGame_TheTurnHolderSings()
        {
            var entry = GameLibrary.List().FirstOrDefault(e => e.File.EndsWith("karaoke-night.json"));
            Assert.IsNotNull(entry, "Karaoke night is in Games");
            Assert.IsTrue(_table.Load(System.IO.File.ReadAllText(entry.File), entry.PackageRoot));
            yield return null;
            Assert.IsTrue(_chalkers.Karaoke);
            Assert.IsNull(_chalkers.Singer, "nobody sings before the start");
            Assert.IsTrue(_table.Execute(new Core.Engine.StartGameCommand()).Ok);
            var first = _table.Game.Session.CurrentSeat;
            Assert.AreEqual(first, _chalkers.Singer);
            yield return null;
            Assert.IsTrue(_environment.StageLight.enabled, "the stage light is on the singer");
            Assert.IsTrue(_table.Execute(new Core.Engine.PerformActionCommand { Actor = first, ActionId = "pass_mic" }).Ok);
            Assert.AreNotEqual(first, _chalkers.Singer, "the microphone passed");
        }

        [UnityTest]
        public IEnumerator AnotherRoom_PutsTheChairsBack()
        {
            Assert.IsTrue(_table.Load(Karaoke.Replace("karaoke", "gameNight"), null));
            yield return null;
            yield return null;
            Assert.IsFalse(_chalkers.Karaoke);
            Assert.AreEqual(0, _chalkers.Sofas.Count);
            Assert.AreEqual(_table.Engine.State.Players.Seats.Count - 1, _chalkers.Chairs.Count);
            Assert.IsNull(_environment.Screen);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
