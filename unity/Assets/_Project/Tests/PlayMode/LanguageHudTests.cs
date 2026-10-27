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
  ""objects"": [ { ""type"": ""Card"", ""id"": ""espada"", ""text"": { ""title"": ""Espada"", ""body"": ""Ataque +1."" } } ],
  ""translations"": {
    ""en"": { ""game.name"": ""Race"", ""seats.a.name"": ""Huntress"", ""actions.pasar.name"": ""Pass"", ""phases.turno.name"": ""Your turn"",
              ""ui.estado.title"": ""Status"", ""ui.estado.0.text"": ""Round {$var.ronda}"",
              ""objects.espada.text.title"": ""Sword"", ""objects.espada.text.body"": ""Attack +1."" }
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

        /// <summary>T10.6: a text card is drawn again in the session's language.</summary>
        [UnityTest]
        public IEnumerator ATextCard_IsDrawnInTheSessionsLanguage()
        {
            var card = (Core.Rules.Card)_table.Engine.Find("espada");
            var spanish = _table.Assets.Face(card).AssetId;
            Assert.IsTrue(_table.Execute(new StartGameCommand { Language = "en" }).Ok);
            yield return null;
            var english = _table.Assets.Face(card).AssetId;
            Assert.AreNotEqual(spanish, english, "another drawing for the English words");
            var words = new Core.Rules.CardText { Title = "Sword", Body = "Attack +1." };
            Assert.AreEqual(Core.Rules.TextFaces.FaceId(words, card.Style, card.Size ?? Core.Rules.Card.DefaultSize), english);
            Texture2D drawn = null;
            for (var i = 0; i < 60 && drawn == null; i++)
            {
                drawn = _table.Assets.Texture(english);
                yield return null;
            }
            Assert.IsNotNull(drawn, "the English face is drawn");
        }

        /// <summary>T10.6: an image with a file per language shows the one of the session's language.</summary>
        [UnityTest]
        public IEnumerator ALocalizedImage_ShowsTheSessionsFile()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tw-localized-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            try
            {
                foreach (var (file, colour) in new[] { ("rojo.png", Color.red), ("blue.png", Color.blue) })
                {
                    var pixels = new Texture2D(4, 4);
                    pixels.SetPixels(Enumerable.Repeat(colour, 16).ToArray());
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, file), pixels.EncodeToPNG());
                    Object.Destroy(pixels);
                }
                var json = Game.Replace(@"""variables"": { ""ronda"": 1 },", @"""variables"": { ""ronda"": 1 },
  ""assets"": [ { ""id"": ""cartel"", ""type"": ""image"", ""path"": ""rojo.png"", ""localized"": { ""en"": ""blue.png"" } } ],");
                Assert.IsTrue(_table.Load(json, folder));
                yield return null;

                IEnumerator Shown(System.Action<Color> read)
                {
                    Texture2D texture = null;
                    for (var i = 0; i < 120 && texture == null; i++)
                    {
                        texture = _table.Assets.Texture("cartel");
                        yield return null;
                    }
                    Assert.IsNotNull(texture, "the image loads");
                    var copy = RenderTexture.GetTemporary(texture.width, texture.height);
                    Graphics.Blit(texture, copy);
                    var previous = RenderTexture.active;
                    RenderTexture.active = copy;
                    var readable = new Texture2D(texture.width, texture.height);
                    readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(copy);
                    read(readable.GetPixel(1, 1));
                    Object.Destroy(readable);
                }

                var before = Color.clear;
                yield return Shown(c => before = c);
                Assert.Greater(before.r, 0.8f, "the game's own file (Spanish, red)");
                Assert.IsTrue(_table.Execute(new StartGameCommand { Language = "en" }).Ok);
                var after = Color.clear;
                yield return Shown(c => after = c);
                Assert.Greater(after.b, 0.8f, "the English file (blue)");
                Assert.Less(after.r, 0.2f);
            }
            finally
            {
                System.IO.Directory.Delete(folder, true);
            }
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
