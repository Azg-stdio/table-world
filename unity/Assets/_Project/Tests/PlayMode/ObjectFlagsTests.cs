using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G39 on the table: face-down cards keep their name secret; an object with hands off stays out of hands.</summary>
    public class ObjectFlagsTests
    {
        [UnityTest]
        public IEnumerator FaceDownNames_AndHandsFlag()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var hud = Object.FindAnyObjectByType<HudController>();
            yield return new WaitForSeconds(0.3f);

            var ace = table.Engine.Find("ace_spades");
            Assert.IsFalse(string.IsNullOrEmpty(ace.Name));
            Assert.AreEqual(ace.Name, TableOverlay.DisplayName(ace), "face up, the name shows");
            table.Execute(new FlipCommand { ObjectId = "ace_spades" });
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("Face-down card", TableOverlay.DisplayName(ace), "a face-down card does not tell its name");
            Assert.IsNull(TableOverlay.TooltipText(table.ViewOf("ace_spades"), table.Engine) is { } tip && tip.Contains(ace.Name) ? tip : null,
                "the tooltip does not name it either");

            // Into the south hand zone with hands off: it stays an ordinary object on the table.
            var token = table.Engine.Find("token_red");
            token.Hands = false;
            table.Execute(new MoveCommand { ObjectId = "token_red", Position = new Vec3(0.1f, 0f, -0.42f) });
            Assert.IsFalse(table.Engine.ZonesOf("token_red").Contains("hand_south"));
        }
    }
}
