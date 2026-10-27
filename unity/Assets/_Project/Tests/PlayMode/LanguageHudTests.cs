using System.Collections;
using System.Linq;
using Core.Engine;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T10.5 in the game window: a game in two languages offers both beside Start, each named in its own language; once
    /// started in one, the turn line, the action buttons and the game's panels read it.
    /// </summary>
    public class LanguageHudTests
    {
        private const string Game = @"{
  ""game"": { ""id"": ""carrera"", ""name"": ""Carrera"", ""language"": ""es"" },
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"", ""name"": ""Cazadora"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""turn"": { ""phases"": [ ""turno"" ] },
  ""phases"": [ { ""id"": ""turno"", ""name"": ""Tu turno"", ""actions"": [ ""pasar"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [ { ""id"": ""pasar"", ""name"": ""Pasar"", ""actor"": ""any"", ""effects"": [ { ""endTurn"": {} } ] } ],
  ""ui"": { ""panels"": [ { ""id"": ""estado"", ""title"": ""Estado"", ""elements"": [ { ""type"": ""label"", ""text"": ""Ronda {$var.ronda}"" } ] } ] },
  ""variables"": { ""ronda"": 1 },
  ""translations"": {
    ""en"": { ""game.name"": ""Race"", ""seats.a.name"": ""Huntress"", ""actions.pasar.name"": ""Pass"", ""phases.turno.name"": ""Your turn"",
              ""ui.estado.title"": ""Status"", ""ui.estado.0.text"": ""Round {$var.ronda}"" }
  }
}";

        private TableView _table;
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            _root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            Assert.IsTrue(_table.Load(Game, null));
            yield return new WaitForSeconds(0.4f);
        }

        private Button Action(string text) => _root.Q("gameActions").Query<Button>().ToList().FirstOrDefault(b => b.text == text);

        [UnityTest]
        public IEnumerator BothLanguages_AreOffered_AndTheGameReadsThePickedOne()
        {
            var spanish = Action("Español");
            var english = Action("English");
            Assert.IsNotNull(spanish, "the game's own language, named in it");
            Assert.IsNotNull(english);
            Assert.IsTrue(spanish.ClassListContains("hud-button--picked"), "its own language until the host picks another");
            Assert.AreEqual("Carrera: not started", _root.Q<Label>("turnLabel").text);

            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = english;
                english.SendEvent(click);
            }
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(Action("English").ClassListContains("hud-button--picked"), "picked");
            Assert.IsFalse(Action("Español").ClassListContains("hud-button--picked"));

            // Start as the HUD would with English picked (its Start needs people sat; the command is the same).
            var result = _table.Execute(new StartGameCommand { Language = "en" });
            Assert.IsTrue(result.Ok, result.Error?.ToString());
            yield return new WaitForSeconds(0.4f);

            StringAssert.Contains("Your turn", _root.Q<Label>("turnLabel").text);
            StringAssert.Contains("Huntress", _root.Q<Label>("turnLabel").text);
            Assert.IsNotNull(Action("Pass"), "the action button reads English");
            Assert.IsNull(Action("English"), "no language toggles once the game runs");
            var panelTexts = _root.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.Contains("Status", panelTexts);
            Assert.Contains("Round 1", panelTexts);
        }

        [UnityTest]
        public IEnumerator AGameInOneLanguage_OffersNoChoice()
        {
            // The same game without its translations or a language of its own.
            var oneLanguage = Game.Substring(0, Game.IndexOf(@"  ""translations""", System.StringComparison.Ordinal)).TrimEnd().TrimEnd(',') + "}";
            Assert.IsTrue(_table.Load(oneLanguage.Replace(@", ""language"": ""es""", ""), null));
            yield return new WaitForSeconds(0.4f);
            Assert.IsNull(Action("English"));
            Assert.IsNull(Action("Español"));
        }
    }
}
