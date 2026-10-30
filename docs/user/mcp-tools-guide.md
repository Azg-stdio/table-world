# MCP tools by job

Your agent builds and tests games through the tools of the Table World MCP server. This page groups them by job, each
with a short example, so you can follow what your agent is doing or ask it for something specific. Exact parameters
are in the [MCP tools reference](../reference/mcp-tools.md).

The examples build one small game, **High Card Race**:

- Two players take turns drawing a card.
- A 10 or higher scores a point.
- The first player to 3 points wins.

Each example is one tool call: the tool's name, then its arguments. Arguments whose names end in `Json` are JSON text;
they are written as plain JSON here so they are easier to read. The examples run in this order against the real server
in a test (`mcp-server.tests/ToolGuideTests.cs`), so they stay correct.

Every tool answers with JSON. `ok: true` means the change was made. When a tool refuses, it lists `errors`, each with
a path, a code, a message and usually a suggestion. When a change is kept but the game is not complete yet, it lists
`problems` instead.

## 1. Start a game

**`get_rules_reference`**: the rules format, for the agent to read once before writing rules.

```call
get_rules_reference
```

**`new_game`**: a new, empty game with its seats, 1 to 12. Up to four seats are named `seat_south`, `seat_north`,
`seat_east` and `seat_west`, each with a hand zone (`hand_south`, ...), on the 1.6 × 1.0 m table. Five or more are
`seat_1` to `seat_12` in clockwise order from the south side (hands `hand_1`, ...; that order is the turn order), and
the table grows so every hand has room. `shape: round` puts every seat (`seat_1`… clockwise from the south, hands
`hand_1`…) evenly round a round table instead, everyone facing the middle, so everyone sees everyone: good for party
games. Make one table for all player counts: `players` is the most players and
`minPlayers` the fewest; `start_game` then says who plays (`seats`, or `players` to spread a number of players evenly
around the table), and the empty seats sit the game out. In the game window players sit down with the **+** over each
seat, and Start plays the seats they sat at. More people than seats can join a session online: they watch, talk and
take a seat when one frees up. `dicePerSeat` gives each seat its own d6s in its colour, in front of its hand, and
`seatNames` names the seats players read (a seat without one is called by its colour).

```call
new_game {"id": "high-card-race", "name": "High Card Race", "players": 2,
          "description": "Draw a card each turn; a 10 or higher scores. First to 3 points wins."}
```

