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
    /// <summary>
    /// T4.5 end to end, with the game window running (Unity Play mode, Table scene). Opt-in: set TW_UNITY=1, otherwise the
    /// test returns immediately. Starts the real server with the bridge, waits for the game to connect, places objects,
    /// then loads simplified UNO and plays a few turns that execute in the game window.
    /// </summary>
    public class BridgeEndToEndTests
    {
        [Fact]
        public async Task AgentBuildsAndPlays_InTheGameWindow()
        {
            if (Environment.GetEnvironmentVariable("TW_UNITY") != "1")
                return;

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "table-world",
                Command = "dotnet",
                Arguments = new[] { typeof(GameTools).Assembly.Location },
            });
            await using var client = await McpClient.CreateAsync(transport);

            async Task<JObject> Call(string tool, params (string Key, object? Value)[] args)
            {
                var result = await client.CallToolAsync(tool, args.ToDictionary(a => a.Key, a => a.Value));
                var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
                return JObject.Parse(text);
            }

            // The game polls the discovery file every 2 s.
            var connected = false;
            for (var i = 0; i < 20 && !connected; i++)
            {
                await Task.Delay(500);
                connected = ((string?)(await Call("get_table_state"))["gameWindow"])?.StartsWith("connected") == true;
            }
            Assert.True(connected, "The game window did not connect. Is the Table scene in Play mode?");

            Assert.True((bool)(await Call("new_game", ("id", "bridge"), ("name", "Bridge check"), ("players", 2)))["ok"]!);
            Assert.True((bool)(await Call("create_deck", ("id", "draw"), ("x", -0.2f)))["ok"]!);
            Assert.True((bool)(await Call("place_object", ("objectJson", "{\"id\":\"big_die\",\"type\":\"Die\",\"faces\":20,\"value\":20,\"transform\":{\"position\":[0.2,0,0]}}")))["ok"]!);
            await Task.Delay(1000);

            var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            Assert.True((bool)(await Call("load_game", ("path", Path.Combine(repo, "games", "uno-lite.json"))))["ok"]!);
            var state = await Call("start_game", ("seed", 5L));
            Assert.True((bool)state["ok"]!, state.ToString());
            for (var turn = 0; turn < 6; turn++)
            {
                var legal = (JObject)state["state"]!["play"]!["legalActions"]!;
                var (seat, actions) = legal.Properties().Select(p => (p.Name, (JArray)p.Value)).First(p => p.Item2.Count > 0);
                var play = actions.FirstOrDefault(a => (string?)a["action"] == "play_card");
                state = play != null
                    ? await Call("perform_action", ("seat", seat), ("action", "play_card"), ("paramsJson", ((JObject)play["choices"]![0]!).ToString()))
                    : await Call("perform_action", ("seat", seat), ("action", "draw_card"));
                Assert.True((bool)state["ok"]!, state.ToString());
            }
            var cut = await Call("table_action", ("action", "cut"), ("target", "draw"), ("value", 4.0));
            Assert.True((bool)cut["ok"]!, cut.ToString());
            Assert.Single(cut["created"]!);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tw-bridge-e2e.json"), cut.ToString());
        }
    }
}
