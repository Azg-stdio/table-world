using System.Linq;
using System.Collections.Generic;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Core.Scripting;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Scripting
{
    /// <summary>Lua and the rules: lua effects call global functions, and the Game API reads and changes the game.</summary>
    public class RulesScriptTests
    {
        private const string Lua = @"
function fight(ctx)
  local dice = Game.roll(2, 6)
  local total = dice[1] + dice[2]
  Game.setVariable('lastRoll', total)
  if ctx.args.cost > Game.getResource(ctx.actor, 'gold') then
    return 'Not enough gold to fight.'
  end
  Game.addResource(ctx.actor, 'gold', -ctx.args.cost)
  Game.addResource(ctx.actor, 'wins', 1)
  Game.log(ctx.actor .. ' fought in ' .. ctx.params.where)
  local token = getObjectFromGUID('marker')
  token.setPosition({10, 1, 6})
  if Game.getResource(ctx.actor, 'wins') >= 2 then Game.win(ctx.actor) end
end

function onChat(message, player)
  if message == 'bonus' then Game.addResource(Game.getCurrentSeat(), 'gold', 5) end
end
";

        private static string Json(int cost) => @"{
  ""objects"": [ { ""id"": ""marker"", ""type"": ""Token"" } ],
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""resources"": [ { ""id"": ""gold"", ""initial"": 5, ""min"": 0 }, { ""id"": ""wins"", ""initial"": 0 }, { ""id"": ""score"", ""initial"": 0 } ],
  ""variables"": { ""lastRoll"": 0 },
  ""turn"": { ""phases"": [ ""main"" ] },
  ""phases"": [ { ""id"": ""main"", ""actions"": [ ""fight"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [ { ""id"": ""fight"", ""actor"": ""current"",
    ""params"": [ { ""name"": ""where"", ""type"": ""choice"", ""options"": [ ""dungeon"", ""ring"" ] } ],
    ""effects"": [ { ""setResource"": { ""player"": ""$actor"", ""resource"": ""score"", ""delta"": 10 } },
                   { ""lua"": { ""call"": ""fight"", ""args"": { ""cost"": " + cost + @" } } },
                   { ""setResource"": { ""player"": ""$actor"", ""resource"": ""score"", ""delta"": -10 } } ] } ]
}";

        private static (GameEngine Game, ScriptHost Host) Start(int cost)
        {
            var state = TableJson.Parse(Json(cost)).State!;
            state.Scripts["global"] = Lua;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, 1);
            var host = new ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return (game, host);
        }

        private static CommandResult Fight(GameEngine game, string seat = "a") =>
            game.Execute(new PerformActionCommand { Actor = seat, ActionId = "fight", Params = { ["where"] = "dungeon" } });

        [Fact]
        public void ALuaEffect_ChangesResourcesVariablesAndTheTable_InsideTheAction()
        {
            var (game, _) = Start(cost: 2);
            var result = Fight(game);
            Assert.True(result.Ok, result.Error?.ToString());
            Assert.True(result.ScriptsRan);
            Assert.Equal(3, game.Session!.Resources["a"]["gold"]);
            Assert.Equal(1, game.Session.Resources["a"]["wins"]);
            // The effects after the lua call ran too.
            Assert.Equal(0, game.Session.Resources["a"]["score"]);
            Assert.InRange((double)game.Session.Variables["lastRoll"], 2, 12);
            Assert.Contains("a fought in dungeon", game.Session.Log);
            // setPosition takes TTS units, as every position in the Lua API.
            Assert.NotEqual(Vec3.Zero, game.Table.Find("marker")!.Transform.Position);
            // A script's table change inside the action is part of the action's events, not a command of its own.
            Assert.Contains(result.Events, e => e.ObjectId == "marker");

            Assert.True(Fight(game).Ok);
            Assert.True(game.Session.Over);
            Assert.Equal("a", game.Session.Winner);
        }

        [Fact]
        public void ARefusal_RollsBackTheWholeAction()
        {
            var (game, _) = Start(cost: 99);
            var hash = TableJson.Hash(game.State);
            var result = Fight(game);
            Assert.False(result.Ok);
            Assert.Equal(ErrorCodes.ScriptFailed, result.Error!.Code);
            Assert.Equal("Not enough gold to fight.", result.Error.Message);
            Assert.Equal(hash, TableJson.Hash(game.State));
            Assert.Equal(0, game.Session!.Resources["a"]["score"]);
        }

        [Fact]
        public void WithoutScripts_ALuaEffectIsSkipped_AsOnAPeer()
        {
            var state = TableJson.Parse(Json(2)).State!;
            state.Scripts["global"] = Lua;
            var game = new GameEngine(state, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            var result = Fight(game);
            Assert.True(result.Ok);
            Assert.False(result.ScriptsRan);
            Assert.Equal(5, game.Session!.Resources["a"]["gold"]);
        }

        [Fact]
        public void TheGameApi_WorksOutsideActions_AsCommandsOfItsOwn()
        {
            var (game, host) = Start(cost: 2);
            host.OnChat("bonus", "Red");
            Assert.Equal(10, game.Session!.Resources["a"]["gold"]);
        }

        [Fact]
        public void TheValidator_WantsTheFunctionInTheGlobalScript()
        {
            var state = TableJson.Parse(Json(2)).State!;
            state.Scripts["global"] = "function other() end";
            var errors = TableValidator.Validate(state);
            Assert.Contains(errors, e => e.Code == ErrorCodes.ScriptFailed && e.Message.Contains("'fight'"));
        }
    
        [Fact]
        public void APeerWithoutScripts_EndsWithTheHostsTable_AfterALuaAction()
        {
            var (game, _) = Start(cost: 2);
            var host = new SessionHost(game);
            var client = new SessionClient("c1");
            var toClient = new Queue<JObject>();
            void Deliver(Outgoing o) => toClient.Enqueue((JObject)o.Message.DeepClone());
            void Flush()
            {
                while (toClient.Count > 0)
                    if (client.Receive(toClient.Dequeue()) is { } reply)
                        foreach (var answer in host.Receive("c1", reply))
                            Deliver(answer);
            }
            Deliver(host.Snapshot("c1"));
            Flush();
            Assert.Null(client.Confirmed!.Scripts);

            Deliver(host.ExecuteLocal(new PerformActionCommand { Actor = "a", ActionId = "fight", Params = { ["where"] = "ring" } }).Broadcast);
            Flush();
            Assert.Equal(1, client.Resyncs);
            Assert.Equal(TableJson.Hash(game.State), TableJson.Hash(client.Confirmed!.State));
            Assert.Equal(3, client.Confirmed.Session!.Resources["a"]["gold"]);
        }
    }
}
