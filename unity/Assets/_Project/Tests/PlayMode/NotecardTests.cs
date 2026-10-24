using System.Collections;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G27 notecard: its name and description are printed on it, follow edits, and hide in hidden zones.</summary>
    public class NotecardTests
    {
        [UnityTest]
        public IEnumerator Notecard_PrintsItsText_FollowsEdits_AndHides()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);

            var view = table.ViewOf("house_rules");
            var face = view.transform.Find(NotecardView.FaceName);
            Assert.IsNotNull(face, "the card has a printed face");
            var root = face.GetComponent<UIDocument>().rootVisualElement;
            Assert.AreEqual("House rules", root.Q<Label>("noteTitle").text);
            StringAssert.StartsWith("Draw two", root.Q<Label>("noteBody").text);

            table.Execute(new SetPropertiesCommand { ObjectId = "house_rules", Name = "Table talk", Description = "No phones." });
            yield return null;
            Assert.AreEqual("Table talk", root.Q<Label>("noteTitle").text, "editing the name reprints it");
            Assert.AreEqual("No phones.", root.Q<Label>("noteBody").text);

            // Into north's secret area (the south player sees only fog there): the text hides with the card.
            table.Execute(new MoveCommand { ObjectId = "house_rules", Position = new Vec3(-0.6f, 0f, 0.15f) });
            yield return null;
            Assert.IsTrue(view.ZoneHidden);
            Assert.IsFalse(face.gameObject.activeSelf, "the text is not readable through the fog");
        }
    }
}
