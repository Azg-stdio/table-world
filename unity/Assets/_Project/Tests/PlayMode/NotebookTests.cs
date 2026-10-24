using System.Collections;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G25: the Notes panel shows shared pages and the seat's private ones, writes, adds and deletes pages.</summary>
    public class NotebookTests
    {
        [UnityTest]
        public IEnumerator Notes_ReadWriteAddDelete_AndPrivatePages()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var hud = Object.FindAnyObjectByType<HudController>();
            var root = hud.GetComponent<UIDocument>().rootVisualElement;
            yield return new WaitForSeconds(0.3f);

            string[] Tabs() => root.Query<Button>(className: "notes-tab").ToList().Select(b => b.text).ToArray();
            table.SeatOverride = "seat_south";
            hud.Notes.Toggle();
            yield return null;
            CollectionAssert.AreEqual(new[] { "Read me", "South's notes" }, Tabs(), "the red seat reads its own page");
            table.SeatOverride = "seat_north";
            hud.Notes.Update();
            CollectionAssert.AreEqual(new[] { "Read me" }, Tabs(), "the blue seat does not");

            hud.Notes.Open("page-1");
            hud.Notes.Write("Read me", "Changed.");
            Assert.AreEqual("Changed.", table.Engine.State.Notebook.First(p => p.Id == "page-1").Body);

            root.Q<Button>("notesNew").SendEvent(Submit(root.Q<Button>("notesNew")));
            yield return null;
            Assert.AreEqual(3, table.Engine.State.Notebook.Count);
            var created = hud.Notes.OpenPage;
            var delete = root.Q<Button>("notesDelete");
            delete.SendEvent(Submit(delete));
            Assert.AreEqual(3, table.Engine.State.Notebook.Count, "the first click asks");
            delete.SendEvent(Submit(delete));
            Assert.AreEqual(2, table.Engine.State.Notebook.Count);
            Assert.IsFalse(table.Engine.State.Notebook.Any(p => p.Id == created));

            hud.PressEscape();
            Assert.IsFalse(hud.Notes.Visible);
        }

        private static EventBase Submit(VisualElement target)
        {
            var e = NavigationSubmitEvent.GetPooled();
            e.target = target;
            return e;
        }
    }
}
