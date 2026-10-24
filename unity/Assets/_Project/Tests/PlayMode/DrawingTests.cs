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
    /// <summary>G28 drawing: the draw, arrow and erase tools, the table menu's clear, and Escape back to grab.</summary>
    public class DrawingTests
    {
        private TableView _table;
        private TableInteraction _interaction;
        private HudController _hud;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _hud = Object.FindAnyObjectByType<HudController>();
            yield return new WaitForSeconds(0.3f);
        }

        private void Stroke(params Vector3[] points)
        {
            _interaction.HandleDrawingTool(true, true, points[0]);
            foreach (var p in points.Skip(1))
                _interaction.HandleDrawingTool(false, true, p);
            _interaction.HandleDrawingTool(false, false, points[points.Length - 1]);
        }

        [UnityTest]
        public IEnumerator DrawArrowErase_AndClear()
        {
            _hud.SetTool(TableInteraction.Tool.Draw);
            Stroke(new Vector3(0.2f, 0f, 0.1f), new Vector3(0.23f, 0f, 0.12f), new Vector3(0.26f, 0f, 0.1f), new Vector3(0.3f, 0f, 0.14f));
            Assert.AreEqual(1, _table.Engine.State.Drawings.Count, "a stroke becomes a drawing");
            var line = _table.Engine.State.Drawings[0];
            Assert.AreEqual(4, line.Points.Count);
            Assert.AreEqual(_table.LocalSeat, line.Seat);
            var seatColor = ZoneViewFactory.SeatColor(_table.Engine.State.Players.Seats.First(s => s.Id == _table.LocalSeat).Color);
            Assert.AreEqual(seatColor.r, line.Color[0], 0.001f, "in the drawer's colour");
            yield return null;
            Assert.AreEqual(1, _table.DrawingsRoot.childCount, "and is drawn");

            Stroke(new Vector3(0.1f, 0f, 0.1f), new Vector3(0.1005f, 0f, 0.1f));
            Assert.AreEqual(1, _table.Engine.State.Drawings.Count, "a click without a stroke draws nothing");

            _hud.SetTool(TableInteraction.Tool.Arrow);
            Stroke(new Vector3(0f, 0f, -0.05f), new Vector3(0.05f, 0f, 0f), new Vector3(0.1f, 0f, 0.05f));
            var arrow = _table.Engine.State.Drawings.Last();
            Assert.IsTrue(arrow.Arrow);
            Assert.AreEqual(2, arrow.Points.Count, "an arrow is straight from press to release");
            Assert.AreEqual(0.1f, arrow.Points[1].X, 0.001f);

            _hud.SetTool(TableInteraction.Tool.Erase);
            _interaction.HandleDrawingTool(true, true, new Vector3(0.05f, 0f, 0.005f));
            Assert.AreEqual(1, _table.Engine.State.Drawings.Count, "rubbing over the arrow erases it");
            Assert.IsFalse(_table.Engine.State.Drawings[0].Arrow);

            _hud.PressEscape();
            Assert.AreEqual(TableInteraction.Tool.Grab, _interaction.ActiveTool, "Escape goes back to grabbing");
            Assert.IsFalse(_hud.Escape.MenuOpen, "and does not open the menu");

            _hud.Overlay.OpenMenu(null, new Vector2(300f, 300f));
            Assert.IsTrue(_hud.Overlay.Choose("Clear my drawings"));
            Assert.IsEmpty(_table.Engine.State.Drawings);
            yield return null;
            Assert.AreEqual(0, _table.DrawingsRoot.childCount);
        }
    }
}
