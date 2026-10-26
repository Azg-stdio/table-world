# MCP tools

> Generated from the server's tool methods by `mcp-server.tests/DocsReferenceTests.cs`. Do not edit: run
> `TW_UPDATE_DOCS=1 dotnet test mcp-server.tests --filter DocsReference`.

Every tool returns JSON with `ok`, plus `errors` (path, code, message, suggestion) when it refuses, or
`problems` when an edit was kept but the document is still incomplete.

## GameTools

### `add_action`

Add or replace a player action: {"id":"draw_card","name":"Draw","actor":"current","params":[...],"preconditions":[conditions],"effects":[effects],"limitPerPhase":1}. Remember to list it in a phase's actions. See get_rules_reference for the format.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `actionJson` | string | required | The action as JSON. |

### `add_asset`

Add a file the table uses: an image (PNG, JPEG) for cards, tokens, tiles and boards, a sprite sheet of card faces (give columns and rows), an OBJ model, audio (MP3, OGG, WAV) or a PDF. The file is copied next to the game file, so it travels with the game and is sent to players who join; or pass url for a file on the web. Then refer to the id: create_deck faceAsset/backAsset, an object's asset/faceAsset/imageAsset. Replaces an asset with the same id. Images reply with their pixel size.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Asset id, e.g. 'monsters' or 'board'. |
| `path` | string | none | Full path of the file on this computer. |
| `url` | string | none | Web address of the file, instead of path. |
| `type` | string | none | image, model, audio, pdf or assetBundle; default from the file's extension. |
| `columns` | integer | none | For a sprite sheet: cards per row. |
| `rows` | integer | none | For a sprite sheet: rows of cards. |

### `add_event`

Set the effects run on an event: onGameStart (setup: shuffle, deal), onTurnStart, onTurnEnd, onReveal (after a blind round is revealed). Example effects: [{"shuffle":{"object":"deck"}},{"deal":{"from":"deck","count":7,"toZone":"$it.hand"}}].

| Parameter | Type | Default | Description |
|---|---|---|---|
| `eventName` | string | required | onGameStart, onTurnStart, onTurnEnd or onReveal. |
| `effectsJson` | string | required | JSON array of effects. |
| `append` | boolean | `false` | true to append to existing effects instead of replacing them. |

### `add_phase`

Add or replace a phase: {"id":"play","name":"Play","actions":["play_card"],"advanceWhen":{"currentPlayerEnds":true},"onEnter":[effects],"onExit":[effects]}. advanceWhen takes exactly one of immediate, allPlayersActed, currentPlayerEnds, condition. Appends the phase to the turn if it is not there. See get_rules_reference for the format.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `phaseJson` | string | required | The phase as JSON. |

### `add_resource`

Add a per-player resource such as score or coins: {"id":"score","name":"Score","initial":0,"min":0}.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `resourceJson` | string | required | The resource as JSON. |

### `ask_host`

Use when the rulebook is ambiguous or silent. Do not guess: call this with the question and the options you see, then ask the human host and continue with their answer.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `question` | string | required | The question for the host, quoting the rulebook passage. |
| `options` | string | none | Comma-separated options you would consider. |

### `create_deck`

Create a deck of cards on the table. cards: 'standard52' or 'standard54' (with jokers) for the builtin French deck (card meta: rank 2-14 aces high, suit, color), or a JSON array of cards {id, name, faceIndex, meta}; for your own art, add_asset a sprite sheet (columns x rows) and pass it as faceAsset, faceIndex counting left to right, top to bottom. Tag a deck '<role>:<seatId>' (e.g. 'deck:seat_south') so rules can say $actor.deck.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Deck id. |
| `cards` | string | `standard52` | 'standard52', 'standard54', or a JSON array of cards. |
| `name` | string | none | Display name. |
| `x` | number | `0` | Position on the table in metres; the table is 1.6 x 1.0, centre 0,0, south seat at z = -0.5. |
| `z` | number | `0` | Position z in metres. |
| `faceUp` | boolean | `false` | true to place it face up (discard piles), false for a face-down draw pile. |
| `tags` | string | none | Comma-separated tags, e.g. 'deck:seat_south'. |
| `faceAsset` | string | none | Image asset (from add_asset) the cards' faceIndex points into; default the builtin French deck. |
| `backAsset` | string | none | Image asset for the card backs; default the builtin blue back. |

### `create_supply`

