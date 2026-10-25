using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>T2.11a: the TTS editing and deck-handling commands.</summary>
    public class EditCommandTests
    {
        private static TableEngine WithCounterAndClock()
        {
            var state = TableJson.Parse(EngineFixture.Json).State!;
            state.Objects.Add(new Counter { Id = "score", Value = 3, Locked = true });
            state.Objects.Add(new Clock { Id = "clock", Mode = ClockMode.Timer, Seconds = 60 });
            Assert.Empty(TableValidator.Validate(state));
            return new TableEngine(state, 1);
        }

        [Fact]
        public void Counter_SetAndAdd_EvenWhenLocked_ClampedToTheDisplay()
        {
            var engine = WithCounterAndClock();
            Assert.True(engine.Execute(new SetCounterCommand { ObjectId = "score", Add = 1 }).Ok);
            Assert.True(engine.Execute(new SetCounterCommand { ObjectId = "score", Add = -10 }).Ok);
            Assert.Equal(-6, ((Counter)engine.Find("score")!).Value);
            Assert.True(engine.Execute(new SetCounterCommand { ObjectId = "score", Value = 42 }).Ok);
            Assert.True(engine.Execute(new SetCounterCommand { ObjectId = "score", Value = int.MaxValue, Add = int.MaxValue }).Ok);
            Assert.Equal(Counter.MaxValue, ((Counter)engine.Find("score")!).Value);
            Assert.Equal(ErrorCodes.MissingField, engine.Execute(new SetCounterCommand { ObjectId = "score" }).Error!.Code);
            Assert.Equal(ErrorCodes.WrongObjectType, engine.Execute(new SetCounterCommand { ObjectId = "d6", Add = 1 }).Error!.Code);

            var json = TableJson.Serialize(engine.State);
            Assert.Contains("\"type\": \"Counter\"", json);
            Assert.Equal(Counter.MaxValue, ((Counter)TableJson.Parse(json).State!.Objects.Single(o => o.Id == "score")).Value);
        }

        [Fact]
        public void Clock_StartPauseModes_AndATimerAtZeroStops()
        {
            var engine = WithCounterAndClock();
            var clock = (Clock)engine.Find("clock")!;
            Assert.True(engine.Execute(new SetClockCommand { ObjectId = "clock", Running = true }).Ok);
            Assert.True(clock.Running);
            Assert.True(engine.Execute(new SetClockCommand { ObjectId = "clock", Running = false, Seconds = 41.5f }).Ok);
            Assert.False(clock.Running);
            Assert.Equal(41.5f, clock.Seconds);
            Assert.True(engine.Execute(new SetClockCommand { ObjectId = "clock", Seconds = 0, Running = true }).Ok);
            Assert.False(clock.Running);   // a timer at zero does not run
            Assert.True(engine.Execute(new SetClockCommand { ObjectId = "clock", Mode = ClockMode.Stopwatch, Running = true }).Ok);
            Assert.True(clock.Running);
            Assert.Equal(ClockMode.Stopwatch, clock.Mode);
            Assert.False(engine.Execute(new SetClockCommand { ObjectId = "clock", Seconds = -1 }).Ok);
            Assert.False(engine.Execute(new SetClockCommand { ObjectId = "clock", Mode = (ClockMode)9 }).Ok);
            Assert.Equal(TableJson.Serialize(engine.State), TableJson.Serialize(TableJson.Parse(TableJson.Serialize(engine.State)).State!));
        }

        [Fact]
        public void Scale_MultipliesAndClamps_AndRefusesBadInput()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new ScaleCommand { ObjectId = "d6", Factor = 2f });
            Assert.Equal(new Vec3(2, 2, 2), engine.Find("d6")!.Transform.Scale);
            for (var i = 0; i < 10; i++)
                engine.Ok(new ScaleCommand { ObjectId = "d6", Factor = 2f });
            Assert.Equal(TableEngine.MaxScale, engine.Find("d6")!.Transform.Scale.X);

            engine.Fails(new ScaleCommand { ObjectId = "d6", Factor = 0f }, ErrorCodes.InvalidValue);
            engine.Fails(new ScaleCommand { ObjectId = "locked_die" }, ErrorCodes.ObjectLocked);
            engine.AssertValid();
        }

        [Fact]
        public void Delete_RemovesObjects_AndBagsWithTheirContents()
        {
            var engine = EngineFixture.Create();

            var bag = engine.Ok(new DeleteCommand { ObjectId = "bag" });
            engine.Ok(new DeleteCommand { ObjectId = "loose" });

            Assert.Equal(new[] { "t1", "t2", "t3", "bag" }, bag.Events.OfType<ObjectRemoved>().Select(e => e.ObjectId));
            Assert.All(new[] { "bag", "t1", "t2", "t3", "loose" }, id => Assert.Null(engine.Find(id)));
            Assert.Null(engine.State.Zones.Single(z => z.Id == "snap").Parent);
            engine.Fails(new DeleteCommand { ObjectId = "locked_die" }, ErrorCodes.ObjectLocked);
            engine.Fails(new DeleteCommand { ObjectId = "coin" }, ErrorCodes.NotOnTable);
            // Contents: empties a container and keeps it (TTS reset()).
            var emptied = engine.Ok(new DeleteCommand { ObjectId = "coins", Contents = true });
            Assert.Equal(new[] { "coin" }, emptied.Events.OfType<ObjectRemoved>().Select(e => e.ObjectId));
            Assert.NotNull(engine.Find("coins"));
            engine.AssertValid();
        }

        [Fact]
        public void Clone_CopiesDecksBagsAndStates_UnderNewIds()
        {
            var engine = EngineFixture.Create();

            var deck = engine.Ok(new CloneCommand { ObjectId = "deck", Position = new Vec3(0.5f, 0, 0.3f) });
            var bag = engine.Ok(new CloneCommand { ObjectId = "bag" });
            var infinite = engine.Ok(new CloneCommand { ObjectId = "coins" });

            var copy = engine.Deck(deck.CreatedId!);
            Assert.Equal("deck-1", copy.Id);
            Assert.Equal(new Vec3(0.5f, 0, 0.3f), copy.Transform.Position);
            Assert.Equal(new[] { "c0-1", "c1-1" }, copy.CardIds().Take(2));
            Assert.Equal(10, copy.Cards.Count);
            Assert.Equal(new[] { "t1-1", "t2-1", "t3-1" }, engine.ContentsOf(bag.CreatedId!).Select(o => o.Id));
            var template = engine.ContentsOf(infinite.CreatedId!).Single();
            Assert.Equal("coin_back-1", template.States["2"].Id);
            engine.AssertValid();
        }

        [Fact]
        public void SetState_SwapsTheActiveObject_KeepingItsPose()
        {
            var state = TableJson.Parse(EngineFixture.Json).State!;
            var book = new Token { Id = "book_closed", Name = "Closed" };
            book.Transform.Position = new Vec3(0.2f, 0, 0.2f);
            book.States["2"] = new Token { Id = "book_open", Name = "Open" };
            book.States["3"] = new Token { Id = "book_torn", Name = "Torn" };
            state.Objects.Add(book);
            var engine = new TableEngine(state, 1);

            var open = engine.Ok(new SetStateCommand { ObjectId = "book_closed", State = "2" });

            Assert.Equal(("book_closed", "book_open"), (open.Events[0].ObjectId, open.Events[1].ObjectId));
            var active = engine.Find("book_open")!;
            Assert.Equal(new Vec3(0.2f, 0, 0.2f), active.Transform.Position);
            Assert.Equal(new[] { "1", "3" }, active.States.Keys.OrderBy(k => k));
            Assert.Null(engine.Find("book_closed"));

            engine.Ok(new SetStateCommand { ObjectId = "book_open", State = "1" });
            Assert.Equal("Closed", engine.Find("book_closed")!.Name);
            Assert.Equal(new[] { "2", "3" }, engine.Find("book_closed")!.States.Keys.OrderBy(k => k));
            engine.Fails(new SetStateCommand { ObjectId = "book_closed", State = "9" }, ErrorCodes.InvalidValue);
            engine.Fails(new SetStateCommand { ObjectId = "d6", State = "2" }, ErrorCodes.InvalidValue);
            engine.AssertValid();
        }

        [Fact]
        public void Cut_LiftsTheTopHalfOrACount()
        {
            var engine = EngineFixture.Create();

            var half = engine.Ok(new CutCommand { DeckId = "deck" });
            Assert.Equal(new[] { "c0", "c1", "c2", "?", "c4" }, engine.Deck(half.CreatedId!).CardIds());
            Assert.Equal(new[] { "c5", "c6", "c7", "c8", "c9" }, engine.Deck().CardIds());

            var two = engine.Ok(new CutCommand { DeckId = "deck", Count = 2 });
            Assert.Equal(new[] { "c5", "c6" }, engine.Deck(two.CreatedId!).CardIds());
            engine.Fails(new CutCommand { DeckId = "deck", Count = 3 }, ErrorCodes.InvalidValue);
            engine.Fails(new CutCommand { DeckId = "d6" }, ErrorCodes.WrongObjectType);
            engine.AssertValid();
        }

        [Fact]
        public void Split_MakesPilesInARow()
        {
            var engine = EngineFixture.Create();

            var result = engine.Ok(new SplitCommand { DeckId = "deck", Piles = 3 });

            Assert.Equal(4, engine.Deck().Cards.Count);
            Assert.Equal(new[] { 4, 2 }, result.CreatedIds!.Select(id => engine.Deck(id).Cards.Count));
            Assert.Equal(new[] { "c8", "c9" }, engine.Deck(result.CreatedIds![1]).CardIds());
            Assert.True(engine.Find(result.CreatedIds[0])!.Transform.Position.X > engine.Deck().Transform.Position.X);
            engine.Fails(new SplitCommand { DeckId = "deck", Piles = 1 }, ErrorCodes.InvalidValue);
            engine.Fails(new SplitCommand { DeckId = "deck", Piles = 5 }, ErrorCodes.InvalidValue);
            engine.AssertValid();
        }

        [Fact]
        public void Spread_LaysEveryCardOutInOrder()
        {
            var engine = EngineFixture.Create();

            var result = engine.Ok(new SpreadCommand { DeckId = "deck" });

            Assert.Empty(engine.Deck().Cards);
            Assert.Equal(10, result.CreatedIds!.Count);
            Assert.Equal("c0", result.CreatedIds[0]);
            var xs = result.CreatedIds.Select(id => engine.Find(id)!.Transform.Position.X).ToList();
            Assert.Equal(xs.OrderBy(x => x), xs);
            Assert.All(result.CreatedIds, id => Assert.False(engine.Find(id)!.IsFaceUp));
            engine.Fails(new SpreadCommand { DeckId = "deck" }, ErrorCodes.ContainerEmpty);
            engine.AssertValid();
        }

        [Fact]
        public void SetDieValue_AndSetProperties()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new SetDieValueCommand { ObjectId = "d6", Value = 3 });
            engine.Ok(new SetPropertiesCommand { ObjectId = "loose", Name = "Trump", Description = "Shown to all", Tint = new[] { 1f, 0.5f, 0.5f } });

            Assert.Equal(3, ((Die)engine.Find("d6")!).Value);
            Assert.Equal(("Trump", "Shown to all"), (engine.Find("loose")!.Name, engine.Find("loose")!.Description));
            engine.Fails(new SetDieValueCommand { ObjectId = "d6", Value = 7 }, ErrorCodes.InvalidValue);
            engine.Fails(new SetDieValueCommand { ObjectId = "deck", Value = 1 }, ErrorCodes.WrongObjectType);
            engine.Fails(new SetPropertiesCommand { ObjectId = "loose", Tint = new[] { 2f, 0f, 0f } }, ErrorCodes.InvalidValue);
            engine.Ok(new SetPropertiesCommand { ObjectId = "loose", Name = "", ClearTint = true });
            Assert.Null(engine.Find("loose")!.Name);
            Assert.Null(engine.Find("loose")!.Tint);
            engine.AssertValid();
        }
    }
}
