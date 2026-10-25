using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// T3.5: a client whose prediction is rebuilt keeps its views. Only the objects named as changed move and redraw;
    /// the rest keep their GameObjects (respawning every view of a big TTS table froze clients for seconds).
    /// </summary>
    public class ReconcileTests
    {
        private TableView _table;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTest]
        public IEnumerator RebuiltPrediction_MovesOnlyWhatChanged_AndKeepsEveryOtherView()
        {
            var before = _table.Views.ToDictionary(p => p.Key, p => p.Value);
            var copy = new GameEngine(TableJson.Parse(TableJson.Serialize(_table.Game.State, indented: false)).State!, 1);
            var moved = copy.Table.Find("ace_spades")!;
            moved.Transform.Position = new Vec3(0.3f, 0f, 0.2f);
            var cloned = copy.Execute(new CloneCommand { ObjectId = "king_hearts" });
            Assert.IsTrue(cloned.Ok);

            _table.Reconcile(copy, new HashSet<string> { "ace_spades", cloned.CreatedId });
            yield return new WaitForSeconds(0.2f);

            Assert.AreSame(copy, _table.Game, "the table follows the new engine");
            foreach (var (id, view) in before)
                Assert.AreSame(view, _table.ViewOf(id), $"{id} keeps its view");
            Assert.AreSame(copy.Table.Find("ace_spades"), _table.ViewOf("ace_spades").Model, "views point at the new objects");
            Assert.AreSame(copy.Table.Find("d6"), _table.ViewOf("d6").Model);
            Assert.AreEqual(0.3f, _table.ViewOf("ace_spades").transform.position.x, 0.01f, "the changed object moved");
            Assert.IsNotNull(_table.ViewOf(cloned.CreatedId), "a new object got a view");
        }
    }
}
