using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// T11.7–T11.8 (ADR 0012 §3–4): eyes-closed phases and voice channels, for any game. Who is awake comes from a team,
    /// a role or a list; sleepers cannot act and do not see or hear the awake; a peer only tells who is awake from what
    /// it knows; a team channel keeps each team to itself; spectators hear and see everyone.
    /// </summary>
    public class NightTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" },
                              { ""id"": ""d"", ""color"": ""yellow"" }, { ""id"": ""e"", ""color"": ""purple"" } ] },
  ""roles"": {
    ""teams"": [ { ""id"": ""village"", ""name"": ""Villagers"" }, { ""id"": ""wolves"", ""name"": ""Werewolves"" } ],
    ""list"": [ { ""id"": ""seer"", ""team"": ""village"" }, { ""id"": ""villager"", ""team"": ""village"", ""count"": ""fill"" },
                { ""id"": ""wolf"", ""team"": ""wolves"", ""count"": 2 } ],
    ""knows"": [ { ""team"": ""wolves"", ""sees"": { ""team"": ""wolves"" } } ]
  },
  ""variables"": { ""night"": 1 },
  ""turn"": { ""phases"": [ ""wolves"", ""seer"", ""day"", ""council"" ] },
  ""phases"": [
    { ""id"": ""wolves"", ""awake"": ""team:wolves"", ""sleepText"": ""Night {$var.night}. Eyes closed."", ""actions"": [ ""act"", ""next"" ] },
    { ""id"": ""seer"", ""awake"": ""role:seer"", ""actions"": [ ""act"", ""next"" ] },
    { ""id"": ""day"", ""actions"": [ ""act"", ""next"" ] },
    { ""id"": ""council"", ""voice"": ""team"", ""actions"": [ ""next"" ] }
  ],
  ""actions"": [
    { ""id"": ""act"", ""actor"": ""any"", ""effects"": [ { ""message"": { ""text"": ""{$actor.name} acted."" } } ] },
    { ""id"": ""next"", ""actor"": ""any"", ""effects"": [ { ""endPhase"": {} } ] }
  ]
}";

        private static GameEngine Start()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 6);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static string[] With(GameEngine game, string role) => game.Session!.Roles.Where(p => p.Value == role).Select(p => p.Key).ToArray();

        private static void Next(GameEngine game, string seat) =>
            Assert.True(game.Execute(new PerformActionCommand { Actor = seat, ActionId = "next" }).Ok);

        [Fact]
        public void AtNight_OnlyTheAwakeAct_SeeAndHearEachOther()
        {
            var game = Start();
            var wolves = With(game, "wolf");
            var villager = With(game, "villager")[0];
            Assert.True(game.EyesClosedPhase);
            Assert.Equal("Night 1. Eyes closed.", game.SleepText);
            Assert.True(game.IsAwake(wolves[0]));
            Assert.False(game.IsAwake(villager));

            var asleep = game.Execute(new PerformActionCommand { Actor = villager, ActionId = "act" });
            Assert.Equal(ErrorCodes.ActionNotAvailable, asleep.Error!.Code);
            Assert.Empty(game.LegalActions(villager));
            var awake = game.Execute(new PerformActionCommand { Actor = wolves[0], ActionId = "act" });
            Assert.True(awake.Ok);
            Assert.True(awake.ReadsSecrets, "who is awake depends on roles: the host runs it");

            Assert.True(game.Hears(wolves[0], wolves[1]));
            Assert.False(game.Hears(wolves[0], villager));
            Assert.False(game.Hears(villager, wolves[0]), "the sleepers are silent");
            Assert.True(game.Hears(wolves[0], null), "a spectator hears all");
            Assert.False(game.Sees(wolves[0], villager), "a sleeper does not see the wolves' pointers");
            Assert.True(game.Sees(villager, wolves[0]));
            Assert.True(game.Sees(wolves[0], null));
        }

        [Fact]
        public void APeer_TellsWhoIsAwake_OnlyFromWhatItKnows()
        {
            var game = Start();
            var wolves = With(game, "wolf");
            var seer = With(game, "seer")[0];
            var wolfView = new GameEngine(TableJson.Parse(SeatView.Serialize(game.State, wolves[0])).State!, 1);
            var seerView = new GameEngine(TableJson.Parse(SeatView.Serialize(game.State, seer)).State!, 1);
            Assert.True(wolfView.IsAwake(wolves[1], wolves[0]), "the pack knows each other");
            Assert.True(wolfView.IsAwake(wolves[0], wolves[0]));
            Assert.False(seerView.IsAwake(wolves[0], seer), "the seer cannot tell who the wolves are");
            Assert.False(seerView.IsAwake(seer, seer));
        }

        [Fact]
        public void ARolePhase_ThenDay_ThenATeamChannel()
        {
            var game = Start();
            var wolves = With(game, "wolf");
            var seer = With(game, "seer")[0];
            var villager = With(game, "villager")[0];
            Next(game, wolves[0]);
            Assert.Equal("seer", game.Session!.Phase);
            Assert.True(game.IsAwake(seer));
            Assert.False(game.IsAwake(wolves[0]));
            Assert.False(game.Hears(seer, villager), "nobody else is awake to hear the seer");
            Assert.Equal("Eyes closed.", game.SleepText);

            Next(game, seer);
            Assert.False(game.EyesClosedPhase);
            Assert.True(game.Hears(villager, wolves[0]));
            Assert.True(game.Sees(wolves[0], villager));

            Next(game, villager);
            Assert.Equal("council", game.Session.Phase);
            Assert.True(game.Hears(wolves[0], wolves[1]));
            Assert.True(game.Hears(seer, villager));
            Assert.False(game.Hears(wolves[0], seer), "each team only hears itself");
        }

        [Fact]
        public void Validation_CatchesAnUnknownAwakeTeam_AndTeamVoiceWithoutTeams()
        {
            var doc = JObject.Parse(Json);
            doc["phases"]![0]!["awake"] = "team:vampires";
            var errors = TableValidator.Validate(TableJson.Parse(doc.ToString()).State!);
            Assert.Contains(errors, e => e.Path == "phases[0].awake" && e.Code == ErrorCodes.UnknownTeam);
            doc.Remove("roles");
            doc["phases"]![0]!["awake"] = "nobody";
            doc["phases"]![1]!["awake"] = "nobody";
            errors = TableValidator.Validate(TableJson.Parse(doc.ToString()).State!);
            Assert.Contains(errors, e => e.Path == "phases[3].voice");
        }
    }
}
