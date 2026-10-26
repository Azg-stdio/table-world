using System.Collections;
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
    /// <summary>The table calculator: a click on a drawn key presses that key, and the display shows the sum.</summary>
    public class CalculatorObjectTests
    {
        [UnityTest]
        public IEnumerator ClickingItsKeys_AddsUp_OnTheDisplay()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            yield return new WaitForSeconds(2f);
            var id = table.Execute(new AddCalculatorCommand { Position = new Vec3(0.6f, 0f, 0.3f), Actor = table.LocalSeat }).CreatedId;
            yield return null;
            var view = table.ViewOf(id);
            Assert.IsNotNull(view.GetComponent<CalculatorFace>(), "drawn as a calculator");

            // Key centres of the face: '1' row 2 col 0, '2' row 2 col 1, '+' row 3 col 3, '9' row 0 col 2, '=' row 3 col 2.
            Vector3 At(int row, int col)
            {
                var u = (6 + col * 28 + 13) / 120f;
                var v = (48 + row * 32 + 15) / 176f;
                return view.transform.TransformPoint(new Vector3((u - 0.5f) * Calculator.Size.X, 0.006f, (0.5f - v) * Calculator.Size.Y));
            }
            foreach (var (row, col) in new[] { (2, 0), (2, 1), (3, 3), (0, 2), (3, 2) })
                Assert.IsTrue(interaction.PressCalculator(view, At(row, col)));
            Assert.AreEqual("21", ((Calculator)table.Engine.Find(id)).Display);
            Assert.AreEqual('=', CalculatorFace.KeyAt(view.transform, At(3, 2)));
            Assert.IsNull(CalculatorFace.KeyAt(0.5f, 0.1f), "the display is not a key");
        }
    }
}
