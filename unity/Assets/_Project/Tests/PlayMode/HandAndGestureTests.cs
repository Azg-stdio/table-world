using System.Collections;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>UNO as people play it: hands stand up for their owner (backs for others), cards are played and drawn by dragging.</summary>
    public class HandAndGestureTests
    {
        private TableView _table;
        private TableInteraction _interaction;

        [UnitySetUp]
        public IEnumerator LoadUno()
        {
            // Wait for the load to finish: right after LoadScene the previous test's table can still be found.
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            var uno = GameLibrary.List().First(e => e.File.EndsWith("uno-lite.json"));
            Assert.IsTrue(_table.Load(File.ReadAllText(uno.File), uno.PackageRoot));
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            yield return null;
        }

        private string Current => _table.Game.Session.CurrentSeat;

        private string HandOf(string seat) => _table.Engine.State.Players.Seats.First(s => s.Id == seat).HandZone;

        [UnityTest]
        public IEnumerator Hands_StandForTheirOwner_AndHideFromOthers()
        {
            yield return new WaitForSeconds(0.4f);
            var me = _table.LocalSeat;
            var other = _table.Engine.State.Players.Seats.First(s => s.Id != me).Id;
            foreach (var card in _table.Engine.ObjectsIn(HandOf(me)))
            {
                var view = _table.ViewOf(card.Id);
                Assert.IsTrue(view.InHand && !view.HideFace, card.Id);
                Assert.AreEqual(TableView.HandTiltDegrees, Vector3.Angle(view.transform.up, Vector3.up), 1f, "leans toward its owner");
            }
            foreach (var card in _table.Engine.ObjectsIn(HandOf(other)))
                Assert.IsTrue(_table.ViewOf(card.Id).HideFace, card.Id);
            // A card in someone else's hand cannot be picked up.
            Assert.IsFalse(_interaction.Grab(_table.ViewOf(_table.Engine.ObjectsIn(HandOf(other)).First().Id), false));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DraggingCards_PlaysOrDraws_ThroughTheRules()
        {
            var seat = Current;
            var hand = HandOf(seat);
            var playable = _table.Game.LegalActions(seat).Where(a => a.ActionId == "play_card").SelectMany(a => a.Choices).Select(c => c["card"]).ToList();
            var unplayable = _table.Engine.ObjectsIn(hand).Select(o => o.Id).FirstOrDefault(id => !playable.Contains(id));

            if (unplayable != null)
            {
                // Dropped on the table: refused, back in the hand, still this seat's turn.
                Assert.IsTrue(_interaction.Grab(_table.ViewOf(unplayable), false));
                _interaction.DragTo(new Vector3(0.1f, 0f, 0f));
                yield return new WaitForSeconds(0.3f);
                _interaction.Release();
                yield return null;
                Assert.IsTrue(_table.Engine.ZonesOf(unplayable).Contains(hand));
                Assert.AreEqual(seat, Current);
            }

            if (playable.Count > 0)
            {
                var card = playable[0];
                Assert.IsTrue(_interaction.Grab(_table.ViewOf(card), false));
                _interaction.DragTo(new Vector3(0.1f, 0f, 0f));
                yield return new WaitForSeconds(0.3f);
                _interaction.Release();
                yield return null;
                Assert.AreEqual(card, ((Deck)_table.Engine.Find("discard")).Cards[0].Id);
            }
            else
            {
                // Nothing to play: dragging the draw pile draws and ends the turn.
                var before = _table.Engine.ObjectsIn(hand).Count();
                Assert.IsFalse(_interaction.Grab(_table.ViewOf("draw"), false));
                Assert.AreEqual(before + 1, _table.Engine.ObjectsIn(hand).Count());
            }
            Assert.AreNotEqual(seat, Current, "the turn passed");
            // The discard pile belongs to the rules: no free takes.
            Assert.IsFalse(_interaction.Grab(_table.ViewOf("discard"), false));
        }
    }
}
