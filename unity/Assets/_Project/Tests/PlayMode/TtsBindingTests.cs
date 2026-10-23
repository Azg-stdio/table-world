using System.Collections;
using System.Linq;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
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
            SceneManager.LoadScene("Table");
            yield return null;
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
    }
}
