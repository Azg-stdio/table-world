using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    public class CommandTests
    {
        [Fact]
        public void Move_SetsPose_AndRejectsLockedOrContainedObjects()
        {
            var engine = EngineFixture.Create();

            var result = engine.Ok(new MoveCommand { ObjectId = "d6", Position = new Vec3(1, 0, 2), Rotation = new Vec3(0, 90, 0) });

            Assert.Equal(new Vec3(1, 0, 2), engine.Find("d6")!.Transform.Position);
            Assert.IsType<ObjectUpdated>(Assert.Single(result.Events));
            engine.Fails(new MoveCommand { ObjectId = "locked_die" }, ErrorCodes.ObjectLocked);
            engine.Fails(new MoveCommand { ObjectId = "t1" }, ErrorCodes.NotOnTable);
            Assert.Equal("Did you mean 'd6'?", engine.Fails(new MoveCommand { ObjectId = "d7" }, ErrorCodes.UnknownObject).Suggestion);
            engine.AssertValid();
        }

        [Fact]
        public void Take_FromDeck_TopBottomIndexAndEntryId()
        {
            var engine = EngineFixture.Create();

            var top = engine.Ok(new TakeCommand { ContainerId = "deck" });
            var bottom = engine.Ok(new TakeCommand { ContainerId = "deck", FromBottom = true });
            var byId = engine.Ok(new TakeCommand { ContainerId = "deck", EntryId = "c5" });
            var byIndex = engine.Ok(new TakeCommand { ContainerId = "deck", Index = 2 });

            Assert.Equal(new[] { "c0", "c9", "c5", "?" }, new[] { top.CreatedId, bottom.CreatedId, byId.CreatedId, byIndex.CreatedId == "deck-1" ? "?" : byIndex.CreatedId });
            Assert.Equal(new[] { "c1", "c2", "c4", "c6", "c7", "c8" }, engine.Deck().CardIds());

            var c0 = Assert.IsType<Card>(engine.Find("c0"));
            Assert.Equal(1, (int)c0.Meta!["rank"]!);
            Assert.False(c0.IsFaceUp);
            Assert.Equal(new Vec3(TableEngine.BesideOffset, 0, 0), c0.Transform.Position);
            Assert.Equal("alt_back", ((Card)engine.Find("c9")!).BackAsset);
            Assert.Equal("anonymous", engine.Find("deck-1")!.Name);
            engine.AssertValid();
        }

        [Fact]
        public void Take_WithPoseAndFailures()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new TakeCommand { ContainerId = "deck", Position = new Vec3(0, 0, 0.2f), Rotation = Vec3.Zero });

            Assert.True(engine.Find("c0")!.IsFaceUp);
            engine.Fails(new TakeCommand { ContainerId = "deck", Index = 9 }, ErrorCodes.InvalidValue);
            engine.Fails(new TakeCommand { ContainerId = "deck", EntryId = "c0" }, ErrorCodes.UnknownObject);
            engine.Fails(new TakeCommand { ContainerId = "d6" }, ErrorCodes.WrongObjectType);
            for (var i = 0; i < 9; i++)
                engine.Ok(new TakeCommand { ContainerId = "deck" });
            engine.Fails(new TakeCommand { ContainerId = "deck" }, ErrorCodes.ContainerEmpty);
            Assert.NotNull(engine.Find("deck"));
            engine.AssertValid();
        }

        [Fact]
        public void Take_FromBagAndInfiniteBag()
        {
            var engine = EngineFixture.Create();

            var fromBag = engine.Ok(new TakeCommand { ContainerId = "bag" });
            var first = engine.Ok(new TakeCommand { ContainerId = "coins" });
            var second = engine.Ok(new TakeCommand { ContainerId = "coins" });

            Assert.Equal("t1", fromBag.CreatedId);
            Assert.Null(engine.Find("t1")!.Container);
            Assert.Equal(new[] { "t2", "t3" }, engine.ContentsOf("bag").Select(o => o.Id));
            var change = Assert.IsType<ContainerChanged>(Assert.Single(fromBag.Events));
            Assert.Equal(("bag", (string?)null), (change.From, change.To));

            Assert.Equal(("coin-1", "coin-2"), (first.CreatedId, second.CreatedId));
            var clone = Assert.IsType<Token>(engine.Find("coin-1"));
            Assert.Equal(TokenShape.Round, clone.Shape);
            Assert.Equal("coin_back-1", clone.States["2"].Id);
            Assert.Equal(new[] { "coin" }, engine.ContentsOf("coins").Select(o => o.Id));
            engine.AssertValid();
        }

        [Fact]
        public void Insert_CardOnDeck_TopOrBottom_KeepsIdAndFollowsDeckDefaults()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new InsertCommand { ObjectId = "loose", IntoId = "deck" });
            engine.Ok(new TakeCommand { ContainerId = "deck" });
            engine.Ok(new InsertCommand { ObjectId = "loose", IntoId = "deck", AtBottom = true });

            var deck = engine.Deck();
            Assert.Equal("loose", deck.Cards.Last().Id);
            Assert.Null(deck.Cards.Last().FaceAsset);
            Assert.Equal(20, deck.Cards.Last().FaceIndex);
            Assert.Null(engine.Find("loose"));
            Assert.Null(engine.State.Zones.Single(z => z.Id == "snap").Parent);
            engine.AssertValid();
        }

        [Fact]
        public void Insert_CardOnCard_FormsADeck()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new TakeCommand { ContainerId = "deck", Position = new Vec3(0.5f, 0, 0) });

            var result = engine.Ok(new InsertCommand { ObjectId = "c0", IntoId = "loose" });

            var deck = engine.Deck(result.CreatedId!);
            Assert.Equal("deck-1", deck.Id);
            Assert.Equal(new[] { "c0", "loose" }, deck.CardIds());
            Assert.Equal(new Vec3(0.3f, 0, 0), deck.Transform.Position);
            Assert.Null(engine.Find("c0"));
            Assert.Null(engine.Find("loose"));
            engine.AssertValid();
        }

        [Fact]
        public void Insert_DeckOnDeck_Merges_AndDeckOnCard_SwallowsTheCard()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new TakeCommand { ContainerId = "deck" });
            engine.Ok(new TakeCommand { ContainerId = "deck" });
            var small = engine.Ok(new InsertCommand { ObjectId = "c1", IntoId = "c0" }).CreatedId!;

            engine.Ok(new InsertCommand { ObjectId = small, IntoId = "deck" });
            Assert.Equal(new[] { "c1", "c0", "c2" }, engine.Deck().CardIds().Take(3));
            Assert.Null(engine.Find(small));

            engine.Ok(new InsertCommand { ObjectId = "deck", IntoId = "loose" });
            Assert.Equal("loose", engine.Deck().CardIds().Last());
            Assert.Equal("alt_back", engine.Deck().Cards.Single(c => c.Id == "c9").BackAsset);
            engine.AssertValid();
        }

        [Fact]
        public void Insert_IntoBags()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new InsertCommand { ObjectId = "d6", IntoId = "bag" });
            engine.Ok(new InsertCommand { ObjectId = "loose", IntoId = "bag", AtBottom = true });

            Assert.Equal(new[] { "d6", "t1", "t2", "t3", "loose" }, engine.ContentsOf("bag").Select(o => o.Id));
            engine.Fails(new InsertCommand { ObjectId = "deck", IntoId = "coins" }, ErrorCodes.InvalidTarget);
            engine.Fails(new InsertCommand { ObjectId = "bag", IntoId = "bag" }, ErrorCodes.InvalidTarget);
            engine.Fails(new InsertCommand { ObjectId = "deck", IntoId = "locked_die" }, ErrorCodes.InvalidTarget);
            engine.Fails(new InsertCommand { ObjectId = "t1", IntoId = "deck" }, ErrorCodes.NotOnTable);
            engine.AssertValid();
        }

        [Fact]
        public void Flip_TurnsOver_AndReversesADeck()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new FlipCommand { ObjectId = "deck" });
            engine.Ok(new FlipCommand { ObjectId = "loose" });

            Assert.True(engine.Deck().IsFaceUp);
            Assert.Equal("c9", engine.Deck().Cards[0].Id);
            Assert.Equal(0f, engine.Deck().Transform.Rotation.Z);
            Assert.False(engine.Find("loose")!.IsFaceUp);
            engine.Fails(new FlipCommand { ObjectId = "locked_die" }, ErrorCodes.ObjectLocked);
        }

        [Fact]
        public void Shuffle_PermutesDeckAndBag()
        {
            var engine = EngineFixture.Create();
            var before = engine.Deck().CardIds();

            var result = engine.Ok(new ShuffleCommand { ObjectId = "deck" });
            engine.Ok(new ShuffleCommand { ObjectId = "bag" });

            Assert.NotEqual(before, engine.Deck().CardIds());
            Assert.Equal(before.OrderBy(x => x), engine.Deck().CardIds().OrderBy(x => x));
            Assert.Contains(result.Events, e => e is Shuffled);
            Assert.Equal(new[] { "t1", "t2", "t3" }, engine.ContentsOf("bag").Select(o => o.Id).OrderBy(x => x));
            engine.Fails(new ShuffleCommand { ObjectId = "d6" }, ErrorCodes.WrongObjectType);
            engine.Fails(new ShuffleCommand { ObjectId = "coins" }, ErrorCodes.WrongObjectType);
            engine.AssertValid();
        }

        [Fact]
        public void Search_ReturnsContentsWithoutChangingState()
        {
            var engine = EngineFixture.Create();
            var hash = TableJson.Hash(engine.State);

            var deck = engine.Ok(new SearchCommand { ContainerId = "deck", Actor = "seat_1" });
            var bag = engine.Ok(new SearchCommand { ContainerId = "bag" });

            Assert.Equal(10, deck.DeckContents!.Count);
            Assert.Equal("seat_1", Assert.IsType<Searched>(Assert.Single(deck.Events)).Actor);
            Assert.Equal(new[] { "t1", "t2", "t3" }, bag.BagContents);
            Assert.Equal(hash, TableJson.Hash(engine.State));
        }

        [Fact]
        public void Deal_RoundRobinIntoHandZones()
        {
            var engine = EngineFixture.Create();

            var result = engine.Ok(new DealCommand { DeckId = "deck", Count = 3, Seats = { "seat_1", "seat_2" } });

            Assert.Equal(new[] { "c0", "c2", "c4" }, result.Dealt!["seat_1"]);
            Assert.Equal(new[] { "c1", "deck-1", "c5" }, result.Dealt!["seat_2"]);
            var hand1 = engine.State.Zones.Single(z => z.Id == "hand_1");
            var hand2 = engine.State.Zones.Single(z => z.Id == "hand_2");
            Assert.All(result.Dealt["seat_1"], id => Assert.True(Geometry.Contains(hand1, engine.Find(id)!.Transform.Position)));
            Assert.All(result.Dealt["seat_2"], id => Assert.True(Geometry.Contains(hand2, engine.Find(id)!.Transform.Position)));
            Assert.All(result.Dealt["seat_2"], id => Assert.True(engine.Find(id)!.IsFaceUp));
            Assert.Equal(4, engine.Deck().Cards.Count);
            engine.AssertValid();
        }

        [Fact]
        public void Deal_ToAllSeats_StopsWhenTheDeckRunsOut_AndChecksSeats()
        {
            var engine = EngineFixture.Create();

            engine.Fails(new DealCommand { DeckId = "deck" }, ErrorCodes.NoHandZone);
            engine.Fails(new DealCommand { DeckId = "deck", Seats = { "seat_9" } }, ErrorCodes.UnknownSeat);
            engine.Fails(new DealCommand { DeckId = "deck", Count = 0, Seats = { "seat_1" } }, ErrorCodes.InvalidValue);
            var result = engine.Ok(new DealCommand { DeckId = "deck", Count = 8, Seats = { "seat_1", "seat_2" } });

            Assert.Equal(5, result.Dealt!["seat_1"].Count);
            Assert.Equal(5, result.Dealt!["seat_2"].Count);
            Assert.Empty(engine.Deck().Cards);
            engine.AssertValid();
        }

        [Fact]
        public void Lock_BlocksMovesUntilUnlocked()
        {
            var engine = EngineFixture.Create();

            engine.Ok(new LockCommand { ObjectId = "d6" });
            engine.Fails(new RollCommand { ObjectId = "d6" }, ErrorCodes.ObjectLocked);
            engine.Ok(new LockCommand { ObjectId = "d6", Locked = false });
            engine.Ok(new RollCommand { ObjectId = "d6" });
        }

        [Fact]
        public void Group_CardsIntoNewDeck_OrIntoTheFirstDeck()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new TakeCommand { ContainerId = "deck" });
            engine.Ok(new TakeCommand { ContainerId = "deck" });

            var group = engine.Ok(new GroupCommand { ObjectIds = { "c1", "loose", "c0" } });
            Assert.Equal(new[] { "c1", "loose", "c0" }, engine.Deck(group.CreatedId!).CardIds());

            var merged = engine.Ok(new GroupCommand { ObjectIds = { "deck", group.CreatedId! } });
            Assert.Equal("deck", merged.CreatedId);
            Assert.Equal(new[] { "c1", "loose", "c0" }, engine.Deck().CardIds().Skip(8));

            engine.Fails(new GroupCommand { ObjectIds = { "deck" } }, ErrorCodes.InvalidValue);
            engine.Fails(new GroupCommand { ObjectIds = { "deck", "d6" } }, ErrorCodes.WrongObjectType);
            engine.AssertValid();
        }

        [Fact]
        public void Roll_SetsAValueInRange()
        {
            var engine = EngineFixture.Create();

            var result = engine.Ok(new RollCommand { ObjectId = "d6" });

            var value = ((Die)engine.Find("d6")!).Value!.Value;
            Assert.InRange(value, 1, 6);
            Assert.Equal(value, Assert.IsType<DieRolled>(result.Events[0]).Value);
            engine.Fails(new RollCommand { ObjectId = "deck" }, ErrorCodes.WrongObjectType);
        }
    }
}
