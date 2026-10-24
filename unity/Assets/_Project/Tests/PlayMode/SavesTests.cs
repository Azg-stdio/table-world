using System.Collections;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G30: named saves from the Escape menu, loading them back, deleting (asked twice), and autosave.</summary>
    public class SavesTests
    {
        private TableView _table;
        private HudController _hud;
        private string _folder;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _hud = Object.FindAnyObjectByType<HudController>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _folder = Path.Combine(Application.temporaryCachePath, "saves-test");
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
            _hud.Saves.Folder = _folder;
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator Clean()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
            yield return null;
        }

        private float X(string id) => _table.Engine.Find(id).Transform.Position.X;

        [UnityTest]
        public IEnumerator SaveFromTheMenu_LoadBack_DeleteAskedTwice()
        {
            var root = _hud.GetComponent<UIDocument>().rootVisualElement;
            _hud.PressEscape();
            using (var click = NavigationSubmitEvent.GetPooled()) { var b = root.Q<Button>("escSaves"); click.target = b; b.SendEvent(click); }
            Assert.IsTrue(_hud.SavesPanel.Visible, "Save & load opens from the menu");
            root.Q<TextField>("saveName").value = "Game night: round 2";
            _hud.SavesPanel.SaveNamed();
            var entry = _hud.Saves.List().Single();
            Assert.AreEqual("Game night: round 2", entry.Name, "the typed name is kept for display");
            Assert.AreEqual("Game night_ round 2.json", Path.GetFileName(entry.File), "the file name is safe");

            var start = X("ace_spades");
            _table.Execute(new MoveCommand { ObjectId = "ace_spades", Position = new Vec3(0.3f, 0f, 0.25f) });
            Assert.IsTrue(_hud.Saves.Load(entry, out var error), error);
            Assert.AreEqual(start, X("ace_spades"), 0.001f, "loading puts the table back");

            yield return null;
            var deleteButton = root.Query<Button>().ToList().First(b => b.text == "Delete");
            using (var click = NavigationSubmitEvent.GetPooled()) { click.target = deleteButton; deleteButton.SendEvent(click); }
            Assert.AreEqual(1, _hud.Saves.List().Count, "the first click only asks");
            var confirm = root.Query<Button>().ToList().First(b => b.text == "Really delete?");
            using (var click = NavigationSubmitEvent.GetPooled()) { click.target = confirm; confirm.SendEvent(click); }
            Assert.IsEmpty(_hud.Saves.List());

            _hud.PressEscape();
            Assert.IsFalse(_hud.SavesPanel.Visible);
            Assert.IsTrue(_hud.Escape.MenuOpen, "Escape steps back to the menu");
            _hud.PressEscape();
        }

        [UnityTest]
        public IEnumerator Autosave_RotatesSlots_OnlyWhenTheTableChanged()
        {
            yield return new WaitForSeconds(SaveLibrary.SettleSeconds);
            _hud.Saves.AutosaveNow();
            Assert.IsNull(_hud.Saves.Tick(10), "an untouched table is not autosaved (settling pieces do not count)");
            _table.Execute(new FlipCommand { ObjectId = "ace_spades" });
            _hud.Saves.AutosaveNow();
            Assert.AreEqual("autosave-1", _hud.Saves.Tick(10));
            _hud.Saves.AutosaveNow();
            Assert.IsNull(_hud.Saves.Tick(10), "no change, no new autosave");
            _table.Execute(new FlipCommand { ObjectId = "ace_spades" });
            _hud.Saves.AutosaveNow();
            Assert.AreEqual("autosave-2", _hud.Saves.Tick(10));
            Assert.IsNull(_hud.Saves.Tick(0), "off");
            var list = _hud.Saves.List();
            Assert.AreEqual(2, list.Count);
            Assert.IsTrue(list.All(e => e.Autosave));
            yield return null;
        }
    }
}
