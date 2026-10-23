using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp
{
    /// <summary>
    /// The board-game vocabulary an agent uses to build and run a game (CLAUDE.md §3: never Unity terms). Every tool
    /// returns JSON: <c>ok</c>, plus <c>errors</c> (path, code, message, suggestion) when refused, or <c>problems</c> when an
    /// edit was kept but the document is still incomplete.
    /// </summary>
    [McpServerToolType]
    public static class GameTools
    {
        private const string Format = "See get_rules_reference for the format.";

        // ---- authoring ----------------------------------------------------------------------------------------------

        [McpServerTool(Name = "new_game"), Description("Start a new game document: name, player seats (seat_south, seat_north, seat_east, seat_west, each with a hand zone hand_<side>), and the builtin standard deck images. Replaces the current document.")]
        public static string NewGame(GameSession session,
            [Description("Short id, lowercase, e.g. 'war'.")] string id,
            [Description("Display name.")] string name,
            [Description("Number of seats to create (1-4).")] int players = 2,
            [Description("Minimum players the game supports.")] int? minPlayers = null,
            [Description("Maximum players the game supports.")] int? maxPlayers = null,
            [Description("One-paragraph summary of the rules.")] string? description = null) =>
            session.Locked(() => Reply(session.NewGame(id, name, players, minPlayers ?? players, maxPlayers ?? players, description), session));

        [McpServerTool(Name = "create_deck"), Description("Create a deck of cards on the table. cards: 'standard52' or 'standard54' (with jokers) for the builtin French deck (card meta: rank 2-14 aces high, suit, color), or a JSON array of cards {id, name, faceIndex, meta}. Tag a deck '<role>:<seatId>' (e.g. 'deck:seat_south') so rules can say $actor.deck.")]
        public static string CreateDeck(GameSession session,
            [Description("Deck id.")] string id,
            [Description("'standard52', 'standard54', or a JSON array of cards.")] string cards = "standard52",
            [Description("Display name.")] string? name = null,
            [Description("Position on the table in metres; the table is 1.6 x 1.0, centre 0,0, south seat at z = -0.5.")] float x = 0,
            [Description("Position z in metres.")] float z = 0,
            [Description("true to place it face up (discard piles), false for a face-down draw pile.")] bool faceUp = false,
            [Description("Comma-separated tags, e.g. 'deck:seat_south'.")] string? tags = null) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var cardArray = cards switch
                {
                    "standard52" => GameSession.StandardCards(jokers: false),
                    "standard54" => GameSession.StandardCards(jokers: true),
                    _ => GameSession.ParseArray(cards, "cards"),
                };
                var deck = new JObject
                {
                    ["id"] = id, ["type"] = "Deck", ["faceAsset"] = "std_deck", ["backAsset"] = "std_back",
                    ["transform"] = Transform(x, z, faceUp ? 0 : 180), ["cards"] = cardArray,
                };
                if (name != null) deck["name"] = name;
                if (Tags(tags) is { } t) deck["tags"] = t;
                Upsert(doc, "objects", deck);
            }), session));

        [McpServerTool(Name = "place_object"), Description("Place any table object from JSON: Card, Deck, Token, Tile, Die, Bag, InfiniteBag, Board, Figure, Model. Example: {\"id\":\"d6\",\"type\":\"Die\",\"faces\":6,\"transform\":{\"position\":[0.3,0,0]}}. Replaces an object with the same id. " + Format)]
        public static string PlaceObject(GameSession session, [Description("The object as JSON.")] string objectJson) =>
            session.Locked(() => Reply(session.Edit(doc => Upsert(doc, "objects", GameSession.ParseObject(objectJson, "objectJson"))), session));

        [McpServerTool(Name = "define_zone"), Description("Define a zone from JSON: HandZone (owner seat, hidden from others), HiddenZone, ScriptingZone (an area rules can count, e.g. a battlefield), SnapPoint. Example: {\"id\":\"battlefield\",\"type\":\"ScriptingZone\",\"transform\":{\"position\":[0,0.05,0]},\"size\":[0.3,0.1,0.2]}. Replaces a zone with the same id.")]
        public static string DefineZone(GameSession session, [Description("The zone as JSON.")] string zoneJson) =>
            session.Locked(() => Reply(session.Edit(doc => Upsert(doc, "zones", GameSession.ParseObject(zoneJson, "zoneJson"))), session));

        [McpServerTool(Name = "set_variables"), Description("Declare game variables with their initial values, as a JSON object, e.g. {\"round\":0,\"trump\":\"\"}. Merges with existing variables.")]
        public static string SetVariables(GameSession session, [Description("JSON object name → initial value.")] string variablesJson) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var vars = doc["variables"] as JObject ?? new JObject();
                foreach (var p in GameSession.ParseObject(variablesJson, "variablesJson").Properties())
                    vars[p.Name] = p.Value;
                doc["variables"] = vars;
            }), session));

        [McpServerTool(Name = "add_resource"), Description("Add a per-player resource such as score or coins: {\"id\":\"score\",\"name\":\"Score\",\"initial\":0,\"min\":0}.")]
        public static string AddResource(GameSession session, [Description("The resource as JSON.")] string resourceJson) =>
            session.Locked(() => Reply(session.Edit(doc => Upsert(doc, "resources", GameSession.ParseObject(resourceJson, "resourceJson"))), session));

        [McpServerTool(Name = "set_turn"), Description("Set the turn structure: order ('clockwise'/'counterclockwise'), startingPlayer (seat id, 'first' or 'random'), and the phase ids played each turn, in order.")]
        public static string SetTurn(GameSession session,
            [Description("Comma-separated phase ids in order, e.g. 'draw,play,discard'.")] string phases,
            [Description("'clockwise' or 'counterclockwise'.")] string order = "clockwise",
            [Description("Seat id, 'first' or 'random'.")] string startingPlayer = "first") =>
            session.Locked(() => Reply(session.Edit(doc => doc["turn"] = new JObject
            {
                ["order"] = order, ["startingPlayer"] = startingPlayer,
                ["phases"] = new JArray(phases.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0)),
            }), session));

        [McpServerTool(Name = "add_phase"), Description("Add or replace a phase: {\"id\":\"play\",\"name\":\"Play\",\"actions\":[\"play_card\"],\"advanceWhen\":{\"currentPlayerEnds\":true},\"onEnter\":[effects],\"onExit\":[effects]}. advanceWhen takes exactly one of immediate, allPlayersActed, currentPlayerEnds, condition. Appends the phase to the turn if it is not there. " + Format)]
        public static string AddPhase(GameSession session, [Description("The phase as JSON.")] string phaseJson) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var phase = GameSession.ParseObject(phaseJson, "phaseJson");
                Upsert(doc, "phases", phase);
                var turn = doc["turn"] as JObject ?? new JObject { ["phases"] = new JArray() };
                var list = turn["phases"] as JArray ?? new JArray();
                if (!list.Any(p => (string?)p == (string?)phase["id"]))
                    list.Add(phase["id"]!);
                turn["phases"] = list;
                doc["turn"] = turn;
            }), session));

        [McpServerTool(Name = "add_action"), Description("Add or replace a player action: {\"id\":\"draw_card\",\"name\":\"Draw\",\"actor\":\"current\",\"params\":[...],\"preconditions\":[conditions],\"effects\":[effects],\"limitPerPhase\":1}. Remember to list it in a phase's actions. " + Format)]
        public static string AddAction(GameSession session, [Description("The action as JSON.")] string actionJson) =>
            session.Locked(() => Reply(session.Edit(doc => Upsert(doc, "actions", GameSession.ParseObject(actionJson, "actionJson"))), session));

        [McpServerTool(Name = "add_event"), Description("Set the effects run on an event: onGameStart (setup: shuffle, deal), onTurnStart, onTurnEnd. Example effects: [{\"shuffle\":{\"object\":\"deck\"}},{\"deal\":{\"from\":\"deck\",\"count\":7,\"toZone\":\"$it.hand\"}}].")]
        public static string AddEvent(GameSession session,
            [Description("onGameStart, onTurnStart or onTurnEnd.")] string eventName,
            [Description("JSON array of effects.")] string effectsJson,
            [Description("true to append to existing effects instead of replacing them.")] bool append = false) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var events = doc["events"] as JObject ?? new JObject();
                var effects = GameSession.ParseArray(effectsJson, "effectsJson");
                if (append && events[eventName] is JArray existing)
                    foreach (var e in effects) existing.Add(e);
                else
                    events[eventName] = effects;
                doc["events"] = events;
            }), session));

        [McpServerTool(Name = "set_win_condition"), Description("Add a win condition: {\"when\":{condition with $player},\"winner\":\"$player\",\"checkAfter\":\"anyCommand\"}. Example: whoever empties their hand wins: {\"when\":{\"zoneCount\":{\"zone\":\"$player.hand\",\"op\":\"==\",\"value\":0}},\"winner\":\"$player\"}.")]
        public static string SetWinCondition(GameSession session,
            [Description("The win condition as JSON.")] string winConditionJson,
            [Description("true to remove the existing win conditions first.")] bool replace = false) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var list = replace ? new JArray() : doc["winConditions"] as JArray ?? new JArray();
                list.Add(GameSession.ParseObject(winConditionJson, "winConditionJson"));
                doc["winConditions"] = list;
            }), session));

        [McpServerTool(Name = "remove"), Description("Remove something by id from a collection: objects, zones, phases, actions, resources, or a variable, or an event by name.")]
        public static string Remove(GameSession session,
            [Description("objects, zones, phases, actions, resources, variables or events.")] string collection,
            [Description("The id (or variable/event name).")] string id) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                switch (doc[collection])
                {
                    case JArray array when array.FirstOrDefault(t => (string?)t["id"] == id) is { } item:
                        item.Remove();
                        break;
                    case JObject map when map.Property(id) is { } property:
                        property.Remove();
                        break;
                    default:
                        throw new EditException(new ValidationError(collection, ErrorCodes.UnknownObject, $"Nothing '{id}' in {collection}."));
                }
                if (collection == "phases" && doc["turn"]?["phases"] is JArray phases)
                    phases.FirstOrDefault(p => (string?)p == id)?.Remove();
            }), session));

        [McpServerTool(Name = "validate_rules"), Description("Check the whole game document. Returns every problem with its path, code, message and a suggestion.")]
        public static string ValidateRules(GameSession session) =>
            session.Locked(() => Json(new { ok = session.Validate().Count == 0, problems = Errors(session.Validate()) }));

        [McpServerTool(Name = "get_rules_document"), Description("The complete game document as JSON (table, rules and, during play, the session).")]
        public static string GetRulesDocument(GameSession session) => session.Locked(() => TableJson.Serialize(session.Current));

        [McpServerTool(Name = "set_rules_document"), Description("Replace the whole game document with JSON (for large edits). Malformed documents are rejected with errors.")]
        public static string SetRulesDocument(GameSession session, [Description("The full document as JSON.")] string documentJson) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var replacement = GameSession.ParseObject(documentJson, "documentJson");
                doc.RemoveAll();
                foreach (var p in replacement.Properties().ToList())
                    doc.Add(p.Name, p.Value);
            }), session));

        [McpServerTool(Name = "get_rules_reference"), Description("The rules format reference: objects, zones, phases, actions, effects, conditions, references ($actor.hand, $param.card.meta.rank, ...), events, win conditions and error codes. Read it before writing rules.")]
        public static string GetRulesReference()
        {
            using var stream = typeof(GameTools).Assembly.GetManifestResourceStream("rules-schema.md")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [McpServerTool(Name = "ask_host"), Description("Use when the rulebook is ambiguous or silent. Do not guess: call this with the question and the options you see, then ask the human host and continue with their answer.")]
        public static string AskHost(
            [Description("The question for the host, quoting the rulebook passage.")] string question,
            [Description("Comma-separated options you would consider.")] string? options = null) =>
            Json(new
            {
                status = "needs_clarification",
                question,
                options = options?.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).ToArray() ?? new string[0],
                instruction = "Ask the human host this question now and wait for the answer before changing the rules.",
            });

        // ---- play -------------------------------------------------------------------------------------------------

        [McpServerTool(Name = "start_game"), Description("Start playing the game: runs onGameStart (shuffle, deal) and the first turn. The document must validate.")]
        public static string StartGame(GameSession session, [Description("Optional seed for a reproducible game.")] long? seed = null) =>
            session.Locked(() =>
            {
                if (seed is { } s)
                    session.Seed = (ulong)s;
                return PlayReply(session.Start(), session);
            });

        [McpServerTool(Name = "perform_action"), Description("Perform a rules action as a seat, e.g. seat 'seat_south', action 'play_card', params {\"card\":\"queen_hearts\"}. get_table_state lists the legal actions and their parameter values.")]
        public static string PerformAction(GameSession session,
            [Description("Seat id performing the action.")] string seat,
            [Description("Action id.")] string action,
            [Description("JSON object of parameter values, e.g. {\"card\":\"ace_spades\"}.")] string? paramsJson = null) =>
            session.Locked(() =>
            {
                var parameters = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(paramsJson))
                {
                    try
                    {
                        foreach (var p in JObject.Parse(paramsJson!).Properties())
                            parameters[p.Name] = p.Value.ToString();
                    }
                    catch (JsonReaderException e)
                    {
                        return Json(new { ok = false, errors = new[] { new { path = "paramsJson", code = ErrorCodes.InvalidJson, message = e.Message, suggestion = (string?)null } } });
                    }
                }
                return PlayReply(session.Execute(new PerformActionCommand { Actor = seat, ActionId = action, Params = parameters }), session);
            });

        [McpServerTool(Name = "deal"), Description("Deal cards from a deck into seats' hands right now (free play, outside the rules), one at a time.")]
        public static string Deal(GameSession session,
            [Description("Deck id.")] string deck,
            [Description("Cards per seat.")] int count = 1,
            [Description("Comma-separated seat ids; empty for every seat.")] string? seats = null) =>
            session.Locked(() =>
            {
                var command = new DealCommand { DeckId = deck, Count = count };
                if (!string.IsNullOrWhiteSpace(seats))
                    command.Seats.AddRange(seats!.Split(',').Select(s => s.Trim()));
                if (session.Game == null)
                {
                    // Setup time: deal on the document itself.
                    var engine = new TableEngine(session.Document, session.Seed);
                    var result = engine.Execute(command);
                    return Json(new { ok = result.Ok, errors = result.Ok ? null : Errors(new[] { result.Error! }), dealt = result.Dealt, state = Summary(session) });
                }
                return PlayReply(session.Execute(command), session);
            });

        [McpServerTool(Name = "reset_game"), Description("Stop the running game and go back to the authored setup.")]
        public static string ResetGame(GameSession session) => session.Locked(() =>
        {
            session.Reset();
            return Json(new { ok = true, state = Summary(session) });
        });

        [McpServerTool(Name = "get_table_state"), Description("What is on the table now: seats with their hands, decks with card counts and visible top cards, zones, and during play the turn, phase, current seat, variables, resources, recent log and every seat's legal actions.")]
        public static string GetTableState(GameSession session) => session.Locked(() => Json(Summary(session)));

        [McpServerTool(Name = "save_game"), Description("Save the game document (with the session if a game is running) to a JSON file.")]
        public static string SaveGame(GameSession session, [Description("File path, e.g. games/my-game.json. Defaults to the file the session was opened with.")] string? path = null) =>
            session.Locked(() =>
            {
                try
                {
                    return Json(new { ok = true, path = session.Save(path) });
                }
                catch (EditException e)
                {
                    return Json(new { ok = false, errors = Errors(new[] { e.Error }) });
                }
            });

        [McpServerTool(Name = "load_game"), Description("Load a game document (or a saved game in progress) from a JSON file.")]
        public static string LoadGame(GameSession session, [Description("File path.")] string path) =>
            session.Locked(() =>
            {
                if (!File.Exists(path))
                    return Json(new { ok = false, errors = new[] { new { path = "path", code = "file_not_found", message = $"No file at {path}.", suggestion = (string?)null } } });
                var parsed = session.Load(path);
                return Json(new { ok = !parsed.Structural, problems = Errors(parsed.Errors), state = parsed.Structural ? null : Summary(session) });
            });

        // ---- helpers ----------------------------------------------------------------------------------------------

        private static string Reply(EditResult result, GameSession session) => Json(result.Ok
            ? (object)new { ok = true, problems = Errors(result.Errors), hint = result.Errors.Count > 0 ? "Kept. The document is incomplete; fix the problems before start_game." : null }
            : new { ok = false, errors = Errors(result.Errors), hint = "Not applied. " + Format });

        private static string PlayReply(PlayResult result, GameSession session)
        {
            if (!result.Ok)
                return Json(new { ok = false, errors = Errors(result.Errors), stateRestored = result.Result?.StateRestored ?? false });
            var messages = result.Result!.Events.OfType<GameMessage>().Select(m => m.Text).ToList();
            return Json(new { ok = true, messages, state = Summary(session) });
        }

        internal static object Summary(GameSession session)
        {
            var state = session.Current;
            var game = session.Game;
            var table = game?.Table ?? new TableEngine(state, 1);
            var session0 = state.Session;
            return new
            {
                game = state.Game == null ? null : new { state.Game.Id, state.Game.Name },
                seats = state.Players.Seats.Select(s => new
                {
                    s.Id,
                    s.Color,
                    hand = s.HandZone == null ? null : table.ObjectsIn(s.HandZone).Select(o => o.Name ?? o.Id).ToArray(),
                    resources = session0?.Resources.TryGetValue(s.Id, out var r) == true ? r : null,
                }),
                objects = state.Objects.Where(o => o.Container == null).Select(o => new
                {
                    o.Id,
                    o.Type,
                    o.Name,
                    cards = o is Deck d ? d.Cards.Count : (int?)null,
                    top = o is Deck { Cards: { Count: > 0 } } deck && deck.IsFaceUp ? deck.Cards[0].Name ?? deck.Cards[0].Id : null,
                    faceUp = o is Card || o is Deck ? o.IsFaceUp : (bool?)null,
                    value = o is Die die ? die.Value : null,
                    tags = o.Tags.Count > 0 ? o.Tags : null,
                    zones = table.ZonesOf(o.Id).Count > 0 ? table.ZonesOf(o.Id) : null,
                }),
                zones = state.Zones.Select(z => new { z.Id, z.Type, objects = table.ObjectsIn(z.Id).Count() }),
                play = session0 == null ? null : new
                {
                    session0.Turn,
                    session0.Phase,
                    session0.CurrentSeat,
                    session0.Over,
                    session0.Winner,
                    variables = session0.Variables,
                    log = session0.Log.Skip(System.Math.Max(0, session0.Log.Count - 10)),
                    legalActions = game == null || session0.Over ? null : state.Players.Seats.ToDictionary(s => s.Id, s => game.LegalActions(s.Id).Select(a => new
                    {
                        action = a.ActionId,
                        a.Name,
                        choices = a.Choices.Count == 1 && a.Choices[0].Count == 0 ? null : a.Choices,
                    })),
                },
            };
        }

        private static object[] Errors(IEnumerable<ValidationError> errors) =>
            errors.Select(e => (object)new { path = e.Path, code = e.Code, message = e.Message, suggestion = e.Suggestion }).ToArray();

        private static JObject Transform(float x, float z, float rotationZ) => new JObject
        {
            ["position"] = new JArray(x, 0, z),
            ["rotation"] = new JArray(0, 0, rotationZ),
        };

        private static JArray? Tags(string? csv) =>
            string.IsNullOrWhiteSpace(csv) ? null : new JArray(csv!.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0));

        /// <summary>Adds the item to the collection, replacing one with the same id.</summary>
        private static void Upsert(JObject doc, string collection, JObject item)
        {
            if (item["id"] == null)
                throw new EditException(new ValidationError(collection, ErrorCodes.MissingField, "The JSON needs an 'id'."));
            var list = doc[collection] as JArray ?? new JArray();
            var existing = list.FirstOrDefault(t => (string?)t["id"] == (string?)item["id"]);
            if (existing != null)
                existing.Replace(item);
            else
                list.Add(item);
            doc[collection] = list;
        }

        internal static string Json(object value) => JsonConvert.SerializeObject(value, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver { NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy() },
        });
    }
}
