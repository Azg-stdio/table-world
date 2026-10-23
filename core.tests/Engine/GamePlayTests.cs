using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Xunit.Abstractions;

namespace Core.Tests.Engine
{
    /// <summary>
    /// T4.3 ⚗: War and simplified UNO played to the end by random players, headless. Every game must terminate, the
    /// 52 cards must be conserved after every action, and the table must stay valid.
    /// </summary>
    public class GamePlayTests
    {
        private readonly ITestOutputHelper _output;

        public GamePlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public const int MaxActions = 20000;

        /// <summary>200 games per run in CI; set TW_GAMES=1000 for the recorded simulation.</summary>
        [Theory]
        [InlineData("war")]
        [InlineData("uno-lite")]
        public void RandomPlayers_FinishEveryGame_ConservingCards(string name)
        {
            var games = int.TryParse(System.Environment.GetEnvironmentVariable("TW_GAMES"), out var n) ? n : 200;
            var actions = new List<int>();
            var wins = new Dictionary<string, int>();
            var capped = 0;
            for (var g = 0; g < games; g++)
            {
                var (winner, count) = PlayOne(name, (ulong)(g + 1), out var battles);
                actions.Add(count);
                wins[winner] = wins.TryGetValue(winner, out var w) ? w + 1 : 1;
                if (battles >= 500)
                    capped++;
            }
            actions.Sort();
            _output.WriteLine($"{name}: {games} games, actions per game min {actions[0]} median {actions[actions.Count / 2]} " +
                              $"p99 {actions[actions.Count * 99 / 100]} max {actions[^1]}; wins {string.Join(", ", wins.Select(p => $"{p.Key} {p.Value}"))}; " +
                              $"ended by the 500-battle rule {capped}");
        }

        [Fact]
        public void War_SameSeedSamePlayers_SameGame()
        {
            Assert.Equal(PlayOne("war", 42, out _), PlayOne("war", 42, out _));
        }

        private static (string Winner, int Actions) PlayOne(string name, ulong seed, out int battles)
        {
            var game = GameFixtures.Load(name, seed);
            var start = game.Execute(new StartGameCommand());
            Assert.True(start.Ok, start.Error?.ToString());
            AssertCards(game, "after start");

            var chooser = new DeterministicRandom(seed * 7919);
            var count = 0;
            while (!game.Session!.Over)
            {
                Assert.True(count < MaxActions, $"{name} seed {seed}: no winner after {MaxActions} actions (turn {game.Session.Turn}).");
                // Every seat that can act is a candidate (War lets both play in the same phase).
                var options = game.State.Players.Seats
                    .SelectMany(s => game.LegalActions(s.Id).SelectMany(a => a.Choices.Select(c => (Seat: s.Id, a.ActionId, Params: c))))
                    .ToList();
                Assert.True(options.Count > 0, $"{name} seed {seed}: nobody can act in phase {game.Session.Phase}, turn {game.Session.Turn}.");
                var pick = options[chooser.Next(options.Count)];
                var result = game.Execute(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
                Assert.True(result.Ok, $"{name} seed {seed}: legal action {pick.ActionId} by {pick.Seat} failed: {result.Error}");
                count++;
                AssertCards(game, $"action {count}");
            }
            Assert.Empty(TableValidator.Validate(game.State));
            battles = game.Session.Variables.TryGetValue("battles", out var b) ? (int)b : 0;
            return (game.Session.Winner!, count);
        }

        private static void AssertCards(GameEngine game, string when)
        {
            var cards = game.State.Objects.Sum(o => o switch { Deck d => d.Cards.Count, Card _ => 1, _ => 0 });
            Assert.True(cards == 52, $"{cards} cards {when}.");
        }
    }
}
