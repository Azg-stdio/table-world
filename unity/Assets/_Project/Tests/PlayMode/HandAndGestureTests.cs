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
            // TTS hands: the free camera (your hand stands at your edge of the table).
            var camera = Object.FindAnyObjectByType<TableCamera>();
            camera.FirstPerson = false;
            camera.ResetView();
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
                // No legal play for it: let go on the table, it goes back into the hand (the game would otherwise lose it).
                Assert.IsTrue(_interaction.Grab(_table.ViewOf(unplayable), false));
                _interaction.DragTo(new Vector3(-0.6f, 0f, 0.3f));
                yield return new WaitForSeconds(0.3f);
                _interaction.Release();
                yield return new WaitForSeconds(0.5f);
                Assert.IsTrue(_table.Engine.ZonesOf(unplayable).Contains(hand), "an unplayable card goes back into the hand: at " + _table.Engine.Find(unplayable).Transform.Position);
                Assert.AreEqual(seat, Current);
            }

            if (playable.Count > 0)
            {
                var card = playable[0];
                // A drawn card is named afresh in a rules game (T11.16); back in a deck it is its deck id again.
                var entry = ((Card)_table.Engine.Find(card)).EntryId ?? card;
                Assert.IsTrue(_interaction.Grab(_table.ViewOf(card), false));
                _interaction.DragTo(new Vector3(0.1f, 0f, 0f));
                yield return new WaitForSeconds(0.3f);
                _interaction.Release();
                yield return null;
                Assert.AreEqual(entry, ((Deck)_table.Engine.Find("discard")).Cards[0].Id);
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

        [UnityTest]
        public IEnumerator WhileTheGameWaitsForAPlayerClick_AThrownCard_GoesBackToTheHand()
        {
            // Love Letter with four: a Guard, Priest, Baron, King or Prince played waits for a click on a player.
            var game = GameLibrary.List().First(e => e.File.EndsWith("love-letter.json"));
            Assert.IsTrue(_table.Load(File.ReadAllText(game.File), game.PackageRoot));
            Assert.IsTrue(_table.Execute(new StartGameCommand { Seats = new System.Collections.Generic.List<string> { "seat_1", "seat_2", "seat_3", "seat_4" } }).Ok);
            yield return null;
            var seat = Current;
            var hand = HandOf(seat);
            var choosing = new[] { "Guard", "Priest", "Baron", "King", "Prince" };
            var cards = _table.Engine.ObjectsIn(hand).ToList();
            var aimed = cards.FirstOrDefault(c => choosing.Contains(c.Name) && !(cards.Any(o => o.Name == "Countess") && (c.Name == "King" || c.Name == "Prince")));
            if (aimed == null)
                Assert.Ignore("no card in this deal waits for a player");
            Assert.IsTrue(_table.Execute(_table.Game.LegalActions(seat).Where(a => a.ActionId == "play_card").SelectMany(a => a.Choices)
                .Where(c => c["card"] == aimed.Id).Select(c => new PerformActionCommand { Actor = seat, ActionId = "play_card", Params = c }).First()).Ok);
            Assert.AreEqual("choose", _table.Game.Session.Phase);

            var other = _table.Engine.ObjectsIn(hand).Single();
            Assert.IsTrue(_interaction.Grab(_table.ViewOf(other.Id), false));
            _interaction.DragTo(new Vector3(0.1f, 0f, 0.1f));
            yield return new WaitForSeconds(0.3f);
            _interaction.Release();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_table.Engine.ZonesOf(other.Id).Contains(hand), "the card goes back into the hand");
            Assert.AreEqual("choose", _table.Game.Session.Phase, "the game still waits for the click");
            Assert.IsTrue(_table.Game.Session.Log.Any(l => l.Contains("click") || l.Contains("clic")), "the log says to click a player: " + string.Join(" / ", _table.Game.Session.Log));
        }

        [UnityTest]
        public IEnumerator APlayableCard_IsPlayed_WhereverItIsLetGo_EvenOverTheHand()
        {
            // Love Letter: every card in the hand can be played (the Countess rule aside), so the deal never matters.
            var game = GameLibrary.List().First(e => e.File.EndsWith("love-letter.json"));
            Assert.IsTrue(_table.Load(File.ReadAllText(game.File), game.PackageRoot));
            Assert.IsTrue(_table.Execute(new StartGameCommand { Seats = new System.Collections.Generic.List<string> { "seat_1", "seat_3" } }).Ok);
            yield return null;
            var seat = Current;
            var hand = HandOf(seat);
            var playable = _table.Game.LegalActions(seat).Where(a => a.ActionId == "play_card").SelectMany(a => a.Choices).Select(c => c["card"])
                .Where(id => _table.Engine.Find(id).Name != "King" && _table.Engine.Find(id).Name != "Prince").ToList();
            if (playable.Count == 0)
                Assert.Ignore("only the King and a Prince in hand: the Countess rule may refuse them");
            Assert.IsTrue(_interaction.Grab(_table.ViewOf(playable[0]), false));
            // Let go just in front of the player, over their own hand zone: still a play, not back into the hand.
            _interaction.DragTo(_table.Engine.State.Zones.First(z => z.Id == hand).Transform.Position.ToUnity());
            yield return new WaitForSeconds(0.3f);
            _interaction.Release();
            yield return null;
            Assert.IsFalse(_table.Engine.ZonesOf(playable[0]).Contains(hand), "played, not put back into the hand");
            Assert.IsTrue(_table.Game.Session.Log.Any(l => l.Contains("plays") || l.Contains("juega")), string.Join(" / ", _table.Game.Session.Log));
        }
    }
}
