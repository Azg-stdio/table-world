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
    /// <summary>G28 grid: set from the table menu, drawn on the felt, drops snap to it unless an object opts out.</summary>
    public class GridTests
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
            yield return new WaitForSeconds(0.3f);
        }

        private void Choose(string entry)
        {
            _overlay.OpenMenu(null, new Vector2(300f, 300f));
            Assert.IsTrue(_overlay.Choose(entry), $"menu entry '{entry}' in: {string.Join(", ", _overlay.MenuEntries)}");
        }

        private IEnumerator Drop(string id, Vector3 at)
        {
            Assert.IsTrue(_interaction.Grab(_table.ViewOf(id), moveContainer: false));
            yield return null;
            _interaction.DragTo(at);
            yield return new WaitForSeconds(0.2f);
            _interaction.Release();
            yield return new WaitForSeconds(1f);
        }

        [UnityTest]
        public IEnumerator DropNearASnapPoint_LandsOnIt()
        {
            // The sample table's discard snap point is at (-0.05, 0.1).
            yield return Drop("ten_diamonds", new Vector3(-0.035f, 0f, 0.112f));
            var p = _table.Engine.Find("ten_diamonds").Transform.Position;
            Assert.AreEqual(-0.05f, p.X, 0.002f, $"on the snap point, at {p}");
            Assert.AreEqual(0.1f, p.Z, 0.002f);
        }

        [UnityTest]
        public IEnumerator Menu_SetsTheGrid_DropsSnap_UnlessTheObjectOptsOut()
        {
            Assert.IsNull(_table.GridLines);
            Choose("Grid: Squares");
            Assert.IsNotNull(_table.Engine.State.Grid);
            Assert.IsNotNull(_table.GridLines, "the lines are drawn");
            Choose("Snap: Centers");
            Choose("Cell: 4 cm");
            Assert.AreEqual(GridSnapping.Centers, _table.Engine.State.Grid.Snapping);

            yield return Drop("queen_clubs", new Vector3(0.21f, 0f, 0.07f));
            var p = _table.Engine.Find("queen_clubs").Transform.Position;
            Assert.AreEqual(0.22f, p.X, 0.003f, $"snapped to the cell centre, at {p.X:0.0000},{p.Z:0.0000}");
            Assert.AreEqual(0.06f, p.Z, 0.003f);

            _table.Engine.Find("ace_spades").Grid = false;
            yield return Drop("ace_spades", new Vector3(0.31f, 0f, 0.07f));
            // It may slide a little after the release (no snap stops it), but it does not land on the cell centre (0.30, 0.06).
            var ace = _table.Engine.Find("ace_spades").Transform.Position;
            Assert.IsTrue(Mathf.Abs(ace.X - 0.30f) > 0.004f || Mathf.Abs(ace.Z - 0.06f) > 0.004f, $"an object that opts out is not snapped, at {ace}");

            Choose("Grid lines: hide");
            Assert.IsNull(_table.GridLines, "lines hidden");
            Assert.AreEqual(GridSnapping.Centers, _table.Engine.State.Grid.Snapping, "snapping stays");
            Choose("Grid: Hexes");
            Assert.AreEqual(GridShape.Hex, _table.Engine.State.Grid.Shape);
            Choose("Grid: None");
            Assert.IsNull(_table.Engine.State.Grid);
        }
    }
}
