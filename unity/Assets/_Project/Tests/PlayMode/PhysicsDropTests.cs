using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// Careless drops, the way people actually play: thrown, half on another card, half on a deck, on a hand zone's edge,
    /// near the rim, from high up. After release a card must come to rest quickly, close to where it was let go, flat,
    /// on the table, with its state recording that rest pose. Each test logs its numbers.
    /// </summary>
    public class PhysicsDropTests
    {
        /// <summary>A card may travel at most this far after release (throws included).</summary>
        public const float MaxSlide = 0.05f;
        public const float MaxSettleSeconds = 1.5f;

        private TableView _table;
        private TableInteraction _interaction;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            // Wait for the load to finish: right after LoadScene the previous test's table can still be found.
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            yield return new WaitForSeconds(0.5f);
        }

        private ObjectView V(string id) => _table.ViewOf(id);

        /// <summary>
        /// Grabs <paramref name="id"/>, carries it to <paramref name="to"/> (fast: the last frames move it
        /// <paramref name="throwBy"/> further), releases, and watches it until it rests or 3 s pass.
        /// </summary>
        private IEnumerator Drop(string id, Vector3 to, Vector3 throwBy, float raise, List<string> report)
        {
            var view = V(id);
            Assert.IsTrue(_interaction.Grab(view, false), "grab " + id);
            _interaction.RaiseHeld(raise);
            _interaction.DragTo(to);
            yield return new WaitForSeconds(0.6f);
            if (throwBy != Vector3.zero)
            {
                _interaction.DragTo(to + throwBy);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
            }
            view = _interaction.Held;
            var releasedAt = view.transform.position;
            _interaction.Release();
            yield return null;
            if (view == null)
            {
                // It joined what it was dropped on (TTS): that pile must rest instead.
                var joined = _table.Views.Values.FirstOrDefault(v => v != null && v.Model is Deck d && d.Cards.Any(c => c.Id == id));
                Assert.IsNotNull(joined, $"{id} vanished without joining a deck");
                var cards = string.Join(", ", ((Deck)joined.Model).Cards.Select(c => c.Id));
                report.Add($"{id}: joined {joined.Id} ({cards})");
                Debug.Log($"[PhysicsDrop] {id}: joined {joined.Id} ({cards}) at {joined.transform.position}");
                view = joined;
                releasedAt = joined.transform.position;
            }
            var path = 0f;
            var last = releasedAt;
            var settled = -1f;
            for (var t = 0f; t < 3f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                if (view == null)
                    break;
                var p = view.transform.position;
                path += Vector3.Distance(Flat(p), Flat(last));
                last = p;
                var resting = view.Body.isKinematic || view.Body.IsSleeping() || view.Body.linearVelocity.magnitude < 0.002f && view.Body.angularVelocity.magnitude < 0.05f;
                if (resting && settled < 0f)
                    settled = t;
                if (!resting)
                    settled = -1f;
            }
            yield return new WaitForSeconds(0.3f);
            if (view.InHand)
            {
                // Dropped into a hand zone: it joins the hand's layout, standing in its slot.
                Assert.IsTrue(_table.Engine.ZonesOf(view.Id).Any(z => _table.Engine.State.Zones.First(x => x.Id == z) is HandZone));
                Debug.Log($"[PhysicsDrop] {id}: joined the hand");
                report.Add($"{id}: joined the hand");
                yield break;
            }
            var slide = Vector3.Distance(Flat(view.transform.position), Flat(releasedAt));
            var tilt = Vector3.Angle(view.transform.up, Vector3.up);
            tilt = Mathf.Min(tilt, 180f - tilt);
            var model = _table.Engine.Find(view.Id);
            var stateGap = Vector3.Distance(model.Transform.Position.ToUnity(), view.transform.position);
            var line = $"{id}: slide {slide * 100f:F1} cm, path {path * 100f:F1} cm, settled {(settled < 0 ? "no" : settled.ToString("F2") + " s")}, tilt {tilt:F1}°, y {view.transform.position.y * 1000f:F1} mm, state gap {stateGap * 1000f:F1} mm";
            Debug.Log("[PhysicsDrop] " + line);
            report.Add(line);
            Assert.Less(slide, MaxSlide, line);
            Assert.Less(tilt, 3.5f, "rests flat: " + line);
            Assert.GreaterOrEqual(settled, 0f, line);
            Assert.Less(settled, MaxSettleSeconds, line);
            Assert.Greater(view.transform.position.y, -0.001f, "stays on the table: " + line);
            Assert.Less(stateGap, 0.002f, "state records the rest pose: " + line);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [UnityTest]
        public IEnumerator Card_DroppedStill_OnFelt() => Drop("ace_spades", new Vector3(0.2f, 0f, 0.05f), Vector3.zero, 0f, new List<string>());

        [UnityTest]
        public IEnumerator Card_ThrownFast() => Drop("ace_spades", new Vector3(0.0f, 0f, 0.0f), new Vector3(0.3f, 0f, 0.1f), 0f, new List<string>());

        [UnityTest]
        public IEnumerator Card_DroppedHalfOnAnotherCard()
        {
            // Overlapping by half: it must land on it (or beside it) and stop, not glide off.
            var other = V("king_hearts").transform.position;
            yield return Drop("ace_spades", other + new Vector3(0.032f, 0f, 0.02f), Vector3.zero, 0f, new List<string>());
        }

        [UnityTest]
        public IEnumerator Card_DroppedHalfOnADeck()
        {
            var deck = V("deck_main").transform.position;
            yield return Drop("ten_diamonds", deck + new Vector3(0.045f, 0f, 0.03f), Vector3.zero, 0f, new List<string>());
        }

        [UnityTest]
        public IEnumerator Card_BarelyOverADeck_EndsFlat()
        {
            // 8 % overlap: below the join threshold, so it must not stay propped on the deck's edge.
            var deck = V("deck_main").transform.position;
            yield return Drop("ten_diamonds", deck + new Vector3(0.058f, 0f, 0.0f), Vector3.zero, 0f, new List<string>());
        }

        [UnityTest]
        public IEnumerator Card_DroppedHalfOnADie()
        {
            var die = V("d6").transform.position;
            yield return Drop("ace_spades", die + new Vector3(0.03f, 0f, 0.02f), Vector3.zero, 0f, new List<string>());
        }

        [UnityTest]
        public IEnumerator Token_DroppedMostlyOverABag_GoesIn()
        {
            var bag = V("bag").transform.position;
            Assert.IsTrue(_interaction.Grab(V("token_red"), false));
            _interaction.DragTo(bag + new Vector3(0.01f, 0f, 0.005f));
            yield return new WaitForSeconds(0.6f);
            _interaction.Release();
            yield return null;
            Assert.AreEqual("bag", _table.Engine.Find("token_red").Container);
        }

        [UnityTest]
        public IEnumerator Cards_DroppedSloppilyIntoAHandWithCards_AllStandInTheHand()
        {
            // hand_south: centre (0, -0.42), 0.6 × 0.12. Two neat drops, then a sloppy one half over the hand's edge
            // and onto the standing cards.
            var spots = new[] { new Vector3(-0.05f, 0f, -0.42f), new Vector3(0.05f, 0f, -0.43f), new Vector3(0.02f, 0f, -0.37f) };
            var ids = new[] { "ace_spades", "ten_diamonds", "king_hearts" };
            for (var i = 0; i < ids.Length; i++)
            {
                Assert.IsTrue(_interaction.Grab(V(ids[i]), false));
                _interaction.DragTo(spots[i]);
                yield return new WaitForSeconds(0.6f);
                _interaction.Release();
                yield return new WaitForSeconds(1.2f);
            }
            foreach (var id in ids)
            {
                var view = V(id);
                Assert.IsTrue(view.InHand, $"{id} stands in the hand (at {view.transform.position:F3})");
                Assert.IsTrue(_table.Engine.ZonesOf(id).Contains("hand_south"), id);
            }
            // Standing cards stay put in a neat row: same height, same lean, evenly spaced.
            var xs = ids.Select(id => V(id).transform.position).OrderBy(p => p.x).ToList();
            Assert.AreEqual(xs[1].x - xs[0].x, xs[2].x - xs[1].x, 0.001f);
            Assert.AreEqual(xs[0].y, xs[2].y, 0.001f);
        }

        [UnityTest]
        public IEnumerator Card_DroppedFromHigh() => Drop("queen_clubs", new Vector3(-0.05f, 0f, 0.05f), Vector3.zero, 0.2f, new List<string>());

        [UnityTest]
        public IEnumerator Card_ThrownAtTheRim() => Drop("red_joker", new Vector3(0.6f, 0f, 0.35f), new Vector3(0.3f, 0f, 0.3f), 0f, new List<string>());

        [UnityTest]
        public IEnumerator Card_DroppedOnAHandZoneEdge()
        {
            // hand_south spans x ±0.3, z -0.48..-0.36: drop across its far edge.
            yield return Drop("king_hearts", new Vector3(0.1f, 0f, -0.36f), Vector3.zero, 0f, new List<string>());
        }

        [UnityTest]
        public IEnumerator Card_DroppedOnTopOfATileAndADie()
        {
            var between = (V("tile_square").transform.position + V("token_red").transform.position) / 2f;
            yield return Drop("ace_spades", between, Vector3.zero, 0f, new List<string>());
        }
    }
}
