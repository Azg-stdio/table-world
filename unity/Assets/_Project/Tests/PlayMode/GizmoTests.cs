using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G28 gizmo: the object menu opens it, typed values (comma or point) move, turn and scale the object.</summary>
    public class GizmoTests
    {
        [UnityTest]
        public IEnumerator ExactPosition_FromTheMenu()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var hud = Object.FindAnyObjectByType<HudController>();
            yield return new WaitForSeconds(0.3f);

            hud.Overlay.OpenMenu(table.ViewOf("token_red"), Vector2.zero);
            Assert.IsTrue(hud.Overlay.Choose("Exact position…"));
            Assert.IsTrue(hud.Gizmo.Visible);
            Assert.AreEqual("token_red", hud.Gizmo.ObjectId);

            hud.Gizmo.Type("x", "12,5");
            hud.Gizmo.Type("z", "-3");
            hud.Gizmo.Type("turn", "45");
            hud.Gizmo.Type("scale", "2");
            Assert.IsTrue(hud.Gizmo.Apply());
            var t = table.Engine.Find("token_red").Transform;
            Assert.AreEqual(0.125f, t.Position.X, 0.0001f, "a comma works as the decimal mark");
            Assert.AreEqual(-0.03f, t.Position.Z, 0.0001f);
            Assert.AreEqual(45f, t.Rotation.Y, 0.01f);
            Assert.AreEqual(2f, t.Scale.X, 0.01f);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0.125f, table.ViewOf("token_red").transform.position.x, 0.005f, "the token is drawn there");

            hud.Gizmo.Type("x", "left a bit");
            Assert.IsFalse(hud.Gizmo.Apply(), "words are not numbers");
            Assert.AreEqual(0.125f, table.Engine.Find("token_red").Transform.Position.X, 0.0001f);

            hud.PressEscape();
            Assert.IsFalse(hud.Gizmo.Visible, "Escape closes the gizmo first");
            Assert.IsFalse(hud.Escape.MenuOpen);
        }
    }
}
