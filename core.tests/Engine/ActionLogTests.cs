using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G11 game log: lines describe what happened without naming cards the reader may not see.</summary>
    public class ActionLogTests
    {
        private static string Single(TableEngine engine, Command command, string? actor = "seat_1", string? viewer = "seat_1")
        {
            var result = engine.Ok(command);
            var (rules, text) = Assert.Single(ActionLog.Describe(engine, command, result, actor, viewer));
            Assert.False(rules);
            return text;
        }

        [Fact]
        public void CommonActions_ReadAsSentences()
        {
            var engine = EngineFixture.Create();
            Assert.Equal("seat_1 shuffled a deck", Single(engine, new ShuffleCommand { ObjectId = "deck" }));
            Assert.Equal("seat_1 dealt 2 cards from a deck to seat_2", Single(engine, new DealCommand { DeckId = "deck", Count = 2, Seats = { "seat_2" } }));
            Assert.Equal("seat_1 dealt 1 card from a deck to seat_1, seat_2", Single(engine, new DealCommand { DeckId = "deck", Count = 1, Seats = { "seat_1", "seat_2" } }));
            Assert.StartsWith("seat_1 rolled a d6: ", Single(engine, new RollCommand { ObjectId = "d6" }));
            Assert.Equal("seat_1 locked a d6", Single(engine, new LockCommand { ObjectId = "d6" }));
            Assert.Equal("Shuffled a deck", Single(engine, new ShuffleCommand { ObjectId = "deck" }, actor: null));
            Assert.Equal("seat_1 searched a deck", Single(engine, new SearchCommand { ContainerId = "deck", Actor = "seat_1" }));
        }

        [Fact]
        public void LabelledDice_AnnounceTheirFace_AndAreNotAddedUp()
        {
            var engine = EngineFixture.Create();
            var die = (Die)engine.Find("d6")!;
            die.Labels = new List<string> { "Trade", "Extra Turn", "Battle", "Legend", "Event", "Item" };
            var text = Single(engine, new RollCommand { ObjectId = "d6" });
            Assert.Contains(text.Substring("seat_1 rolled a d6: ".Length), die.Labels);
            Assert.Equal("Extra Turn", die.Face(2));
            Assert.Equal("7", die.Face(7));
            Assert.False(die.Numeric);
            Assert.True(new Die { Labels = new List<string> { "1", "2", "", "4", "5", "6" } }.Numeric);
        }

        [Fact]
        public void Moves_AreNotLogged_NorRefusedCommands()
        {
            var engine = EngineFixture.Create();
            var move = new MoveCommand { ObjectId = "d6", Position = new Vec3(0.1f, 0, 0) };
            Assert.Empty(ActionLog.Describe(engine, move, engine.Ok(move), "seat_1", "seat_1"));
            var refused = new ShuffleCommand { ObjectId = "nothing" };
            Assert.Empty(ActionLog.Describe(engine, refused, engine.Execute(refused), "seat_1", "seat_1"));
        }

        [Fact]
        public void Cards_AreNamedOnlyFaceUpAndInSight()
        {
            var engine = EngineFixture.Create();
            ((Card)engine.Find("loose")!).Name = "Ace of Spades";
            // The loose card starts face up on the open table.
            Assert.Equal("Ace of Spades", ActionLog.NameFor(engine, "loose", "seat_2"));
            Assert.Equal("seat_1 flipped a card", Single(engine, new FlipCommand { ObjectId = "loose" }));
            Assert.Equal("seat_1 flipped Ace of Spades", Single(engine, new FlipCommand { ObjectId = "loose" }));

            // Into seat_1's hand: seat_1 may read it, seat_2 may not.
            engine.Ok(new MoveCommand { ObjectId = "loose", Position = new Vec3(0, 0.05f, -0.5f) });
            Assert.Equal("Ace of Spades", ActionLog.NameFor(engine, "loose", "seat_1"));
            Assert.Equal("a card", ActionLog.NameFor(engine, "loose", "seat_2"));

            // A card drawn face down from the deck is never named.
            var take = new TakeCommand { ContainerId = "deck" };
            var lines = ActionLog.Describe(engine, take, engine.Ok(take), "seat_2", "seat_1");
            Assert.Equal("seat_2 took a card from a deck", lines.Single().Text);
        }

        [Fact]
        public void HiddenZones_HideEvenWhatKindOfThingItIs()
        {
            var engine = EngineFixture.Create();
            engine.State.Zones.Add(new HiddenZone { Id = "secret", VisibleTo = { "seat_2" }, Size = new Vec3(0.2f, 0.2f, 0.2f), Transform = new TableTransform { Position = new Vec3(0.8f, 0, 0) } });
            engine.Ok(new MoveCommand { ObjectId = "d6", Position = new Vec3(0.8f, 0, 0) });
            Assert.Equal("something", ActionLog.NameFor(engine, "d6", "seat_1"));
            Assert.Equal("a d6", ActionLog.NameFor(engine, "d6", "seat_2"));
        }
    }
}
