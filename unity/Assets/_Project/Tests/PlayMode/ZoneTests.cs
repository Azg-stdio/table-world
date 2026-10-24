using System.Collections;
using System.Linq;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G18 on the sample table: the Market lays out what is dropped in it; the tile mixer randomizes.</summary>
    public class ZoneTests
    {
        private TableView _table;
        private TableInteraction _interaction;
        private TableOverlay _overlay;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator Drop(string id, Vector3 at)
        {
            Assert.IsTrue(_interaction.Grab(_table.ViewOf(id), moveContainer: false), $"grab {id}");
            yield return null;
            _interaction.DragTo(at);
            yield return new WaitForSeconds(0.2f);
            _interaction.Release();
            yield return new WaitForSeconds(1f);
        }

        [UnityTest]
        public IEnumerator Market_LinesUpDroppedCards_FaceUp_AndTheMenuLaysItOut()
        {
            var market = _table.Engine.State.Zones.OfType<LayoutZone>().Single(z => z.Id == "market");
            _table.Execute(new Core.Engine.FlipCommand { ObjectId = "king_hearts" });
            yield return new WaitForSeconds(0.5f);
            yield return Drop("king_hearts", new Vector3(-0.5f, 0f, -0.36f));
            yield return Drop("queen_clubs", new Vector3(-0.45f, 0f, -0.36f));
            var king = _table.Engine.Find("king_hearts");
            var queen = _table.Engine.Find("queen_clubs");
            Assert.IsTrue(king.IsFaceUp, "the market turns cards face up");
            var left = market.Transform.Position.X - market.Size.X / 2f;
            var cell = Card.DefaultSize.X + market.Spacing;
            Assert.AreEqual(left + cell / 2f, king.Transform.Position.X, 0.01f, "first card in the first slot");
            Assert.AreEqual(left + cell * 1.5f, queen.Transform.Position.X, 0.01f, "second card in the next slot");
            Assert.AreEqual(king.Transform.Position.Z, queen.Transform.Position.Z, 0.01f, "on one row");
            // The views follow the state once physics settles.
            Assert.AreEqual(queen.Transform.Position.X, _table.ViewOf("queen_clubs").transform.position.x, 0.01f);

            _overlay.OpenMenu(_table.ViewOf("king_hearts"), Vector2.zero);
            CollectionAssert.Contains(_overlay.MenuEntries, "Lay out");
            _overlay.CloseMenu();
            _overlay.OpenMenu(_table.ViewOf("tile_hex"), Vector2.zero);
            CollectionAssert.Contains(_overlay.MenuEntries, "Randomize");
            Assert.IsTrue(_overlay.Choose("Randomize"));
            yield return new WaitForSeconds(0.5f);
            var tiles = new[] { "tile_square", "tile_hex", "tile_circle" }.Select(id => _table.Engine.Find(id).Transform.Position.X).OrderBy(x => x).ToArray();
            Assert.AreEqual(new[] { 0.05f, 0.12f, 0.19f }, tiles.Select(x => Mathf.Round(x * 100f) / 100f).ToArray(), "the tiles swapped places among their own spots");
        }

        [UnityTest]
        public IEnumerator CoreFootprints_MatchWhatTheViewDraws()
        {
            yield return null;
            foreach (var id in new[] { "ace_spades", "token_red", "tile_square", "d6", "bag", "score", "timer" })
            {
                var view = _table.ViewOf(id);
                var drawn = view.Renderer.bounds.size;
                var footprint = Footprint.Of(view.Model);
                var wide = Mathf.Max(drawn.x, drawn.z);
                Assert.AreEqual(Mathf.Max(footprint.X, footprint.Y), wide, wide * 0.2f + 0.002f, $"{id}: core footprint {footprint.X}×{footprint.Y}, drawn {drawn.x}×{drawn.z}");
            }
        }
    }
}
