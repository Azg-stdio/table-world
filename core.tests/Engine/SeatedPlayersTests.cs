using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>One table for 3–6 players: the game starts with the seats chosen, and the empty ones are left out.</summary>
    public class SeatedPlayersTests
    {
        private static string Json()
        {
            var seats = string.Join(",", Enumerable.Range(1, 6).Select(n => $@"{{ ""id"": ""s{n}"", ""color"": ""{SeatLayout.Colors[n - 1]}"", ""handZone"": ""h{n}"" }}"));
            var hands = string.Join(",", Enumerable.Range(1, 6).Select(n => $@"{{ ""id"": ""h{n}"", ""type"": ""HandZone"", ""owner"": ""s{n}"", ""transform"": {{ ""position"": [{n * 0.2 - 0.7}, 0.05, -0.4] }}, ""size"": [0.15, 0.1, 0.1] }}"));
            return $@"{{
  ""game"": {{ ""id"": ""party"", ""name"": ""Party"", ""minPlayers"": 3, ""maxPlayers"": 6 }},
  ""assets"": [ {{ ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 }}, {{ ""id"": ""back"", ""type"": ""image"", ""path"": ""builtin:standard_back"" }} ],
  ""objects"": [ {{ ""id"": ""d"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""cards"": [ {string.Join(",", Enumerable.Range(0, 20).Select(i => $@"{{ ""id"": ""c{i}"", ""faceIndex"": {i} }}"))} ] }} ],
  ""zones"": [ {hands} ],
  ""players"": {{ ""seats"": [ {seats} ] }},
  ""resources"": [ {{ ""id"": ""score"", ""initial"": 0 }} ],
  ""turn"": {{ ""phases"": [ ""all"", ""own"" ] }},
  ""phases"": [ {{ ""id"": ""all"", ""actions"": [ ""ready"" ], ""advanceWhen"": {{ ""allPlayersActed"": true }} }},
               {{ ""id"": ""own"", ""actions"": [ ""pass"" ], ""advanceWhen"": {{ ""currentPlayerEnds"": true }} }} ],
  ""actions"": [ {{ ""id"": ""ready"", ""actor"": ""any"", ""limitPerPhase"": 1, ""effects"": [ {{ ""setResource"": {{ ""player"": ""$actor"", ""resource"": ""score"", ""delta"": 1 }} }} ] }},
                {{ ""id"": ""pass"", ""actor"": ""current"", ""effects"": [ {{ ""endTurn"": {{}} }} ] }} ],
  ""events"": {{ ""onGameStart"": [ {{ ""deal"": {{ ""from"": ""d"", ""count"": 2, ""toZone"": ""$it.hand"" }} }} ] }}
}}";
        }

        private static GameEngine Game()
        {
            var parsed = TableJson.Parse(Json());
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            return new GameEngine(parsed.State!, 1);
        }

        [Fact]
        public void ThreeOfSixSeats_Play_AndTheEmptyOnesAreLeftOut()
        {
            var game = Game();
            var seats = SeatLayout.Spread(game.State.Players.Seats.Select(s => s.Id).ToList(), 3);
            Assert.Equal(new[] { "s1", "s3", "s5" }, seats);
            Assert.True(game.Execute(new StartGameCommand { Seats = seats }).Ok);
            Assert.Equal(new[] { "s1", "s3", "s5" }, game.Session!.Players);
            Assert.Equal(new[] { "s1", "s3", "s5" }, game.Playing.Select(s => s.Id));

            // Dealt only to the players.
            Assert.Equal(2, game.Table.ObjectsIn("h3").Count());
            Assert.Empty(game.Table.ObjectsIn("h2"));

            // "Everyone acted" means the three players; an empty seat cannot act.
            Assert.Equal(ErrorCodes.NotPlaying, game.Execute(new PerformActionCommand { Actor = "s2", ActionId = "ready" }).Error!.Code);
            foreach (var seat in seats)
                Assert.True(game.Execute(new PerformActionCommand { Actor = seat, ActionId = "ready" }).Ok);
            Assert.Equal("own", game.Session.Phase);

            // Turns skip the empty seats.
            Assert.True(game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "pass" }).Ok);
            Assert.Equal("s3", game.Session.CurrentSeat);
            Assert.Empty(game.LegalActions("s2"));
        }

        [Fact]
        public void WithoutChosenSeats_EverySeatPlays_AndTheCountIsChecked()
        {
            var game = Game();
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            Assert.Empty(game.Session!.Players);
            Assert.Equal(6, game.Playing.Count);

            var two = Game().Execute(new StartGameCommand { Seats = { "s1", "s4" } });
            Assert.Equal(ErrorCodes.PlayerCount, two.Error!.Code);
            Assert.Equal(ErrorCodes.UnknownSeat, Game().Execute(new StartGameCommand { Seats = { "s1", "s2", "x" } }).Error!.Code);
        }

        [Fact]
        public void Spread_KeepsPlayersApart()
        {
            var twelve = Enumerable.Range(1, 12).Select(n => $"seat_{n}").ToList();
            Assert.Equal(new[] { "seat_1", "seat_4", "seat_7", "seat_10" }, SeatLayout.Spread(twelve, 4));
            Assert.Equal(twelve, SeatLayout.Spread(twelve, 12));
            Assert.Equal(new[] { "seat_1" }, SeatLayout.Spread(twelve, 1));
        }
    }
}
