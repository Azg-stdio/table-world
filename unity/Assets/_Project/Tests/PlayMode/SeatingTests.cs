using System.Collections;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using TableWorld.View.Hud;
using TableWorld.View.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T11.10: a chair at every seat but the player's own, outside the table's edge and facing in, never two in the same
    /// place; a chalker sits on its chair with its hands on the table.
    /// </summary>
    public class SeatingTests
    {
        private HudController _hud;
        private ChalkersView _chalkers;
        private TableView _table;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _hud = Object.FindAnyObjectByType<HudController>();
            _chalkers = Object.FindAnyObjectByType<ChalkersView>();
            _table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _hud.Escape.Set(s => s.HearMyself = false);
            yield return null;
        }

        [Test]
        public void EverySeatButYours_HasAChair_AtTheEdge()
        {
            var seats = _table.Engine.State.Players.Seats.Select(s => s.Id).Where(s => s != _table.LocalSeat).ToList();
            Assert.Greater(seats.Count, 0, "the sample table has other seats");
            CollectionAssert.AreEquivalent(seats, _chalkers.Chairs.Keys);
            var half = TableGeometry.Size / 2f;
            foreach (var (seat, chair) in _chalkers.Chairs)
            {
                var at = chair.Root.transform.position;
                Assert.IsTrue(Mathf.Abs(at.x) > half.x || Mathf.Abs(at.z) > half.y, $"{seat}'s chair is outside the table");
                var toCentre = new Vector3(-at.x, 0f, -at.z);
                Assert.Greater(Vector3.Dot(chair.Root.transform.forward, toCentre.normalized), 0.3f, $"{seat}'s chair faces the table");
                Assert.Less(chair.Seat.y, 0f, "the seat is below the table top");
            }
            var chairs = _chalkers.Chairs.Values.ToList();
            for (var i = 0; i < chairs.Count; i++)
                for (var j = i + 1; j < chairs.Count; j++)
                    Assert.Greater(Vector3.Distance(chairs[i].Root.transform.position, chairs[j].Root.transform.position), ChalkerChair.SeatWidth, "chairs do not overlap");
        }

        [UnityTest]
        public IEnumerator TenSeats_WithoutHands_SpreadRoundTheTable()
        {
            var colors = new[] { "white", "brown", "red", "orange", "yellow", "green", "teal", "blue", "purple", "pink" };
            var seats = string.Join(",", colors.Select((c, i) => $"{{\"id\":\"seat_{i}\",\"color\":\"{c}\"}}"));
            Assert.IsTrue(_table.Load($"{{\"schemaVersion\":\"0.1\",\"objects\":[],\"players\":{{\"seats\":[{seats}]}}}}", null));
            yield return null;
            yield return null;
            Assert.AreEqual(9, _chalkers.Chairs.Count, "every seat but yours");
            var chairs = _chalkers.Chairs.Values.Select(c => c.Root.transform.position).ToList();
            for (var i = 0; i < chairs.Count; i++)
                for (var j = i + 1; j < chairs.Count; j++)
                    Assert.Greater(Vector3.Distance(chairs[i], chairs[j]), ChalkerChair.SeatWidth, "chairs do not overlap");
        }

        /// <summary>T11.12: O puts the eyes where this player's chalker would sit, looking across; again goes back.</summary>
        [UnityTest]
        public IEnumerator SeatView_PutsTheEyesAtYourChair_AndGoesBack()
        {
            var camera = Object.FindAnyObjectByType<TableCamera>();
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            var before = camera.transform.position;
            interaction.ToggleSeatView();
            yield return null;
            Assert.IsTrue(camera.IsSeatView);
            var place = _chalkers.Place(_table.LocalSeat).Value;
            var eye = place.Position + Vector3.up * Chalker.EyeHeight;
            Assert.AreEqual(0f, Vector3.Distance(eye, camera.transform.position), 0.02f, "eyes at the chair");
            Assert.Greater(Vector3.Dot(camera.transform.forward, place.Facing), 0.7f, "looking across the table");
            interaction.ToggleSeatView();
            yield return null;
            Assert.IsFalse(camera.IsSeatView);
            Assert.AreEqual(0f, Vector3.Distance(before, camera.transform.position), 0.02f, "back where it was");
        }

        /// <summary>A hot seat: a second seat taken from this window shows a chalker, though nobody else is connected.</summary>
        [UnityTest]
        public IEnumerator ASeatTakenFromThisWindow_ShowsAChalker()
        {
            var rules = "{\"schemaVersion\":\"0.1\",\"objects\":[],\"players\":{\"seats\":[{\"id\":\"a\",\"color\":\"red\"},{\"id\":\"b\",\"color\":\"blue\"},{\"id\":\"c\",\"color\":\"green\"}]},"
                + "\"turn\":{\"phases\":[\"p\"]},\"phases\":[{\"id\":\"p\",\"actions\":[\"x\"]}],\"actions\":[{\"id\":\"x\",\"actor\":\"current\",\"effects\":[{\"endTurn\":{}}]}]}";
            Assert.IsTrue(_table.Load(rules, null));
            yield return null;
            Assert.IsFalse(_chalkers.Shown.ContainsKey("b"));
            _hud.ClickSeat("b");
            yield return null;
            yield return null;
            Assert.IsTrue(_chalkers.Shown.ContainsKey("b"), "the second seat shows its chalker");
            Assert.IsFalse(_chalkers.Shown.ContainsKey(_table.LocalSeat), "never one where the camera sits");
            Assert.IsFalse(_chalkers.Shown.ContainsKey("c"), "an empty seat keeps only its chair");
        }

        [Test]
        public void AtEdge_KeepsClearOfTheCorners_AndFacesIn()
        {
            var half = TableGeometry.Size / 2f;
            var (position, facing) = ChalkersView.AtEdge(new Vector3(0.3f, 0f, -1f).normalized, new Vector3(5f, 0f, -half.y));
            Assert.AreEqual(-half.y - ChalkersView.SeatDistance, position.z, 1e-4f, "on the near side");
            Assert.AreEqual(half.x - ChalkersView.CornerClearance, position.x, 1e-4f, "slid back from the corner");
            Assert.AreEqual(Vector3.forward, facing);
            var (side, sideFacing) = ChalkersView.AtEdge(Vector3.right, null);
            Assert.AreEqual(half.x + ChalkersView.SeatDistance, side.x, 1e-4f);
            Assert.AreEqual(Vector3.left, sideFacing);
        }

        [UnityTest]
        public IEnumerator Chalker_SitsOnItsChair_HandsOnTheTable()
        {
            _hud.Escape.Set(s => s.HearMyself = true);
            yield return null;
            yield return null;
            var chalker = _chalkers.Shown[ChalkersView.MirrorKey];
            // Its own chair, or the seat's chair already where it sits.
            var chair = _chalkers.Chairs.Values.OrderBy(c => Vector3.Distance(c.Root.transform.position, chalker.Root.transform.position)).First();
            Assert.AreEqual(chair.Root.transform.position, chalker.Root.transform.position, "the chalker is at its chair");
            Assert.AreEqual(1, _chalkers.Chairs.Values.Count(c => Vector3.Distance(c.Root.transform.position, chalker.Root.transform.position) < ChalkerChair.SeatWidth),
                "one chair under it, never two");
            var thigh = chalker.Root.GetComponentsInChildren<Renderer>().First(r => r.name == "Thigh").bounds;
            Assert.AreEqual(chair.Seat.y, thigh.min.y, 0.02f, "the thighs rest on the cushion");
            var feet = chalker.Root.GetComponentsInChildren<Renderer>().Where(r => r.name == "Foot").ToList();
            Assert.AreEqual(2, feet.Count);
            foreach (var foot in feet)
                Assert.AreEqual(EnvironmentView.FloorY, foot.bounds.min.y, 0.01f, "the feet are on the floor");
            var hand = chalker.RightHand.position;
            Assert.AreEqual(0.04f, hand.y, 0.03f, "the hand rests on the table top");
            var edge = (chalker.Root.transform.position - hand).magnitude;
            Assert.Greater(Vector3.Dot(hand - chalker.Root.transform.position, chalker.Root.transform.forward), ChalkersView.SeatDistance - 0.02f, "past the table's edge");
            Assert.Less(edge, 0.5f);
        }
    }
}
