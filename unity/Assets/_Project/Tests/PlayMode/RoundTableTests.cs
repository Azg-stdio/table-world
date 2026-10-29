using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// A round table (M12): ten seats evenly round a disc, the box top and rims swapped for a disc and a ring that pieces
    /// rest on, the drop and lost limits round, and each chair straight out from the centre facing it.
    /// </summary>
    public class RoundTableTests
    {
        private TableView _table;

        private static string Game()
        {
            var (size, places) = SeatLayout.Arrange(10, TableShape.Round);
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var json = new StringBuilder();
            json.Append("{ \"environment\": { \"table\": { \"shape\": \"round\", \"size\": [").Append(F(size.X)).Append(", ").Append(F(size.Y)).Append("] } },");
            json.Append(" \"players\": { \"seats\": [");
            json.Append(string.Join(", ", places.Select(p => $"{{ \"id\": \"{p.SeatId}\", \"color\": \"{p.Color}\", \"handZone\": \"{p.HandId}\" }}")));
            json.Append("] }, \"zones\": [");
            json.Append(string.Join(", ", places.Select(p =>
                $"{{ \"id\": \"{p.HandId}\", \"type\": \"HandZone\", \"owner\": \"{p.SeatId}\", \"size\": [{F(p.Width)}, 0.1, 0.12], " +
                $"\"transform\": {{ \"position\": [{F(p.Position.X)}, 0.05, {F(p.Position.Z)}], \"rotation\": [0, {F(p.Yaw)}, 0] }} }}")));
            json.Append("], \"objects\": [ { \"id\": \"die\", \"type\": \"Die\", \"transform\": { \"position\": [0.62, 0.15, -0.3] } } ] }");
            return json.ToString();
        }

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            Assert.IsTrue(_table.Load(Game(), null), "the round table loads");
            yield return new WaitForSeconds(1.5f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            // The next test's scene starts on the default table.
            TableGeometry.Use(null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTable_IsADisc_PiecesRestOn_AndItsLimitsAreRound()
        {
            Assert.IsTrue(TableGeometry.Round);
            Assert.AreEqual(TableGeometry.Size.x, TableGeometry.Size.y, 1e-4f);
            var parts = GameObject.Find("Table").transform.Cast<Transform>().ToList();
            var disc = parts.Single(p => p.name == EnvironmentView.RoundSurfaceName);
            var ring = parts.Single(p => p.name == EnvironmentView.RoundRimName);
            Assert.IsTrue(disc.gameObject.activeInHierarchy && ring.gameObject.activeInHierarchy);
            Assert.IsTrue(disc.GetComponent<MeshCollider>().convex, "the disc's collider is solid");
            Assert.IsFalse(parts.Single(p => p.name == "Surface").gameObject.activeSelf, "the box top is put away");
            Assert.IsFalse(parts.Single(p => p.name == "RimNorth").gameObject.activeSelf);

            // The die dropped above the disc lands on it.
            var die = _table.ViewOf("die").transform.position;
            Assert.That(die.y, Is.InRange(-0.01f, 0.1f), $"the die rests on the disc ({die})");

            // Inside the square but off the disc: lost; a drop there is pulled in onto the disc.
            var r = TableGeometry.Radius;
            var offDisc = new Vector3(r * 0.85f, 0f, r * 0.85f);
            Assert.IsTrue(TableGeometry.IsLost(offDisc + new Vector3(0.1f, 0f, 0.1f)));
            Assert.IsFalse(TableGeometry.OnSurface(offDisc));
            var dropped = TableGeometry.ClampToDropArea(offDisc);
            Assert.LessOrEqual(new Vector2(dropped.x, dropped.z).magnitude, r - TableGeometry.DropMargin + 1e-4f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryChair_StandsStraightOutFromTheCentre_FacingIt()
        {
            var chalkers = Object.FindAnyObjectByType<ChalkersView>();
            foreach (var seat in _table.Engine.State.Players.Seats)
            {
                var place = chalkers.Place(seat.Id);
                Assert.IsNotNull(place, seat.Id);
                var (position, facing) = place.Value;
                var flat = new Vector2(position.x, position.z);
                Assert.AreEqual(TableGeometry.Radius + ChalkersView.SeatDistance, flat.magnitude, 0.01f, $"{seat.Id}'s chair is a step from the edge");
                Assert.Greater(Vector3.Dot(facing.normalized, -new Vector3(position.x, 0f, position.z).normalized), 0.99f, $"{seat.Id} faces the centre");
            }
            yield return null;
        }
    }
}