Put physical pieces on the table, so players handle gold, mana and points as pieces instead of reading numbers: piece coin, bead (a sphere: mana, energy, life), gem, cube, pawn or checker, in a colour (gold, silver, a seat colour, or [r,g,b]). mode 'infinite' is a bank that never runs out (players drag pieces out of it), 'bag' a bag holding count pieces, 'pile' count loose pieces. Rules can count pieces in a zone (zoneCount) and Lua with getObjects. Tag pieces '<role>:<seatId>' to give them to a seat.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Id of the bank, bag, or the first piece of a pile. |
| `piece` | string | `coin` | coin, bead, gem, cube, pawn or checker. |
| `color` | string | `gold` | gold, silver, bronze, a seat colour (red, blue...), or [r,g,b] 0-1. |
| `mode` | string | `infinite` | infinite, bag or pile. |
| `count` | integer | `10` | Pieces in a bag or pile. |
| `name` | string | none | Display name, e.g. 'Gold' or 'Mana'. |
| `x` | number | `0` | Position x in metres (table centre is 0). |
| `z` | number | `0` | Position z in metres. |
| `tags` | string | none | Comma-separated tags for the pieces. |

### `create_text_deck`

Create a deck of text cards: no images needed, the game prints each card's words (title, kind line, body, footer, a corner value, optional art image) and draws the back from the style, with backLabel naming the pile (e.g. 'EQUIPMENT'). cards: JSON array of {id?, title, kind?, body?, footer?, corner?, art? (asset id), artIndex?, count? (copies, default 1), meta?, tags?, style?}; copies get ids <id>_2, <id>_3... style: {paper, ink, accent, backPaper, backInk: [r,g,b] 0-1, backLabel}; a card's own style overrides the deck's (e.g. dark night cards in a day/night deck). Card name is the title and description the body, so tooltips and search show them. Ask the host how each deck should look when the rulebook does not say.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Deck id. |
| `cards` | string | required | JSON array of cards. |
| `name` | string | none | Display name. |
| `style` | string | none | JSON style for every card: {"paper":[r,g,b],"ink":[r,g,b],"accent":[r,g,b],"backPaper":[r,g,b],"backInk":[r,g,b],"backLabel":"EQUIPMENT"}. |
| `backAsset` | string | none | Image asset for the backs instead of the drawn back. |
| `x` | number | `0` | Position x in metres (table centre is 0). |
| `z` | number | `0` | Position z in metres. |
| `faceUp` | boolean | `false` | true to place it face up. |
| `tags` | string | none | Comma-separated tags, e.g. 'deck:seat_south'. |

### `deal`

Deal cards from a deck into seats' hands right now (free play, outside the rules), one at a time.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `deck` | string | required | Deck id. |
| `count` | integer | `1` | Cards per seat. |
| `seats` | string | none | Comma-separated seat ids; empty for every seat. |

### `define_zone`

Define a zone from JSON: HandZone (owner seat, hidden from others), HiddenZone, ScriptingZone (an area rules can count, e.g. a battlefield), SnapPoint, LayoutZone (arranges what is dropped in it in a grid: direction rightThenDown\|downThenRight\|leftThenDown\|downThenLeft, maxPerRow, spacing m, facing keep\|faceUp\|faceDown, manualOnly), RandomizeZone (players shuffle where things in it lie). Example: {"id":"battlefield","type":"ScriptingZone","transform":{"position":[0,0.05,0]},"size":[0.3,0.1,0.2]}. Any zone can show the total of the dice thrown in it (diceTotal: true; per player for dice tagged owner:<seat>; rules read $diceTotal.<zone> and $diceTotal.<zone>.<seat>). Any zone can carry rules: onEnter / onLeave effects run during a game when a player puts a piece in or takes it out ($it = the piece, $actor = who moved it), so moving a pawn onto a space can be the move itself; a refuse effect ({"refuse":{"reason":"..."}}) undoes the move; inside a blind round they wait for the reveal. Replaces a zone with the same id.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `zoneJson` | string | required | The zone as JSON. |

### `get_rules_document`

The complete game document as JSON (table, rules and, during play, the session).

### `get_rules_reference`

The rules format reference: objects, zones, phases, actions, effects, conditions, references ($actor.hand, $param.card.meta.rank, ...), events, win conditions and error codes. Read it before writing rules.

### `get_table_state`

What is on the table now: seats with their hands, decks with card counts and visible top cards, zones, and during play the turn, phase, current seat, variables, resources, recent log and every seat's legal actions.

### `load_game`

Load a game document (or a saved game in progress) from a JSON file.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | required | File path. |

### `new_game`

