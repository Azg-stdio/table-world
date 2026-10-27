using System.Collections;
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
    /// T10.2: switching the interface's language redraws the HUD at once: the UXML's buttons, the turn line the code
    /// writes, and back. (Every other test reads English: <see cref="NoAgentBridge"/> pins it.)
    /// </summary>
    public class InterfaceLanguageTests
    {
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator BackToEnglish()
        {
            Ui.Override = "en";
            Ui.SetLanguage("");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Spanish_RedrawsTheHud_AndBack()
        {
            Assert.AreEqual("Games", _root.Q<Button>("gamesButton").text);
            Assert.AreEqual("Save (F5)", _root.Q<Button>("saveButton").text);

            Ui.Override = "es";
            Ui.SetLanguage("");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual("es", Ui.Language);
            Assert.AreEqual("Juegos", _root.Q<Button>("gamesButton").text, "a UXML button");
            Assert.AreEqual("Guardar (F5)", _root.Q<Button>("saveButton").text);
            var turn = _root.Q<Label>("turnLabel").text;
            Assert.IsFalse(turn.Contains("Free play") || turn.Contains("not started"), $"the turn line the code writes: {turn}");

            Ui.Override = "en";
            Ui.SetLanguage("");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual("Games", _root.Q<Button>("gamesButton").text, "back to English");
        }
    }
}
