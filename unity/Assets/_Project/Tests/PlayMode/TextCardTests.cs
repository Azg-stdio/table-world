using System.Collections;
using System.Linq;
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
    /// <summary>Text cards: the game draws faces from words and backs from the style, and every view of the card shows them.</summary>
    public class TextCardTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""seat_south"", ""color"": ""red"", ""handZone"": ""hand_south"" } ] },
  ""zones"": [ { ""id"": ""hand_south"", ""type"": ""HandZone"", ""owner"": ""seat_south"", ""transform"": { ""position"": [0, 0.05, -0.42] }, ""size"": [0.6, 0.1, 0.12] } ],
  ""objects"": [
    { ""id"": ""gear"", ""type"": ""Deck"", ""transform"": { ""position"": [-0.3, 0, 0.1], ""rotation"": [0, 180, 180] },
      ""style"": { ""accent"": [0.55, 0.36, 0.1], ""backLabel"": ""EQUIPMENT"" },
      ""cards"": [ { ""id"": ""sword"", ""text"": { ""title"": ""Sword"", ""kind"": ""Equipment"", ""body"": ""Attack +1."", ""footer"": ""Price 4"" } },
                   { ""id"": ""shield"", ""text"": { ""title"": ""Shield"", ""body"": ""Attack +2."" } } ] },
    { ""id"": ""note"", ""type"": ""Card"", ""transform"": { ""position"": [0.2, 0, 0.1], ""rotation"": [0, 180, 0] },
      ""text"": { ""title"": ""Day of Truce"", ""kind"": ""Day"", ""body"": ""Nobody may start a duel today."" } }
  ] }";

        [UnityTest]
        public IEnumerator TextFacesAndBacks_AreDrawn_AndShownOnTheTableAndInTheHand()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(2f);
            Assert.IsTrue(table.Load(Json, null));

            var note = (Card)table.Engine.Find("note");
            var faceId = TextFaces.FaceId(note);
            var deck = (Deck)table.Engine.Find("gear");
            var backId = TextFaces.BackId(deck, deck.Cards[0]);
            Texture2D face = null, back = null;
            for (var t = 0f; t < 5f && (face == null || back == null); t += Time.unscaledDeltaTime)
            {
                face = table.Assets.Texture(faceId);
                back = table.Assets.Texture(backId);
                yield return null;
            }
            Assert.IsNotNull(face, "the face was drawn");
            Assert.IsNotNull(back, "the back was drawn");
            Assert.AreEqual(TextCardPainter.CellWidth, face.width);
            Assert.AreEqual(TextCardPainter.CellHeight(TextFaces.Aspect(Card.DefaultSize)), face.height);
            yield return null;

            // The face-up card shows its drawing on top; the face-down deck shows the drawn back on top.
            Assert.AreSame(face, table.ViewOf("note").Renderer.sharedMaterials[0].mainTexture);
            Assert.AreSame(back, table.ViewOf("gear").Renderer.sharedMaterials[1].mainTexture);

            // Dealt into the hand: the hand bar shows the card's drawn face.
            Assert.IsTrue(table.Execute(new DealCommand { DeckId = "gear", Count = 1, Seats = { "seat_south" } }).Ok);
            VisualElement card = null;
            for (var t = 0f; t < 5f; t += Time.unscaledDeltaTime)
            {
                card = Object.FindObjectsByType<UIDocument>().Select(d => d.rootVisualElement?.Q("hand-sword")).FirstOrDefault(e => e != null);
                if (card != null && card.resolvedStyle.backgroundImage.sprite != null)
                    break;
                yield return null;
            }
            Assert.IsNotNull(card, "the dealt card is in the hand bar");
            Assert.IsNotNull(card.resolvedStyle.backgroundImage.sprite, "with its drawn face");
        }

        [Test]
        public void FittedSizes_StayInTheirRanges()
        {
            Assert.AreEqual(40f, TextCardPainter.BodySize("Attack +1.", 500f));
            Assert.Less(TextCardPainter.BodySize(new string('x', 600), 500f), 26f);
            Assert.GreaterOrEqual(TextCardPainter.BodySize(new string('x', 5000), 200f), 16f);
            Assert.Less(TextCardPainter.BackLabelSize("EQUIPAMIENTO"), TextCardPainter.BackLabelSize("ITEM"));
            Assert.Greater(TextCardPainter.Readable(new Color(0.9f, 0.9f, 0.5f)).r, 0f);
            Assert.Less(TextCardPainter.Readable(new Color(0.9f, 0.9f, 0.5f)).r, 0.5f, "dark ink on a light band");
            Assert.Greater(TextCardPainter.Readable(new Color(0.2f, 0.1f, 0.1f)).r, 0.5f, "light ink on a dark band");
        }
    }
}
