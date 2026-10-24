using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G15: peeks and blindfolds change nothing on the table but are told to everyone through the log.</summary>
    public class AnnouncementTests
    {
        [Fact]
        public void PeekAndBlindfold_ChangeNothing_AndAreLogged()
        {
            var engine = EngineFixture.Create();
            ((Card)engine.Find("loose")!).Name = "Ace of Spades";
            engine.Ok(new FlipCommand { ObjectId = "loose" });
            var before = TableJson.Hash(engine.State);
            var peek = engine.Ok(new PeekCommand { ObjectId = "loose", Actor = "seat_1" });
            var blind = engine.Ok(new BlindfoldCommand { On = true, Actor = "seat_2" });
            Assert.Equal(before, TableJson.Hash(engine.State));
            Assert.Equal("seat_1 peeked at a card", ActionLog.Describe(engine, new PeekCommand { ObjectId = "loose" }, peek, "seat_1", "seat_2").Single().Text);
            Assert.Equal("seat_2 put on a blindfold", ActionLog.Describe(engine, new BlindfoldCommand { On = true }, blind, "seat_2", null).Single().Text);
            engine.Fails(new PeekCommand { ObjectId = "nothing" }, ErrorCodes.UnknownObject);
        }
    }
}
