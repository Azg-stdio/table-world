using System.Collections;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Cards lying face up on the table are drawn turned toward the local seat, a quarter turn at a time, so they read the
    /// right way up; only the drawing and the click box turn, never the card's pose (Miguel, 2026-10-30).
    /// </summary>
    public class CardsFaceMeTests
    {
        private TableView _table;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return null;
        }

        [TearDown]
        public void RestoreSetting() => TableView.CardsFaceMe = true;

        [UnityTest]
        public IEnumerator ACardLyingSideways_IsDrawnTurnedToMe_WithoutMoving()
        {
            Assert.AreEqual("seat_south", _table.LocalSeat);
            var card = _table.Engine.Find("ace_spades");
            Assert.IsTrue(card.IsFaceUp);
            // Yaw 90: sideways to the south seat (it reads upright from there at yaw 180).
            Assert.IsTrue(_table.Execute(new MoveCommand { ObjectId = "ace_spades", Position = card.Transform.Position, Rotation = new Vec3(0f, 90f, 0f) }).Ok);
            yield return new WaitForSeconds(0.5f);
            var view = _table.ViewOf("ace_spades");
            Assert.AreEqual(1, view.TurnQuarters, "drawn a quarter turn toward the south seat");
            var box = (BoxCollider)view.Collider;
            Assert.Greater(box.size.x, box.size.z, "the click box turns with the drawing (a card is taller than wide)");
            Assert.AreEqual(90f, _table.Engine.Find("ace_spades").Transform.Rotation.Y, 0.5f, "the card's own pose is untouched");

            // The Alt close-up shows it as drawn for this player: its top edge away from the south seat (+Z), whatever
            // way the card really lies, face up or (its back) face down.
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            var look = Object.FindAnyObjectByType<LookCamera>();
            foreach (var rotation in new[] { new Vec3(0f, 90f, 0f), new Vec3(0f, 0f, 0f), new Vec3(0f, 270f, 0f), new Vec3(0f, 0f, 180f), new Vec3(0f, 90f, 180f) })
            {
                Assert.IsTrue(_table.Execute(new MoveCommand { ObjectId = "ace_spades", Position = card.Transform.Position, Rotation = rotation }).Ok);
                yield return new WaitForSeconds(0.5f);
                interaction.Look(view, false);
                yield return null;
                yield return null;
                Assert.Less(Vector3.Angle(look.transform.up, Vector3.forward), 5f, $"lying at {rotation}: the close-up reads upright from the seat");
                interaction.Look(null, false);
            }
            Assert.IsTrue(_table.Execute(new MoveCommand { ObjectId = "ace_spades", Position = card.Transform.Position, Rotation = new Vec3(0f, 90f, 0f) }).Ok);
            yield return new WaitForSeconds(0.5f);

            TableView.CardsFaceMe = false;
            yield return null;
            yield return null;
            Assert.AreEqual(0, view.TurnQuarters, "with the setting off it is drawn as it lies");
            Assert.Less(box.size.x, box.size.z);
        }
    }
}
