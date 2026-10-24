using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T2.11g: the device bindings of docs/tts-interactions.md, with a simulated keyboard and mouse (Input System test
    /// fixture). Each test moves the pointer over a real object in the Table scene and presses the TTS key or button.
    /// </summary>
    public class TtsBindingTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private Mouse _mouse;
        private TableView _table;
        private TableInteraction _interaction;
        private TableCamera _camera;
        private Camera _view;

        public override void Setup()
        {
            base.Setup();
            // The game reads devices directly; the project-wide actions asset only reacts (noisily) to the fixture's device swaps.
            InputSystem.actions?.Disable();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            // Wait for the load to finish: right after LoadScene the previous test's table can still be found.
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _camera = Object.FindAnyObjectByType<TableCamera>();
            _view = _camera.GetComponent<Camera>();
            yield return new WaitForSeconds(0.5f);
        }

        private TableObject M(string id) => _table.Engine.Find(id);

        private Vector2 ScreenOf(string id) => _view.WorldToScreenPoint(_table.ViewOf(id).transform.position);

        private Vector2 ScreenOf(Vector3 world) => _view.WorldToScreenPoint(world);

        private IEnumerator Point(Vector2 screen)
        {
            Set(_mouse.position, screen);
            yield return null;
            yield return null;
        }

        private IEnumerator Tap(ButtonControl button)
        {
            Press(button);
            yield return null;
            Release(button);
            yield return null;
        }

        private IEnumerator Chord(ButtonControl modifier, ButtonControl key)
        {
            Press(modifier);
            yield return null;
            yield return Tap(key);
            Release(modifier);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Keys_F_R_L_QE_Delete_OverTheHoveredObject()
        {
            yield return Point(ScreenOf("ace_spades"));
            Assert.AreEqual("ace_spades", _interaction.Hovered?.Id);
            yield return Tap(_keyboard.fKey);
            Assert.IsFalse(M("ace_spades").IsFaceUp, "F flips");
            yield return Tap(_keyboard.eKey);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(15f, M("ace_spades").Transform.Rotation.Y, 0.5f, "E rotates the hovered object");
            yield return Tap(_keyboard.lKey);
            Assert.IsTrue(M("ace_spades").Locked, "L locks");
            yield return Tap(_keyboard.lKey);

            yield return Point(ScreenOf("d6"));
            yield return Tap(_keyboard.rKey);
            Assert.IsNotNull(((Die)M("d6")).Value, "R rolls");

            yield return Point(ScreenOf("token_red"));
            yield return Tap(_keyboard.deleteKey);
            Assert.IsNull(M("token_red"), "Delete deletes");
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.zKey);
            Assert.IsNotNull(M("token_red"), "Ctrl+Z undoes");
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.yKey);
            Assert.IsNull(M("token_red"), "Ctrl+Y redoes");
        }

        [UnityTest]
        public IEnumerator R_OverASelectedDie_RollsEverySelectedDie()
        {
            var rolled = new System.Collections.Generic.List<string>();
            void Count(Command command, CommandResult result) { if (command is RollCommand roll && result.Ok) rolled.Add(roll.ObjectId); }
            _table.CommandExecuted += Count;
            _interaction.SetSelection(new[] { _table.ViewOf("d6"), _table.ViewOf("d20") });
            yield return Point(ScreenOf("d6"));
            yield return Tap(_keyboard.rKey);
            _table.CommandExecuted -= Count;
            CollectionAssert.AreEquivalent(new[] { "d6", "d20" }, rolled);
            var status = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement.Q<Label>("statusLabel").text;
            var total = ((Die)M("d6")).Value + ((Die)M("d20")).Value;
            StringAssert.Contains("(d6)", status);
            StringAssert.Contains("(d20)", status);
            StringAssert.EndsWith($"total {total}", status, "the status line lists both dice and their total");
        }

        [UnityTest]
        public IEnumerator Keys_CopyPaste_Scale_Group()
        {
            yield return Point(ScreenOf("token_blue"));
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.cKey);
            yield return Point(ScreenOf(new Vector3(0.3f, 0f, 0.15f)));
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.vKey);
            Assert.IsNotNull(M("token_blue-1"), "Ctrl+C / Ctrl+V pastes a copy under the pointer");

            yield return Point(ScreenOf("tile_hex"));
            Press(_keyboard.equalsKey);
            yield return null;
            Release(_keyboard.equalsKey);
            yield return null;
            Assert.AreEqual(1.1f, M("tile_hex").Transform.Scale.X, 0.001f, "+ scales up");

            _interaction.SetSelection(new[] { _table.ViewOf("ace_spades"), _table.ViewOf("king_hearts") });
            yield return Point(ScreenOf("ace_spades"));
            yield return Tap(_keyboard.gKey);
            Assert.IsNull(M("ace_spades"), "G groups the selection into a deck");
        }

        [UnityTest]
        public IEnumerator NumberRow_TwoDigitsWithinASecond_DrawsTwelve()
        {
            yield return Point(ScreenOf("deck_main"));
            yield return Tap(_keyboard.digit1Key);
            yield return Tap(_keyboard.digit2Key);
            Assert.AreEqual("12", _interaction.PendingDigits);
            yield return new WaitForSeconds(TableInteraction.DigitBufferSeconds + 0.2f);
            Assert.AreEqual(42, ((Deck)M("deck_main")).Cards.Count);
        }

        [UnityTest]
        public IEnumerator Camera_Space_P_Z_CtrlAndShiftDigits()
        {
            yield return Tap(_keyboard.pKey);
            Assert.IsTrue(_camera.IsTopDown, "P: top-down");
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.digit4Key);
            yield return Tap(_keyboard.spaceKey);
            Assert.IsFalse(_camera.IsTopDown, "Space resets");
            yield return Chord(_keyboard.leftShiftKey, _keyboard.digit4Key);
            Assert.Greater(_camera.Pitch, 80f, "Shift+4 loads the camera saved with Ctrl+4");
            yield return Point(ScreenOf(new Vector3(0f, 0f, 0f)));
            yield return Tap(_keyboard.zKey);
            Assert.IsTrue(_camera.IsQuickZoomed, "Z zooms to the pointer");
        }

        [UnityTest]
        public IEnumerator LeftDrag_GrabsAndDrops_ShiftDragMovesAWholeDeck()
        {
            yield return Point(ScreenOf("queen_clubs"));
            Press(_mouse.leftButton);
            yield return null;
            Assert.AreEqual("queen_clubs", _interaction.Held?.Id);
            yield return Point(ScreenOf(new Vector3(0.25f, 0f, 0.1f)));
            yield return new WaitForSeconds(0.3f);
            Release(_mouse.leftButton);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(0.25f, M("queen_clubs").Transform.Position.X, 0.03f);

            yield return Point(ScreenOf("deck_up"));
            Press(_keyboard.leftShiftKey);
            Press(_mouse.leftButton);
            yield return null;
            Release(_keyboard.leftShiftKey);
            Assert.AreEqual("deck_up", _interaction.Held?.Id, "Shift + drag takes the whole deck");
            Release(_mouse.leftButton);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PressOnADeck_DragTakesTheTopCard_HoldLiftsTheDeck_ClickOnlySelects()
        {
            var deck = (Deck)M("deck_main");
            var count = deck.Cards.Count;
            var at = ScreenOf("deck_main");

            // Click: nothing is taken or lifted.
            yield return Point(at);
            Press(_mouse.leftButton);
            yield return null;
            Assert.IsNull(_interaction.Held, "a press alone does not take anything yet");
            Release(_mouse.leftButton);
            yield return null;
            Assert.IsNull(_interaction.Held);
            Assert.AreEqual(count, ((Deck)M("deck_main")).Cards.Count);
            Assert.AreEqual("deck_main", _interaction.Selected?.Id);

            // Press and keep still: the whole deck lifts.
            Press(_mouse.leftButton);
            yield return new WaitForSeconds(TableInteraction.HoldToLiftSeconds + 0.15f);
            Assert.AreEqual("deck_main", _interaction.Held?.Id, "holding still lifts the deck");
            Release(_mouse.leftButton);
            yield return new WaitForSeconds(1f);

            // Press and drag at once: the top card comes out.
            yield return Point(ScreenOf("deck_main"));
            Press(_mouse.leftButton);
            yield return null;
            yield return Point(ScreenOf("deck_main") + new Vector2(40f, 0f));
            yield return null;
            Assert.IsNotNull(_interaction.Held);
            Assert.AreNotEqual("deck_main", _interaction.Held.Id, "dragging takes the top card");
            Assert.AreEqual(count - 1, ((Deck)M("deck_main")).Cards.Count);
            Release(_mouse.leftButton);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChipStack_DropAddsToIt_DragTakesOne_HoldLiftsIt_MenuSplits()
        {
            int Count(string id) => Stacks.CountOf(M(id));
            Assert.AreEqual(10, Count("chips_gold"));
            StringAssert.EndsWith("stack of 10", TableOverlay.TooltipText(_table.ViewOf("chips_gold"), _table.Engine));

            // Carry the loose chip over the stack and let go: it joins.
            yield return Point(ScreenOf("chip_gold"));
            Press(_mouse.leftButton);
            yield return null;
            Assert.AreEqual("chip_gold", _interaction.Held?.Id);
            yield return Point(ScreenOf("chips_gold"));
            yield return new WaitForSeconds(0.3f);
            Release(_mouse.leftButton);
            yield return new WaitForSeconds(0.5f);
            Assert.IsNull(M("chip_gold"), "the loose chip joined the stack");
            Assert.AreEqual(11, Count("chips_gold"));

            // Press and drag at once: one chip comes off the top.
            yield return Point(ScreenOf("chips_gold"));
            Press(_mouse.leftButton);
            yield return null;
            yield return Point(ScreenOf("chips_gold") + new Vector2(60f, 0f));
            yield return null;
            var taken = _interaction.Held;
            Assert.IsNotNull(taken);
            Assert.AreNotEqual("chips_gold", taken.Id, "dragging takes one chip");
            Assert.AreEqual(1, Stacks.CountOf(taken.Model));
            Assert.AreEqual(10, Count("chips_gold"));
            yield return Point(ScreenOf(new Vector3(0.2f, 0f, -0.15f)));
            yield return new WaitForSeconds(0.2f);
            Release(_mouse.leftButton);
            yield return new WaitForSeconds(0.8f);

            // Press and keep still: the whole stack lifts.
            yield return Point(ScreenOf("chips_gold"));
            Press(_mouse.leftButton);
            yield return new WaitForSeconds(TableInteraction.HoldToLiftSeconds + 0.15f);
            Assert.AreEqual("chips_gold", _interaction.Held?.Id, "holding still lifts the whole stack");
            Release(_mouse.leftButton);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(10, Count("chips_gold"));

            // Right-click menu: split in half.
            var overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            overlay.OpenMenu(_table.ViewOf("chips_gold"), Vector2.zero);
            CollectionAssert.Contains(overlay.MenuEntries, "Split in half");
            Assert.IsTrue(overlay.Choose("Split in half"));
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(5, Count("chips_gold"));
            var height = _table.ViewOf("chips_gold").GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            Assert.AreEqual(5 * ObjectViewFactory.TokenThickness, height, 0.0005f, "the stack is as tall as its chips");
        }

        /// <summary>Screen position (Input System pixels, origin bottom left) of a HUD element's centre.</summary>
        private static Vector2 ScreenOf(UnityEngine.UIElements.VisualElement element)
        {
            var root = element.panel.visualTree;
            var scale = Screen.width / root.worldBound.width;
            var c = element.worldBound.center;
            return new Vector2(c.x * scale, Screen.height - c.y * scale);
        }

        /// <summary>A point on the visible top strip of a hand card (resting cards are mostly below the screen edge).</summary>
        [UnityTest]
        public IEnumerator FreeTurns_OnFromTheTableMenu_EndTurnPassesThePlay()
        {
            var hud = Object.FindAnyObjectByType<HudController>();
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            hud.Overlay.OpenMenu(null, new Vector2(Screen.width / 2f, Screen.height / 2f));
            Assert.IsTrue(hud.Overlay.Choose("Turns: In order"));
            Assert.AreEqual("seat_south", _table.Engine.State.FreeTurns?.Current);
            yield return new WaitForSeconds(0.4f);
            StringAssert.StartsWith("seat_south's turn", root.Q<Label>("turnLabel").text);
            Assert.AreEqual(1, root.Query(className: "seat-chip--turn").ToList().Count, "the current seat's chip is marked");

            var endTurn = root.Q("gameActions").Q<Button>();
            Assert.IsNotNull(endTurn);
            StringAssert.StartsWith("End turn", endTurn.text);
            Assert.AreEqual(endTurn, endTurn.panel.Pick(endTurn.worldBound.center), "nothing covers the button");
            // Same as the hand toggle test: the fixture's virtual mouse does not reach UI Toolkit buttons, a submit does.
            using (var click = NavigationSubmitEvent.GetPooled()) { click.target = endTurn; endTurn.SendEvent(click); }
            yield return null;
            Assert.AreEqual("seat_north", _table.Engine.State.FreeTurns.Current, "End turn passes the turn");
            Assert.AreEqual("seat_north", _table.LocalSeat, "at a hot-seat table the next seat plays");
            yield return new WaitForSeconds(0.4f);
            StringAssert.StartsWith("seat_north's turn", root.Q<Label>("turnLabel").text);

            hud.Overlay.OpenMenu(null, Vector2.zero);
            Assert.IsTrue(hud.Overlay.Choose("Turns: Off"));
            Assert.IsNull(_table.Engine.State.FreeTurns);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual("Free play", root.Q<Label>("turnLabel").text);
            Assert.IsTrue(root.Q("gamePanel").ClassListContains("hidden"));
        }

        private static Vector2 TopOf(VisualElement element)
        {
            var root = element.panel.visualTree;
            var scale = Screen.width / root.worldBound.width;
            var r = element.worldBound;
            return new Vector2(r.center.x * scale, Screen.height - (r.yMin + 10f) * scale);
        }

        [UnityTest]
        public IEnumerator HandBar_DragACardOutOntoTheTable_ThenDropItBackOnTheBar()
        {
            Assert.IsTrue(_table.Execute(new MoveCommand { ObjectId = "king_hearts", Position = new Vec3(0f, 0f, -0.42f), Actor = "seat_south" }).Ok);
            yield return new WaitForSeconds(0.5f);
            var hud = Object.FindAnyObjectByType<HudController>();
            // The shown/hidden choice is remembered between sessions; this test starts from a shown hand.
            hud.HandBar.SetCollapsed(false);
            var root = hud.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement;
            var card = root.Q("hand-king_hearts");
            Assert.IsNotNull(card, "the hand bar shows the card");

            // Out of the bar onto the table.
            yield return Point(TopOf(card));
            Press(_mouse.leftButton);
            for (var i = 0; i < 5 && _interaction.Held == null; i++)
                yield return null;
            Assert.AreEqual("king_hearts", _interaction.Held?.Id, "pressing a card in the bar picks it up");
            yield return Point(ScreenOf(new Vector3(0.2f, 0f, 0.05f)));
            yield return new WaitForSeconds(0.4f);
            Release(_mouse.leftButton);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(_table.Engine.ZonesOf("king_hearts").Contains("hand_south"));
            Assert.AreEqual(0.2f, M("king_hearts").Transform.Position.X, 0.03f);
            Assert.IsNull(root.Q("hand-king_hearts"));

            // Back: pick it up on the table, release over the bar.
            yield return Point(ScreenOf("king_hearts"));
            Press(_mouse.leftButton);
            yield return null;
            Assert.AreEqual("king_hearts", _interaction.Held?.Id);
            var bar = root.Q("handBar");
            yield return Point(ScreenOf(bar));
            yield return new WaitForSeconds(0.3f);
            Release(_mouse.leftButton);
            yield return null;
            Assert.IsTrue(_table.Engine.ZonesOf("king_hearts").Contains("hand_south"), "released over the bar: back in the hand, at once");
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(_table.ViewOf("king_hearts").InHand);
            Assert.IsNotNull(root.Q("hand-king_hearts"));

            // Hide hand: the cards sink to a thin strip, the button says so, and Show brings them back.
            var toggle = root.Q<Button>("handToggle");
            var shown = root.Q("hand-king_hearts").worldBound.yMin;
            using (var click = NavigationSubmitEvent.GetPooled()) { click.target = toggle; toggle.SendEvent(click); }
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual("Show hand", toggle.text);
            Assert.Greater(root.Q("hand-king_hearts").worldBound.yMin, shown + 30f, "hidden: the card sinks");
            Assert.Less(root.Q("hand-king_hearts").worldBound.yMin, root.panel.visualTree.worldBound.height, "a strip stays visible");
            using (var click = NavigationSubmitEvent.GetPooled()) { click.target = toggle; toggle.SendEvent(click); }
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual("Hide hand", toggle.text);
            Assert.AreEqual(shown, root.Q("hand-king_hearts").worldBound.yMin, 1f);
        }

        /// <summary>Heading of an object about the vertical, 0..360 (independent of flips).</summary>
        private float Heading(string id)
        {
            var f = _table.ViewOf(id).transform.forward;
            return Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
        }

        [UnityTest]
        public IEnumerator HoldingQE_TurnsContinuously_TapsSnapToTheNextStep()
        {
            yield return Point(ScreenOf("king_hearts"));
            Assert.AreEqual("king_hearts", _interaction.Hovered?.Id);

            // Hold E: it turns smoothly while held and stops where it is released, off the 15° grid.
            Press(_keyboard.eKey);
            yield return new WaitForSeconds(0.25f);
            var during = Heading("king_hearts");
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(Heading("king_hearts"), during + 5f, "keeps turning while held");
            Release(_keyboard.eKey);
            yield return new WaitForSeconds(0.4f);
            var stopped = Heading("king_hearts");
            Assert.Greater(stopped, 10f);
            Assert.Greater(Mathf.Abs(stopped / 15f - Mathf.Round(stopped / 15f)), 0.02f, $"stops anywhere ({stopped:F1}°), not on a step");
            Assert.AreEqual(stopped, M("king_hearts").Transform.Rotation.Y, 0.5f, "the state records where it stopped");

            // Tap E: on to the next multiple of 15. Tap Q twice: back two steps on the grid.
            yield return Tap(_keyboard.eKey);
            yield return new WaitForSeconds(0.3f);
            var next = Mathf.Ceil(stopped / 15f) * 15f;
            Assert.AreEqual(next, Heading("king_hearts"), 0.5f, $"E from {stopped:F1}° snaps to {next}°");
            Assert.AreEqual(next, M("king_hearts").Transform.Rotation.Y, 0.5f);
            yield return Tap(_keyboard.qKey);
            yield return Tap(_keyboard.qKey);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(next - 30f, Heading("king_hearts"), 0.5f, "two quick Q taps: two steps back");
        }

        [UnityTest]
        public IEnumerator F1AndQuestionMark_ToggleTheControlsList_EscapeCloses()
        {
            var help = Object.FindAnyObjectByType<HudController>().Help;
            Assert.IsFalse(help.Visible);
            yield return Tap(_keyboard.f1Key);
            Assert.IsTrue(help.Visible, "F1 opens the controls");
            yield return Tap(_keyboard.f1Key);
            Assert.IsFalse(help.Visible, "F1 again closes them");
            yield return Chord(_keyboard.leftShiftKey, _keyboard.slashKey);
            Assert.IsTrue(help.Visible, "? opens them");
            yield return Tap(_keyboard.escapeKey);
            Assert.IsFalse(help.Visible, "Escape closes them");
            var root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            Assert.Greater(root.Query(className: "help-row").ToList().Count, 40, "every binding is listed");

            // The wheel over the open list scrolls it, not the camera; over the table it zooms again.
            yield return Tap(_keyboard.f1Key);
            Assert.IsTrue(help.Visible);
            yield return Point(ScreenOf(root.Q("helpPanel")));
            var distance = _camera.Distance;
            Set(_mouse.scroll, new Vector2(0f, -120f));
            yield return null;
            Set(_mouse.scroll, Vector2.zero);
            yield return null;
            Assert.AreEqual(distance, _camera.Distance, 0.0001f, "no camera zoom over the panel");
            yield return Tap(_keyboard.escapeKey);
            yield return Point(ScreenOf(new Vector3(0.3f, 0f, 0.2f)));
            Set(_mouse.scroll, new Vector2(0f, -120f));
            yield return null;
            Set(_mouse.scroll, Vector2.zero);
            yield return null;
            Assert.AreNotEqual(distance, _camera.Distance, "the wheel zooms over the table");
        }

        [UnityTest]
        public IEnumerator LeftDragOnEmptyTable_BoxSelects()
        {
            var a = ScreenOf(new Vector3(-0.4f, 0f, -0.2f));
            var b = ScreenOf(new Vector3(0.2f, 0f, -0.05f));
            yield return Point(a);
            Press(_mouse.leftButton);
            yield return null;
            yield return Point(b);
            Assert.IsNotNull(_interaction.BoxRect);
            Release(_mouse.leftButton);
            yield return null;
            Assert.IsTrue(_interaction.Selection.Any(v => v.Id == "ace_spades"));
            Assert.IsTrue(_interaction.Selection.Any(v => v.Id == "joker" || v.Id == "red_joker"));
        }

        [UnityTest]
        public IEnumerator RightClick_OpensMenus_RightClickWhileHolding_AddsToTheGroup()
        {
            var overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            yield return Point(ScreenOf("deck_main"));
            yield return Tap(_mouse.rightButton);
            Assert.IsTrue(overlay.MenuOpen);
            Assert.Contains("Search", overlay.MenuEntries.ToList());
            overlay.CloseMenu();

            yield return Point(ScreenOf("ten_diamonds"));
            Press(_mouse.leftButton);
            yield return null;
            yield return Point(ScreenOf("king_hearts"));
            yield return Tap(_mouse.rightButton);
            Assert.AreEqual(2, _interaction.HeldGroup.Count);
            Press(_keyboard.leftAltKey);
            yield return Tap(_mouse.rightButton);
            Release(_keyboard.leftAltKey);
            Assert.AreEqual(1, _interaction.HeldGroup.Count, "Alt + right click drops the last one");
            Release(_mouse.leftButton);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MiddleClick_Flips_Alt_LooksAndPeeks()
        {
            yield return Point(ScreenOf("king_hearts"));
            yield return Tap(_mouse.middleButton);
            Assert.IsFalse(M("king_hearts").IsFaceUp, "middle click flips");

            Press(_keyboard.leftAltKey);
            yield return null;
            yield return null;
            Assert.AreEqual("king_hearts", _interaction.Looking?.Id, "Alt looks");
            Press(_keyboard.leftShiftKey);
            yield return null;
            yield return null;
            Assert.IsTrue(_interaction.Peeking, "Alt + Shift peeks");
            Release(_keyboard.leftShiftKey);
            Release(_keyboard.leftAltKey);
            yield return null;
            yield return null;
            Assert.IsNull(_interaction.Looking);
        }

        [UnityTest]
        public IEnumerator Tab_ClickPings_DragMeasures()
        {
            Press(_keyboard.tabKey);
            yield return Point(ScreenOf(new Vector3(0.1f, 0f, 0.2f)));
            yield return Tap(_mouse.leftButton);
            Assert.IsNotNull(Object.FindAnyObjectByType<PingMarker>(), "Tab + click pings");

            Press(_mouse.leftButton);
            yield return null;
            yield return Point(ScreenOf(new Vector3(0.3f, 0f, 0.2f)));
            Assert.AreEqual(0.2f, _interaction.MeasuredDistance ?? 0f, 0.02f, "Tab + drag measures");
            Release(_mouse.leftButton);
            Release(_keyboard.tabKey);
            yield return null;
            Assert.IsNull(_interaction.MeasuredDistance);
        }

        [UnityTest]
        public IEnumerator Hover_ShowsTheTooltip()
        {
            yield return Point(ScreenOf("ace_spades"));
            yield return new WaitForSeconds(TableOverlay.TooltipNameSeconds + 0.2f);
            Assert.AreEqual("ace_spades", _interaction.TooltipView?.Id);
            Assert.Greater(_interaction.TooltipSeconds, TableOverlay.TooltipNameSeconds);

            // Decks and bags show how much they hold (TTS).
            var deck = (Deck)M("deck_main");
            yield return Point(ScreenOf("deck_main"));
            yield return new WaitForSeconds(TableOverlay.TooltipNameSeconds + 0.2f);
            var root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            StringAssert.EndsWith($"{deck.Cards.Count} cards", root.Q<Label>("tooltip").text);
            Assert.IsFalse(root.Q<Label>("tooltip").ClassListContains("hidden"));
            StringAssert.EndsWith("3 items", TableOverlay.TooltipText(_table.ViewOf("bag"), _table.Engine));
            StringAssert.EndsWith("endless", TableOverlay.TooltipText(_table.ViewOf("coin_supply"), _table.Engine));
        }

        [UnityTest]
        public IEnumerator WheelWhileHolding_Raises_U_PlacesUnder_N_Nudges()
        {
            yield return Point(ScreenOf("king_hearts"));
            Press(_mouse.leftButton);
            yield return new WaitForSeconds(0.3f);
            var before = _table.ViewOf("king_hearts").transform.position.y;
            Set(_mouse.scroll, new Vector2(0f, 120f));
            yield return null;
            Set(_mouse.scroll, Vector2.zero);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(_table.ViewOf("king_hearts").transform.position.y, before + 0.005f, "wheel raises the held object");

            yield return Point(ScreenOf("deck_up"));
            yield return Tap(_keyboard.uKey);
            Assert.AreEqual("king_hearts", ((Deck)M("deck_up")).Cards.Last().Id, "U places it under the deck");
            Release(_mouse.leftButton);
            yield return null;

            var token = _table.ViewOf("token_blue").transform.position;
            yield return Point(ScreenOf(token + new Vector3(-0.01f, 0f, 0f)));
            Press(_keyboard.nKey);
            yield return null;
            Release(_keyboard.nKey);
            yield return new WaitForSeconds(1f);
            Assert.Greater(_table.ViewOf("token_blue").transform.position.x, token.x + 0.01f, "N nudges");
        }

        [UnityTest]
        public IEnumerator Enter_OpensChat_KeysTypeInsteadOfActing_RollsAndActionsAreLogged()
        {
            var hud = Object.FindAnyObjectByType<HudController>();
            var chat = hud.Chat;
            var session = Object.FindAnyObjectByType<TableWorld.View.Net.NetworkTableSession>();
            Assert.IsFalse(chat.Open);
            yield return Point(ScreenOf("ace_spades"));
            yield return Tap(_keyboard.enterKey);
            yield return null;
            Assert.IsTrue(chat.Open, "Enter opens the chat");
            Assert.IsTrue(hud.TypingInUi(), "the input has the keyboard");

            // A real key press also reaches the focused input as a UI key event (the fixture's keyboard does not send
            // one): the Enter that opened the chat must not send and close it at once.
            var input = hud.GetComponent<UIDocument>().rootVisualElement.Q<TextField>("chatInput");
            chat.Hide();
            yield return null;
            yield return null;
            chat.Show();
            using (var enter = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { enter.target = input; input.SendEvent(enter); }
            yield return null;
            Assert.IsTrue(chat.Open, "the opening Enter does not close the chat");
            yield return null;
            Assert.IsTrue(chat.Open, "nor a frame later");

            var faceUp = M("ace_spades").IsFaceUp;
            yield return Tap(_keyboard.fKey);
            yield return Tap(_keyboard.wKey);
            Assert.AreEqual(faceUp, M("ace_spades").IsFaceUp, "F types a letter while chatting, it does not flip");

            chat.Send("hello table");
            chat.Send("/roll 3d6+1");
            chat.Send("/nonsense");
            var entries = session.Chat.Entries;
            var said = entries.Single(e => e.Kind == Core.Net.ChatKind.Say);
            Assert.AreEqual("hello table", said.Text);
            Assert.AreEqual(_table.LocalSeat, said.Seat);
            var roll = entries.Single(e => e.Kind == Core.Net.ChatKind.Roll);
            Assert.AreEqual(3, roll.Dice.Count);
            StringAssert.StartsWith("rolled 3d6+1: ", roll.Text);
            Assert.IsTrue(entries.Any(e => e.Kind == Core.Net.ChatKind.Notice && e.Text.Contains("Unknown command")), "a bad command answers the sender");
            yield return null;
            Assert.IsTrue(chat.DrawnText.Contains("hello table"));

            chat.Hide();
            yield return null;
            Assert.IsFalse(hud.TypingInUi());
            yield return Point(ScreenOf("ace_spades"));
            yield return Tap(_keyboard.fKey);
            Assert.AreNotEqual(faceUp, M("ace_spades").IsFaceUp, "closed, F flips again");
            Assert.IsTrue(entries.Any(e => e.Kind == Core.Net.ChatKind.Action && e.Text.Contains("flipped")), "the flip is in the game log");

            // Closed, the chat still shows the recent messages, never the action log.
            yield return null;
            Assert.IsTrue(chat.DrawnText.Contains("hello table"));
            Assert.IsFalse(chat.DrawnText.Any(t => t.Contains("flipped")));
            yield return Tap(_keyboard.enterKey);
            yield return null;
            chat.ShowTab(log: true);
            yield return null;
            Assert.IsTrue(chat.DrawnText.Any(t => t.Contains("flipped")), "the Log tab lists actions");
            chat.Hide();
        }
    }
}
