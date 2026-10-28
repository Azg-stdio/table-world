using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Core.Scripting;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Core.Tests.Engine
{
    /// <summary>
    /// A second sandbox check (asymmetric information): games/defuse.json, built through the MCP tools by
    /// tools/mcp/build_defuse.py. Only the Defuser sees the bomb (a hidden zone shown to a role), only the Experts read
    /// the manual (a notebook page for a role); a cut by the manual wins, three wrong cuts lose. The manual's rules are
    /// checked against a separate reading of them here, over many deals.
    /// </summary>
    public class DefusePlayTests
    {
        private readonly ITestOutputHelper _output;

        public DefusePlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameEngine Load(int players, ulong seed)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("defuse")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            var host = new ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            var start = game.Execute(new StartGameCommand { Seats = seats });
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        /// <summary>The wires left to right, by their table ids (the script keeps the cards' own ids, as getGUID gives them).</summary>
        private static List<string> Order(GameEngine game) =>
            ((JArray)game.Session!.Variables["order"]!).Select(t => game.Table.FindByOwnId((string)t!)!.Id).ToList();

        private static string Colour(GameEngine game, string id) => (string)((Card)game.Table.Find(id)!).Meta!["color"]!;

        /// <summary>The manual, read independently of the game's Lua: the wire to cut, from 1.</summary>
        private static int Manual(List<string> c)
        {
            int Count(string x) => c.Count(w => w == x);
            int Last(string x) => c.LastIndexOf(x) + 1;
            var n = c.Count;
            return n switch
            {
                3 => Count("red") == 0 ? 2 : c[n - 1] == "white" ? n : 1,
                4 => Count("red") > 1 ? Last("red") : Count("yellow") == 0 ? 3 : 1,
                5 => c[n - 1] == "black" ? 4 : Count("red") == 1 ? 1 : 2,
                _ => Count("yellow") == 0 ? 3 : Count("white") > 1 ? 6 : 5,
            };
        }

        [Fact]
        public void TheBomb_IsTheDefusersAlone_AndTheManualTheExperts()
        {
            var game = Load(4, 3);
            var defuser = game.Session!.Roles.Single(p => p.Value == "defuser").Key;
            var expert = game.Session.Roles.First(p => p.Value == "expert").Key;
            var order = Order(game);
            Assert.InRange(order.Count, 3, 6);
            Assert.All(order, id => Assert.Contains("bomb", game.Table.ZonesOf(id)));
            Assert.All(order, id => Assert.False(game.Table.IsHiddenFrom(id, defuser)));
            Assert.All(order, id => Assert.True(game.Table.IsHiddenFrom(id, expert)));

            // Over the wire the Experts hold the wires as unknown cards, and nobody holds their order.
            var view = TableJson.Parse(SeatView.Serialize(game.State, expert)).State!;
            Assert.All(order, id => Assert.True(((Card)view.Objects.Single(o => o.Id == id)).Unknown));
            Assert.False(view.Session!.Variables.ContainsKey("order"));
            var mine = TableJson.Parse(SeatView.Serialize(game.State, defuser)).State!;
            Assert.All(order, id => Assert.False(((Card)mine.Objects.Single(o => o.Id == id)).Unknown));

            var manual = game.State.Notebook.Single(p => p.Id == "manual");
            Assert.True(manual.VisibleTo(null, false, game.State, expert));
            Assert.False(manual.VisibleTo(null, false, game.State, defuser));
            Assert.True(game.State.Notebook.Single(p => p.Id == "rules").VisibleTo(null, false, game.State, defuser));
            Assert.Empty(game.LegalActions(expert));
            Assert.Equal(order.Count, game.LegalActions(defuser).Single(a => a.ActionId == "cut").Choices.Count);
        }

        [Fact]
        public void TheManualsWire_Defuses_AndThreeWrongOnesExplode()
        {
            var counts = new Dictionary<int, int>();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var game = Load(2 + (int)(seed % 5), seed);
                var defuser = game.Session!.Roles.Single(p => p.Value == "defuser").Key;
                var order = Order(game);
                counts[order.Count] = counts.TryGetValue(order.Count, out var c) ? c + 1 : 1;
                var right = order[Manual(order.Select(id => Colour(game, id)).ToList()) - 1];
                CommandResult Cut(string wire) => game.Execute(new PerformActionCommand { Actor = defuser, ActionId = "cut", Params = { ["wire"] = wire } });

                if (seed % 2 == 0)
                {
                    Assert.True(Cut(right).Ok);
                    Assert.Equal("squad", game.Session.WinnerTeam);
                    continue;
                }
                // Wrong wires: strikes, a wire cut twice is refused, and the third strike explodes the bomb.
                var wrong = order.Where(id => id != right).ToList();
                Assert.True(Cut(wrong[0]).Ok);
                Assert.Equal("That wire is already cut.", Cut(wrong[0]).Error!.Message);
                Assert.True(Cut(wrong[1]).Ok);
                Assert.Equal(2, (int)game.Session.Variables["strikes"]!);
                if (wrong.Count > 2)
                {
                    Assert.True(Cut(wrong[2]).Ok);
                    Assert.Equal("bomb", game.Session.WinnerTeam);
                    Assert.Contains("The bomb wins.", game.Session.Log);
                }
                else
                {
                    Assert.True(Cut(right).Ok);
                    Assert.Equal("squad", game.Session.WinnerTeam);
                }
            }
            _output.WriteLine("Wires per bomb over 60 deals: " + string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}")));
            Assert.Equal(new[] { 3, 4, 5, 6 }, counts.Keys.OrderBy(k => k));
        }
    }
}