**`set_messages`**: the game's own words for the lines the engine writes in the log, such as the game's language. A
line left out stays in English. Choice options get their words from `labels` on the parameter
([rules and scripting](rules-and-scripting.md#3-actions)).

```call
set_messages {"messagesJson": {"win": "{seat} takes the race!", "choseInSecret": "{seat} has chosen."}}
```

**`set_translations`** and **`translate_game`**: the game in other languages. Say once which language the game is
written in (`gameLanguage`), then `translate_game` lists every text a language lacks, by path key, with the text as
written; send the translations back in batches until `left` is 0. Players pick the language when the game starts
([languages in the rules format](../rules-schema.md#languages)).

```call
set_translations {"language": "es", "gameLanguage": "en", "translationsJson": {"game.name": "Carrera de cartas"}}
```

```call
translate_game {"language": "es"}
```

**`ask_host`**: when the rulebook does not say something, the agent asks you instead of guessing. The server only
hands the question back; your agent asks you in the chat.

```call
ask_host {"question": "The rules say 'a high card scores'. Which ranks are high?",
          "options": "10 and higher, face cards only, aces only"}
```

## 2. Put things on the table

**`create_deck`**: a deck. `standard52` is the built-in French deck; its cards know their `rank` (2–14, aces high),
`suit` and `color`.

```call
create_deck {"id": "draw", "name": "Draw pile", "cards": "standard52", "x": 0, "z": 0}
```

**`create_text_deck`**: a deck of cards made only of words, for prototyping before there is any art. The game prints
each card: a title band (with an optional value in the corner), a kind line, the rules text, a footer, and optionally
an image from `add_asset`. The back is drawn from the deck's style with a word naming the pile, so players can tell
the decks apart. `count` makes copies; a card's own `style` overrides the deck's.

```call
create_text_deck {"id": "gear", "name": "Equipment", "x": 0.4, "z": 0.2, "style": {"accent": [0.6, 0.4, 0.1], "backLabel": "EQUIPMENT"}, "cards": [{"title": "Sword", "kind": "Equipment", "body": "Attack +1.", "footer": "Price 4", "count": 3}, {"title": "Heavy Shield", "kind": "Equipment", "body": "Attack +X (X = one die of another player).", "corner": "10"}]}
```

**`screenshot`**: the agent's eyes. The game window takes a picture of the table, with its HUD, and the agent sees
it: as a seat's player sits (`view` `seat`), the whole table from above (`top`), one object up close (`object`, with
`target`), or the screen as it is (`current`). Agents use it after placing things, to check that pieces face the
right seat, that cards are readable and that nothing overlaps. It needs the game window open.

```call
screenshot {"view": "top"}
```

**`set_environment`**: the place the table sits in and how the game looks: the room (`gameNight`, `tavern` with its
candles and fire, `karaoke`, `none`), the panels' `theme` (`parchment` for a medieval game), and in `json` the light,
the table's felt and wood, and the room's `look` (vignette, bloom, fog). Pieces keep their true colours in any room.

```call
set_environment {"room": "tavern", "theme": "parchment", "json": {"look": {"vignette": 0.5}}}
```

**`set_notebook`**: a page of the table's notebook, which players open from Notes in the top bar while they play:
the rulebook (a page per chapter), a quick reference, a scenario, or a page private to one seat. A PDF rulebook goes on
the table instead, as a `Pdf` object players click to read.

```call
set_notebook {"id": "rules_turn", "title": "The turn", "text": "1. Reveal a day/night card.
2. Spin the roulette."}
```

**`set_script`**: Lua for the table, for what the rules format cannot say: the global script (`target` `global`),
whose functions rules call with a `lua` effect, or an object's script (buttons, reactions to drops). The code is
checked for syntax errors first. [Rules and scripting](rules-and-scripting.md) explains the `Game` functions.

```call
set_script {"target": "global", "code": "function bonus(ctx)\n  Game.log(ctx.actor .. ' gets a bonus')\nend"}
```

**`run_lua`**: tries Lua against a copy of the table as it is now, with the table's scripts loaded, and shows what it
returned and printed. Nothing changes in the real game, so the agent can test a function before a rule calls it.
`limit` caps how many printed lines come back (50 by default), so a loop over the whole table stays readable.

```call
run_lua {"code": "return #Game.getSeats()"}
```

**`add_asset`**: a file the table uses: card art, a sprite sheet of card faces (give `columns` and `rows`), a board
or token image, a 3D model (GLB or OBJ), music or a PDF. With `path` (a file on your computer) the file is copied into a
`<game>-assets` folder next to the game file, so the game keeps working if you move the original and players who join
receive it; with `url` the game downloads it. Images reply with their size in pixels, and a sheet with the size of one
card, so the agent can size the cards to match. Use the id as a deck's `faceAsset` or `backAsset`, or an object's image.

```call
add_asset {"id": "board_art", "url": "https://example.com/board.png"}
```

**`place_object`**: any other object: tokens, tiles, dice, bags, boards, figures, models. With `seat`, the object goes in
front of that seat's hand and faces that player (`inward` and `along` in metres), so each player's character card or
player board reads upright from their own chair without any rotation maths.

```call
place_object {"objectJson": {"id": "d6", "type": "Die", "faces": 6, "transform": {"position": [0.3, 0, 0]}}}
```

**`create_supply`**: physical pieces, so players handle money and points with their hands: coins, beads (mana,
life), gems, cubes, pawns or checkers, in a colour. A bank that never runs out (`infinite`), a bag of so many, or a loose
pile. Rules count them in zones; tag them `<role>:<seatId>` to give them to a seat.

```call
create_supply {"id": "bank", "piece": "coin", "color": "gold", "mode": "infinite", "name": "Gold", "x": 0.6, "z": 0.3}
```

**`define_zone`**: an area of the table: a hand, a hidden area, an area the rules can count, a snap point, an area
that lays out what is dropped in it, or one that shuffles it.

```call
define_zone {"zoneJson": {"id": "middle", "type": "ScriptingZone", "transform": {"position": [0, 0.05, 0]},
             "size": [0.3, 0.1, 0.2]}}
```

### Music and sounds

The music comes from the player's own music library (packs they imported), never with the game: a game names its
tracks, and friends who lack a pack receive the tracks from the host while playing. Ask the player before importing a
pack: only music they may use (its licence) goes in.

**`import_music`**: a pack from a zip or a folder of MP3, OGG or WAV files into the library, once the player confirms
they may use it. One file per track (the smallest format), loops kept as their own tracks, the licence kept beside them.

```call
import_music {"path": "Fantasy Pack.zip", "rightsConfirmed": true, "source": "https://example.com/the-author"}
```

**`list_music`**: what the library holds, each track with its address, length and tags (ambient, dark, loop, fx...).
Pick ambient or loop tracks for talk and play, dark ones for a tense moment, fx for a stinger.

```call
list_music {"search": "loop"}
```

**`preview_music`**: plays a track in the game window, on this computer only, so the player hears it before you add it.
It needs the game window open.

```call
preview_music {"track": "library:fantasy-pack/ambient-1-loop"}
```

**`set_music`**: the game's playlists and the one it starts with. Phases switch between them with the `music` effect
(`{"music": {"playlist": "ritual", "crossfade": 3}}`) and mark moments with the `sound` effect (`{"sound": {"play":
"gong", "to": "awake"}}`).

```call
set_music {"playlistsJson": "{\"tavern\": [\"library:fantasy-pack/ambient-1-loop\"], \"ritual\": [\"library:fantasy-pack/dark-1\"]}", "start": "tavern", "shuffle": true, "volume": 0.6}
```

## 3. Write the rules

**`set_variables`** and **`add_resource`**: numbers the rules keep. A variable is shared by everyone; a resource has
one value per player.

```call
set_variables {"variablesJson": {"draws": 0}}
```

```call
add_resource {"resourceJson": {"id": "score", "name": "Score", "initial": 0, "min": 0}}
```

**`add_action`**: something a player can do, with its preconditions and effects. This one draws a card and scores it
when it is a 10 or higher.

```call
add_action {"actionJson": {"id": "draw_card", "name": "Draw a card", "actor": "current",
  "preconditions": [{"zoneCount": {"zone": "draw", "op": ">", "value": 0}}],
  "effects": [
    {"if": {"condition": {"compare": {"left": "$obj.draw.top.meta.rank", "op": ">=", "right": 10}},
            "then": [{"setResource": {"player": "$actor", "resource": "score", "delta": 1}}]}},
    {"move": {"from": "draw", "to": "$actor.hand", "faceUp": true}},
    {"endTurn": {}}]}}
```

**`add_phase`**: a step of a turn, and which actions it allows.

```call
add_phase {"phaseJson": {"id": "play", "name": "Play", "actions": ["draw_card"]}}
```

**`set_turn`**: the order of the phases, the direction of play and who starts.

```call
set_turn {"phases": "play", "order": "clockwise", "startingPlayer": "first"}
```

**`add_event`**: what happens at the start of the game and of each turn, and at the end of each turn.

```call
add_event {"eventName": "onGameStart", "effectsJson": [{"shuffle": {"object": "draw"}}]}
```

```call
add_event {"eventName": "onTurnEnd", "effectsJson": [{"setVariable": {"name": "draws", "delta": 1}}]}
```

**`set_win_condition`**: how the game ends. `$player` is each seat in turn.

```call
set_win_condition {"winConditionJson": {"when": {"resource": {"player": "$player", "resource": "score", "op": ">=", "value": 3}},
                   "winner": "$player", "checkAfter": "anyCommand"}}
```

**`set_roles`**: secret roles dealt when the game starts (Avalon, Werewolf). Each player is told only their own, and
rules read it as `$actor.role`. `count` is a number of seats or `"fill"` for the rest; `knows` says who sees whom
([rules and scripting](rules-and-scripting.md#3-actions), "Hidden roles").

```call
set_roles {"rolesJson": {"teams": [{"id": "racers", "name": "Racers"}],
  "list": [{"id": "hare", "name": "Hare", "team": "racers"}, {"id": "tortoise", "name": "Tortoise", "team": "racers", "count": "fill"}]}}
```

## 4. Check and test

**`validate_rules`**: checks the whole game. The agent runs it until it reports no problems. A problem looks like this:

```json
{ "path": "actions[0].effects[0].shufle", "code": "unknown_effect", "message": "Unknown effect 'shufle'.", "suggestion": "Did you mean 'shuffle'?" }
```

```call
validate_rules
```

**`start_game`**: starts playing: shuffles, deals and begins the first turn. A seed makes the shuffle repeatable.

```call
start_game {"seed": 7}
```

**`get_table_state`**: what is on the table, whose turn it is, and every legal action for every seat with its
choices. Your agent reads it before acting, and so can you. `detail: "play"` leaves out the objects and zones and
keeps the turn, the last log lines, each seat's resources and hand size, and the legal actions with their first
choices; `perform_action` replies in that short form.

```call
get_table_state
```

**`perform_action`**: plays a legal action as a seat, the way a HUD button does. Actions with choices take them in
`paramsJson`, e.g. `{"card": "ace_spades"}`.

```call
perform_action {"seat": "seat_south", "action": "draw_card"}
```

**`table_action`**: what hands do on the table, outside the rules:

- flip, rotate, move, lock and unlock;
- shuffle, roll and set a die's value;
- cut, split, spread, take, insert and group;
- delete, clone, change state, scale and rename;
- lay out a zone, or randomize one.

```call
table_action {"action": "roll", "target": "d6"}
```

**`get_log`**: reads what happened, one line per event. `source: "game"` is the game window's journal of the session:
every command players and rules did, or refused with the reason, the rules' lines, turns, moves the rules made by
themselves, Lua prints, warnings and errors. `source: "agent"` is the server's own: every tool call with its arguments,
result and time. When a move is refused or something looks wrong, read the refusals and errors; to follow a playtest,
pass the last `seq` you saw as `after`.

```call
get_log {"source": "agent", "kinds": "refused,error", "max": 20}
```

**`reset_game`**: stops the game and goes back to the setup. Rules can only be edited while no game is running.

```call
reset_game
```

## 5. Change and tidy up

**`deal`**: deals cards into hands straight away, outside the rules (for free-play tables).

```call
deal {"deck": "draw", "count": 2}
```

**`remove`**: removes something by id: an object, zone, phase, action, resource, variable or event.

```call
remove {"collection": "objects", "id": "d6"}
```

**`get_rules_document`**: the whole game as JSON, the same as the saved file.

```call
get_rules_document
```

**`set_rules_document`**: replaces the whole game with a JSON document, for large edits. This one starts again from
a blank table.

```call
set_rules_document {"documentJson": {"schemaVersion": "0.1",
  "game": {"id": "blank", "name": "Blank table", "minPlayers": 1, "maxPlayers": 4}}}
```

## 6. Save and load

**`save_game`**: writes the game to a file. A running game is saved with its turn, scores and shuffle, so it resumes
exactly. Started with `--file`, the server also saves after every change.

```call
save_game {"path": "games/high-card-race.json"}
```

**`load_game`**: opens a game file, or a saved game in progress.

```call
load_game {"path": "games/high-card-race.json"}
```

The game window's Games tab lists only the player's Games folder (and the samples). `save_game` and `load_game` say
when the file is somewhere else, such as a project folder or a file a build script wrote.

**`add_to_games`**: copies the game, with every image and file it uses, into the Games folder as `<name>/table.json`,
so it shows in the Games tab. Running it again updates the same copy, and later edits in the session are saved there.

```call
add_to_games {"name": "High Card Race"}
```
