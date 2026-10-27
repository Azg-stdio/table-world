using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using ModelContextProtocol.Protocol;
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

        [McpServerTool(Name = "new_game"), Description("Start a new game document: name, player seats with a hand zone each, and the builtin standard deck images. Up to 4 seats are seat_south, seat_north, seat_east, seat_west (hands hand_<side>) on a 1.6 x 1.0 m table; 5 to 12 seats are seat_1..seat_N clockwise from the south side (hands hand_<n>, seat list order is turn order) on a table that grows to fit them. get_table_state shows where each hand lies. Make one table for every player count: players = the most players, minPlayers = the fewest; start_game then picks who plays. People beyond the seats can still join a session and watch. Replaces the current document.")]
        public static string NewGame(GameSession session,
            [Description("Short id, lowercase, e.g. 'war'.")] string id,
            [Description("Display name.")] string name,
            [Description("Number of seats to create (1-12).")] int players = 2,
            [Description("Minimum players the game supports.")] int? minPlayers = null,
            [Description("Maximum players the game supports.")] int? maxPlayers = null,
            [Description("One-paragraph summary of the rules.")] string? description = null,
            [Description("Dice for each seat in its colour (d6, tagged owner:<seat>), in front of its hand; 0 for none.")] int dicePerSeat = 0,
            [Description("Optional names players read for the seats, comma-separated in seat order (e.g. 'Hunter, Mage, Thief'); a seat without one is called by its colour.")] string? seatNames = null) =>
            session.Locked(() => Reply(session.NewGame(id, name, players, minPlayers ?? players, maxPlayers ?? players, description, dicePerSeat,
                seatNames?.Split(',').Select(n => n.Trim()).ToArray()), session));

        [McpServerTool(Name = "set_messages"), Description("The game's own words for the lines the engine writes in the log, e.g. in the game's language: {\"win\":\"¡{seat} gana!\",\"choseInSecret\":\"{seat} eligió en secreto.\",\"chose\":\"{seat} eligió {option}.\"}. Keys: win, choseInSecret, chose ({seat}, {action}, {option}, {choice}), blindRound, reveal ({count}), shows ({card}). A key left out keeps the English line. Option labels are set on choice params (\"labels\": {\"dungeon\":\"Mazmorra\"}).")]
        public static string SetMessages(GameSession session, [Description("JSON object: message key to text.")] string messagesJson) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var game = doc["game"] as JObject ?? new JObject();
                game["messages"] = GameSession.ParseObject(messagesJson, "messagesJson");
                doc["game"] = game;
            }), session));

        [McpServerTool(Name = "set_roles"), Description("Hidden roles (Avalon, Werewolf, Wink Murder), dealt at random when the game starts, each player told privately who they are. JSON: {\"teams\":[{id,name}], \"list\":[{id, name, team, count (number, default 1, or \"fill\" for the seats left in its team), optional (the host ticks it at start), description}], \"byPlayers\":{\"5\":{\"good\":3,\"evil\":2}} (team sizes per player count), \"knows\":[{role or team that sees, sees:{team, roles, except}, as: team (default) | role | oneOf}]}. A role's own knows rules replace its team's (Oberon: {\"role\":\"oberon\",\"sees\":{}}). Roles with a set (\"set\":\"objective\") deal another hidden trait beside the role. Rules read roles with $actor.role / $seat.<id>.team / $actor.role.<set>; effects revealRole {seat, to, teamOnly, set}, setRole {seat, role, tell}, swapRoles {a, b, tell}, setTeam {seat, team, tell}; to is a seat, team:<id>, role:<id> or all, and message takes to as well for a private line. Ask the host which optional roles exist and who knows whom when the rulebook is unclear.")]
        public static string SetRoles(GameSession session, [Description("The roles JSON object; empty or '{}' removes them.")] string rolesJson) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var roles = string.IsNullOrWhiteSpace(rolesJson) ? new JObject() : GameSession.ParseObject(rolesJson, "rolesJson");
                if (roles.Count == 0)
                    doc.Remove("roles");
                else
                    doc["roles"] = roles;
            }), session));

        [McpServerTool(Name = "create_deck"), Description("Create a deck of cards on the table. cards: 'standard52' or 'standard54' (with jokers) for the builtin French deck (card meta: rank 2-14 aces high, suit, color), or a JSON array of cards {id, name, faceIndex, meta}; for your own art, add_asset a sprite sheet (columns x rows) and pass it as faceAsset, faceIndex counting left to right, top to bottom. Tag a deck '<role>:<seatId>' (e.g. 'deck:seat_south') so rules can say $actor.deck.")]
        public static string CreateDeck(GameSession session,
            [Description("Deck id.")] string id,
            [Description("'standard52', 'standard54', or a JSON array of cards.")] string cards = "standard52",
            [Description("Display name.")] string? name = null,
            [Description("Position on the table in metres; the table is 1.6 x 1.0, centre 0,0, south seat at z = -0.5.")] float x = 0,
            [Description("Position z in metres.")] float z = 0,
            [Description("true to place it face up (discard piles), false for a face-down draw pile.")] bool faceUp = false,
            [Description("Comma-separated tags, e.g. 'deck:seat_south'.")] string? tags = null,
            [Description("Image asset (from add_asset) the cards' faceIndex points into; default the builtin French deck.")] string? faceAsset = null,
            [Description("Image asset for the card backs; default the builtin blue back.")] string? backAsset = null) =>
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
                    ["id"] = id, ["type"] = "Deck", ["faceAsset"] = faceAsset ?? "std_deck", ["backAsset"] = backAsset ?? "std_back",
                    ["transform"] = Transform(x, z, faceUp ? 0 : 180), ["cards"] = cardArray,
                };
                if (name != null) deck["name"] = name;
                if (Tags(tags) is { } t) deck["tags"] = t;
                Upsert(doc, "objects", deck);
            }), session));

        [McpServerTool(Name = "create_text_deck"), Description("Create a deck of text cards: no images needed, the game prints each card's words (title, kind line, body, footer, a corner value, optional art image) and draws the back from the style, with backLabel naming the pile (e.g. 'EQUIPMENT'). cards: JSON array of {id?, title, kind?, body?, footer?, corner?, art? (asset id), artIndex?, count? (copies, default 1), meta?, tags?, style?}; copies get ids <id>_2, <id>_3... style: {paper, ink, accent, backPaper, backInk: [r,g,b] 0-1, backLabel}; a card's own style overrides the deck's (e.g. dark night cards in a day/night deck). Card name is the title and description the body, so tooltips and search show them. Ask the host how each deck should look when the rulebook does not say.")]
        public static string CreateTextDeck(GameSession session,
            [Description("Deck id.")] string id,
            [Description("JSON array of cards.")] string cards,
            [Description("Display name.")] string? name = null,
            [Description("JSON style for every card: {\"paper\":[r,g,b],\"ink\":[r,g,b],\"accent\":[r,g,b],\"backPaper\":[r,g,b],\"backInk\":[r,g,b],\"backLabel\":\"EQUIPMENT\"}.")] string? style = null,
            [Description("Image asset for the backs instead of the drawn back.")] string? backAsset = null,
            [Description("Position x in metres (table centre is 0).")] float x = 0,
            [Description("Position z in metres.")] float z = 0,
            [Description("true to place it face up.")] bool faceUp = false,
            [Description("Comma-separated tags, e.g. 'deck:seat_south'.")] string? tags = null) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var entries = new JArray();
                var used = new HashSet<string>();
                var source = GameSession.ParseArray(cards, "cards");
                for (var i = 0; i < source.Count; i++)
                {
                    if (source[i] is not JObject card)
                        throw new EditException(new ValidationError($"cards[{i}]", ErrorCodes.InvalidValue, "Each card is a JSON object."));
                    var title = (string?)card["title"];
                    var baseId = (string?)card["id"] ?? GameSession.Slug(title ?? $"{id}_card_{i + 1}");
                    var copies = (int?)card["count"] ?? 1;
                    if (copies < 1 || copies > 100)
                        throw new EditException(new ValidationError($"cards[{i}].count", ErrorCodes.InvalidValue, "count is 1 to 100 copies."));
                    var text = new JObject();
                    foreach (var field in new[] { "title", "kind", "body", "footer", "corner", "art", "artIndex" })
                        if (card[field] is { } value)
                            text[field] = value;
                    for (var copy = 1; copy <= copies; copy++)
                    {
                        var cardId = copy == 1 ? baseId : $"{baseId}_{copy}";
                        for (var n = 2; !used.Add(cardId); n++)
                            cardId = $"{baseId}_{copies + n}";
                        var entry = new JObject { ["id"] = cardId, ["text"] = text.DeepClone() };
                        if (title != null) entry["name"] = title;
                        if (card["body"] is { } body) entry["description"] = body;
                        foreach (var field in new[] { "meta", "tags", "style" })
                            if (card[field] is { } value)
                                entry[field] = value.DeepClone();
                        entries.Add(entry);
                    }
                }
                var deck = new JObject
                {
                    ["id"] = id, ["type"] = "Deck", ["faceAsset"] = "", ["backAsset"] = backAsset ?? "",
                    ["transform"] = Transform(x, z, faceUp ? 0 : 180), ["cards"] = entries,
                };
                if (style != null) deck["style"] = GameSession.ParseObject(style, "style");
                if (name != null) deck["name"] = name;
                if (Tags(tags) is { } t) deck["tags"] = t;
                Upsert(doc, "objects", deck);
            }), session));

        [McpServerTool(Name = "set_notebook"), Description("Write a page of the table's notebook (Notes in the top bar), which every player can open while playing: put the rulebook there (one page per chapter reads best: Setup, Turn, Combat...), a quick reference, a scenario. seat makes the page private to that seat. Up to 64 pages of 50 000 characters. Empty text removes the page. For a PDF rulebook use add_asset (type pdf) and place_object {type: Pdf} instead: players click it to read.")]
        public static string SetNotebook(GameSession session,
            [Description("Page id, e.g. 'rules_setup'.")] string id,
            [Description("Tab title, up to 64 characters.")] string title,
            [Description("The page's text; empty removes the page.")] string text,
            [Description("Seat id to make the page private to; empty for everyone.")] string? seat = null) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var pages = doc["notebook"] as JArray ?? new JArray();
                var existing = pages.FirstOrDefault(p => (string?)p["id"] == id);
                if (text.Length == 0)
                {
                    existing?.Remove();
                    doc["notebook"] = pages;
                    return;
                }
                if (title.Length > NoteTab.MaxTitle || text.Length > NoteTab.MaxBody)
                    throw new EditException(new ValidationError("text", ErrorCodes.InvalidValue, $"A page holds a title of up to {NoteTab.MaxTitle} characters and up to {NoteTab.MaxBody:N0} characters of text.", "Split it into several pages."));
                if (existing == null && pages.Count >= NoteTab.MaxTabs)
                    throw new EditException(new ValidationError("id", ErrorCodes.InvalidValue, $"The notebook holds up to {NoteTab.MaxTabs} pages."));
                var color = seat == null ? null : session.Current.Players.Seats.FirstOrDefault(x => x.Id == seat)?.Color
                    ?? throw new EditException(new ValidationError("seat", ErrorCodes.UnknownSeat, $"Seat '{seat}' does not exist."));
                var page = new JObject { ["id"] = id, ["title"] = title, ["body"] = text };
                if (color != null) page["color"] = color;
                if (existing != null) existing.Replace(page);
                else pages.Add(page);
                doc["notebook"] = pages;
            }), session));

        [McpServerTool(Name = "screenshot"), Description("Look at the table in the game window: returns a picture with the HUD. view: 'seat' (as that seat's player sits, default the first seat), 'top' (the whole table from above), 'object' (target up close: check a card's face, a token's size), 'current' (what is on screen). Use it after placing things to check positions, facing, sizes and readability; needs the game window open.")]
        public static IEnumerable<ContentBlock> Screenshot(GameSession session,
            [Description("seat, top, object or current.")] string view = "seat",
            [Description("For view 'seat': the seat id.")] string? seat = null,
            [Description("For view 'object': the object id.")] string? target = null,
            [Description("Picture width in pixels (320-2560).")] int width = 1280)
        {
            var (jpeg, w, h, error) = session.Screenshot(view, seat, target, width);
            if (jpeg == null)
                return new ContentBlock[] { new TextContentBlock { Text = Json(new { ok = false, error }) } };
            return new ContentBlock[]
            {
                new TextContentBlock { Text = Json(new { ok = true, view, seat, target, width = w, height = h }) },
                ImageContentBlock.FromBytes(jpeg, "image/jpeg"),
            };
        }

        [McpServerTool(Name = "set_script"), Description("Set Lua for the table: target 'global' for the global script (rules call its functions with lua effects: {\"lua\":{\"call\":\"fight\",\"args\":{...}}}), or an object id for that object's script (buttons, onDrop...). The Lua API is TTS's (objects, UI, Wait, events) plus Game: Game.getResource(seat,id), Game.setResource/addResource(seat,id,value), Game.getVariable/setVariable(name,value), Game.getCurrentSeat(), Game.getPhase(), Game.getSeats(), Game.endTurn(), Game.endPhase(), Game.gotoPhase(id), Game.win(seat), Game.log(text), Game.perform(seat,action,params), Game.roll(count,sides). A function called by a lua effect gets ctx {actor, params, args, it}; returning false or a reason string refuses the action and rolls it back. The code is checked for syntax errors; try it with run_lua. See resource tableworld://docs/rules-and-scripting.")]
        public static string SetScript(GameSession session,
            [Description("'global' or an object id.")] string target,
            [Description("The Lua source; empty removes it.")] string code) =>
            session.Locked(() =>
            {
                if (code.Length > 0 && Core.Scripting.ScriptHost.CheckSyntax(code) is { } syntax)
                    return Json(new { ok = false, errors = new[] { new { path = "code", code = ErrorCodes.ScriptFailed, message = syntax, suggestion = (string?)"Fix the Lua and send it again." } } });
                return Reply(session.Edit(doc =>
                {
                    if (target == "global")
                    {
                        var scripts = doc["scripts"] as JObject ?? new JObject();
                        if (code.Length == 0) scripts.Remove("global");
                        else scripts["global"] = code;
                        doc["scripts"] = scripts;
                        return;
                    }
                    var obj = (doc["objects"] as JArray)?.FirstOrDefault(o => (string?)o["id"] == target)
                        ?? throw new EditException(new ValidationError("target", ErrorCodes.UnknownObject, $"Object '{target}' does not exist.", "Use 'global' or an object id."));
                    if (code.Length == 0) ((JObject)obj).Remove("script");
                    else obj["script"] = code;
                }), session);
            });

        [McpServerTool(Name = "run_lua"), Description("Try Lua against a copy of the table as it is now (the game in play, or the setup), with the table's scripts loaded: call your functions, read Game.getResource, move objects. Nothing changes in the real game. Returns what the code returned, what it printed, refused commands, and the copy's resources, variables and phase afterwards.")]
        public static string RunLua(GameSession session, [Description("Lua to run, e.g. 'return fight({actor=\"seat_south\", params={}, args={cost=2}})'.")] string code,
            [Description("At most this many printed lines and characters of the returned value come back (the rest is counted), so a big table does not flood the reply.")] int limit = 50) =>
            session.Locked(() =>
            {
                var (error, value, lines, after) = session.TryLua(code);
                limit = System.Math.Max(1, limit);
                var returned = value != null && value.Length > limit * 80 ? value.Substring(0, limit * 80) + $"... ({value.Length - limit * 80} more characters)" : value;
                return Json(new
                {
                    ok = error == null, error, returned, printed = lines.Take(limit),
                    morePrinted = lines.Count > limit ? lines.Count - limit : (int?)null,
                    after = after.Session == null ? null : new { phase = after.Session.Phase, currentSeat = after.Session.CurrentSeat, resources = after.Session.Resources, variables = after.Session.Variables, log = after.Session.Log.TakeLast(10) },
                });
            });

        [McpServerTool(Name = "add_asset"), Description("Add a file the table uses: an image (PNG, JPEG) for cards, tokens, tiles and boards, a sprite sheet of card faces (give columns and rows), an OBJ model, audio (MP3, OGG, WAV) or a PDF. The file is copied next to the game file, so it travels with the game and is sent to players who join; or pass url for a file on the web. Then refer to the id: create_deck faceAsset/backAsset, an object's asset/faceAsset/imageAsset. Replaces an asset with the same id. Images reply with their pixel size.")]
        public static string AddAsset(GameSession session,
            [Description("Asset id, e.g. 'monsters' or 'board'.")] string id,
            [Description("Full path of the file on this computer.")] string? path = null,
            [Description("Web address of the file, instead of path.")] string? url = null,
            [Description("image, model, audio, pdf or assetBundle; default from the file's extension.")] string? type = null,
            [Description("For a sprite sheet: cards per row.")] int? columns = null,
            [Description("For a sprite sheet: rows of cards.")] int? rows = null) =>
            session.Locked(() =>
            {
                string? stored = null;
                var result = session.Edit(doc =>
                {
                    if ((path == null) == (url == null))
                        throw new EditException(new ValidationError("path", ErrorCodes.MissingField, "Give either path or url.", "path is a file on this computer; url a web address."));
                    var asset = new JObject { ["id"] = id };
                    if (path != null)
                    {
                        stored = session.ImportAssetFile(path);
                        asset["path"] = stored;
                    }
                    var extension = System.IO.Path.GetExtension(path ?? url!.Split('?')[0]);
                    if (url != null)
                    {
                        if (!Asset.IsWebAddress(url))
                            throw new EditException(new ValidationError("url", ErrorCodes.InvalidValue, $"'{url}' is not an http or https address."));
                        // Where the package would hold it; each machine fetches the address itself while it does not.
                        var folder = session.FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(session.FilePath) + "-assets" : "assets";
                        asset["path"] = $"{folder}/{id}{(extension.Length > 0 ? extension : ".png")}";
                        asset["url"] = url;
                    }
                    asset["type"] = type ?? (GameSession.AssetExtensions.TryGetValue(extension, out var inferred) ? inferred : "image");
                    if (columns != null) asset["columns"] = columns;
                    if (rows != null) asset["rows"] = rows;
                    Upsert(doc, "assets", asset);
                });
                if (!result.Ok || stored == null)
                    return Reply(result, session);
                var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(session.FilePath!)!, stored);
                var size = GameSession.ImageSize(file);
                return Json(new
                {
                    ok = true, problems = Errors(result.Errors), asset = new { id, path = stored, width = size?.Width, height = size?.Height },
                    cell = size != null && columns > 0 && rows > 0 ? new { width = size.Value.Width / columns, height = size.Value.Height / rows } : null,
                    savedTo = session.FilePath,
                });
            });

        [McpServerTool(Name = "create_supply"), Description("Put physical pieces on the table, so players handle gold, mana and points as pieces instead of reading numbers: piece coin, bead (a sphere: mana, energy, life), gem, cube, pawn or checker, in a colour (gold, silver, a seat colour, or [r,g,b]). mode 'infinite' is a bank that never runs out (players drag pieces out of it), 'bag' a bag holding count pieces, 'pile' count loose pieces. Rules can count pieces in a zone (zoneCount) and Lua with getObjects. Tag pieces '<role>:<seatId>' to give them to a seat.")]
        public static string CreateSupply(GameSession session,
            [Description("Id of the bank, bag, or the first piece of a pile.")] string id,
            [Description("coin, bead, gem, cube, pawn or checker.")] string piece = "coin",
            [Description("gold, silver, bronze, a seat colour (red, blue...), or [r,g,b] 0-1.")] string color = "gold",
            [Description("infinite, bag or pile.")] string mode = "infinite",
            [Description("Pieces in a bag or pile.")] int count = 10,
            [Description("Display name, e.g. 'Gold' or 'Mana'.")] string? name = null,
            [Description("Position x in metres (table centre is 0).")] float x = 0,
            [Description("Position z in metres.")] float z = 0,
            [Description("Comma-separated tags for the pieces.")] string? tags = null) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var pieces = new[] { "coin", "bead", "gem", "cube", "pawn", "checker" };
                if (!pieces.Contains(piece))
                    throw new EditException(new ValidationError("piece", ErrorCodes.InvalidValue, $"Unknown piece '{piece}'.", "Use one of: " + string.Join(", ", pieces) + "."));
                if (mode != "infinite" && mode != "bag" && mode != "pile")
                    throw new EditException(new ValidationError("mode", ErrorCodes.InvalidValue, $"Unknown mode '{mode}'.", "Use infinite, bag or pile."));
                if (count < 1 || count > 200)
                    throw new EditException(new ValidationError("count", ErrorCodes.InvalidValue, "count is 1 to 200."));
                var rgb = color.TrimStart().StartsWith("[") ? GameSession.ParseArray(color, "color").Select(v => (float)v).ToArray() : color.ToLowerInvariant() switch
                {
                    "gold" => new[] { 0.95f, 0.75f, 0.2f },
                    "silver" => new[] { 0.8f, 0.82f, 0.85f },
                    "bronze" => new[] { 0.72f, 0.45f, 0.2f },
                    _ => SeatLayout.Rgb(color),
                };
                var assetId = "piece_" + piece;
                var assets = doc["assets"] as JArray ?? new JArray();
                if (!assets.Any(a => (string?)a["id"] == assetId))
                    assets.Add(new JObject { ["id"] = assetId, ["type"] = "model", ["path"] = "builtin:" + piece });
                doc["assets"] = assets;
                var label = name ?? char.ToUpperInvariant(piece[0]) + piece.Substring(1);
                JObject Piece(string pieceId, float px, float pz, string? container)
                {
                    var o = new JObject
                    {
                        ["id"] = pieceId, ["type"] = "Model", ["name"] = label, ["asset"] = assetId, ["tint"] = new JArray(rgb),
                        ["kind"] = piece == "coin" ? "coin" : piece == "checker" ? "chip" : "generic",
                        ["material"] = piece == "coin" ? "metal" : piece == "bead" || piece == "gem" ? "glass" : "plastic",
                        ["transform"] = new JObject { ["position"] = new JArray(px, 0, pz) },
                    };
                    if (container != null) o["container"] = container;
                    if (Tags(tags) is { } t) o["tags"] = t;
                    return o;
                }
                if (mode == "pile")
                {
                    for (var i = 0; i < count; i++)
                        Upsert(doc, "objects", Piece(i == 0 ? id : $"{id}_{i + 1}", x + (i % 5) * 0.026f, z + (i / 5) * 0.026f, null));
                    return;
                }
                var bag = new JObject
                {
                    ["id"] = id, ["type"] = mode == "infinite" ? "InfiniteBag" : "Bag", ["name"] = mode == "infinite" ? label : label + " bag",
                    ["tint"] = new JArray(rgb), ["transform"] = new JObject { ["position"] = new JArray(x, 0, z) },
                };
                Upsert(doc, "objects", bag);
                for (var i = 0; i < (mode == "infinite" ? 1 : count); i++)
                    Upsert(doc, "objects", Piece($"{id}_piece{(i == 0 ? "" : "_" + (i + 1))}", x, z, id));
            }), session));

        [McpServerTool(Name = "place_object"), Description("Place any table object from JSON: Card, Deck, Token, Tile, Die, Bag, InfiniteBag, Board, Figure, Model, Counter, Clock, Notecard, Calculator (players press its keys or type while it is selected). Example: {\"id\":\"d6\",\"type\":\"Die\",\"faces\":6,\"transform\":{\"position\":[0.3,0,0]}}. With seat, the object goes in front of that seat's hand and faces it (a character card, a player board): inward metres towards the centre, along metres to the player's right; its own rotation then only adds a flip (rotation z 180 = face down). Replaces an object with the same id. Check placements with screenshot. " + Format)]
        public static string PlaceObject(GameSession session, [Description("The object as JSON.")] string objectJson,
            [Description("Seat id to place it for: in front of that seat, facing it.")] string? seat = null,
            [Description("With seat: metres from the hand towards the centre.")] float inward = 0.15f,
            [Description("With seat: metres to the player's right of the hand's middle (negative: left).")] float along = 0f) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var obj = GameSession.ParseObject(objectJson, "objectJson");
                if (seat != null)
                    PlaceForSeat(session, obj, seat, inward, along);
                Upsert(doc, "objects", obj);
            }), session));

        /// <summary>Sets <paramref name="obj"/>'s position in front of <paramref name="seatId"/>'s hand, turned to read upright from that seat.</summary>
        private static void PlaceForSeat(GameSession session, JObject obj, string seatId, float inward, float along)
        {
            var state = session.Current;
            var seatModel = state.Players.Seats.FirstOrDefault(x => x.Id == seatId)
                ?? throw new EditException(new ValidationError("seat", ErrorCodes.UnknownSeat, $"Seat '{seatId}' does not exist.",
                    "Seats: " + string.Join(", ", state.Players.Seats.Select(x => x.Id)) + "."));
            var hand = state.Zones.FirstOrDefault(z => z.Id == seatModel.HandZone)
                ?? throw new EditException(new ValidationError("seat", ErrorCodes.UnknownZone, $"Seat '{seatId}' has no hand zone to place things in front of."));
            var place = new SeatLayout.Place { Position = hand.Transform.Position, Yaw = hand.Transform.Rotation.Y };
            var at = SeatLayout.InFrontOf(place, along, inward);
            var transform = obj["transform"] as JObject ?? new JObject();
            var rotation = transform["rotation"] as JArray;
            var flip = rotation != null && rotation.Count == 3 ? (float)rotation[2] : 0f;
            transform["position"] = new JArray(at.X, 0, at.Z);
            // Images read upright from the south seat at yaw 180 (ADR 0005): a seat's own yaw turns that towards it.
            transform["rotation"] = new JArray(0, Geometry.NormalizeAngle(place.Yaw + 180f), flip);
            obj["transform"] = transform;
        }

        [McpServerTool(Name = "define_zone"), Description("Define a zone from JSON: HandZone (owner seat, hidden from others), HiddenZone, ScriptingZone (an area rules can count, e.g. a battlefield), SnapPoint, LayoutZone (arranges what is dropped in it in a grid: direction rightThenDown|downThenRight|leftThenDown|downThenLeft, maxPerRow, spacing m, facing keep|faceUp|faceDown, manualOnly), RandomizeZone (players shuffle where things in it lie). Example: {\"id\":\"battlefield\",\"type\":\"ScriptingZone\",\"transform\":{\"position\":[0,0.05,0]},\"size\":[0.3,0.1,0.2]}. Any zone can show the total of the dice thrown in it (diceTotal: true; per player for dice tagged owner:<seat>; rules read $diceTotal.<zone> and $diceTotal.<zone>.<seat>). Any zone can carry rules: onEnter / onLeave effects run during a game when a player puts a piece in or takes it out ($it = the piece, $actor = who moved it), so moving a pawn onto a space can be the move itself; a refuse effect ({\"refuse\":{\"reason\":\"...\"}}) undoes the move; inside a blind round they wait for the reveal. Replaces a zone with the same id.")]
        public static string DefineZone(GameSession session, [Description("The zone as JSON.")] string zoneJson) =>
            session.Locked(() => Reply(session.Edit(doc => Upsert(doc, "zones", GameSession.ParseObject(zoneJson, "zoneJson"))), session));

        [McpServerTool(Name = "set_variables"), Description("Declare game variables with their initial values, as a JSON object, e.g. {\"round\":0,\"trump\":\"\"}. Merges with existing variables. secret lists variables only the host's rules read (a hidden victim, a shuffled order): players' machines never receive them.")]
        public static string SetVariables(GameSession session, [Description("JSON object name → initial value.")] string variablesJson,
            [Description("Comma-separated variable names kept secret on the host (hidden roles' games), e.g. 'victim,saved'.")] string? secret = null) =>
            session.Locked(() => Reply(session.Edit(doc =>
            {
                var vars = doc["variables"] as JObject ?? new JObject();
                foreach (var p in GameSession.ParseObject(variablesJson, "variablesJson").Properties())
                    vars[p.Name] = p.Value;
                doc["variables"] = vars;
                var names = (secret ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (names.Count > 0)
                {
                    var list = doc["secretVariables"] as JArray ?? new JArray();
                    foreach (var name in names.Where(n => list.All(t => (string?)t != n)))
                        list.Add(name);
                    doc["secretVariables"] = list;
                }
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

        [McpServerTool(Name = "add_phase"), Description("Add or replace a phase: {\"id\":\"play\",\"name\":\"Play\",\"actions\":[\"play_card\"],\"advanceWhen\":{\"currentPlayerEnds\":true},\"onEnter\":[effects],\"onExit\":[effects]}. advanceWhen takes exactly one of immediate, allPlayersActed, currentPlayerEnds, condition. Appends the phase to the turn if it is not there. A phase may close eyes: \"awake\" (nobody, team:<id>, role:<id>, a seat or $var.<list>) keeps the others asleep (dark screen with \"sleepText\", no actions, no sight or sound of the awake), and \"voice\" (all, awake, team, none) sets who hears whom. " + Format)]
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

        [McpServerTool(Name = "add_action"), Description("Add or replace a player action: {\"id\":\"draw_card\",\"name\":\"Draw\",\"actor\":\"current\",\"params\":[...],\"preconditions\":[conditions],\"effects\":[effects],\"limitPerPhase\":1}. A vote is an action with \"vote\": {options or seats:true, labels, who (all, team:<id>, role:<id>, a seat or $var.<list>), reveal (named|count|result), decide (most|majority|unanimous), otherwise, onResult [effects reading $vote.result / $vote.<option>]} and no effects; players vote with params {\"choice\": ...}. Remember to list it in a phase's actions. " + Format)]
        public static string AddAction(GameSession session, [Description("The action as JSON.")] string actionJson) =>
            session.Locked(() => Reply(session.Edit(doc => Upsert(doc, "actions", GameSession.ParseObject(actionJson, "actionJson"))), session));

        [McpServerTool(Name = "add_event"), Description("Set the effects run on an event: onGameStart (setup: shuffle, deal), onTurnStart, onTurnEnd, onReveal (after a blind round is revealed). Example effects: [{\"shuffle\":{\"object\":\"deck\"}},{\"deal\":{\"from\":\"deck\",\"count\":7,\"toZone\":\"$it.hand\"}}].")]
        public static string AddEvent(GameSession session,
            [Description("onGameStart, onTurnStart, onTurnEnd or onReveal.")] string eventName,
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

        [McpServerTool(Name = "start_game"), Description("Start playing the game: deals the hidden roles (if the game has them), runs onGameStart (shuffle, deal) and the first turn, with the seats that play. A table keeps every seat it has (make a 3-6 player game with 6 seats, once); seats or players picks who plays this time, and empty seats are left out of turns, deals and forEach. The document must validate.")]
        public static string StartGame(GameSession session,
            [Description("Optional seed for a reproducible game.")] long? seed = null,
            [Description("Comma-separated seat ids that play; empty for every seat.")] string? seats = null,
            [Description("How many play, spread evenly around the table (instead of seats).")] int? players = null,
            [Description("Comma-separated optional roles to deal this time (e.g. 'percival,morgana'); the others stay out.")] string? roles = null) =>
            session.Locked(() =>
            {
                if (seed is { } s)
                    session.Seed = (ulong)s;
                var all = session.Current.Players.Seats.Select(x => x.Id).ToList();
                var chosen = players is { } n ? SeatLayout.Spread(all, n)
                    : (seats ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var optional = (roles ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                return PlayReply(session.Start(chosen, optional), session);
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
                if (session.Game == null && !session.Remote)
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
        public static string GetTableState(GameSession session,
            [Description("'full' (default): every object and zone; 'play': only the turn, log, seats' resources and hand sizes, and the legal actions with their first choices.")] string detail = "full") =>
            session.Locked(() => Json(Summary(session, compact: detail == "play")));

        [McpServerTool(Name = "save_game"), Description("Save the game document (with the session if a game is running) to a JSON file.")]
        public static string SaveGame(GameSession session, [Description("File path, e.g. games/my-game.json. Defaults to the file the session was opened with.")] string? path = null) =>
            session.Locked(() =>
            {
                try
                {
                    return Json(new { ok = true, path = session.Save(path), gamesTab = GamesTabHint(session) });
                }
                catch (EditException e)
                {
                    return Json(new { ok = false, errors = Errors(new[] { e.Error }) });
                }
            });

        [McpServerTool(Name = "add_to_games"), Description("Put the game in the player's Games tab: copies it (its setup) with every image and file it uses into the Games folder as '<name>/table.json'. Use it when the game file lives anywhere else (a project folder, a file a build script wrote and load_game opened); the Games tab lists only that folder. Running it again updates the same copy, and later edits in this session are saved to it.")]
        public static string AddToGames(GameSession session, [Description("Name of the folder (and of the entry in the Games tab); defaults to the game's name.")] string? name = null) =>
            session.Locked(() =>
            {
                try
                {
                    return Json(new { ok = true, path = session.AddToGames(name), inGamesTab = true });
                }
                catch (EditException e)
                {
                    return Json(new { ok = false, errors = Errors(new[] { e.Error }) });
                }
            });

        /// <summary>For a game saved where the Games tab does not look, how to put it there (null when it is listed or unknown).</summary>
        internal static string? GamesTabHint(GameSession session) =>
            GamesFolder.Lists(session.FilePath) == false
                ? $"This file is not in the Games tab (it lists {GamesFolder.Known()}). Call add_to_games so the player can open it there."
                : null;

        [McpServerTool(Name = "load_game"), Description("Load a game document (or a saved game in progress) from a JSON file.")]
        public static string LoadGame(GameSession session, [Description("File path.")] string path) =>
            session.Locked(() =>
            {
                if (!File.Exists(path))
                    return Json(new { ok = false, errors = new[] { new { path = "path", code = "file_not_found", message = $"No file at {path}.", suggestion = (string?)null } } });
                var parsed = session.Load(path);
                return Json(new { ok = !parsed.Structural, problems = Errors(parsed.Errors), gamesTab = parsed.Structural ? null : GamesTabHint(session),
                    state = parsed.Structural ? null : Summary(session) });
            });

        // ---- helpers ----------------------------------------------------------------------------------------------

        private static string Reply(EditResult result, GameSession session) => Json(result.Ok
            ? (object)new { ok = true, problems = Errors(result.Errors), hint = result.Errors.Count > 0 ? "Kept. The document is incomplete; fix the problems before start_game." : null, savedTo = session.FilePath }
            : new { ok = false, errors = Errors(result.Errors), hint = "Not applied. " + Format });

        private static string PlayReply(PlayResult result, GameSession session)
        {
            if (!result.Ok)
                return Json(new { ok = false, errors = Errors(result.Errors), stateRestored = result.Result?.StateRestored ?? false });
            return Json(new { ok = true, messages = result.Messages, state = Summary(session, compact: true) });
        }

        /// <summary>Choices listed per legal action in a compact reply; the rest are counted.</summary>
        public const int CompactChoices = 12;

        /// <summary>
        /// The table as the agent reads it. Compact (the reply to a move, or <c>get_table_state detail=play</c>): the turn,
        /// the last lines of the log, each seat's resources and hand size, and the legal actions with their first choices,
        /// without the objects and zones.
        /// </summary>
        internal static object Summary(GameSession session, bool compact = false)
        {
            var state = session.Current;
            var game = session.Game;
            var table = game?.Table ?? new TableEngine(state, 1);
            var session0 = state.Session;
            if (compact)
                return new
                {
                    seats = state.Players.Seats.Select(s => new
                    {
                        s.Id,
                        name = Names.Of(s),
                        handCards = s.HandZone == null ? 0 : table.ObjectsIn(s.HandZone).Count(),
                        resources = session0?.Resources.TryGetValue(s.Id, out var r) == true ? r : null,
                    }),
                    play = session0 == null ? null : new
                    {
                        session0.Turn,
                        session0.Phase,
                        session0.CurrentSeat,
                        session0.Over,
                        session0.Winner,
                        secretsWaiting = session0.Secrets.Count > 0 ? session0.Secrets.Count : (int?)null,
                        // Seat → role: every seat headless; in the game window only what that player may know.
                        roles = session0.Roles.Count > 0 ? session0.Roles : null,
                        log = session0.Log.Skip(System.Math.Max(0, session0.Log.Count - 5)),
                        legalActions = game == null || session0.Over ? null : state.Players.Seats
                            .Select(s => (s.Id, Actions: game.LegalActions(s.Id)))
                            .Where(s => s.Actions.Count > 0)
                            .ToDictionary(s => s.Id, s => s.Actions.Select(a => new
                            {
                                action = a.ActionId,
                                choices = a.Choices.Count == 1 && a.Choices[0].Count == 0 ? null : a.Choices.Take(CompactChoices),
                                moreChoices = a.Choices.Count > CompactChoices ? a.Choices.Count - CompactChoices : (int?)null,
                            })),
                    },
                };
            return new
            {
                game = state.Game == null ? null : new { state.Game.Id, state.Game.Name },
                gameWindow = session.Remote ? "connected: play runs in the game window" : "not connected: playing headless",
                savedTo = session.FilePath,
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
            // Yaw 180: the piece reads upright from the south seat, as TTS draws images (ADR 0005).
            ["rotation"] = new JArray(0, 180, rotationZ),
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