Start a new game document: name, player seats with a hand zone each, and the builtin standard deck images. Up to 4 seats are seat_south, seat_north, seat_east, seat_west (hands hand_<side>) on a 1.6 x 1.0 m table; 5 to 12 seats are seat_1..seat_N clockwise from the south side (hands hand_<n>, seat list order is turn order) on a table that grows to fit them. get_table_state shows where each hand lies. Make one table for every player count: players = the most players, minPlayers = the fewest; start_game then picks who plays. People beyond the seats can still join a session and watch. Replaces the current document.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Short id, lowercase, e.g. 'war'. |
| `name` | string | required | Display name. |
| `players` | integer | `2` | Number of seats to create (1-12). |
| `minPlayers` | integer | none | Minimum players the game supports. |
| `maxPlayers` | integer | none | Maximum players the game supports. |
| `description` | string | none | One-paragraph summary of the rules. |
| `dicePerSeat` | integer | `0` | Dice for each seat in its colour (d6, tagged owner:<seat>), in front of its hand; 0 for none. |

### `perform_action`

Perform a rules action as a seat, e.g. seat 'seat_south', action 'play_card', params {"card":"queen_hearts"}. get_table_state lists the legal actions and their parameter values.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `seat` | string | required | Seat id performing the action. |
| `action` | string | required | Action id. |
| `paramsJson` | string | none | JSON object of parameter values, e.g. {"card":"ace_spades"}. |

### `place_object`

Place any table object from JSON: Card, Deck, Token, Tile, Die, Bag, InfiniteBag, Board, Figure, Model, Counter, Clock, Notecard, Calculator (players press its keys or type while it is selected). Example: {"id":"d6","type":"Die","faces":6,"transform":{"position":[0.3,0,0]}}. With seat, the object goes in front of that seat's hand and faces it (a character card, a player board): inward metres towards the centre, along metres to the player's right; its own rotation then only adds a flip (rotation z 180 = face down). Replaces an object with the same id. Check placements with screenshot. See get_rules_reference for the format.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `objectJson` | string | required | The object as JSON. |
| `seat` | string | none | Seat id to place it for: in front of that seat, facing it. |
| `inward` | number | `0.15` | With seat: metres from the hand towards the centre. |
| `along` | number | `0` | With seat: metres to the player's right of the hand's middle (negative: left). |

### `remove`

Remove something by id from a collection: objects, zones, phases, actions, resources, or a variable, or an event by name.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `collection` | string | required | objects, zones, phases, actions, resources, variables or events. |
| `id` | string | required | The id (or variable/event name). |

### `reset_game`

Stop the running game and go back to the authored setup.

### `run_lua`

Try Lua against a copy of the table as it is now (the game in play, or the setup), with the table's scripts loaded: call your functions, read Game.getResource, move objects. Nothing changes in the real game. Returns what the code returned, what it printed, refused commands, and the copy's resources, variables and phase afterwards.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `code` | string | required | Lua to run, e.g. 'return fight({actor="seat_south", params={}, args={cost=2}})'. |

### `save_game`

Save the game document (with the session if a game is running) to a JSON file.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | none | File path, e.g. games/my-game.json. Defaults to the file the session was opened with. |

### `screenshot`

