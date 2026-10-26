using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>A blind round: what each seat moves is hidden from the others until the reveal.</summary>
    public class BlindRoundTests
    {
        [Fact]
        public void MovesInABlindRound_AreSeenOnlyByTheMover_UntilTheReveal()
        {
            var engine = EngineFixture.Create();
            var loose = engine.Find("loose")!;
            loose.InvisibleTo.Add("purple");
            engine.Ok(new BlindRoundCommand { On = true, Actor = "seat_1" });

            engine.Ok(new MoveCommand { Actor = "seat_1", ObjectId = "loose", Position = new Vec3(0.2f, 0, 0.1f), Rotation = Vec3.Zero });
            engine.Ok(new MoveCommand { Actor = "seat_2", ObjectId = "d6", Position = new Vec3(-0.2f, 0, 0.1f), Rotation = Vec3.Zero });
            var taken = engine.Ok(new TakeCommand { Actor = "seat_3", ContainerId = "bag", Position = new Vec3(0, 0, 0.2f) }).CreatedId!;
            // A move without a seat (the host's setup, a script) is not part of anyone's blind move.
            engine.Ok(new MoveCommand { ObjectId = "coins", Position = new Vec3(0.1f, 0, -0.1f), Rotation = Vec3.Zero });

            Assert.Equal(new[] { "purple", "blue", "green" }, loose.InvisibleTo);
            Assert.Equal(new[] { "red", "green" }, engine.Find("d6")!.InvisibleTo);
            Assert.Equal(new[] { "red", "blue" }, engine.Find(taken)!.InvisibleTo);
            Assert.Empty(engine.Find("coins")!.InvisibleTo);
            Assert.Equal("seat_2", engine.State.Blind!.MovedBy["d6"]);

            var reveal = engine.Ok(new BlindRoundCommand { On = false, Actor = "seat_2" });
            Assert.Null(engine.State.Blind);
            Assert.Equal(new[] { "purple" }, loose.InvisibleTo);
            Assert.Empty(engine.Find("d6")!.InvisibleTo);
            Assert.Empty(engine.Find(taken)!.InvisibleTo);
            Assert.Contains(reveal.Events.OfType<GameMessage>(), m => m.Text.Contains("revealed the blind round (3 pieces)"));
        }

        [Fact]
        public void CardsDealtToAHand_StayHiddenByTheHand_NotByTheRound()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new BlindRoundCommand { On = true });
            engine.Ok(new DealCommand { Actor = "seat_1", DeckId = "deck", Count = 1, Seats = { "seat_1", "seat_2" } });
            Assert.All(engine.State.Objects.OfType<Card>().Where(c => c.Id != "loose"), c => Assert.Empty(c.InvisibleTo));
        }

        [Fact]
        public void TheRound_IsSaved_AndASecondStartKeepsWhatIsHidden()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new BlindRoundCommand { On = true });
            engine.Ok(new MoveCommand { Actor = "seat_1", ObjectId = "d6", Position = new Vec3(0.2f, 0, 0), Rotation = Vec3.Zero });
            engine.Ok(new BlindRoundCommand { On = true });
            Assert.Equal("seat_1", engine.State.Blind!.MovedBy["d6"]);
            var again = TableJson.Parse(TableJson.Serialize(engine.State)).State!;
            Assert.Equal(new[] { "blue", "green" }, again.Objects.First(o => o.Id == "d6").InvisibleTo);
            Assert.Empty(again.Blind!.Before["d6"]);
        }
    }
}
