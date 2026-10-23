using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T2.9 ⚗: the sample table survives 50 random commands with real views and physics in between. Commands go through
    /// <see cref="TableView.Execute"/> and, for grabs, through <see cref="TableInteraction"/>, so view event handling,
    /// physics sync and the roll animation all run. Any error log fails the test.
    /// </summary>
    public class SmokeTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator SampleTable_SurvivesFiftyRandomCommands()
        {
            _root = new GameObject("Smoke");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(_root.transform);
            ground.transform.localScale = new Vector3(3f, 0.04f, 3f);
            ground.transform.position = new Vector3(0f, -0.02f, 0f);

            var table = _root.AddComponent<TableView>();
            table.Configure("", _root.transform, seed: 7);
            var interaction = _root.AddComponent<TableInteraction>();
            interaction.Configure(table, null, null);
            Assert.IsTrue(table.Load(Resources.Load<TextAsset>("Tables/sample").text, packageRoot: null));
            yield return new WaitForSeconds(0.5f);

            var chooser = new DeterministicRandom(2026);
            var executed = 0;
            var succeeded = 0;
            for (var step = 0; step < 50; step++)
            {
                var onTable = table.Engine.State.Objects.Where(o => o.Container == null).Select(o => o.Id).ToList();
                string Pick() => onTable[chooser.Next(onTable.Count)];

                if (chooser.Next(5) == 0)
                {
                    // A real gesture: grab (taking from containers), drag somewhere on the table, drop.
                    var view = table.ViewOf(Pick());
                    if (interaction.Grab(view, moveContainer: chooser.Next(2) == 0))
                    {
                        interaction.DragTo(new Vector3(chooser.Next(100) / 100f - 0.5f, 0f, chooser.Next(60) / 100f - 0.3f));
                        yield return new WaitForSeconds(0.15f);
                        interaction.Release();
                        succeeded++;
                    }
                    executed++;
                    yield return null;
                    continue;
                }

                Command command = chooser.Next(9) switch
                {
                    0 => new MoveCommand { ObjectId = Pick(), Position = new Vec3(chooser.Next(100) / 100f - 0.5f, 0.05f, chooser.Next(60) / 100f - 0.3f) },
                    1 => new TakeCommand { ContainerId = Pick() },
                    2 => new InsertCommand { ObjectId = Pick(), IntoId = Pick() },
                    3 => new FlipCommand { ObjectId = Pick() },
                    4 => new ShuffleCommand { ObjectId = Pick() },
                    5 => new DealCommand { DeckId = Pick(), Count = 2, Seats = { "seat_south", "seat_north" } },
                    6 => new LockCommand { ObjectId = Pick(), Locked = chooser.Next(3) == 0 },
                    7 => new GroupCommand { ObjectIds = { Pick(), Pick() } },
                    _ => new RollCommand { ObjectId = Pick() },
                };
                if (table.Execute(command).Ok)
                    succeeded++;
                executed++;
                yield return null;
            }

            // Let rolls finish and physics settle, then the view and the state must agree.
            yield return new WaitForSeconds(2.5f);
            var errors = TableValidator.Validate(table.Engine.State);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            var expected = table.Engine.State.Objects.Where(o => o.Container == null).Select(o => o.Id).OrderBy(x => x).ToList();
            var actual = table.Views.Where(kv => kv.Value != null).Select(kv => kv.Key).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(expected, actual);
            foreach (var view in table.Views.Values)
                Assert.AreSame(table.Engine.Find(view.Id), view.Model, $"View {view.Id} is bound to a stale model.");

            Debug.Log($"[Smoke] {executed} commands, {succeeded} succeeded, {expected.Count} objects on the table, hash {TableJson.Hash(table.Engine.State)}");
            Assert.Greater(succeeded, 10);
        }
    }
}
