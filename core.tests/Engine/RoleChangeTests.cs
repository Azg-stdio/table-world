using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// Roles beyond the deal (ADR 0012, the sandbox checks after T11.4): a conversion that tells the player and their new
    /// team, a swap no one sees, a seat changing team but not role, a private message to a team or a role, and a second
    /// hidden trait dealt beside the role. None of it is game-specific: a Werewolf-like table with objectives tries it.
    /// </summary>
    public class RoleChangeTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" },
                              { ""id"": ""d"", ""color"": ""yellow"" }, { ""id"": ""e"", ""color"": ""purple"" }, { ""id"": ""f"", ""color"": ""teal"" } ] },
  ""roles"": {
    ""teams"": [ { ""id"": ""village"", ""name"": ""Villagers"" }, { ""id"": ""wolves"", ""name"": ""Werewolves"" } ],
    ""list"": [
      { ""id"": ""seer"", ""name"": ""Seer"", ""team"": ""village"" },
      { ""id"": ""villager"", ""name"": ""Villager"", ""team"": ""village"", ""count"": ""fill"" },
      { ""id"": ""wolf"", ""name"": ""Werewolf"", ""team"": ""wolves"", ""count"": 2 },
      { ""id"": ""spy"", ""name"": ""Spy"", ""set"": ""objective"" },
      { ""id"": ""loyal"", ""name"": ""Loyal"", ""set"": ""objective"", ""count"": ""fill"" }
    ],
    ""knows"": [ { ""team"": ""wolves"", ""sees"": { ""team"": ""wolves"" } } ]
  },
  ""turn"": { ""phases"": [ ""play"" ] },
  ""phases"": [ { ""id"": ""play"", ""actions"": [ ""bite"", ""swap"", ""turn"", ""whisper"", ""spy_move"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [
    { ""id"": ""bite"", ""actor"": ""any"", ""params"": [ { ""name"": ""target"", ""type"": ""seat"" } ],
      ""effects"": [ { ""setRole"": { ""seat"": ""$param.target"", ""role"": ""wolf"", ""tell"": true } },
                     { ""revealRole"": { ""seat"": ""$param.target"", ""to"": ""team:wolves"" } } ] },
    { ""id"": ""swap"", ""actor"": ""any"", ""params"": [ { ""name"": ""x"", ""type"": ""seat"" }, { ""name"": ""y"", ""type"": ""seat"" } ],
      ""effects"": [ { ""swapRoles"": { ""a"": ""$param.x"", ""b"": ""$param.y"" } } ] },
    { ""id"": ""turn"", ""actor"": ""any"", ""effects"": [ { ""setTeam"": { ""seat"": ""$actor"", ""team"": ""wolves"", ""tell"": true } } ] },
    { ""id"": ""whisper"", ""actor"": ""any"", ""effects"": [ { ""message"": { ""text"": ""The moon is full."", ""to"": ""role:seer"" } } ] },
    { ""id"": ""spy_move"", ""actor"": ""any"",
      ""preconditions"": [ { ""compare"": { ""left"": ""$actor.role.objective"", ""right"": ""spy"" } } ],
      ""effects"": [ { ""message"": { ""text"": ""A spy moved."" } } ] }
  ]
}";

        private static GameEngine Start(ulong seed = 3)
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, seed);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static string With(GameEngine game, string role) => game.Session!.Roles.First(p => p.Value == role).Key;

        private static CommandResult Do(GameEngine game, string actor, string action, params (string Key, string Value)[] args)
        {
            var command = new PerformActionCommand { Actor = actor, ActionId = action };
            foreach (var (key, value) in args)
                command.Params[key] = value;
            var result = game.Execute(command);
            Assert.True(result.ReadsSecrets, $"{action} touches roles: the host runs it");
            return result;
        }

        [Fact]
        public void ASecondTrait_IsDealtBesideTheRole_AndReadByRules()
        {
            var game = Start();
            var s = game.Session!;
            Assert.Equal(6, s.Traits.Count);
            Assert.Single(s.Traits.Values, t => t["objective"] == "spy");
            var spy = s.Traits.First(p => p.Value["objective"] == "spy").Key;
            Assert.Contains("Objective: Spy.", GameEngine.RoleLines(game.State, spy));
            Assert.Equal(2, s.Roles.Values.Count(r => r == "wolf"));

            var loyal = s.Traits.First(p => p.Value["objective"] == "loyal").Key;
            Assert.Equal(ErrorCodes.PreconditionFailed, game.Execute(new PerformActionCommand { Actor = loyal, ActionId = "spy_move" }).Error!.Code);
            Assert.True(game.Execute(new PerformActionCommand { Actor = spy, ActionId = "spy_move" }).Ok);

            // A seat's traits reach only that seat.
            var view = TableJson.Parse(SeatView.Serialize(game.State, loyal)).State!.Session!;
            Assert.Equal(new[] { loyal }, view.Traits.Keys);
        }

        [Fact]
        public void AConversion_TellsThePlayer_AndTheirNewTeam()
        {
            var game = Start();
            var villager = With(game, "villager");
            var wolves = game.Session!.Roles.Where(p => p.Value == "wolf").Select(p => p.Key).ToList();
            var result = Do(game, wolves[0], "bite", ("target", villager));

            Assert.Equal("wolf", game.RoleOf(villager));
            Assert.Equal("wolves", game.TeamOf(villager));
            var told = result.Events.OfType<PrivateMessage>().ToList();
            Assert.Contains(told, m => m.Seat == villager && m.Text == "You are now Werewolf (Werewolves).");
            // The pack learns it, privately; the village does not.
            foreach (var wolf in wolves)
            {
                Assert.Contains(told, m => m.Seat == wolf && m.Text.EndsWith("is Werewolf."));
                Assert.Contains(game.Session.Knows[wolf], k => k.Seat == villager && k.Role == "wolf");
            }
            Assert.DoesNotContain(told, m => m.Seat == With(game, "seer"));
            Assert.DoesNotContain(game.Session.Log, l => l.Contains("Werewolf"));
        }

        [Fact]
        public void ASwap_MovesRolesAndTeams_Silently()
        {
            var game = Start();
            var seer = With(game, "seer");
            var wolf = With(game, "wolf");
            var result = Do(game, seer, "swap", ("x", seer), ("y", wolf));
            Assert.Equal("wolf", game.RoleOf(seer));
            Assert.Equal("seer", game.RoleOf(wolf));
            Assert.Equal("wolves", game.TeamOf(seer));
            Assert.Empty(result.Events.OfType<PrivateMessage>());
            // What the pack saw at the deal stays what it saw.
            var other = game.Session!.Roles.First(p => p.Value == "wolf" && p.Key != seer).Key;
            Assert.Contains(game.Session.Knows[other], k => k.Seat == wolf && k.Team == "wolves");
        }

        [Fact]
        public void ATeamChange_KeepsTheRole_AndRulesReadTheNewTeam()
        {
            var game = Start();
            var villager = With(game, "villager");
            var result = Do(game, villager, "turn");
            Assert.Equal("villager", game.RoleOf(villager));
            Assert.Equal("wolves", game.TeamOf(villager));
            Assert.Contains(result.Events.OfType<PrivateMessage>(), m => m.Seat == villager && m.Text == "You are now Werewolves.");
            Assert.Equal("You are Villager (Werewolves).", GameEngine.RoleLines(game.State, villager)[0]);
            var view = TableJson.Parse(SeatView.Serialize(game.State, With(game, "seer"))).State!.Session!;
            Assert.Empty(view.Teams);
        }

        [Fact]
        public void AMessageToARole_ReachesOnlyThatRole()
        {
            var game = Start();
            var seer = With(game, "seer");
            var result = Do(game, With(game, "villager"), "whisper");
            var told = result.Events.OfType<PrivateMessage>().ToList();
            Assert.Equal(new[] { seer }, told.Select(m => m.Seat));
            Assert.Equal("The moon is full.", told[0].Text);
            Assert.DoesNotContain("The moon is full.", game.Session!.Log);
            Assert.Empty(result.Events.OfType<GameMessage>());
        }

        [Fact]
        public void Validation_CatchesUnknownSetsTeamsAndRoles()
        {
            var doc = JObject.Parse(Json);
            var effects = (JArray)doc["actions"]![0]!["effects"]!;
            effects.Add(JObject.Parse(@"{ ""setRole"": { ""seat"": ""$actor"", ""role"": ""vampire"" } }"));
            effects.Add(JObject.Parse(@"{ ""revealRole"": { ""seat"": ""$actor"", ""to"": ""team:vampires"" } }"));
            effects.Add(JObject.Parse(@"{ ""revealRole"": { ""seat"": ""$actor"", ""set"": ""secret_goal"" } }"));
            effects.Add(JObject.Parse(@"{ ""setTeam"": { ""seat"": ""$actor"", ""team"": ""vampires"" } }"));
            var errors = TableValidator.Validate(TableJson.Parse(doc.ToString()).State!);
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownRole && e.Path.EndsWith("setRole.role"));
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownTeam && e.Path.EndsWith("revealRole.to"));
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownRole && e.Path.EndsWith("revealRole.set"));
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownTeam && e.Path.EndsWith("setTeam.team"));
        }
    }
}
