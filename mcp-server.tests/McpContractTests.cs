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

        /// <summary>Where the server saves new games (a server without <c>--file</c> would use the player's Games folder).</summary>
        public string GamesFolder { get; } = Path.Combine(Path.GetTempPath(), "tw-mcp-tests-" + Guid.NewGuid().ToString("N"));

        /// <summary>The server's music library, a temporary one (the player's own is never touched by tests).</summary>
        public string MusicFolder => Path.Combine(GamesFolder, "music");

        public async Task InitializeAsync()
        {
            var server = typeof(GameTools).Assembly.Location;
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "table-world",
                Command = "dotnet",
                Arguments = new[] { server, "--no-bridge", "--games", GamesFolder, "--music", MusicFolder },
            });
            Client = await McpClient.CreateAsync(transport);
        }

        public async Task DisposeAsync()
        {
            await Client.DisposeAsync();
            if (Directory.Exists(GamesFolder))
                Directory.Delete(GamesFolder, recursive: true);
        }

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

        /// <summary>
        /// Started without --file (as a player connects it), each new game is saved in the Games folder under its id, so
        /// the game window lists it; starting the same game again keeps its file, another game gets its own.
        /// </summary>
        [Fact]
        public async Task WithoutAFile_EachNewGameIsSavedInTheGamesFolder()
        {
            var first = await Call("new_game", ("id", "pile_race"), ("name", "Pile Race"), ("players", 2));
            Ok(first);
            var path = (string?)first["savedTo"];
            Assert.Equal(Path.GetFullPath(Path.Combine(_server.GamesFolder, "pile_race.json")), path);
            Ok(await Call("create_deck", ("id", "draw")));
            Assert.Contains("\"draw\"", File.ReadAllText(path!));

            Assert.Equal(path, (string?)(await Call("new_game", ("id", "pile_race"), ("name", "Pile Race"), ("players", 2)))["savedTo"]);
            var other = (string?)(await Call("new_game", ("id", "Other Game!"), ("name", "Other"), ("players", 2)))["savedTo"];
            Assert.Equal(Path.GetFullPath(Path.Combine(_server.GamesFolder, "Other-Game.json")), other);
            Assert.True(File.Exists(path) && File.Exists(other));
        }

        [Fact]
        public async Task ListsEveryTool()
        {
            var tools = (await _server.Client.ListToolsAsync()).Select(t => t.Name).OrderBy(n => n).ToArray();
            var expected = new[]
            {
                "add_action", "add_asset", "add_event", "add_phase", "add_resource", "add_to_games", "ask_host", "create_deck", "create_supply", "create_text_deck", "deal", "define_zone",
                "get_rules_document", "get_rules_reference", "get_table_state", "import_music", "list_music", "load_game", "new_game", "perform_action",
                "place_object", "preview_music", "remove", "reset_game", "run_lua", "save_game", "screenshot", "set_environment", "set_messages", "set_music", "set_notebook", "set_roles", "set_rules_document", "set_script", "set_translations", "set_turn", "set_variables",
                "set_win_condition", "start_game", "table_action", "translate_game", "validate_rules",
            };
            Assert.Equal(expected, tools);
        }

        [Fact]
        public async Task GuidesAndPrompts_AreServed()
        {
            // M8 T9.11: an agent gets the rules reference, the brief template and the guide without the repository.
            var resources = (await _server.Client.ListResourcesAsync()).Select(r => r.Uri).OrderBy(u => u).ToArray();
            Assert.Equal(new[] { "tableworld://docs/agent-guide", "tableworld://docs/game-brief-template", "tableworld://docs/rules-and-scripting", "tableworld://docs/rules-reference" }, resources);
            var brief = await _server.Client.ReadResourceAsync("tableworld://docs/game-brief-template");
            var text = string.Concat(brief.Contents.OfType<ModelContextProtocol.Protocol.TextResourceContents>().Select(c => c.Text));
            Assert.Contains("Game brief", text);

            var prompts = (await _server.Client.ListPromptsAsync()).Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "build_game_from_brief", "review_rulebook" }, prompts);
            var prompt = await _server.Client.GetPromptAsync("review_rulebook", new Dictionary<string, object?> { ["rulebook"] = "Each player draws 7 cards." });
            var said = string.Concat(prompt.Messages.Select(m => m.Content).OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
            Assert.Contains("Each player draws 7 cards.", said);
            Assert.Contains("tableworld://docs/game-brief-template", said);
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
        public async Task NewGame_SeatsUpToTwelve_OnATableThatGrows()
        {
            Ok(await Call("new_game", ("id", "party"), ("name", "Party"), ("players", 12)));
            var doc = await Call("get_rules_document");
            Assert.Equal(12, doc["players"]!["seats"]!.Count());
            Assert.Equal("seat_12", (string?)doc["players"]!["seats"]![11]!["id"]);
            Assert.True((float)doc["environment"]!["table"]!["size"]![0]! > 1.6f);
            Ok(await Call("validate_rules"));

            // One table for 3–6 players, started with three of its six seats.
            Ok(await Call("new_game", ("id", "flexible"), ("name", "Flexible"), ("players", 6), ("minPlayers", 3)));
            Ok(await Call("add_action", ("actionJson", "{\"id\":\"pass\",\"actor\":\"current\",\"effects\":[{\"endTurn\":{}}]}")));
            Ok(await Call("add_phase", ("phaseJson", "{\"id\":\"main\",\"actions\":[\"pass\"],\"advanceWhen\":{\"currentPlayerEnds\":true}}")));
            Ok(await Call("start_game", ("players", 3)));
            var started = await Call("get_rules_document");
            Assert.Equal(new[] { "seat_1", "seat_3", "seat_5" }, started["session"]!["players"]!.Select(p => (string)p!));
            Ok(await Call("perform_action", ("seat", "seat_1"), ("action", "pass")));
            Assert.Equal("seat_3", (string?)(await Call("get_rules_document"))["session"]!["currentSeat"]);
            Ok(await Call("reset_game"));

            var tooMany = await Call("new_game", ("id", "crowd"), ("name", "Crowd"), ("players", 13));
            Assert.False((bool)tooMany["ok"]!);
            Assert.Equal("players", (string?)tooMany["errors"]![0]!["path"]);
        }

        [Fact]
        public async Task TranslateGame_ListsWhatIsLeft_SetTranslationsFillsIt()
        {
            Ok(await Call("new_game", ("id", "carrera"), ("name", "Carrera"), ("players", 2)));
            Ok(await Call("add_action", ("actionJson", "{\"id\":\"pasar\",\"name\":\"Pasar\",\"actor\":\"current\",\"effects\":[{\"message\":{\"text\":\"@pasa\"}},{\"endTurn\":{}}]}")));
            Ok(await Call("add_phase", ("phaseJson", "{\"id\":\"turno\",\"name\":\"Tu turno\",\"actions\":[\"pasar\"],\"advanceWhen\":{\"currentPlayerEnds\":true}}")));

            var noLanguage = await Call("translate_game", ("language", "en"));
            Assert.False((bool)noLanguage["ok"]!);
            Assert.Equal("game.language", (string?)noLanguage["errors"]![0]!["path"]);

            // The game's own string first, in its own language; the @pasa reference then validates.
            Ok(await Call("set_translations", ("language", "es"), ("gameLanguage", "es"), ("translationsJson", "{\"strings.pasa\":\"Pasa el turno.\"}")));
            Ok(await Call("validate_rules"));

            var left = await Call("translate_game", ("language", "en"));
            Ok(left);
            Assert.Equal("Pasar", (string?)left["missing"]!["actions.pasar.name"]);
            Assert.Equal("Tu turno", (string?)left["missing"]!["phases.turno.name"]);
            Assert.Equal("Pasa el turno.", (string?)left["missing"]!["strings.pasa"]);
            var total = (int)left["total"]!;

            Ok(await Call("set_translations", ("language", "en"), ("translationsJson", "{\"actions.pasar.name\":\"Pass\",\"phases.turno.name\":\"Your turn\",\"strings.pasa\":\"Passes the turn.\",\"game.name\":\"Race\"}")));
            var after = await Call("translate_game", ("language", "en"));
            Assert.Equal(total - 4, (int)after["left"]!);
            Assert.Null(after["missing"]!["actions.pasar.name"]);

            var unknown = await Call("set_translations", ("language", "en"), ("translationsJson", "{\"actions.volar.name\":\"Fly\"}"));
            Assert.False((bool)unknown["ok"]!);
            Assert.Equal("translations.en.actions.volar.name", (string?)unknown["errors"]![0]!["path"]);

            Ok(await Call("set_translations", ("language", "en"), ("translationsJson", "{\"game.name\":\"\"}")));
            var doc = await Call("get_rules_document");
            Assert.Equal("es", (string?)doc["game"]!["language"]);
            Assert.Null(doc["translations"]!["en"]!["game.name"]);
            Assert.Equal("Pass", (string?)doc["translations"]!["en"]!["actions.pasar.name"]);
        }

        [Fact]
        public async Task NewGame_PicksTheRoom()
        {
            Ok(await Call("new_game", ("id", "singalong"), ("name", "Sing-along"), ("players", 4), ("room", "karaoke")));
            var doc = await Call("get_rules_document");
            Assert.Equal("karaoke", (string?)doc["environment"]!["room"]);
            Assert.Null(doc["environment"]!["table"]!["size"]);
            Ok(await Call("validate_rules"));

            var unknown = await Call("new_game", ("id", "disco"), ("name", "Disco"), ("players", 2), ("room", "disco"));
            Assert.False((bool)unknown["ok"]!);
            Assert.Equal("room", (string?)unknown["errors"]![0]!["path"]);
        }

        [Fact]
        public async Task AddAsset_CopiesTheFileNextToTheGame_AndDecksUseIt()
        {
            var outside = Path.Combine(Path.GetTempPath(), "tw-asset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try
            {
                // A PNG header is all the size check reads: 1000 x 600 px, a 5 x 3 sheet of 200 x 200 cells.
                var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0x03, 0xE8, 0, 0, 0x02, 0x58, 8, 6, 0, 0, 0 };
                var sheet = Path.Combine(outside, "Monsters.png");
                File.WriteAllBytes(sheet, png);

                var game = await Call("new_game", ("id", "art"), ("name", "Art"), ("players", 3));
                var root = Path.GetDirectoryName((string)game["savedTo"]!)!;
                var added = await Call("add_asset", ("id", "monsters"), ("path", sheet), ("columns", 5), ("rows", 3));
                Ok(added);
                Assert.Equal("art-assets/Monsters.png", (string?)added["asset"]!["path"]);
                Assert.Equal(1000, (int)added["asset"]!["width"]!);
                Assert.Equal(200, (int)added["cell"]!["height"]!);
                Assert.True(File.Exists(Path.Combine(root, "art-assets", "Monsters.png")));

                // The same file again is not copied twice; a different file of the same name gets a number.
                Ok(await Call("add_asset", ("id", "monsters"), ("path", sheet), ("columns", 5), ("rows", 3)));
                File.WriteAllBytes(sheet, png.Concat(new byte[] { 1 }).ToArray());
                Assert.Equal("art-assets/Monsters-2.png", (string?)(await Call("add_asset", ("id", "monsters_b"), ("path", sheet)))["asset"]!["path"]);

                Ok(await Call("create_deck", ("id", "deck"), ("cards", "[{\"id\":\"grumo\",\"faceIndex\":0},{\"id\":\"troll\",\"faceIndex\":14}]"), ("faceAsset", "monsters"), ("backAsset", "monsters_b")));
                var doc = await Call("get_rules_document");
                Assert.Equal("monsters", (string?)doc["objects"]!.First(o => (string?)o["id"] == "deck")["faceAsset"]);
                Assert.Equal(5, (int)doc["assets"]!.First(a => (string?)a["id"] == "monsters")["columns"]!);
                Ok(await Call("validate_rules"));

                var missing = await Call("add_asset", ("id", "x"), ("path", Path.Combine(outside, "nope.png")));
                Assert.Equal("invalid_path", (string?)missing["errors"]![0]!["code"]);
                File.WriteAllText(Path.Combine(outside, "card.webp"), "");
                Assert.Equal("invalid_value", (string?)(await Call("add_asset", ("id", "x"), ("path", Path.Combine(outside, "card.webp"))))["errors"]![0]!["code"]);
            }
            finally
            {
                Directory.Delete(outside, recursive: true);
            }
        }

        [Fact]
        public async Task CreateTextDeck_PrintsWords_WithCopiesAndStyles()
        {
            Ok(await Call("new_game", ("id", "words"), ("name", "Words"), ("players", 3)));
            var cards = "[{\"title\":\"Escudo Pesado\",\"kind\":\"Equipamiento\",\"body\":\"Ataque +X.\",\"footer\":\"Precio 10 · Maná 3\",\"count\":3,\"meta\":{\"price\":10}}," +
                        "{\"id\":\"night\",\"title\":\"Noche Normal\",\"style\":{\"paper\":[0.1,0.1,0.2],\"ink\":[1,1,1]}}]";
            var style = "{\"accent\":[0.6,0.4,0.1],\"backLabel\":\"EQUIPAMIENTO\"}";
            Ok(await Call("create_text_deck", ("id", "gear"), ("cards", cards), ("style", style), ("x", 0.3)));
            var deck = (await Call("get_rules_document"))["objects"]!.First(o => (string?)o["id"] == "gear");
            var ids = deck["cards"]!.Select(c => (string?)c["id"]).ToArray();
            Assert.Equal(new[] { "escudo_pesado", "escudo_pesado_2", "escudo_pesado_3", "night" }, ids);
            Assert.Equal("Escudo Pesado", (string?)deck["cards"]![1]!["name"]);
            Assert.Equal("Precio 10 · Maná 3", (string?)deck["cards"]![2]!["text"]!["footer"]);
            Assert.Equal(10, (int)deck["cards"]![0]!["meta"]!["price"]!);
            Assert.Equal("EQUIPAMIENTO", (string?)deck["style"]!["backLabel"]);
            Assert.NotNull(deck["cards"]![3]!["style"]);
            Ok(await Call("validate_rules"));

            var state = await Call("get_table_state");
            Assert.Contains("gear", state.ToString());
        }

        [Fact]
        public async Task HiddenPlay_HideShowBlindRound_AndSecretChoices()
        {
            Ok(await Call("new_game", ("id", "hidden"), ("name", "Hidden"), ("players", 3)));
            Ok(await Call("place_object", ("objectJson", "{\"id\":\"chip\",\"type\":\"Token\",\"transform\":{\"position\":[0.1,0,0]}}")));
            Ok(await Call("place_object", ("objectJson", "{\"id\":\"worker\",\"type\":\"Token\",\"transform\":{\"position\":[-0.1,0,0]}}")));

            async Task<JToken> Object(string id) => (await Call("get_rules_document"))["objects"]!.First(o => (string?)o["id"] == id);
            Ok(await Call("table_action", ("action", "hide"), ("target", "chip"), ("seat", "seat_south")));
            Assert.Equal(new[] { "blue", "green" }, (await Object("chip"))["invisibleTo"]!.Select(c => (string)c!));
            Ok(await Call("table_action", ("action", "show"), ("target", "chip")));
            Assert.Null((await Object("chip"))["invisibleTo"]);

            Ok(await Call("table_action", ("action", "blind_round"), ("target", "table"), ("value", 1)));
            Ok(await Call("table_action", ("action", "move"), ("target", "worker"), ("x", 0.3f), ("z", 0.1f), ("seat", "seat_north")));
            Assert.Equal(new[] { "red", "green" }, (await Object("worker"))["invisibleTo"]!.Select(c => (string)c!));
            Ok(await Call("table_action", ("action", "blind_round"), ("target", "table"), ("value", 0)));
            Assert.Null((await Object("worker"))["invisibleTo"]);

            // Rules: everyone picks a space in secret; the phase ends when all have, and its onExit reveals.
            Ok(await Call("add_resource", ("resourceJson", "{\"id\":\"space\",\"initial\":0}")));
            Ok(await Call("add_action", ("actionJson", "{\"id\":\"pick\",\"actor\":\"any\",\"secret\":true,\"limitPerPhase\":1,\"params\":[{\"name\":\"space\",\"type\":\"choice\",\"options\":[\"1\",\"2\"]}],\"effects\":[{\"setResource\":{\"player\":\"$actor\",\"resource\":\"space\",\"value\":\"$param.space\"}}]}")));
            Ok(await Call("add_action", ("actionJson", "{\"id\":\"done\",\"actor\":\"current\",\"effects\":[{\"endTurn\":{}}]}")));
            Ok(await Call("add_phase", ("phaseJson", "{\"id\":\"choose\",\"actions\":[\"pick\"],\"advanceWhen\":{\"allPlayersActed\":true},\"onExit\":[{\"reveal\":{}}]}")));
            Ok(await Call("add_phase", ("phaseJson", "{\"id\":\"act\",\"actions\":[\"done\"],\"advanceWhen\":{\"currentPlayerEnds\":true}}")));
            Ok(await Call("validate_rules"));
            Ok(await Call("start_game", ("seed", 3L)));
            Ok(await Call("perform_action", ("seat", "seat_east"), ("action", "pick"), ("paramsJson", "{\"space\":\"2\"}")));
            var mid = await Call("get_rules_document");
            Assert.Equal(0, (double)mid["session"]!["resources"]!["seat_east"]!["space"]!);
            Ok(await Call("perform_action", ("seat", "seat_south"), ("action", "pick"), ("paramsJson", "{\"space\":\"1\"}")));
            Ok(await Call("perform_action", ("seat", "seat_north"), ("action", "pick"), ("paramsJson", "{\"space\":\"1\"}")));
            var after = await Call("get_rules_document");
            Assert.Equal(2, (double)after["session"]!["resources"]!["seat_east"]!["space"]!);
            Assert.Equal("act", (string?)after["session"]!["phase"]);
            Ok(await Call("reset_game"));
        }

        [Fact]
        public async Task Lua_SetChecked_TriedOnACopy_AndCalledByTheRules()
        {
            Ok(await Call("new_game", ("id", "scripted"), ("name", "Scripted"), ("players", 2)));
            var broken = await Call("set_script", ("target", "global"), ("code", "function fight(ctx"));
            Assert.False((bool)broken["ok"]!);
            Assert.Equal("script_failed", (string?)broken["errors"]![0]!["code"]);

            Ok(await Call("set_script", ("target", "global"), ("code",
                "function fight(ctx)\n  if Game.getResource(ctx.actor, 'gold') < ctx.args.cost then return 'Not enough gold.' end\n" +
                "  Game.addResource(ctx.actor, 'gold', -ctx.args.cost)\n  Game.addResource(ctx.actor, 'wins', 1)\n  print('fought')\nend")));
            Ok(await Call("add_resource", ("resourceJson", "{\"id\":\"gold\",\"initial\":3}")));
            Ok(await Call("add_resource", ("resourceJson", "{\"id\":\"wins\",\"initial\":0}")));
            Ok(await Call("add_action", ("actionJson", "{\"id\":\"fight\",\"actor\":\"current\",\"effects\":[{\"lua\":{\"call\":\"fight\",\"args\":{\"cost\":2}}},{\"endTurn\":{}}]}")));
            Ok(await Call("add_phase", ("phaseJson", "{\"id\":\"main\",\"actions\":[\"fight\"],\"advanceWhen\":{\"currentPlayerEnds\":true}}")));
            Ok(await Call("validate_rules"));

            // Tried on a copy before the game starts: the setup has no session, so the function reports no gold.
            var tried = await Call("run_lua", ("code", "return 1 + 2"));
            Ok(tried);
            Assert.Equal("3", (string?)tried["returned"]);

            Ok(await Call("start_game", ("seed", 1L)));
            var copy = await Call("run_lua", ("code", "fight({actor='seat_south', params={}, args={cost=2}}) return Game.getResource('seat_south','gold')"));
            Assert.Equal("1", (string?)copy["returned"]);
            Assert.Contains(copy["printed"]!, l => ((string?)l)!.Contains("fought"));
            // The real game did not change.
            Assert.Equal(3, (double)(await Call("get_rules_document"))["session"]!["resources"]!["seat_south"]!["gold"]!);

            var played = await Call("perform_action", ("seat", "seat_south"), ("action", "fight"));
            Ok(played);
            Assert.Contains(played["messages"]!, l => ((string?)l)!.Contains("fought"));
            var session = (await Call("get_rules_document"))["session"]!;
            Assert.Equal(1, (double)session["resources"]!["seat_south"]!["gold"]!);
            Assert.Equal(1, (double)session["resources"]!["seat_south"]!["wins"]!);

            // seat_south's second fight finds 1 gold for a cost of 2: refused, and rolled back.
            Ok(await Call("perform_action", ("seat", "seat_north"), ("action", "fight")));
            var refused = await Call("perform_action", ("seat", "seat_south"), ("action", "fight"));
            Assert.False((bool)refused["ok"]!);
            Assert.Equal("Not enough gold.", (string?)refused["errors"]![0]!["message"]);
            Ok(await Call("reset_game"));
        }

        [Fact]
        public async Task CreateSupply_MakesBanksBagsAndPilesOfPieces()
        {
            Ok(await Call("new_game", ("id", "pieces"), ("name", "Pieces"), ("players", 2)));
            Ok(await Call("create_supply", ("id", "gold"), ("piece", "coin"), ("color", "gold"), ("mode", "infinite"), ("name", "Oro"), ("x", 0.5f)));
            Ok(await Call("create_supply", ("id", "mana"), ("piece", "bead"), ("color", "blue"), ("mode", "bag"), ("count", 6)));
            Ok(await Call("create_supply", ("id", "gems"), ("piece", "gem"), ("color", "[0.6,0.2,0.8]"), ("mode", "pile"), ("count", 3), ("tags", "gems:seat_south")));
            var doc = await Call("get_rules_document");
            var objects = doc["objects"]!;
            Assert.Equal("InfiniteBag", (string?)objects.First(o => (string?)o["id"] == "gold")["type"]);
            Assert.Single(objects, o => (string?)o["container"] == "gold");
            Assert.Equal(6, objects.Count(o => (string?)o["container"] == "mana"));
            Assert.Equal(3, objects.Count(o => ((string?)o["id"])!.StartsWith("gems")));
            Assert.Contains(doc["assets"]!, a => (string?)a["path"] == "builtin:bead");
            Ok(await Call("validate_rules"));
            var bad = await Call("create_supply", ("id", "x"), ("piece", "spaceship"));
            Assert.Equal("invalid_value", (string?)bad["errors"]![0]!["code"]);
        }

        [Fact]
        public async Task SetNotebook_PutsTheRulesOnTheTable()
        {
            Ok(await Call("new_game", ("id", "booked"), ("name", "Booked"), ("players", 2)));
            Ok(await Call("set_notebook", ("id", "rules_turn"), ("title", "El turno"), ("text", "1. Revela una carta de día/noche.")));
            Ok(await Call("set_notebook", ("id", "secret"), ("title", "Your mission"), ("text", "Win quietly."), ("seat", "seat_north")));
            var notebook = (await Call("get_rules_document"))["notebook"]!;
            Assert.Equal(2, notebook.Count());
            Assert.Equal("blue", (string?)notebook.First(p => (string?)p["id"] == "secret")["color"]);
            Ok(await Call("set_notebook", ("id", "secret"), ("title", "x"), ("text", "")));
            Assert.Single((await Call("get_rules_document"))["notebook"]!);
        }

        [Fact]
        public async Task TableAction_DoesEveryPhysicalAction()
        {
            Ok(await Call("new_game", ("id", "hands"), ("name", "Hands"), ("players", 2)));
            Ok(await Call("create_deck", ("id", "deck"), ("cards", "standard52")));
            Ok(await Call("place_object", ("objectJson", "{\"id\":\"d6\",\"type\":\"Die\",\"transform\":{\"position\":[0.3,0,0]}}")));

            async Task<JObject> Act(string action, string target, double? value = null, string? other = null, float? x = null, float? z = null)
            {
                var reply = await Call("table_action", ("action", action), ("target", target), ("value", value), ("other", other), ("x", x), ("z", z));
                Ok(reply);
                return reply;
            }
            int Cards(JObject reply, string id) => (int)reply["state"]!["objects"]!.First(o => (string?)o["id"] == id)["cards"]!;

            Assert.Equal(26, Cards(await Act("cut", "deck"), "deck"));
            var split = await Act("split", "deck", value: 2);
            Assert.Equal(13, Cards(split, "deck"));
            await Act("shuffle", "deck");
            await Act("flip", "deck");
            await Act("rotate", "deck", value: 90);
            await Act("move", "deck", x: -0.4f, z: 0.1f);
            var taken = await Act("take", "deck", x: 0.1f, z: 0.1f);
            var card = (string)taken["created"]![0]!;
            await Act("insert", card, value: 1, other: "deck");
            await Act("spread", "deck-1");
            await Act("roll", "d6");
            await Act("set_die_value", "d6", value: 4);
            await Act("lock", "d6");
            await Act("unlock", "d6");
            await Act("scale", "d6", value: 1.5);
            await Act("rename", "d6", other: "Big die");
            var clone = await Act("clone", "d6", x: 0.5f, z: 0.2f);
            await Act("delete", (string)clone["created"]![0]!);
            var bad = await Call("table_action", ("action", "juggle"), ("target", "d6"));
            Assert.Equal("unknown_command", (string?)bad["errors"]![0]!["code"]);
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

        /// <summary>
        /// A game kept in a project folder is not in the Games tab: save_game and load_game say so, and add_to_games copies
        /// it with its images into the Games folder, where later edits are saved too.
        /// </summary>
        [Fact]
        public async Task AGameElsewhere_IsPutInTheGamesTab_WithItsFiles()
        {
            var project = Path.Combine(Path.GetTempPath(), $"tw-project-{Guid.NewGuid():N}");
            Directory.CreateDirectory(project);
            try
            {
                var art = Path.Combine(project, "art.png");
                File.WriteAllBytes(art, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="));
                Ok(await Call("new_game", ("id", "far_away"), ("name", "Far Away"), ("players", 2)));
                var saved = await Call("save_game", ("path", Path.Combine(project, "far-away.json")));
                Ok(saved);
                Assert.Contains("add_to_games", (string?)saved["gamesTab"]);
                Ok(await Call("add_asset", ("id", "art"), ("path", art)));
                Assert.Contains("add_to_games", (string?)(await Call("load_game", ("path", Path.Combine(project, "far-away.json"))))["gamesTab"]);

                var added = await Call("add_to_games");
                Ok(added);
                var table = Path.Combine(_server.GamesFolder, "Far Away", "table.json");
                Assert.Equal(Path.GetFullPath(table), (string?)added["path"]);
                Assert.True(File.Exists(Path.Combine(_server.GamesFolder, "Far Away", "art.png")), "its image goes with it");
                Assert.Null((string?)(await Call("save_game"))["gamesTab"]);

                // Edits now land in the copy; adding again updates the same folder.
                Ok(await Call("create_deck", ("id", "late_deck")));
                Assert.Contains("late_deck", File.ReadAllText(table));
                Assert.Equal(Path.GetFullPath(table), (string?)(await Call("add_to_games"))["path"]);
            }
            finally
            {
                Directory.Delete(project, recursive: true);
            }
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
