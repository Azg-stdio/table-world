using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>Showing a card to everyone: nothing moves, every peer gets the card to draw big, and the log says who.</summary>
    public class ShowCardTests
    {
        [Fact]
        public void ACard_OrADecksTopCard_IsShown_WithoutMoving()
        {
            var engine = EngineFixture.Create();
            var shown = engine.Ok(new ShowCommand { ObjectId = "loose", Actor = "seat_1" });
            var card = shown.Events.OfType<CardShown>().Single();
            Assert.Equal("loose", card.Card.Id);
            Assert.Equal("seat_1", card.Seat);
            Assert.Contains(shown.Events.OfType<GameMessage>(), m => m.Text.StartsWith("Red shows"));

            var top = engine.Ok(new ShowCommand { ObjectId = "deck" }).Events.OfType<CardShown>().Single();
            Assert.Equal("c0", top.Card.Id);
            Assert.Equal(10, engine.Deck().Cards.Count);
            engine.Fails(new ShowCommand { ObjectId = "d6" }, ErrorCodes.InvalidValue);
        }

        [Fact]
        public void TheRules_ShowTheCardTheyDraw()
        {
            var game = MiniGame.Start(MiniGame.Json(MiniGame.Action(@"{ ""move"": { ""from"": ""d"", ""to"": ""pile"", ""faceUp"": true } }, { ""show"": { ""object"": ""$obj.pile.top"" } }")));
            var result = game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" });
            Assert.True(result.Ok, result.Error?.ToString());
            var shown = result.Events.OfType<CardShown>().Single().Card;
            Assert.Equal("c0", shown.EntryId ?? shown.Id);
        }
    }
}
