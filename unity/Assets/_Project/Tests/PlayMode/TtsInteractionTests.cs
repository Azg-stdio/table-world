using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T2.11b–f: every TTS interaction in docs/tts-interactions.md, through the same public gestures and menu entries
    /// the keys and clicks use, in the real Table scene with the sample table. Device bindings are covered separately.
    /// </summary>
    public class TtsInteractionTests
    {
        private TableView _table;
        private TableInteraction _interaction;
        private TableCamera _camera;
        private TableOverlay _overlay;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            SceneManager.LoadScene("Table");
            yield return null;
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _camera = Object.FindAnyObjectByType<TableCamera>();
            _overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            yield return new WaitForSeconds(0.5f);
        }

        private ObjectView V(string id) => _table.ViewOf(id);
        private TableObject M(string id) => _table.Engine.Find(id);

        // ---- 2. picking up and moving ----

        [UnityTest]
        public IEnumerator Rotate_HoveredAndHeld_InFifteenDegreeSteps()
        {
            Assert.IsTrue(_interaction.Rotate(V("ace_spades"), 15f));
            Assert.AreEqual(15f, M("ace_spades").Transform.Rotation.Y, 0.01f);
            Assert.IsTrue(_interaction.Rotate(V("ace_spades"), 90f, otherAxis: true));
            Assert.AreNotEqual(0f, M("ace_spades").Transform.Rotation.X + M("ace_spades").Transform.Rotation.Z);

            _interaction.Grab(V("king_hearts"), false);
            _interaction.Rotate(V("king_hearts"), -15f);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(345f, V("king_hearts").transform.eulerAngles.y, 0.5f);
            _interaction.Release();
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(345f, M("king_hearts").Transform.Rotation.Y, 1f);
        }

        [UnityTest]
        public IEnumerator Flip_AndFlipOnTheOtherAxis()
        {
            Assert.IsTrue(_interaction.Flip(V("ace_spades")));
            Assert.IsFalse(M("ace_spades").IsFaceUp);
            Assert.IsTrue(_interaction.Flip(V("queen_clubs"), otherAxis: true));
            Assert.IsFalse(M("queen_clubs").IsFaceUp);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scale_GrowsAndShrinks()
        {
            Assert.IsTrue(_interaction.Scale(new[] { V("token_red") }, TableInteraction.ScaleStep));
            Assert.AreEqual(1.1f, M("token_red").Transform.Scale.X, 0.001f);
            Assert.IsTrue(_interaction.Scale(new[] { V("token_red") }, 1f / TableInteraction.ScaleStep));
            Assert.AreEqual(1f, M("token_red").Transform.Scale.X, 0.001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaceUnder_PutsAHeldCardAtTheBottomOfADeck()
        {
            _interaction.Grab(V("king_hearts"), false);
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(_interaction.PlaceUnder(V("deck_up")));
            Assert.AreEqual("king_hearts", ((Deck)M("deck_up")).Cards.Last().Id);
        }

        [UnityTest]
        public IEnumerator PlaceUnder_SlidesAnObjectUnderAnother()
        {
            _interaction.Grab(V("tile_square"), false);
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(_interaction.PlaceUnder(V("tile_hex")));
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(V("tile_hex").transform.position.y, V("tile_square").transform.position.y);
        }

        [UnityTest]
        public IEnumerator Nudge_PushesNearbyObjectsAway()
        {
            var before = V("token_blue").transform.position;
            Assert.Greater(_interaction.Nudge(before + new Vector3(-0.01f, 0f, 0f)), 0);
            yield return new WaitForSeconds(1f);
            Assert.Greater(V("token_blue").transform.position.x, before.x + 0.01f);
        }

        [UnityTest]
        public IEnumerator TapDown_AndWheelRaise_ChangeTheHoldHeight()
        {
            _interaction.Grab(V("ace_spades"), false);
            yield return new WaitForSeconds(0.3f);
            var normal = V("ace_spades").transform.position.y;
            _interaction.SetTapDown(true);
            yield return new WaitForSeconds(0.3f);
            Assert.Less(V("ace_spades").transform.position.y, normal - 0.02f);
            _interaction.SetTapDown(false);
            _interaction.RaiseHeld(0.1f);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(V("ace_spades").transform.position.y, normal + 0.05f);
            _interaction.Release();
        }

        [UnityTest]
        public IEnumerator HeldGroup_AddWithRightClick_DropLastWithAltRightClick()
        {
            _interaction.Grab(V("ace_spades"), false);
            Assert.IsTrue(_interaction.AddToHeld(V("king_hearts")));
            Assert.AreEqual(2, _interaction.HeldGroup.Count);
            _interaction.DragTo(new Vector3(0.3f, 0f, 0.3f));
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(_interaction.DropLast());
            Assert.AreEqual(1, _interaction.HeldGroup.Count);
            _interaction.Release();
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(M("ace_spades").Transform.Position.Z, 0.2f);
            Assert.Greater(M("king_hearts").Transform.Position.Z, 0.2f);
        }

        [UnityTest]
        public IEnumerator DropLast_FromAHeldDeck_DropsItsBottomCard()
        {
            Assert.IsTrue(_interaction.Grab(V("deck_up"), moveContainer: true));
            Assert.IsTrue(_interaction.DropLast());
            Assert.AreEqual(2, ((Deck)M("deck_up")).Cards.Count);
            Assert.IsNotNull(M("deck_up-1"));
            _interaction.Release();
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoxSelect_ThenPickUpTheWholeSelection()
        {
            var count = _interaction.SelectInScreenRect(new Rect(0, 0, Screen.width, Screen.height));
            Assert.Greater(count, 10);
            Assert.IsTrue(_interaction.GrabSelection(V("ace_spades")));
            Assert.Greater(_interaction.HeldGroup.Count, 5);
            _interaction.Release();
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(0, _interaction.HeldGroup.Count);
            Assert.IsEmpty(TableValidator.Validate(_table.Engine.State));
        }

        // ---- 3. looking ----

        [UnityTest]
        public IEnumerator LookAndPeek_ShowTheCloseUp()
        {
            _interaction.Look(V("ace_spades"), peek: false);
            yield return null;
            var look = Object.FindAnyObjectByType<LookCamera>();
            Assert.AreEqual(V("ace_spades"), look.Target);
            Assert.IsTrue(look.GetComponent<Camera>().enabled);
            _interaction.Look(V("deck_main"), peek: true);
            Assert.IsTrue(_interaction.Peeking);
            _interaction.Look(null, false);
            yield return null;
            Assert.IsNull(look.Target);
            Assert.AreEqual(0, V("deck_main").gameObject.layer);
        }

        // ---- 4. cards, decks, containers, dice ----

        [UnityTest]
        public IEnumerator Digits_TypedTogether_DrawThatManyToTheHand()
        {
            _interaction.TypeDigit(V("deck_main"), 1);
            _interaction.TypeDigit(V("deck_main"), 2);
            Assert.AreEqual("12", _interaction.PendingDigits);
            Assert.IsTrue(_interaction.FlushDigits());
            Assert.AreEqual(54 - 12, ((Deck)M("deck_main")).Cards.Count);
            Assert.AreEqual(12, _table.Engine.ObjectsIn("hand_south").Count());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Digits_OverAMultiStateObject_SwitchState()
        {
            var state = TableJson.Parse(Resources.Load<TextAsset>("Tables/sample").text).State;
            var book = new Token { Id = "book", Name = "Closed" };
            book.Transform.Position = new Vec3(-0.5f, 0, 0.3f);
            book.States["2"] = new Token { Id = "book_open", Name = "Open", Shape = TokenShape.Square };
            state.Objects.Add(book);
            Assert.IsTrue(_table.Load(TableJson.Serialize(state), null));
            yield return null;
            _interaction.TypeDigit(V("book"), 2);
            Assert.IsTrue(_interaction.FlushDigits());
            Assert.IsNull(M("book"));
            Assert.AreEqual("Open", M("book_open").Name);
            Assert.IsNotNull(V("book_open"));
        }

        [UnityTest]
        public IEnumerator Group_CardsIntoADeck()
        {
            Assert.IsTrue(_interaction.Group(new[] { V("ace_spades"), V("king_hearts"), V("ten_diamonds") }));
            Assert.IsNull(M("ace_spades"));
            Assert.AreEqual(3, ((Deck)M("deck-1")).Cards.Count);
            yield return null;
        }

        // ---- 5. editing ----

        [UnityTest]
        public IEnumerator CopyPaste_DeleteUndoRedo()
        {
            Assert.AreEqual(1, _interaction.Copy(new[] { V("token_red") }));
            var pasted = _interaction.Paste(new Vector3(0.4f, 0f, -0.3f));
            Assert.AreEqual(1, pasted.Count);
            Assert.AreEqual(0.4f, M(pasted[0]).Transform.Position.X, 0.001f);

            Assert.IsTrue(_interaction.Delete(new[] { V("token_blue") }));
            Assert.IsNull(M("token_blue"));
            Assert.IsTrue(_table.Undo());
            yield return null;
            Assert.IsNotNull(M("token_blue"));
            Assert.IsNotNull(V("token_blue"));
            Assert.IsTrue(_table.Redo());
            Assert.IsNull(M("token_blue"));
        }

        [UnityTest]
        public IEnumerator ContextMenu_Deck_EveryEntryWorks()
        {
            void Open() => _overlay.OpenMenu(V("deck_main"), new Vector2(Screen.width / 2f, Screen.height / 2f));
            Open();
            CollectionAssert.IsSubsetOf(new[] { "Draw 1 to hand", "Draw 5 to hand", "Deal 1 to everyone", "Shuffle (R)", "Search", "Cut", "Split into: 3", "Spread",
                "Flip (F)", "Rotate 90° (E ×6)", "Lock", "Copy (Ctrl+C)", "Duplicate", "Scale: +", "Tint: red", "Apply name and description", "Delete (Del)" }, _overlay.MenuEntries.ToList());

            Assert.IsTrue(_overlay.Choose("Draw 1 to hand"));
            Assert.AreEqual(53, ((Deck)M("deck_main")).Cards.Count);
            Open(); _overlay.Choose("Deal 1 to everyone");
            Assert.AreEqual(51, ((Deck)M("deck_main")).Cards.Count);
            Open(); _overlay.Choose("Cut");
            Assert.AreEqual(26, ((Deck)M("deck_main")).Cards.Count);
            Open(); _overlay.Choose("Split into: 2");
            Assert.AreEqual(13, ((Deck)M("deck_main")).Cards.Count);
            Open(); _overlay.Choose("Search");
            Assert.IsTrue(_overlay.SearchOpen);
            // The sample deck's cards have no ids: the Search window takes them by position.
            Assert.IsTrue(_overlay.TakeFromSearch(V("deck_main"), null, 1));
            Assert.AreEqual(12, ((Deck)M("deck_main")).Cards.Count);
            _overlay.CloseSearch();
            Open(); _overlay.Choose("Spread");
            Assert.AreEqual(0, ((Deck)M("deck_main")).Cards.Count);
            Open(); _overlay.Choose("Tint: red");
            Assert.IsNotNull(M("deck_main").Tint);
            Open(); _overlay.Choose("Lock");
            Assert.IsTrue(M("deck_main").Locked);
            Open(); _overlay.Choose("Unlock");
            Open(); _overlay.Choose("Delete (Del)");
            Assert.IsNull(M("deck_main"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContextMenu_DieBagAndCard()
        {
            _overlay.OpenMenu(V("d20"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Set value: 7"));
            Assert.AreEqual(7, ((Die)M("d20")).Value);
            _overlay.OpenMenu(V("d20"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Roll (R)"));

            _overlay.OpenMenu(V("bag"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Search"));
            Assert.IsTrue(_overlay.TakeFromSearch(V("bag"), "bag_token_2", -1));
            Assert.IsNull(M("bag_token_2").Container);
            _overlay.CloseSearch();
            _overlay.OpenMenu(V("bag"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Take 1 to hand"));

            _overlay.OpenMenu(V("ace_spades"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Duplicate"));
            Assert.IsNotNull(M("ace_spades-1"));
            _overlay.OpenMenu(V("ace_spades"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Rotate 90° (E ×6)"));
            Assert.AreEqual(90f, M("ace_spades").Transform.Rotation.Y, 0.5f);
            _overlay.OpenMenu(V("ace_spades"), Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Scale: +"));
            Assert.AreEqual(1.1f, M("ace_spades").Transform.Scale.X, 0.001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContextMenu_GlobalAndCamera()
        {
            _overlay.OpenMenu(null, new Vector2(Screen.width / 2f, Screen.height / 2f));
            Assert.IsTrue(_overlay.Choose("Camera: top-down (P)"));
            Assert.IsTrue(_camera.IsTopDown);
            _camera.Save(3);
            _camera.ResetView();
            Assert.IsFalse(_camera.IsTopDown);
            Assert.IsTrue(_camera.Load(3));
            Assert.Greater(_camera.Pitch, 80f);
            _camera.ToggleQuickZoom(new Vector3(0.2f, 0f, 0.1f));
            Assert.AreEqual(TableCamera.QuickZoomDistance, _camera.Distance, 0.001f);
            _camera.ToggleQuickZoom(Vector3.zero);
            Assert.Greater(_camera.Distance, TableCamera.QuickZoomDistance);

            _interaction.Copy(new[] { V("token_red") });
            _overlay.OpenMenu(null, new Vector2(Screen.width / 2f, Screen.height / 2f));
            Assert.IsTrue(_overlay.Choose("Paste (Ctrl+V)"));
            Assert.IsNotNull(M("token_red-1"));
            _overlay.OpenMenu(null, Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Undo (Ctrl+Z)"));
            Assert.IsNull(M("token_red-1"));
            yield return null;
        }

        // ---- 6. tools ----

        [UnityTest]
        public IEnumerator Ping_SpawnsAMarkerThatFades()
        {
            _interaction.Ping(new Vector3(0.1f, 0f, 0.1f));
            yield return null;
            Assert.IsNotNull(Object.FindAnyObjectByType<PingMarker>());
            yield return new WaitForSeconds(PingMarker.Seconds + 0.2f);
            Assert.IsNull(Object.FindAnyObjectByType<PingMarker>());
        }
    }
}
