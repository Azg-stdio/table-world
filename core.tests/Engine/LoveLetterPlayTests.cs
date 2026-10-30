using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Core.Tests.Engine
{
    /// <summary>
    /// games/love-letter.json, built through the MCP tools by tools/mcp/build_love_letter.py, played by random players with
    /// two, three and four seats until someone has the tokens to win. Moves the rules refuse (choosing a protected player,
    /// keeping the Countess with the King) are tried and refused like a player's; after every move the sixteen cards are
    /// all somewhere, everyone still in holds one card (two on their own turn), and nobody out holds any.
    /// </summary>
    public class LoveLetterPlayTests
    {
        private readonly ITestOutputHelper _output;

        public LoveLetterPlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static (GameEngine Game, ScriptHost Scripts) Load(int players, ulong seed)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("love-letter")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            var host = new ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            var start = game.Execute(new StartGameCommand { Seats = seats });
            Assert.True(start.Ok, start.Error?.ToString());
            return (game, host);
        }

        private static string Side(string seat) => seat.Substring(seat.IndexOf('_') + 1);

        private static int Value(TableObject card) =>
            card.Tags.Select(t => t.Length == 2 && t[0] == 'v' ? t[1] - '0' : 0).Max();

        private static void CheckTable(GameEngine game, ulong seed)
        {
            var s = game.Session!;
            var loose = game.State.Objects.Count(o => o is Card && o.Tags.Any(t => t.StartsWith("v")));
            Assert.True(game.Deck("deck").Cards.Count + loose == 16, $"seed {seed}: {game.Deck("deck").Cards.Count} + {loose} cards");
            foreach (var seat in game.Playing.Select(p => p.Id))
            {
                var held = game.Table.ObjectsIn("hand_" + Side(seat)).Count();
                var expected = s.Eliminated.Contains(seat) || s.Phase == "round_end" ? 0 : seat == s.CurrentSeat && s.Phase == "play" ? 2 : 1;
                Assert.True(held == expected, $"seed {seed}: {seat} holds {held} in {s.Phase} (turn of {s.CurrentSeat}), expected {expected}: " +
                    string.Join("; ", game.Table.ObjectsIn("hand_" + Side(seat)).Select(o => $"{o.Name} at {o.Transform.Position.X:0.000},{o.Transform.Position.Z:0.000} in [{string.Join(",", game.Table.ZonesOf(o.Id))}]")));
                // The tokens before each player are the ones they have won.
                Assert.Equal(s.Resources[seat]["tokens"], game.Table.ObjectsIn("tokens_" + Side(seat)).Count());
            }
        }

        /// <summary>A card that can pick only one player picks them by itself: the game never waits on that choice.</summary>
        private static void NeverAsksTheObvious(GameEngine game, ulong seed)
        {
            var s = game.Session!;
            if (s.Over || s.Phase != "choose")
                return;
            var choices = game.LegalActions(s.CurrentSeat!).Where(a => a.ActionId == "target").SelectMany(a => a.Choices).Count();
            Assert.True(choices > 1, $"seed {seed}: {s.CurrentSeat} is asked to choose among {choices} player(s)");
        }

        private static (string Winner, int Rounds) Play(GameEngine game, ScriptHost scripts, ulong seed)
        {
            var chooser = new DeterministicRandom(seed * 7919);
            var errors = new List<string>();
            scripts.Log += line => { if (line.Contains("rror")) errors.Add(line); };
            var moves = 0;
            CheckTable(game, seed);
            while (!game.Session!.Over)
            {
                Assert.True(moves++ < 3000, $"seed {seed}: no winner after 3000 moves.");
                var s = game.Session;
                // The hands shown at the end of a round: the host's clock ends the pause.
                if (game.Phase!.Seconds != null)
                {
                    var ended = game.Execute(new EndTimedPhaseCommand { Phase = s.Phase!, Visit = s.PhaseVisit });
                    Assert.True(ended.Ok, $"seed {seed}: {ended.Error}");
                    CheckTable(game, seed);
                    continue;
                }
                var options =game.Playing.Select(p => p.Id)
                    .SelectMany(p => game.LegalActions(p).SelectMany(a => a.Choices.Select(c => new PerformActionCommand { Actor = p, ActionId = a.ActionId, Params = c })))
                    .OrderBy(_ => chooser.Next(1000)).ToList();
                Assert.True(options.Count > 0, $"seed {seed}: nobody can act in {s.Phase}.");
                var done = false;
                foreach (var option in options)
                {
                    var hand = game.Table.ObjectsIn("hand_" + Side(option.Actor!)).ToList();
                    var result = game.Execute(option);
                    if (!result.Ok)
                    {
                        Assert.True(result.Error!.Code is ErrorCodes.Refused or ErrorCodes.ScriptFailed, $"seed {seed}: {option.ActionId} failed: {result.Error}");
                        continue;
                    }
                    // The Countess never stays in a hand while the King or a Prince is played.
                    if (option.ActionId == "play_card" && game.Table.Find(option.Params["card"]) is { } played && Value(played) is 5 or 6)
                        Assert.DoesNotContain(hand, c => Value(c) == 7);
                    done = true;
                    break;
                }
                Assert.True(done, $"seed {seed}: every move refused in {s.Phase}.");
                CheckTable(game, seed);
                NeverAsksTheObvious(game, seed);
            }
            Assert.Empty(errors);
            var winner = game.Session.Winner!;
            Assert.True(game.Session.Resources[winner]["tokens"] >= (int)game.Session.Variables["toWin"]!);
            return (winner, (int)game.Session.Variables["round"]!);
        }

        [Theory]
        [InlineData(2, 7)]
        [InlineData(3, 5)]
        [InlineData(4, 4)]
        public void RandomGames_EndWithAWinner_AndNoCardLost(int players, int toWin)
        {
            var games = int.TryParse(System.Environment.GetEnvironmentVariable("TW_GAMES"), out var n) ? n : 30;
            var rounds = 0;
            for (ulong seed = 1; seed <= (ulong)games; seed++)
            {
                var (game, scripts) = Load(players, seed);
                Assert.Equal(toWin, (int)game.Session!.Variables["toWin"]!);
                rounds += Play(game, scripts, seed).Rounds;
            }
            _output.WriteLine($"{games} games of {players}: {rounds / (double)games:0.0} rounds on average");
        }

        [Fact]
        public void TwoPlayers_SetThreeCardsAsideFaceUp_AndOneFaceDown()
        {
            var (game, _) = Load(2, 1);
            Assert.Equal(3, game.Table.ObjectsIn("aside").Count(o => o.IsFaceUp));
            var burned = Assert.Single(game.Table.ObjectsIn("burn"));
            Assert.False(burned.IsFaceUp);
            (game, _) = Load(4, 1);
            Assert.Empty(game.Table.ObjectsIn("aside"));
        }
    }
}
