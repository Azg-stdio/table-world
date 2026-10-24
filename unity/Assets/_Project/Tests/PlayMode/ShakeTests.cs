using System.Collections;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G32: shaking a held deck back and forth shuffles it once; slow moves do not.</summary>
    public class ShakeTests
    {
        [UnityTest]
        public IEnumerator ShakingAHeldDeck_ShufflesIt()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);

            var shakes = 0;
            interaction.Shaken += _ => shakes++;
            var deck = (Deck)table.Engine.Find("deck_main");
            var before = string.Join(",", deck.Cards.ConvertAll(c => c.FaceIndex));
            Assert.IsTrue(interaction.Grab(table.ViewOf("deck_main"), moveContainer: true));
            yield return null;
            // One slow move across: not a shake.
            interaction.DragTo(new Vector3(-0.3f, 0f, 0.1f));
            yield return new WaitForSeconds(0.1f);
            interaction.DragTo(new Vector3(-0.2f, 0f, 0.1f));
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(0, shakes);
            // Quick strokes left and right.
            for (var i = 0; i < 6; i++)
            {
                interaction.DragTo(new Vector3(i % 2 == 0 ? -0.26f : -0.2f, 0f, 0.1f));
                yield return new WaitForSeconds(0.05f);
            }
            Assert.AreEqual(1, shakes, "one shake, one shuffle");
            Assert.AreNotEqual(before, string.Join(",", ((Deck)table.Engine.Find("deck_main")).Cards.ConvertAll(c => c.FaceIndex)));
            for (var i = 0; i < 6; i++)
            {
                interaction.DragTo(new Vector3(i % 2 == 0 ? -0.26f : -0.2f, 0f, 0.1f));
                yield return new WaitForSeconds(0.05f);
            }
            Assert.AreEqual(1, shakes, "not again right away");
            interaction.Release();
        }
    }
}
