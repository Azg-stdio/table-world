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
using UnityEngine.UIElements;

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

        [UnityTest]
        public IEnumerator MenuToggles_TurnFlagsOffAndOn_AndSnapOffIgnoresSnapPoints()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            var overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            yield return new WaitForSeconds(0.3f);

            overlay.OpenMenu(table.ViewOf("ten_diamonds"), Vector2.zero);
            Assert.IsTrue(overlay.Choose("● Tooltip"));
            Assert.IsFalse(table.Engine.Find("ten_diamonds").Tooltip);
            overlay.OpenMenu(table.ViewOf("ten_diamonds"), Vector2.zero);
            Assert.IsTrue(overlay.Choose("○ Tooltip"), "the entry shows it is off and turns it back on");
            Assert.IsTrue(table.Engine.Find("ten_diamonds").Tooltip);

            overlay.OpenMenu(table.ViewOf("ten_diamonds"), Vector2.zero);
            Assert.IsTrue(overlay.Choose("● Snap to points"));
            // The discard snap point is at (-0.05, 0.1): with snapping off the card lands where it is dropped.
            Assert.IsTrue(interaction.Grab(table.ViewOf("ten_diamonds"), moveContainer: false));
            yield return null;
            interaction.DragTo(new Vector3(-0.035f, 0f, 0.112f));
            yield return new WaitForSeconds(0.2f);
            interaction.Release();
            yield return new WaitForSeconds(1f);
            var p = table.Engine.Find("ten_diamonds").Transform.Position;
            Assert.Greater(Mathf.Abs(p.X + 0.05f) + Mathf.Abs(p.Z - 0.1f), 0.004f, $"not snapped, at {p}");
        }

        [UnityTest]
        public IEnumerator PeekIsAnnounced_AndTheBlindfoldDarkensTheScreen()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            var hud = Object.FindAnyObjectByType<HudController>();
            var session = Object.FindAnyObjectByType<TableWorld.View.Net.NetworkTableSession>();
            yield return new WaitForSeconds(0.3f);

            interaction.Look(table.ViewOf("ace_spades"), peek: true);
            interaction.Look(null, false);
            Assert.IsFalse(session.Chat.Entries.Any(e => e.Text.Contains("peeked")), "peeking at a face-up card is nothing to tell");
            table.Execute(new FlipCommand { ObjectId = "ace_spades" });
            yield return new WaitForSeconds(0.5f);
            interaction.Look(table.ViewOf("ace_spades"), peek: true);
            interaction.Look(table.ViewOf("ace_spades"), peek: true);
            interaction.Look(null, false);
            Assert.AreEqual(1, session.Chat.Entries.Count(e => e.Text.Contains("peeked at a card")), "one peek, one notice");

            var blindfold = hud.GetComponent<UIDocument>().rootVisualElement.Q("blindfold");
            interaction.ToggleBlindfold();
            Assert.IsTrue(interaction.Blindfolded);
            Assert.IsFalse(blindfold.ClassListContains("hidden"));
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text.Contains("put on a blindfold")));
            interaction.ToggleBlindfold();
            Assert.IsTrue(blindfold.ClassListContains("hidden"));
        }
    }
}
