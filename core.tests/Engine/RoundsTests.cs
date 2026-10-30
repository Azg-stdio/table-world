using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// Games played in rounds (Love Letter): players knocked out of a round come back for the next (<c>revive</c>), the
    /// round's winner starts the next one (<c>turnTo</c>), and one player may look at another's hand (<c>show</c> with
    /// <c>to</c>): the card reaches that player's machine alone and the log does not name it.
    /// </summary>
    public class RoundsTests
    {
        private const string Json = @"{
  ""assets"": [ { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 },
                { ""id"": ""back"", ""type"": ""image"", ""path"": ""builtin:standard_back"" } ],
  ""objects"": [
    { ""id"": ""d"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""transform"": { ""rotation"": [0, 0, 180] }, ""cards"": [
      { ""id"": ""c0"", ""name"": ""Baron"" }, { ""id"": ""c1"", ""name"": ""Priest"" }, { ""id"": ""c2"", ""name"": ""Guard"" } ] } ],
  ""zones"": [
    { ""id"": ""h1"", ""type"": ""HandZone"", ""owner"": ""s1"", ""transform"": { ""position"": [0, 0.05, -0.45] }, ""size"": [0.5, 0.1, 0.1] },
    { ""id"": ""h2"", ""type"": ""HandZone"", ""owner"": ""s2"", ""transform"": { ""position"": [0.45, 0.05, 0] }, ""size"": [0.5, 0.1, 0.1] },
    { ""id"": ""h3"", ""type"": ""HandZone"", ""owner"": ""s3"", ""transform"": { ""position"": [0, 0.05, 0.45] }, ""size"": [0.5, 0.1, 0.1] } ],
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"", ""handZone"": ""h1"" }, { ""id"": ""s2"", ""color"": ""blue"", ""handZone"": ""h2"" },
                             { ""id"": ""s3"", ""color"": ""green"", ""handZone"": ""h3"" } ] },
  ""variables"": { ""starts"": 0, ""out"": false, ""picked"": """" },
  ""turn"": { ""phases"": [ ""main"", ""aim"" ] },
  ""phases"": [ { ""id"": ""main"", ""actions"": [ ""pass"", ""knock"", ""bringBack"", ""toThird"", ""toKnocked"", ""peek"", ""lua"", ""aimAt"" ] },
              { ""id"": ""aim"", ""actions"": [ ""pick"" ] } ],
  ""actions"": [
    { ""id"": ""pass"", ""actor"": ""current"", ""effects"": [ { ""endTurn"": {} } ] },
    { ""id"": ""aimAt"", ""actor"": ""current"", ""effects"": [ { ""gotoPhase"": { ""phase"": ""aim"" } } ] },
    { ""id"": ""pick"", ""actor"": ""current"", ""auto"": true, ""onPlayer"": true, ""params"": [ { ""name"": ""target"", ""type"": ""seat"" } ],
      ""preconditions"": [ { ""check"": { ""condition"": { ""compare"": { ""left"": ""$param.target.id"", ""op"": ""!="", ""right"": ""$actor.id"" } },
                                         ""reason"": ""{$actor.name}: choose another player."" } } ],
      ""effects"": [ { ""setVariable"": { ""name"": ""picked"", ""value"": ""$param.target.id"" } }, { ""endTurn"": {} } ] },
    { ""id"": ""knock"", ""actor"": ""current"", ""effects"": [ { ""eliminate"": { ""seat"": ""s2"" } } ] },
    { ""id"": ""bringBack"", ""actor"": ""current"", ""effects"": [ { ""forEach"": { ""players"": true, ""do"": [ { ""revive"": { ""seat"": ""$it"" } } ] } } ] },
    { ""id"": ""toThird"", ""actor"": ""current"", ""effects"": [ { ""turnTo"": { ""seat"": ""s3"" } } ] },
    { ""id"": ""toKnocked"", ""actor"": ""current"", ""effects"": [ { ""eliminate"": { ""seat"": ""s2"" } }, { ""turnTo"": { ""seat"": ""s2"" } } ] },
    { ""id"": ""peek"", ""actor"": ""current"", ""params"": [ { ""name"": ""target"", ""type"": ""seat"" } ],
      ""effects"": [ { ""show"": { ""object"": ""$param.target.hand.top"", ""to"": ""$actor"" } } ] },
    { ""id"": ""lua"", ""actor"": ""current"", ""params"": [ { ""name"": ""what"", ""type"": ""choice"", ""options"": [ ""out"", ""back"", ""to"" ] } ],
      ""effects"": [ { ""lua"": { ""call"": ""act"" } } ] } ],
  ""events"": {
    ""onGameStart"": [ { ""deal"": { ""from"": ""d"", ""count"": 1, ""toZone"": ""$it.hand"" } } ],
    ""onTurnStart"": [ { ""setVariable"": { ""name"": ""starts"", ""delta"": 1 } } ] },
  ""scripts"": { ""global"": ""function act(ctx)\n  local what = ctx.params.what\n  if what == 'out' then Game.eliminate('s3') end\n  if what == 'back' then Game.revive('s3') end\n  if what == 'to' then Game.turnTo('s3') end\n  Game.setVariable('out', Game.isOut('s3'))\nend"" }
}";

        private static GameEngine Start()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 1);
            var host = new Core.Scripting.ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            Assert.True(game.Execute(new StartGameCommand { Seats = new List<string> { "s1", "s2", "s3" } }).Ok);
            Assert.Equal("s1", game.Session!.CurrentSeat);
            return game;
        }

        private static void Do(GameEngine game, string action, string? param = null, string? value = null)
        {
            var command = new PerformActionCommand { Actor = game.Session!.CurrentSeat, ActionId = action };
            if (param != null)
                command.Params[param] = value!;
            var result = game.Execute(command);
            Assert.True(result.Ok, result.Error?.ToString());
        }

        [Fact]
        public void AKnockedOutSeat_IsPassedBy_UntilRevived()
        {
            var game = Start();
            Do(game, "knock");
            Do(game, "pass");
            Assert.Equal("s3", game.Session!.CurrentSeat);
            Do(game, "bringBack");
            Assert.DoesNotContain("s2", game.Session.Eliminated);
            Do(game, "pass");
            Do(game, "pass");
            Assert.Equal("s2", game.Session.CurrentSeat);
        }

        [Fact]
        public void TurnTo_GivesTheNextTurnToThatSeat_AsANewTurn()
        {
            var game = Start();
            var turn = game.Session!.Turn;
            Do(game, "toThird");
            Assert.Equal("s3", game.Session.CurrentSeat);
            Assert.Equal(turn + 1, game.Session.Turn);
            Assert.Equal("main", game.Session.Phase);
            Assert.Equal(2, (int)game.Session.Variables["starts"]!);

            // A seat out of the game cannot take a turn: the whole action is undone.
            var refused = game.Execute(new PerformActionCommand { Actor = "s3", ActionId = "toKnocked" });
            Assert.False(refused.Ok);
            Assert.Equal(ErrorCodes.UnknownSeat, refused.Error!.Code);
            Assert.DoesNotContain("s2", game.Session.Eliminated);
        }

        [Fact]
        public void Lua_PutsOut_BringsBack_AndHandsTheTurnOn()
        {
            var game = Start();
            Do(game, "lua", "what", "out");
            Assert.True((bool)game.Session!.Variables["out"]!);
            Assert.Contains("s3", game.Session.Eliminated);
            Do(game, "lua", "what", "back");
            Assert.False((bool)game.Session.Variables["out"]!);
            Do(game, "lua", "what", "to");
            Assert.Equal("s3", game.Session.CurrentSeat);
        }

        [Fact]
        public void APreconditionCheck_GivesItsReason_AndLeavesTheChoiceOut()
        {
            var game = Start();
            Do(game, "aimAt");
            Assert.Equal("aim", game.Session!.Phase);
            var refused = game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "pick", Params = { ["target"] = "s1" } });
            Assert.Equal(ErrorCodes.PreconditionFailed, refused.Error!.Code);
            Assert.Equal("Red: choose another player.", refused.Error.Message);
            var choices = game.LegalActions("s1").Single(a => a.ActionId == "pick").Choices.Select(c => c["target"]);
            Assert.Equal(new[] { "s2", "s3" }, choices);
        }

        [Fact]
        public void AnAutoAction_WithOneChoiceLeft_IsDoneByItself()
        {
            var game = Start();
            Do(game, "knock");
            // Only s3 can be picked now: going to the phase picks it, in the same move.
            Do(game, "aimAt");
            Assert.Equal("s3", (string)game.Session!.Variables["picked"]!);
            Assert.Equal("s3", game.Session.CurrentSeat);
            Assert.Equal("main", game.Session.Phase);
        }

        [Fact]
        public void ALook_AtAnotherHand_ReachesOnlyTheOneWhoLooks_AndIsNotNamed()
        {
            var game = Start();
            var result = game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "peek", Params = { ["target"] = "s2" } });
            Assert.True(result.Ok, result.Error?.ToString());
            var shown = result.Events.OfType<CardShown>().Single();
            Assert.Equal("Priest", shown.Card.Name);
            Assert.Equal(new[] { "s1" }, shown.Seats);
            Assert.True(shown.For("s1"));
            Assert.False(shown.For("s2"));
            Assert.False(shown.For(null), "spectators do not see it");
            Assert.DoesNotContain(result.Events.OfType<GameMessage>(), m => m.Text.Contains("Priest"));

            // Over the network only the looker's machine is sent the card.
            game = Start();
            var host = new SessionHost(game);
            var received = new Dictionary<string, List<string>>();
            foreach (var seat in new[] { "s1", "s2", "s3" })
            {
                host.Seats[seat] = seat;
                host.Snapshot(seat);
                received[seat] = new List<string>();
            }
            var peek = new PerformActionCommand { Actor = "s1", ActionId = "peek", Params = { ["target"] = "s2" } };
            foreach (var outgoing in host.ExecuteLocal(peek).Messages)
                foreach (var seat in outgoing.To == null ? received.Keys.ToList() : new List<string> { outgoing.To })
                    if (outgoing.Message["events"] is JArray events)
                        received[seat].AddRange(EventCodec.DecodeAll(events).OfType<CardShown>().Select(c => c.Card.Name ?? ""));
            Assert.Equal(new[] { "Priest" }, received["s1"]);
            Assert.Empty(received["s2"]);
            Assert.Empty(received["s3"]);
        }
    }
}
