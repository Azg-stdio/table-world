using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Tests.Engine;

namespace Core.Tests.Rules
{
    /// <summary>Text cards: words instead of a face image, a styled back, and both follow the card in and out of decks.</summary>
    public class TextCardTests
    {
        private const string Json = @"{
  ""assets"": [ { ""id"": ""portraits"", ""type"": ""image"", ""path"": ""img/portraits.png"", ""columns"": 5, ""rows"": 4 } ],
  ""objects"": [
    { ""id"": ""gear"", ""type"": ""Deck"", ""style"": { ""accent"": [0.7, 0.5, 0.1], ""backLabel"": ""EQUIPMENT"" },
      ""cards"": [
        { ""id"": ""sword"", ""text"": { ""title"": ""Sword"", ""kind"": ""Equipment"", ""body"": ""Attack +1."", ""footer"": ""Price 4 · Mana 0"" } },
        { ""id"": ""troll"", ""text"": { ""title"": ""Troll"", ""corner"": ""8x"", ""art"": ""portraits"", ""artIndex"": 5 },
          ""style"": { ""paper"": [0.2, 0.2, 0.3], ""ink"": [1, 1, 1], ""backLabel"": ""EQUIPMENT"" } },
        { ""id"": ""shield"", ""text"": { ""title"": ""Shield"" } } ] },
    { ""id"": ""note"", ""type"": ""Card"", ""text"": { ""title"": ""Loose note"" } }
  ]
}";

        private static TableEngine Engine()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            return new TableEngine(parsed.State!, 1);
        }

        [Fact]
        public void TextCards_NeedNoImages_AndRoundTrip()
        {
            var engine = Engine();
            var note = (Card)engine.Find("note")!;
            Assert.True(note.TextFace);
            Assert.True(note.TextBack);
            var again = TableJson.Parse(TableJson.Serialize(engine.State)).State!;
            var troll = ((Deck)again.Objects.First(o => o.Id == "gear")).Cards[1];
            Assert.Equal("8x", troll.Text!.Corner);
            Assert.Equal(5, troll.Text.ArtIndex);
            Assert.Equal("EQUIPMENT", troll.Style!.BackLabel);
        }

        [Fact]
        public void ATakenCard_KeepsItsWords_AndTheDecksLook_AndPutBackFollowsTheDeckAgain()
        {
            var engine = Engine();
            var taken = engine.Ok(new TakeCommand { ContainerId = "gear", EntryId = "sword" });
            var sword = (Card)engine.Find(taken.CreatedId!)!;
            Assert.Equal("Attack +1.", sword.Text!.Body);
            Assert.Equal("EQUIPMENT", sword.Style!.BackLabel);
            Assert.True(sword.TextFace);

            engine.Ok(new InsertCommand { ObjectId = sword.Id, IntoId = "gear" });
            var entry = engine.Deck("gear").Cards[0];
            Assert.Equal("sword", entry.Id);
            Assert.Equal("Sword", entry.Text!.Title);
            Assert.Null(entry.Style);

            // A card with its own look keeps it.
            var troll = (Card)engine.Find(engine.Ok(new TakeCommand { ContainerId = "gear", EntryId = "troll" }).CreatedId!)!;
            Assert.Equal(new[] { 0.2f, 0.2f, 0.3f }, troll.Style!.Paper);
            engine.Ok(new InsertCommand { ObjectId = troll.Id, IntoId = "gear" });
            Assert.NotNull(engine.Deck("gear").Cards.First(c => c.Id == "troll").Style);
        }

        [Fact]
        public void GroupedTextCards_FormADeckWithTheirLook()
        {
            var engine = Engine();
            var a = engine.Ok(new TakeCommand { ContainerId = "gear", EntryId = "sword", Position = new Vec3(0.3f, 0, 0) }).CreatedId!;
            var b = engine.Ok(new TakeCommand { ContainerId = "gear", EntryId = "shield", Position = new Vec3(0.3f, 0, 0.2f) }).CreatedId!;
            var grouped = engine.Ok(new GroupCommand { ObjectIds = { a, b } });
            var deck = engine.State.Objects.OfType<Deck>().First(d => d.Id != "gear");
            Assert.Equal("EQUIPMENT", deck.Style!.BackLabel);
            Assert.All(deck.Cards, c => Assert.NotNull(c.Text));
            Assert.Empty(TableValidator.Validate(engine.State));
        }

        [Fact]
        public void TheValidator_ChecksArtAndColours_AndStillWantsAFaceForCardsWithoutText()
        {
            var state = TableJson.Parse(Json).State!;
            var gear = (Deck)state.Objects.First(o => o.Id == "gear");
            gear.Cards[1].Text!.Art = "nope";
            gear.Cards[2].Text = null;
            gear.Style!.Accent = new[] { 2f, 0f, 0f };
            var errors = TableValidator.Validate(state);
            Assert.Contains(errors, e => e.Path == "objects[0].cards[1].text.art" && e.Code == ErrorCodes.UnknownAsset);
            Assert.Contains(errors, e => e.Path == "objects[0].cards[2].faceAsset" && e.Code == ErrorCodes.MissingField);
            Assert.Contains(errors, e => e.Path == "objects[0].style.accent");
            // The deck's style still draws that card's back.
            Assert.DoesNotContain(errors, e => e.Path == "objects[0].cards[2].backAsset");
        }

        [Fact]
        public void DrawingIds_AreStable_SharedByIdenticalCards_AndNewWhenTheWordsChange()
        {
            var engine = Engine();
            var gear = engine.Deck("gear");
            var sword = TextFaces.FaceId(gear, gear.Cards[0])!;
            Assert.StartsWith("text:f", sword);
            Assert.Equal(sword, TextFaces.FaceId(new CardText { Title = "Sword", Kind = "Equipment", Body = "Attack +1.", Footer = "Price 4 · Mana 0" }, gear.Style, Card.DefaultSize));
            Assert.NotEqual(sword, TextFaces.FaceId(gear, gear.Cards[2]));
            // Every card of the deck shares its back; the troll's own style has the same back fields, so the same back.
            Assert.Equal(TextFaces.BackId(gear, gear.Cards[0]), TextFaces.BackId(gear, gear.Cards[2]));

            Assert.True(AssetUse.Uses(gear, sword));
            Assert.True(AssetUse.Uses(gear, "portraits"));
            var taken = (Card)engine.Find(engine.Ok(new TakeCommand { ContainerId = "gear", EntryId = "sword" }).CreatedId!)!;
            Assert.Equal(sword, TextFaces.FaceId(taken));
            Assert.True(AssetUse.Uses(taken, sword));
        }
    }
}
