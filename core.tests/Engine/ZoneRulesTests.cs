using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>Rules react to pieces moved by hand: zone effects, refusals, and blind rounds that hold them until the reveal.</summary>
    public class ZoneRulesTests
    {
        private const string Json = @"{
  ""objects"": [
    { ""id"": ""pawn_a"", ""type"": ""Token"", ""transform"": { ""position"": [-0.5, 0, -0.3] } },
    { ""id"": ""pawn_b"", ""type"": ""Token"", ""transform"": { ""position"": [0.5, 0, -0.3] } }
  ],
  ""zones"": [
    { ""id"": ""shop"", ""type"": ""ScriptingZone"", ""transform"": { ""position"": [0, 0.05, 0.2] }, ""size"": [0.2, 0.1, 0.2],
      ""onEnter"": [ { ""setResource"": { ""player"": ""$actor"", ""resource"": ""visits"", ""delta"": 1 } },
                     { ""message"": { ""text"": ""{$actor} goes shopping"" } } ],
      ""onLeave"": [ { ""setResource"": { ""player"": ""$actor"", ""resource"": ""visits"", ""delta"": -1 } } ] },
    { ""id"": ""dungeon"", ""type"": ""ScriptingZone"", ""transform"": { ""position"": [0.4, 0.05, 0.2] }, ""size"": [0.2, 0.1, 0.2],
      ""onEnter"": [ { ""if"": { ""condition"": { ""variable"": { ""name"": ""blocked"", ""op"": ""=="", ""value"": ""dungeon"" } },
                                ""then"": [ { ""refuse"": { ""reason"": ""The roulette blocked the dungeon."" } } ] } } ] }
  ],
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""resources"": [ { ""id"": ""visits"", ""initial"": 0 } ],
  ""variables"": { ""blocked"": ""dungeon"" },
  ""turn"": { ""phases"": [ ""move"" ] },
  ""phases"": [ { ""id"": ""move"", ""actions"": [ ""done"" ], ""advanceWhen"": { ""currentPlayerEnds"": true },
                  ""onEnter"": [ { ""blindRound"": { ""on"": true } } ] } ],
  ""actions"": [ { ""id"": ""done"", ""actor"": ""current"", ""effects"": [ { ""blindRound"": { ""on"": false } }, { ""endTurn"": {} } ] } ],
  ""events"": { ""onReveal"": [ { ""message"": { ""text"": ""Revealed!"" } } ] }
}";

        private static GameEngine Start(bool blind)
        {
            var json = blind ? Json : Json.Replace(@"""onEnter"": [ { ""blindRound"": { ""on"": true } } ] ", @"""onEnter"": [] ");
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static CommandResult Move(GameEngine game, string seat, string pawn, float x, float z) =>
            game.Execute(new MoveCommand { Actor = seat, ObjectId = pawn, Position = new Vec3(x, 0, z), Rotation = Vec3.Zero });

        [Fact]
        public void MovingAPawnIntoAZone_RunsItsEffects_AndOutRunsTheLeaveEffects()
        {
            var game = Start(blind: false);
            Assert.True(Move(game, "a", "pawn_a", 0, 0.2f).Ok);
            Assert.Equal(1, game.Session!.Resources["a"]["visits"]);
            Assert.Contains("Red goes shopping", game.Session.Log);
            Assert.True(Move(game, "a", "pawn_a", -0.5f, -0.3f).Ok);
            Assert.Equal(0, game.Session.Resources["a"]["visits"]);
        }

        [Fact]
        public void ARefusedMove_IsUndone_WithTheReason()
        {
            var game = Start(blind: false);
            var result = Move(game, "b", "pawn_b", 0.4f, 0.2f);
            Assert.False(result.Ok);
            Assert.Equal(ErrorCodes.Refused, result.Error!.Code);
            Assert.Equal("The roulette blocked the dungeon.", result.Error.Message);
            Assert.Equal(new Vec3(0.5f, 0, -0.3f), game.Table.Find("pawn_b")!.Transform.Position);
        }

        [Fact]
        public void InABlindRound_ZoneEffectsWait_ThenRunAtTheReveal_InTurnOrder()
        {
            var game = Start(blind: true);
            Assert.NotNull(game.State.Blind);
            Assert.True(Move(game, "b", "pawn_b", 0.05f, 0.2f).Ok);
            Assert.True(Move(game, "a", "pawn_a", 0, 0.25f).Ok);
            // Hidden from the other seat, and nothing has happened yet.
            Assert.Equal(new[] { "red" }, game.Table.Find("pawn_b")!.InvisibleTo);
            Assert.Equal(0, game.Session!.Resources["a"]["visits"]);
            Assert.Equal(2, game.Session.HeldZoneMoves.Count);

            Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "done" }).Ok);
            Assert.Equal(1, game.Session.Resources["a"]["visits"]);
            Assert.Equal(1, game.Session.Resources["b"]["visits"]);
            var log = game.Session.Log;
            Assert.True(log.IndexOf("Red goes shopping") < log.IndexOf("Blue goes shopping"), string.Join(" | ", log));
            Assert.Equal("Revealed!", log.Last(l => l == "Revealed!"));
            Assert.Empty(game.Table.Find("pawn_b")!.InvisibleTo);
            // The next turn opens a new blind round.
            Assert.NotNull(game.State.Blind);
        }

        [Fact]
        public void ARefusalHeldByABlindRound_FailsTheReveal_AndTheGameStaysAsItWas()
        {
            var game = Start(blind: true);
            Assert.True(Move(game, "b", "pawn_b", 0.4f, 0.2f).Ok);
            var reveal = game.Execute(new PerformActionCommand { Actor = "a", ActionId = "done" });
            Assert.Equal(ErrorCodes.Refused, reveal.Error!.Code);
            Assert.NotNull(game.State.Blind);
            Assert.Single(game.Session!.HeldZoneMoves);
        }
    }
}
