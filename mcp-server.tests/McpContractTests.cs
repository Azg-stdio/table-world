using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp.Tests
{
    /// <summary>Starts the real server process over stdio once for the class.</summary>
    public sealed class ServerFixture : IAsyncLifetime
    {
        public McpClient Client { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            var server = typeof(GameTools).Assembly.Location;
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "table-world",
                Command = "dotnet",
                Arguments = new[] { server },
            });
            Client = await McpClient.CreateAsync(transport);
        }

        public async Task DisposeAsync() => await Client.DisposeAsync();

        public async Task<JObject> Call(string tool, Dictionary<string, object?>? args = null)
        {
            var result = await Client.CallToolAsync(tool, args ?? new Dictionary<string, object?>());
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            Assert.False(result.IsError == true, $"{tool} raised: {text}");
            return text.TrimStart().StartsWith("{") ? JObject.Parse(text) : new JObject { ["text"] = text };
        }
    }

    /// <summary>T4.4: one contract test per tool over stdio, plus a game built and played only through the tools.</summary>
    [Collection("server")]
    public class McpContractTests : IClassFixture<ServerFixture>
    {
        private readonly ServerFixture _server;

        public McpContractTests(ServerFixture server)
        {
            _server = server;
        }

        private Task<JObject> Call(string tool, params (string Key, object? Value)[] args) =>
            _server.Call(tool, args.ToDictionary(a => a.Key, a => a.Value));

        private static void Ok(JObject reply) => Assert.True((bool?)reply["ok"] == true, reply.ToString());

        [Fact]
        public async Task ListsEveryTool()
        {
            var tools = (await _server.Client.ListToolsAsync()).Select(t => t.Name).OrderBy(n => n).ToArray();
            var expected = new[]
            {
                "add_action", "add_event", "add_phase", "add_resource", "ask_host", "create_deck", "deal", "define_zone",
                "get_rules_document", "get_rules_reference", "get_table_state", "load_game", "new_game", "perform_action",
                "place_object", "remove", "reset_game", "save_game", "set_rules_document", "set_turn", "set_variables",
                "set_win_condition", "start_game", "validate_rules",
            };
            Assert.Equal(expected, tools);
        }

        [Fact]
        public async Task AuthoringTools_ApplyKeepIncompleteAndRejectMalformed()
        {
            Ok(await Call("new_game", ("id", "probe"), ("name", "Probe"), ("players", 2)));
            Ok(await Call("create_deck", ("id", "draw"), ("cards", "standard52")));
            Ok(await Call("place_object", ("objectJson", "{\"id\":\"d6\",\"type\":\"Die\",\"faces\":6,\"transform\":{\"position\":[0.3,0,0]}}")));
            Ok(await Call("define_zone", ("zoneJson", "{\"id\":\"middle\",\"type\":\"ScriptingZone\",\"size\":[0.3,0.1,0.2]}")));
            Ok(await Call("set_variables", ("variablesJson", "{\"round\":0}")));
            Ok(await Call("add_resource", ("resourceJson", "{\"id\":\"score\",\"initial\":0}")));
            Ok(await Call("set_turn", ("phases", "play")));

            // Kept but incomplete: the phase names an action that does not exist yet.
            var phase = await Call("add_phase", ("phaseJson", "{\"id\":\"play\",\"actions\":[\"draw_one\"]}"));
            Ok(phase);
            Assert.Equal("unknown_action", (string?)phase["problems"]![0]!["code"]);

            Ok(await Call("add_action", ("actionJson", "{\"id\":\"draw_one\",\"actor\":\"current\",\"effects\":[{\"move\":{\"from\":\"draw\",\"to\":\"$actor.hand\",\"faceUp\":true}},{\"endTurn\":{}}]}")));
            Ok(await Call("add_event", ("eventName", "onGameStart"), ("effectsJson", "[{\"shuffle\":{\"object\":\"draw\"}}]")));
            Ok(await Call("set_win_condition", ("winConditionJson", "{\"when\":{\"zoneCount\":{\"zone\":\"$player.hand\",\"op\":\">=\",\"value\":3}},\"winner\":\"$player\"}")));
            var valid = await Call("validate_rules");
            Ok(valid);

            // Malformed: unknown effect kind, rejected with a suggestion and not applied.
            var bad = await Call("add_action", ("actionJson", "{\"id\":\"x\",\"effects\":[{\"shufle\":{\"object\":\"draw\"}}]}"));
            Assert.False((bool)bad["ok"]!);
            Assert.Equal("unknown_effect", (string?)bad["errors"]![0]!["code"]);
            Assert.Equal("Did you mean 'shuffle'?", (string?)bad["errors"]![0]!["suggestion"]);
            Assert.DoesNotContain((await Call("get_rules_document"))["actions"]!, a => (string?)a["id"] == "x");

            Ok(await Call("remove", ("collection", "objects"), ("id", "d6")));
            var doc = await Call("get_rules_document");
            Assert.DoesNotContain(doc["objects"]!, o => (string?)o["id"] == "d6");
            Ok(await Call("set_rules_document", ("documentJson", doc.ToString())));
        }

        [Fact]
        public async Task ReferenceAndHostTools()
        {
            var reference = (string)(await Call("get_rules_reference"))["text"]!;
            Assert.Contains("## Effects (v0)", reference);

            var ask = await Call("ask_host", ("question", "Do aces count high?"), ("options", "high, low"));
            Assert.Equal("needs_clarification", (string?)ask["status"]);
            Assert.Equal(new[] { "high", "low" }, ask["options"]!.Select(o => (string)o!));
        }

        [Fact]
        public async Task PlayTools_DealStartPerformSaveLoadReset()
        {
            Ok(await Call("new_game", ("id", "race"), ("name", "Race to three"), ("players", 2)));
            Ok(await Call("create_deck", ("id", "draw"), ("x", -0.1f)));
            Ok(await Call("add_action", ("actionJson", "{\"id\":\"draw_one\",\"actor\":\"current\",\"effects\":[{\"move\":{\"from\":\"draw\",\"to\":\"$actor.hand\",\"faceUp\":true}},{\"endTurn\":{}}]}")));
            Ok(await Call("add_phase", ("phaseJson", "{\"id\":\"play\",\"actions\":[\"draw_one\"]}")));
            Ok(await Call("set_win_condition", ("winConditionJson", "{\"when\":{\"zoneCount\":{\"zone\":\"$player.hand\",\"op\":\">=\",\"value\":3}},\"winner\":\"$player\"}")));

            var dealt = await Call("deal", ("deck", "draw"), ("count", 1));
            Ok(dealt);
            Assert.Equal(50, (int)dealt["state"]!["objects"]!.First(o => (string?)o["id"] == "draw")["cards"]!);

            Ok(await Call("start_game", ("seed", 7L)));
            var notYours = await Call("perform_action", ("seat", "seat_north"), ("action", "draw_one"));
            Assert.Equal("not_your_turn", (string?)notYours["errors"]![0]!["code"]);

            JObject state = null!;
            // Each hand already holds the card dealt during setup, so South reaches three on its second draw.
            foreach (var seat in new[] { "seat_south", "seat_north", "seat_south" })
            {
                state = await Call("perform_action", ("seat", seat), ("action", "draw_one"));
                Ok(state);
            }
            Assert.Equal("game_over", (string?)(await Call("perform_action", ("seat", "seat_north"), ("action", "draw_one")))["errors"]![0]!["code"]);
            Assert.True((bool)state["state"]!["play"]!["over"]!);
            Assert.Equal("seat_south", (string?)state["state"]!["play"]!["winner"]);

            var path = Path.Combine(Path.GetTempPath(), $"tw-{Guid.NewGuid():N}.json");
            Ok(await Call("save_game", ("path", path)));
            var loaded = await Call("load_game", ("path", path));
            Ok(loaded);
            Assert.Equal("seat_south", (string?)loaded["state"]!["play"]!["winner"]);
            File.Delete(path);

            var reset = await Call("reset_game");
            Ok(reset);
            Assert.Null(reset["state"]!["play"]);
        }

        /// <summary>The way an agent works: build War from nothing with the tools, then play it to the end.</summary>
        [Fact]
        public async Task BuildWarThroughTools_AndPlayItToTheEnd()
        {
            Ok(await Call("new_game", ("id", "war"), ("name", "War"), ("players", 2)));
            Ok(await Call("create_deck", ("id", "deck_main"), ("cards", "standard52")));
            foreach (var (seat, side) in new[] { ("seat_south", -1), ("seat_north", 1) })
            {
                Ok(await Call("create_deck", ("id", $"deck_{seat}"), ("cards", "[]"), ("x", 0.15f * side), ("z", 0.25f * side), ("tags", $"deck:{seat}")));
                Ok(await Call("create_deck", ("id", $"battle_{seat}"), ("cards", "[]"), ("faceUp", true), ("x", 0.15f * side), ("z", 0.08f * side), ("tags", $"battle:{seat}")));
            }
            Ok(await Call("set_variables", ("variablesJson", "{\"battles\":0}")));
            Ok(await Call("add_action", ("actionJson", @"{ ""id"": ""play_top_card"", ""actor"": ""any"", ""limitPerPhase"": 1,
                ""preconditions"": [ { ""zoneCount"": { ""zone"": ""$actor.deck"", ""op"": "">"", ""value"": 0 } } ],
                ""effects"": [ { ""move"": { ""from"": ""$actor.deck"", ""to"": ""$actor.battle"", ""faceUp"": true } } ] }")));
            Ok(await Call("add_phase", ("phaseJson", @"{ ""id"": ""battle"", ""actions"": [""play_top_card""], ""advanceWhen"": { ""allPlayersActed"": true } }")));
            string Collect(string winner, string loser) => $@"[ {{ ""move"": {{ ""from"": ""$seat.{winner}.battle"", ""take"": ""all"", ""to"": ""$seat.{winner}.deck"", ""at"": ""bottom"" }} }},
                                                                 {{ ""move"": {{ ""from"": ""$seat.{loser}.battle"", ""take"": ""all"", ""to"": ""$seat.{winner}.deck"", ""at"": ""bottom"" }} }} ]";
            Ok(await Call("add_phase", ("phaseJson", $@"{{ ""id"": ""resolve"", ""advanceWhen"": {{ ""immediate"": true }}, ""onEnter"": [ {{ ""if"": {{
                ""condition"": {{ ""compare"": {{ ""left"": ""$seat.seat_south.battle.top.meta.rank"", ""op"": "">"", ""right"": ""$seat.seat_north.battle.top.meta.rank"" }} }},
                ""then"": {Collect("seat_south", "seat_north")},
                ""else"": [ {{ ""if"": {{ ""condition"": {{ ""compare"": {{ ""left"": ""$seat.seat_south.battle.top.meta.rank"", ""op"": ""<"", ""right"": ""$seat.seat_north.battle.top.meta.rank"" }} }},
                                        ""then"": {Collect("seat_north", "seat_south")},
                                        ""else"": [ {{ ""forEach"": {{ ""players"": true, ""do"": [ {{ ""if"": {{ ""condition"": {{ ""zoneCount"": {{ ""zone"": ""$it.deck"", ""op"": ""<"", ""value"": 2 }} }},
                                                     ""then"": [ {{ ""win"": {{ ""player"": ""$other"" }} }} ], ""else"": [ {{ ""move"": {{ ""from"": ""$it.deck"", ""to"": ""$it.battle"" }} }} ] }} }} ] }} }},
                                                  {{ ""gotoPhase"": {{ ""phase"": ""battle"" }} }} ] }} }} ] }} }} ] }}")));
            Ok(await Call("add_event", ("eventName", "onGameStart"), ("effectsJson", @"[ { ""shuffle"": { ""object"": ""deck_main"" } }, { ""deal"": { ""from"": ""deck_main"", ""count"": 26, ""toTag"": ""deck"", ""faceDown"": true } } ]")));
            Ok(await Call("add_event", ("eventName", "onTurnEnd"), ("effectsJson", @"[ { ""setVariable"": { ""name"": ""battles"", ""delta"": 1 } } ]")));
            Ok(await Call("set_win_condition", ("winConditionJson", @"{ ""when"": { ""and"": [ { ""zoneCount"": { ""zone"": ""$player.deck"", ""op"": ""=="", ""value"": 0 } }, { ""zoneCount"": { ""zone"": ""$player.battle"", ""op"": ""=="", ""value"": 0 } } ] }, ""winner"": ""$other"", ""checkAfter"": ""turnEnd"" }")));
            Ok(await Call("set_win_condition", ("winConditionJson", @"{ ""when"": { ""and"": [ { ""variable"": { ""name"": ""battles"", ""op"": "">="", ""value"": 200 } }, { ""compare"": { ""left"": ""$player.deck.count"", ""op"": "">"", ""right"": ""$other.deck.count"" } } ] }, ""winner"": ""$player"", ""checkAfter"": ""turnEnd"" }")));
            Ok(await Call("validate_rules"));

            var state = await Call("start_game", ("seed", 11L));
            Ok(state);
            var steps = 0;
            while ((bool?)state["state"]!["play"]!["over"] != true)
            {
                Assert.True(steps++ < 2000, "War did not finish.");
                var legal = (JObject)state["state"]!["play"]!["legalActions"]!;
                var seat = legal.Properties().First(p => ((JArray)p.Value).Count > 0).Name;
                state = await Call("perform_action", ("seat", seat), ("action", "play_top_card"));
                Ok(state);
            }
            Assert.NotNull((string?)state["state"]!["play"]!["winner"]);
        }
    }
}
