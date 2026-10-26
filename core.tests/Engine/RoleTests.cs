using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// T11.4 (ADR 0012 §2): hidden roles. Avalon's roles deal for 5 to 10 players with the right team sizes; Merlin,
    /// Percival, the minions, Mordred and Oberon know what the rules give them; rules read roles; revealRole shows one;
    /// and over the protocol each seat receives its own role and what it knows, nothing more.
    /// </summary>
    public class RoleTests
    {
        /// <summary>Avalon's roles, ten seats, one action only the assassin may take, one that shows a role to the actor.</summary>
        internal static string Avalon(int seats = 10) => @"{
  ""players"": { ""seats"": [ " + string.Join(", ", Enumerable.Range(1, seats).Select(i => $@"{{ ""id"": ""s{i}"", ""color"": ""{Colors[i - 1]}"" }}")) + @" ] },
  ""game"": { ""id"": ""avalon"", ""name"": ""Avalon"", ""minPlayers"": 5, ""maxPlayers"": 10 },
  ""roles"": {
    ""teams"": [ { ""id"": ""good"", ""name"": ""Loyal servants of Arthur"" }, { ""id"": ""evil"", ""name"": ""Minions of Mordred"" } ],
    ""list"": [
      { ""id"": ""merlin"", ""name"": ""Merlin"", ""team"": ""good"" },
      { ""id"": ""percival"", ""name"": ""Percival"", ""team"": ""good"", ""optional"": true },
      { ""id"": ""servant"", ""name"": ""Loyal servant"", ""team"": ""good"", ""count"": ""fill"" },
      { ""id"": ""assassin"", ""name"": ""Assassin"", ""team"": ""evil"" },
      { ""id"": ""morgana"", ""name"": ""Morgana"", ""team"": ""evil"", ""optional"": true },
      { ""id"": ""mordred"", ""name"": ""Mordred"", ""team"": ""evil"", ""optional"": true },
      { ""id"": ""oberon"", ""name"": ""Oberon"", ""team"": ""evil"", ""optional"": true },
      { ""id"": ""minion"", ""name"": ""Minion of Mordred"", ""team"": ""evil"", ""count"": ""fill"" }
    ],
    ""byPlayers"": {
      ""5"": { ""good"": 3, ""evil"": 2 }, ""6"": { ""good"": 4, ""evil"": 2 }, ""7"": { ""good"": 4, ""evil"": 3 },
      ""8"": { ""good"": 5, ""evil"": 3 }, ""9"": { ""good"": 6, ""evil"": 3 }, ""10"": { ""good"": 6, ""evil"": 4 }
    },
    ""knows"": [
      { ""role"": ""merlin"", ""sees"": { ""team"": ""evil"", ""except"": [ ""mordred"" ] } },
      { ""team"": ""evil"", ""sees"": { ""team"": ""evil"", ""except"": [ ""oberon"" ] } },
      { ""role"": ""oberon"", ""sees"": {} },
      { ""role"": ""percival"", ""sees"": { ""roles"": [ ""merlin"", ""morgana"" ] }, ""as"": ""oneOf"" }
    ]
  },
  ""turn"": { ""phases"": [ ""play"" ] },
  ""phases"": [ { ""id"": ""play"", ""actions"": [ ""assassinate"", ""peek"", ""pass"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [
    { ""id"": ""assassinate"", ""actor"": ""any"", ""params"": [ { ""name"": ""target"", ""type"": ""seat"" } ],
      ""preconditions"": [ { ""compare"": { ""left"": ""$actor.role"", ""right"": ""assassin"" } } ],
      ""effects"": [ { ""revealRole"": { ""seat"": ""$param.target"" } } ] },
    { ""id"": ""peek"", ""actor"": ""any"", ""params"": [ { ""name"": ""target"", ""type"": ""seat"" } ],
      ""effects"": [ { ""revealRole"": { ""seat"": ""$param.target"", ""to"": ""$actor"", ""teamOnly"": true } } ] },
    { ""id"": ""pass"", ""actor"": ""current"", ""effects"": [ { ""endTurn"": {} } ] }
  ]
}";

        private static readonly string[] Colors = { "white", "brown", "red", "orange", "yellow", "green", "teal", "blue", "purple", "pink" };

        private static GameEngine Start(int players, ulong seed, params string[] optional)
        {
            var parsed = TableJson.Parse(Avalon(players));
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, seed);
            var start = game.Execute(new StartGameCommand { Roles = optional.ToList() });
            Assert.True(start.Ok, start.Error?.ToString());
            Assert.True(start.ReadsSecrets, "a deal is for the host to run");
            return game;
        }

        private static string SeatOf(GameEngine game, string role) => game.Session!.Roles.First(p => p.Value == role).Key;

        [Theory]
        [InlineData(5, 3, 2)]
        [InlineData(6, 4, 2)]
        [InlineData(7, 4, 3)]
        [InlineData(8, 5, 3)]
        [InlineData(9, 6, 3)]
        [InlineData(10, 6, 4)]
        public void EveryCount_DealsItsTeamSizes(int players, int good, int evil)
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var game = Start(players, seed, "percival", "morgana");
                var roles = game.Session!.Roles;
                Assert.Equal(players, roles.Count);
                var team = roles.Values.Select(r => game.State.Roles!.List.First(d => d.Id == r).Team).ToList();
                Assert.Equal(good, team.Count(t => t == "good"));
                Assert.Equal(evil, team.Count(t => t == "evil"));
                foreach (var single in new[] { "merlin", "percival", "assassin", "morgana" })
                    Assert.Single(roles.Values, r => r == single);
                Assert.DoesNotContain("mordred", roles.Values);
            }
            // Not always the same seats.
            Assert.NotEqual(SeatOf(Start(players, 1), "merlin") + SeatOf(Start(players, 2), "merlin") + SeatOf(Start(players, 3), "merlin"),
                string.Concat(Enumerable.Repeat(SeatOf(Start(players, 1), "merlin"), 3)));
        }

        [Fact]
        public void TooManyOptionalRoles_AreRefusedWithTheReason()
        {
            var game = new GameEngine(TableJson.Parse(Avalon(5)).State!, 1);
            var start = game.Execute(new StartGameCommand { Roles = { "morgana", "mordred" } });
            Assert.Equal(ErrorCodes.RoleCount, start.Error!.Code);
            Assert.Contains("Minions of Mordred has 2 seats", start.Error.Message);
            Assert.Null(game.Session);
            var unknown = game.Execute(new StartGameCommand { Roles = { "lancelot" } });
            Assert.Equal(ErrorCodes.UnknownRole, unknown.Error!.Code);
        }

        [Fact]
        public void EachRole_KnowsWhatItsRulesGiveIt()
        {
            var game = Start(10, 4, "percival", "morgana", "mordred", "oberon");
            var s = game.Session!;
            string Of(string role) => SeatOf(game, role);
            List<RoleSight> Knows(string role) => s.Knows[Of(role)];

            // Ten players with all four optional minions: the evil team is exactly them.
            Assert.DoesNotContain("minion", s.Roles.Values);
            // Merlin sees the evil team, but not Mordred.
            var merlin = Knows("merlin").Select(k => k.Seat).ToHashSet();
            Assert.Equal(new[] { Of("assassin"), Of("morgana"), Of("oberon") }.ToHashSet(), merlin);
            Assert.All(Knows("merlin"), k => Assert.Equal("evil", k.Team));
            // The minions see each other, but not Oberon; Oberon sees nobody.
            Assert.Equal(new[] { Of("morgana"), Of("mordred") }.ToHashSet(), Knows("assassin").Select(k => k.Seat).ToHashSet());
            Assert.Empty(Knows("oberon"));
            // Percival sees Merlin and Morgana, not which is which.
            Assert.Equal(new[] { Of("merlin"), Of("morgana") }.ToHashSet(), Knows("percival").Select(k => k.Seat).ToHashSet());
            Assert.All(Knows("percival"), k => Assert.Equal(new[] { "merlin", "morgana" }, k.OneOf));
            Assert.Empty(Knows("servant"));
        }

        [Fact]
        public void TheDeal_TellsEachSeatAlone()
        {
            var parsed = TableJson.Parse(Avalon(5)).State!;
            var game = new GameEngine(parsed, 2);
            var start = game.Execute(new StartGameCommand());
            var told = start.Events.OfType<PrivateMessage>().ToList();
            var merlin = SeatOf(game, "merlin");
            Assert.Contains(told, m => m.Seat == merlin && m.Text == "You are Merlin (Loyal servants of Arthur).");
            Assert.Contains(told, m => m.Seat == merlin && m.Text.EndsWith(" are Minions of Mordred."));
            Assert.Equal(5, told.Count(m => m.Text.StartsWith("You are")));
            Assert.DoesNotContain(game.Session!.Log, l => l.Contains("You are") || l.Contains("Mordred"));
            // The game log shows a seat its own lines only.
            var lines = ActionLog.Describe(game.Table, new StartGameCommand(), start, null, merlin).Select(l => l.Text).ToList();
            Assert.Contains("You are Merlin (Loyal servants of Arthur).", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("You are") && l != "You are Merlin (Loyal servants of Arthur).");
        }

        [Fact]
        public void Rules_ReadRoles_AndRevealOne()
        {
            var game = Start(5, 3);
            var assassin = SeatOf(game, "assassin");
            var merlin = SeatOf(game, "merlin");
            var servant = SeatOf(game, "servant");
            var refused = game.Execute(new PerformActionCommand { Actor = servant, ActionId = "assassinate", Params = { ["target"] = merlin } });
            Assert.Equal(ErrorCodes.PreconditionFailed, refused.Error!.Code);

            // Peeking shows only the team, to the one who looked.
            var peek = game.Execute(new PerformActionCommand { Actor = servant, ActionId = "peek", Params = { ["target"] = assassin } });
            Assert.True(peek.ReadsSecrets);
            Assert.Contains(game.Session!.Knows[servant], k => k.Seat == assassin && k.Team == "evil" && k.Role == null);
            Assert.Contains(peek.Events.OfType<PrivateMessage>(), m => m.Seat == servant && m.Text.EndsWith("is Minions of Mordred."));
            Assert.DoesNotContain(game.Session.Knows[merlin], k => k.Seat == merlin);

            // The assassin's strike shows Merlin's role to everyone, in the shared log.
            var strike = game.Execute(new PerformActionCommand { Actor = assassin, ActionId = "assassinate", Params = { ["target"] = merlin } });
            Assert.True(strike.Ok, strike.Error?.ToString());
            Assert.Contains(game.Session.Log, l => l.EndsWith("is Merlin."));
            foreach (var seat in game.Session.Roles.Keys.Where(s => s != merlin))
                Assert.Contains(game.Session.Knows[seat], k => k.Seat == merlin && k.Role == "merlin");
        }

        [Fact]
        public void Lua_ReadsRoles_AndRevealsOne()
        {
            var doc = JObject.Parse(Avalon(5));
            ((JArray)doc["actions"]!).Add(JObject.Parse(@"{ ""id"": ""confess"", ""actor"": ""any"", ""effects"": [ { ""lua"": { ""call"": ""confess"" } } ] }"));
            ((JArray)doc["phases"]![0]!["actions"]!).Add("confess");
            doc["scripts"] = new JObject { ["global"] = @"
function confess(ctx)
  if Game.getTeam(ctx.actor) ~= 'evil' then return 'Only a minion confesses.' end
  Game.revealRole(ctx.actor)
  Game.setVariable('last', Game.getRole(ctx.actor))
end" };
            doc["variables"] = new JObject { ["last"] = "" };
            var state = TableJson.Parse(doc.ToString()).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, 5);
            var host = new Core.Scripting.ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            Assert.True(game.Execute(new StartGameCommand()).Ok);

            var servant = SeatOf(game, "servant");
            var refused = game.Execute(new PerformActionCommand { Actor = servant, ActionId = "confess" });
            Assert.Equal("Only a minion confesses.", refused.Error!.Message);
            var assassin = SeatOf(game, "assassin");
            Assert.True(game.Execute(new PerformActionCommand { Actor = assassin, ActionId = "confess" }).Ok);
            Assert.Equal("assassin", (string)game.Session!.Variables["last"]!);
            Assert.Contains(game.Session.Knows[servant], k => k.Seat == assassin && k.Role == "assassin");
        }

        [Fact]
        public void Validation_CatchesRolesThatCannotDeal()
        {
            var bad = JObject.Parse(Avalon(5));
            var roles = (JObject)bad["roles"]!;
            ((JObject)roles["byPlayers"]!)["5"] = JObject.Parse(@"{ ""good"": 0, ""evil"": 5 }");
            ((JArray)roles["list"]!).Add(JObject.Parse(@"{ ""id"": ""galahad"", ""team"": ""knights"" }"));
            ((JArray)roles["knows"]!).Add(JObject.Parse(@"{ ""role"": ""lancelot"", ""sees"": { ""team"": ""evil"" } }"));
            var errors = TableValidator.Validate(TableJson.Parse(bad.ToString()).State!);
            Assert.Contains(errors, e => e.Code == ErrorCodes.RoleCount && e.Path == "roles.byPlayers.5");
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownTeam && e.Path == "roles.list[8].team");
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownRole && e.Path == "roles.knows[4].role");

            var twoFills = JObject.Parse(Avalon(5));
            ((JObject)twoFills["roles"]!).Remove("byPlayers");
            Assert.Contains(TableValidator.Validate(TableJson.Parse(twoFills.ToString()).State!), e => e.Code == ErrorCodes.RoleCount && e.Path == "roles.list");
        }

        [Fact]
        public void OverTheProtocol_EachSeatGetsItsOwnRole_AndNothingMore()
        {
            var game = new GameEngine(TableJson.Parse(Avalon(5)).State!, 9);
            var host = new SessionHost(game);
            var seats = game.State.Players.Seats.Select(s => s.Id).ToList();
            var clients = seats.ToDictionary(s => s, s => new SessionClient(s));
            var received = seats.ToDictionary(s => s, s => new List<string>());
            var lines = seats.ToDictionary(s => s, s => new List<string>());
            void Deliver(Outgoing outgoing)
            {
                foreach (var id in outgoing.To == null ? seats : new List<string> { outgoing.To })
                {
                    received[id].Add(outgoing.Message.ToString(Newtonsoft.Json.Formatting.None));
                    Assert.Null(clients[id].Receive((JObject)outgoing.Message.DeepClone()));
                }
            }
            foreach (var seat in seats)
            {
                host.Seats[seat] = seat;
                var id = seat;
                clients[seat].Ordered += (_, command, result) =>
                    lines[id].AddRange(ActionLog.Describe(clients[id].Confirmed!.Table, command, result, null, id).Select(l => l.Text));
                Deliver(host.Snapshot(seat));
            }
            host.ExecuteLocal(new StartGameCommand()).Messages.ForEach(Deliver);

            foreach (var seat in seats)
            {
                var mine = game.Session!.Roles[seat];
                var view = clients[seat].Confirmed!.Session!;
                Assert.Equal(new Dictionary<string, string> { [seat] = mine }, view.Roles);
                Assert.Equal(game.Session.Knows[seat].Select(k => k.Seat), view.Knows[seat].Select(k => k.Seat));
                Assert.Single(view.Knows);
                // No message named another seat's role, nor told it to someone else.
                foreach (var other in seats.Where(o => o != seat))
                {
                    var theirs = game.Session.Roles[other];
                    var known = game.Session.Knows[seat].FirstOrDefault(k => k.Seat == other);
                    if (known?.Role == null)
                        Assert.DoesNotContain(received[seat], m => m.Contains($"\"{other}\":\"{theirs}\"") || m.Contains($"\"seat\":\"{other}\",\"role\":\"{theirs}\""));
                }
                Assert.Single(lines[seat], l => l.StartsWith("You are"));
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(clients[seat].Confirmed!.State));
            }

            // An action whose condition reads the actor's role is host-run: every peer gets its result, nobody resyncs.
            var assassin = SeatOf(game, "assassin");
            var merlin = SeatOf(game, "merlin");
            foreach (var outgoing in host.Receive(assassin, clients[assassin].Submit(new PerformActionCommand
                     { Actor = assassin, ActionId = "assassinate", Params = { ["target"] = merlin } }).Intent!))
                Deliver(outgoing);
            foreach (var seat in seats)
            {
                Assert.Equal(0, clients[seat].Resyncs);
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(clients[seat].Confirmed!.State));
                if (seat != merlin)
                    Assert.Contains(clients[seat].Confirmed!.Session!.Knows[seat], k => k.Seat == merlin && k.Role == "merlin");
            }
        }
    }
}
