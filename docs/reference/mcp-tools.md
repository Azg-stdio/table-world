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

### `add_event`

Set the effects run on an event: onGameStart (setup: shuffle, deal), onTurnStart, onTurnEnd. Example effects: [{"shuffle":{"object":"deck"}},{"deal":{"from":"deck","count":7,"toZone":"$it.hand"}}].

| Parameter | Type | Default | Description |
|---|---|---|---|
| `eventName` | string | required | onGameStart, onTurnStart or onTurnEnd. |
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

Create a deck of cards on the table. cards: 'standard52' or 'standard54' (with jokers) for the builtin French deck (card meta: rank 2-14 aces high, suit, color), or a JSON array of cards {id, name, faceIndex, meta}. Tag a deck '<role>:<seatId>' (e.g. 'deck:seat_south') so rules can say $actor.deck.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Deck id. |
| `cards` | string | `standard52` | 'standard52', 'standard54', or a JSON array of cards. |
| `name` | string | none | Display name. |
| `x` | number | `0` | Position on the table in metres; the table is 1.6 x 1.0, centre 0,0, south seat at z = -0.5. |
| `z` | number | `0` | Position z in metres. |
| `faceUp` | boolean | `false` | true to place it face up (discard piles), false for a face-down draw pile. |
| `tags` | string | none | Comma-separated tags, e.g. 'deck:seat_south'. |

### `deal`

Deal cards from a deck into seats' hands right now (free play, outside the rules), one at a time.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `deck` | string | required | Deck id. |
| `count` | integer | `1` | Cards per seat. |
| `seats` | string | none | Comma-separated seat ids; empty for every seat. |

### `define_zone`

Define a zone from JSON: HandZone (owner seat, hidden from others), HiddenZone, ScriptingZone (an area rules can count, e.g. a battlefield), SnapPoint, LayoutZone (arranges what is dropped in it in a grid: direction rightThenDown\|downThenRight\|leftThenDown\|downThenLeft, maxPerRow, spacing m, facing keep\|faceUp\|faceDown, manualOnly), RandomizeZone (players shuffle where things in it lie). Example: {"id":"battlefield","type":"ScriptingZone","transform":{"position":[0,0.05,0]},"size":[0.3,0.1,0.2]}. Replaces a zone with the same id.

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

Start a new game document: name, player seats (seat_south, seat_north, seat_east, seat_west, each with a hand zone hand_<side>), and the builtin standard deck images. Replaces the current document.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | string | required | Short id, lowercase, e.g. 'war'. |
| `name` | string | required | Display name. |
| `players` | integer | `2` | Number of seats to create (1-4). |
| `minPlayers` | integer | none | Minimum players the game supports. |
| `maxPlayers` | integer | none | Maximum players the game supports. |
| `description` | string | none | One-paragraph summary of the rules. |

### `perform_action`

Perform a rules action as a seat, e.g. seat 'seat_south', action 'play_card', params {"card":"queen_hearts"}. get_table_state lists the legal actions and their parameter values.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `seat` | string | required | Seat id performing the action. |
| `action` | string | required | Action id. |
| `paramsJson` | string | none | JSON object of parameter values, e.g. {"card":"ace_spades"}. |

### `place_object`

Place any table object from JSON: Card, Deck, Token, Tile, Die, Bag, InfiniteBag, Board, Figure, Model. Example: {"id":"d6","type":"Die","faces":6,"transform":{"position":[0.3,0,0]}}. Replaces an object with the same id. See get_rules_reference for the format.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `objectJson` | string | required | The object as JSON. |

### `remove`

Remove something by id from a collection: objects, zones, phases, actions, resources, or a variable, or an event by name.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `collection` | string | required | objects, zones, phases, actions, resources, variables or events. |
| `id` | string | required | The id (or variable/event name). |

### `reset_game`

Stop the running game and go back to the authored setup.

### `save_game`

Save the game document (with the session if a game is running) to a JSON file.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `path` | string | none | File path, e.g. games/my-game.json. Defaults to the file the session was opened with. |

### `set_rules_document`

Replace the whole game document with JSON (for large edits). Malformed documents are rejected with errors.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `documentJson` | string | required | The full document as JSON. |

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

Start playing the game: runs onGameStart (shuffle, deal) and the first turn. The document must validate.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `seed` | Int64 | none | Optional seed for a reproducible game. |

### `validate_rules`

Check the whole game document. Returns every problem with its path, code, message and a suggestion.

## TableTools

### `table_action`

Do something physical on the table, like a player's hands (outside the rules): flip, rotate (value = degrees about the vertical), move (x, z), lock, unlock, shuffle (deck or bag), roll (die), set_die_value (value), cut (value = cards, default half), split (value = piles), spread (deck into a row), take (from a deck or bag; value = index from the top, or other = card id; from a stack, value = how many pieces; x, z = where), insert (other = deck/bag/card to put it into; value 1 = at the bottom), group (other = comma-separated ids to gather with it into a deck), delete, clone (x, z), set_state (other = state key), scale (value = factor), rename (other = new name, text = description), lay_out (target = a LayoutZone id: arrange what is in it), randomize (target = a RandomizeZone id: shuffle where things in it lie).

| Parameter | Type | Default | Description |
|---|---|---|---|
| `action` | string | required | One of: flip, rotate, move, lock, unlock, shuffle, roll, set_die_value, cut, split, spread, take, insert, group, delete, clone, set_state, scale, rename, lay_out, randomize |
| `target` | string | required | The object (or deck/bag) id. |
| `value` | number | none | Number argument (degrees, cards, piles, die value, index, factor) as described for the action. |
| `other` | string | none | Text argument (another id, ids, state key or name) as described for the action. |
| `x` | number | none | Position x in metres (table centre is 0, the table is 1.6 x 1.0). |
| `z` | number | none | Position z in metres (south seat at z = -0.5). |
| `text` | string | none | Description, for rename. |
