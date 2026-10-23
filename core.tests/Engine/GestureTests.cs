using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>Gestures map to rules actions: playing a hand card, drawing from a pile, and refusing what the rules own.</summary>
    public class GestureTests
    {
        private static GameEngine Started(string game)
        {
            var engine = GameFixtures.Load(game, 3);
            Assert.True(engine.Execute(new StartGameCommand()).Ok);
            return engine;
        }

        private static string HandOf(GameEngine game, string seat) =>
            game.State.Players.Seats.First(s => s.Id == seat).HandZone!;

        [Fact]
        public void Uno_DraggingAPlayableHandCard_PlaysIt()
        {
            var game = Started("uno-lite");
            var seat = game.Session!.CurrentSeat!;
            var hand = game.Table.ObjectsIn(HandOf(game, seat)).Select(o => o.Id).ToList();
            var legal = game.LegalActions(seat).First(a => a.ActionId == "play_card").Choices.Select(c => c["card"]).ToList();

            foreach (var card in hand)
            {
                var plays = Gestures.Plays(game, seat, card);
                if (legal.Contains(card))
                    Assert.Equal("play_card", Assert.Single(plays).ActionId);
                else
                    Assert.Empty(plays);
            }

            // Another seat's cards are never playable by this seat, and nobody plays out of turn.
            var other = game.State.Players.Seats.First(s => s.Id != seat).Id;
            Assert.All(game.Table.ObjectsIn(HandOf(game, other)), o => Assert.Empty(Gestures.Plays(game, seat, o.Id)));
            Assert.All(hand, card => Assert.Empty(Gestures.Plays(game, other, card)));
            Assert.True(Gestures.PlaysObjects(game));
        }

        [Fact]
        public void Uno_DraggingFromTheDrawPile_Draws_ButTheDiscardDoesNot()
        {
            var game = Started("uno-lite");
            var seat = game.Session!.CurrentSeat!;
            var other = game.State.Players.Seats.First(s => s.Id != seat).Id;

            Assert.Equal("draw_card", Assert.Single(Gestures.Draws(game, seat, "draw")).ActionId);
            Assert.Empty(Gestures.Draws(game, other, "draw"));
            // The discard pile is only gathered back by the reshuffle: no draw, but the rules own it.
            Assert.Empty(Gestures.Draws(game, seat, "discard"));
            Assert.True(Gestures.RulesTakeFrom(game, seat, "discard"));
            Assert.True(Gestures.RulesTakeFrom(game, other, "draw"));

            var before = game.Table.ObjectsIn(HandOf(game, seat)).Count();
            Assert.True(game.Execute(Gestures.Draws(game, seat, "draw")[0]).Ok);
            Assert.Equal(before + 1, game.Table.ObjectsIn(HandOf(game, seat)).Count());
            Assert.NotEqual(seat, game.Session.CurrentSeat);
        }

        [Fact]
        public void War_DraggingYourDeck_PlaysTheTopCard()
        {
            var game = Started("war");
            foreach (var seat in game.State.Players.Seats.Select(s => s.Id))
            {
                var deck = game.State.Objects.First(o => o.Tags.Contains("deck:" + seat)).Id;
                var other = game.State.Objects.First(o => o.Tags.Any(t => t.StartsWith("deck:") && t != "deck:" + seat)).Id;
                Assert.Equal("play_top_card", Assert.Single(Gestures.Draws(game, seat, deck)).ActionId);
                Assert.Empty(Gestures.Draws(game, seat, other));
                Assert.True(Gestures.RulesTakeFrom(game, seat, deck));
            }
            Assert.False(Gestures.PlaysObjects(game));
        }

        [Fact]
        public void BeforeTheGameStarts_GesturesAreFreePlay()
        {
            var game = GameFixtures.Load("uno-lite", 3);
            Assert.Empty(Gestures.Draws(game, "seat_south", "draw"));
            Assert.False(Gestures.RulesTakeFrom(game, "seat_south", "draw"));
            Assert.False(Gestures.PlaysObjects(game));
        }
    }
}