Look at the table in the game window: returns a picture with the HUD. view: 'seat' (as that seat's player sits, default the first seat), 'top' (the whole table from above), 'object' (target up close: check a card's face, a token's size), 'current' (what is on screen). Use it after placing things to check positions, facing, sizes and readability; needs the game window open.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `view` | string | `seat` | seat, top, object or current. |
| `seat` | string | none | For view 'seat': the seat id. |
| `target` | string | none | For view 'object': the object id. |
| `width` | integer | `1280` | Picture width in pixels (320-2560). |

### `set_notebook`

Write a page of the table's notebook (Notes in the top bar), which every player can open while playing: put the rulebook there (one page per chapter reads best: Setup, Turn, Combat...), a quick reference, a scenario. seat makes the page private to that seat. Up to 64 pages of 50 000 characters. Empty text removes the page. For a PDF rulebook use add_asset (type pdf) and place_object {type: Pdf} instead: players click it to read.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Page id, e.g. 'rules_setup'. |
| `title` | string | required | Tab title, up to 64 characters. |
| `text` | string | required | The page's text; empty removes the page. |
| `seat` | string | none | Seat id to make the page private to; empty for everyone. |

### `set_rules_document`

Replace the whole game document with JSON (for large edits). Malformed documents are rejected with errors.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `documentJson` | string | required | The full document as JSON. |

### `set_script`

Set Lua for the table: target 'global' for the global script (rules call its functions with lua effects: {"lua":{"call":"fight","args":{...}}}), or an object id for that object's script (buttons, onDrop...). The Lua API is TTS's (objects, UI, Wait, events) plus Game: Game.getResource(seat,id), Game.setResource/addResource(seat,id,value), Game.getVariable/setVariable(name,value), Game.getCurrentSeat(), Game.getPhase(), Game.getSeats(), Game.endTurn(), Game.endPhase(), Game.gotoPhase(id), Game.win(seat), Game.log(text), Game.perform(seat,action,params), Game.roll(count,sides). A function called by a lua effect gets ctx {actor, params, args, it}; returning false or a reason string refuses the action and rolls it back. The code is checked for syntax errors; try it with run_lua. See resource tableworld://docs/rules-and-scripting.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `target` | string | required | 'global' or an object id. |
| `code` | string | required | The Lua source; empty removes it. |

### `set_turn`

Set the turn structure: order ('clockwise'/'counterclockwise'), startingPlayer (seat id, 'first' or 'random'), and the phase ids played each turn, in order.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `phases` | string | required | Comma-separated phase ids in order, e.g. 'draw,play,discard'. |
| `order` | string | `clockwise` | 'clockwise' or 'counterclockwise'. |
| `startingPlayer` | string | `first` | Seat id, 'first' or 'random'. |

### `set_variables`

Declare game variables with their initial values, as a JSON object, e.g. {"round":0,"trump":""}. Merges with existing variables.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `variablesJson` | string | required | JSON object name → initial value. |

### `set_win_condition`

Add a win condition: {"when":{condition with $player},"winner":"$player","checkAfter":"anyCommand"}. Example: whoever empties their hand wins: {"when":{"zoneCount":{"zone":"$player.hand","op":"==","value":0}},"winner":"$player"}.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `winConditionJson` | string | required | The win condition as JSON. |
| `replace` | boolean | `false` | true to remove the existing win conditions first. |

### `start_game`

Start playing the game: runs onGameStart (shuffle, deal) and the first turn, with the seats that play. A table keeps every seat it has (make a 3-6 player game with 6 seats, once); seats or players picks who plays this time, and empty seats are left out of turns, deals and forEach. The document must validate.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `seed` | Int64 | none | Optional seed for a reproducible game. |
| `seats` | string | none | Comma-separated seat ids that play; empty for every seat. |
| `players` | integer | none | How many play, spread evenly around the table (instead of seats). |

### `validate_rules`

Check the whole game document. Returns every problem with its path, code, message and a suggestion.

## TableTools

### `table_action`

Do something physical on the table, like a player's hands (outside the rules): flip, rotate (value = degrees about the vertical), move (x, z), lock, unlock, shuffle (deck or bag), roll (die), set_die_value (value), cut (value = cards, default half), split (value = piles), spread (deck into a row), take (from a deck or bag; value = index from the top, or other = card id; from a stack, value = how many pieces; x, z = where), insert (other = deck/bag/card to put it into; value 1 = at the bottom), group (other = comma-separated ids to gather with it into a deck), delete, clone (x, z), set_state (other = state key), scale (value = factor), rename (other = new name, text = description), lay_out (target = a LayoutZone id: arrange what is in it), randomize (target = a RandomizeZone id: shuffle where things in it lie), hide (only seat sees it, or nobody without seat: a hidden chip or bid), show (everyone sees it again), show_card (a card, or a deck's top card, big on every screen to read it out), blind_round (target 'table'; value 1 starts a blind round in which whatever each seat moves is hidden from the others, value 0 reveals everything moved in it). seat = the seat doing it (blind moves only count when a seat makes them).

| Parameter | Type | Default | Description |
|---|---|---|---|
| `action` | string | required | One of: flip, rotate, move, lock, unlock, shuffle, roll, set_die_value, cut, split, spread, take, insert, group, delete, clone, set_state, scale, rename, lay_out, randomize, hide, show, blind_round, show_card |
| `target` | string | required | The object (or deck/bag) id. |
| `value` | number | none | Number argument (degrees, cards, piles, die value, index, factor) as described for the action. |
| `other` | string | none | Text argument (another id, ids, state key or name) as described for the action. |
| `x` | number | none | Position x in metres (table centre is 0, the table is 1.6 x 1.0). |
| `z` | number | none | Position z in metres (south seat at z = -0.5). |
| `text` | string | none | Description, for rename. |
| `seat` | string | none | Seat id doing it, as that player's hands. |
