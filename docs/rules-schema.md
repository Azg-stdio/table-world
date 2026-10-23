# Rules format — v0

Status: **v0, implemented by `core/`** (table model T2.1, commands T2.2, rules format T4.1, interpreter T4.2). When `core/Rules` implements something different, change this doc in the same commit. Machine-readable schema: `docs/rules-schema.v0.json`, generated from the code.

## Goals

1. An LLM agent writes it through MCP tools, one piece at a time, and gets validation errors it can act on.
2. The engine in `core/` interprets it with no Unity dependency, so 1 000 headless games can run in a test.
3. Every TTS primitive maps to one object type here (see `docs/tts-mapping.md`).
4. Anything that does not fit falls through to sandboxed Lua, never to C#.

## Top-level document

```jsonc
{
  "schemaVersion": "0.1",
  "game": { "id": "war", "name": "War", "minPlayers": 2, "maxPlayers": 2, "description": "..." },
  "assets":   [ /* Asset */ ],
  "objects":  [ /* TableObject, the initial setup */ ],
  "zones":    [ /* Zone */ ],
  "players":  { "seats": [ /* Seat */ ] },
  "resources": [ /* Resource */ ],
  "variables": { "round": 0 },
  "turn":     { "order": "clockwise", "startingPlayer": "first", "phases": ["battle", "resolve"] },
  "phases":   [ /* Phase */ ],
  "actions":  [ /* Action */ ],
  "events":   { /* event name -> Effect[] */ },
  "winConditions": [ /* WinCondition */ ],
  "ui":       { "panels": [ /* Panel */ ] },
  "scripts":  { "global": "-- optional Lua (M5)" },
  "session":  { /* written by the engine once a game starts */ }
}
```

Ids are strings matching `[a-z0-9_-]+`. **Objects, deck cards and zones share one id namespace** (effects can target either); assets and seats have their own. Cross-references use ids. Unknown keys are a validation error, keys are case-sensitive, and comments (`//`) are allowed in the JSON.

The table part of this document (`schemaVersion`, `assets`, `objects`, `zones`, `players`) is implemented by `Core.Rules.TableState` (T2.1) and is also the **save-file format**. Serialization omits nulls, defaults and empty lists; `TableJson.Hash` is the SHA-256 of the compact JSON and is the state identity used by save/load tests.

## Assets

```jsonc
{ "id": "deck_faces", "type": "image", "path": "assets/faces.png", "columns": 10, "rows": 7, "hash": "sha256:..." }
{ "id": "dragon",     "type": "model", "path": "assets/dragon.glb" }
{ "id": "std_deck",   "type": "image", "path": "builtin:standard_deck", "columns": 10, "rows": 7 }
```

`type`: `image` | `model` (glTF) | `assetBundle` | `audio`. Paths are relative to the game package and must stay inside it (no `..`, drive letters or leading slash; error `invalid_path`), because the host sends these files to every player; `builtin:<name>` is an asset the client generates (the MVP's procedural deck and dice). Hashes are filled by the tooling, not the agent. **The sprite-sheet grid belongs to the image** (`columns` × `rows`, default 1 × 1, row-major, index 0 top-left, same as TTS `NumWidth` × `NumHeight`), so one deck can mix cards from several sheets like a TTS `CustomDeck` with several keys.

## Table objects

Common fields (all optional except `id` and `type`):

```jsonc
{
  "id": "deck_main", "type": "Deck", "name": "Main deck", "description": "", "gmNotes": "",
  "tags": ["draw"], "locked": false,
  "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1] },
  "container": null,                 // id of the Bag/InfiniteBag that holds it, or null
  "tint": [1, 1, 1],                 // optional RGB(A) multiplier 0–1 (TTS ColorDiffuse)
  "states": { "2": { /* object */ } }, // alternate states keyed by positive integer; the object itself is the active state
  "script": null,                    // per-object Lua
  "ttsExtra": { }                    // TTS fields carried verbatim but not interpreted (importer only)
}
```

Units are meters, Y up, left-handed like Unity. **The table surface is Y = 0.** Rotation is Euler degrees in Unity order. **Face up is not stored**: it is derived from rotation (up axis pointing up), so a card at `[0, 0, 0]` is face up and `[0, 0, 180]` is face down (same as TTS). Effects that say `faceUp` set the rotation.

Containment: only `Bag` and `InfiniteBag` hold objects (via the child's `container`); bags may nest, cycles are invalid, and the order of contents is the order in `objects` (first = top). States cannot have a container or nested states. Cards inside a deck are not objects: they are entries in the deck's `cards`.

| `type` | Extra fields |
|---|---|
| `Card` | `faceAsset`, `faceIndex`, `backAsset`, `backIndex`, `size` (`[w, h]` in m, default 0.0635 × 0.0889), `meta` (free-form, e.g. `{ "rank": 11, "suit": "hearts" }`) |
| `Deck` | default `faceAsset`, `backAsset`, `backIndex`; `size`; `cards`: `[ { "id"?, "faceIndex", "name", "description", "gmNotes", "tags", "meta", "script", "faceAsset"?, "backAsset"?, "backIndex"? } ]` ordered **top first** (`cards[0]` is the card a draw takes; same order as TTS `DeckIDs`). Per-card asset fields override the deck's. A card without `id` gets `<deckId>-<n>` when it leaves the deck |
| `Token` | `asset` (image), `shape` (`round` \| `square` \| `image`), `thickness` (m), `stackable` |
| `Tile` | `asset`, `shape` (`square` \| `hex` \| `circle` \| `rounded`), `thickness`, `stackable` |
| `Die` | `faces` (4, 6, 8, 10, 12, 20; default 6), `labels` (optional, one per face), `value` (1..faces, null before the first roll), `asset` (custom dice image) |
| `Model` | `asset` (`model` or `assetBundle`), `diffuseAsset`, `collider` (`convex` \| `box` \| `mesh`), `kind` (`generic` \| `figurine` \| `dice` \| `coin` \| `board` \| `chip`), `material` (`plastic` \| `wood` \| `metal` \| `cardboard` \| `glass`) |
| `Bag` / `InfiniteBag` | `asset` (optional model look); contents are the objects whose `container` is this id; `InfiniteBag` clones its template on take |
| `Board` | `asset`, `size` |
| `Figure` | `asset`, `height` (m) |

## Zones

```jsonc
{ "id": "hand_p1", "type": "HandZone", "owner": "seat_1", "transform": {...}, "size": [0.4, 0.1, 0.2] }
{ "id": "battlefield", "type": "ScriptingZone", "transform": {...}, "size": [0.3, 0.1, 0.3] }
{ "id": "snap_discard", "type": "SnapPoint", "transform": {...}, "rotationSnap": true, "parent": "board_main" }
{ "id": "gm_area", "type": "HiddenZone", "visibleTo": ["host"], ... }
```

Every zone has `name`, `tags`, `transform`, `size` (box in m, default 0.1 on each axis; ignored by `SnapPoint`) and an optional `parent` object it moves with (TTS `AttachedSnapPoints`). `HiddenZone.visibleTo` lists seat ids or `host`. A seat's `handZone` must be a `HandZone` owned by that seat.

## Players

```jsonc
"players": { "seats": [ { "id": "seat_1", "color": "red", "handZone": "hand_p1" }, { "id": "seat_2", "color": "blue", "handZone": "hand_p2" } ] }
```

Roles (`host`, `player`, `spectator`) are runtime facts, not part of the game file.

## Table commands (engine, T2.2)

Physical interaction goes through `Core.Engine.TableEngine.Execute(Command)`; the Unity view never edits `TableState` itself. Commands: `Move`, `Take`, `Insert`, `Flip`, `Shuffle`, `Search`, `Deal`, `Lock`, `Group`, `Roll`. Rule effects (T4.x) are built on the same operations.

- **Transactional:** a command is fully checked before anything changes; a failed command returns one `ValidationError` (field, code, message, suggestion) and leaves the state byte-identical.
- **Deterministic:** all randomness (shuffle, roll) comes from a seeded PCG32 (`DeterministicRandom`, verified against the reference implementation). Same state + seed + commands → same hash. This is what lets the host be authoritative in M3 and lets tests replay sessions.
- **Dice:** two ways to roll. `Roll` (R key, rule effects) is the engine's seeded roll; the view animates it and shows that face. A die thrown or dropped by hand is decided by physics, as in TTS: when it settles, the view sends `Move` with `dieValue` = the face on top, and the engine records it. Replays stay deterministic because the value travels inside the command.
- **Decks:** a deck stays a deck at 0 or 1 cards (unlike TTS, which turns the last card into a loose card), so rules can keep referring to `deck_p1`. Flipping a deck reverses its order like a real deck. Cards joining a deck take the deck's orientation.
- **New ids:** a card leaving a deck without an id becomes `<deckId>-<n>`; infinite-bag clones become `<templateId>-<n>`; decks formed from loose cards become `deck-<n>`. Always the first free `n`, so ids are deterministic too.
- **Locked objects** cannot be moved, flipped, rolled, grouped or inserted, but you can still take from, shuffle, search and deal from a locked deck or bag (TTS behaviour).
- **Deal** goes round robin from the top into each seat's `HandZone`, face up, laid out along the zone's local X; it stops early if the deck runs out.
- **Events:** `ObjectAdded`, `ObjectRemoved`, `ObjectUpdated`, `ContainerChanged`, `Shuffled`, `DieRolled`, `Searched`, `ZoneEntered`, `ZoneLeft`. The view re-reads the object from the state on each event.
- **Zones (T2.6):** an object is in a zone when its centre lies inside the zone's box (`size` × `scale`, rotated with the zone). Objects inside bags or decks are in no zone; snap points contain nothing. After every successful command the engine appends `ZoneEntered`/`ZoneLeft` for each object the command touched (the rule events `onEnterZone`/`onLeaveZone` build on them). `IsHiddenFrom(object, seat)`: true inside another seat's `HandZone`, or inside a `HiddenZone` whose `visibleTo` lacks the seat (`host` for the host); M3 uses it to filter what each client receives.

## Resources and variables

```jsonc
"resources": [ { "id": "score", "name": "Score", "initial": 0, "min": 0, "max": 99 } ],   // one value per seat
"variables": { "battles": 0, "color": "red" }                                           // declared with their initial value
```

Effects can only set declared variables and resources; the validator rejects undeclared names with a suggestion.

## Turn structure and phases

```jsonc
"turn": { "order": "clockwise", "startingPlayer": "first", "phases": ["battle", "resolve"] },
"phases": [
  { "id": "battle",  "name": "Battle",  "actions": ["play_top_card"], "advanceWhen": { "allPlayersActed": true } },
  { "id": "resolve", "name": "Resolve", "advanceWhen": { "immediate": true }, "onEnter": [ /* Effects */ ], "onExit": [] }
]
```

- `order`: `clockwise` (seat list order) or `counterclockwise`. `startingPlayer`: a seat id, `first` or `random`.
- A turn plays the listed phases in order; after the last one the turn ends (`onTurnEnd`, win checks with `checkAfter: turnEnd`), the next seat becomes current, `onTurnStart` runs and the first phase begins.
- `advanceWhen` sets exactly one of: `immediate` (right after `onEnter`), `allPlayersActed` (every seat performed an action in this phase visit), `currentPlayerEnds` (only through an `endPhase`/`endTurn` effect; the default when `advanceWhen` is omitted), `condition` (a Condition, checked after every action).
- `actions` is a whitelist: only those actions can be performed during the phase.
- Entering a phase resets who has acted and the per-phase action counts. `gotoPhase` re-enters a phase (War uses it for ties).
- More than 200 phase changes without waiting for a player is reported as `rules_loop` and rolled back.

## Actions

```jsonc
{
  "id": "play_card", "name": "Play a card", "actor": "current",              // current | any
  "params": [ { "name": "card", "type": "object", "filter": { "inZone": "$actor.hand", "type": "Card" } } ],
  "preconditions": [ { "or": [
    { "compare": { "left": "$param.card.meta.suit", "op": "==", "right": "$obj.discard.top.meta.suit" } },
    { "compare": { "left": "$param.card.meta.rank", "op": "==", "right": "$obj.discard.top.meta.rank" } } ] } ],
  "effects": [ { "move": { "object": "$param.card", "to": "discard" } }, { "endTurn": {} } ],
  "limitPerPhase": 1                                                          // optional, per seat per phase visit
}
```

Param types: `object` (filter `inZone` — a zone, deck or reference —, `type`, `hasTag`), `seat`, `number`, `choice` (with `options`). The engine lists the legal actions of a seat with every parameter combination that passes the preconditions (`GameEngine.LegalActions`); the HUD, the MCP `get_table_state` tool and random test players all use that list.

## References

Fields that name a thing accept a plain id (object, zone, or a card inside a deck by its id) or a `$` reference:

| Root | Meaning |
|---|---|
| `$actor` | seat performing the action |
| `$current` | seat whose turn it is |
| `$player` | seat being checked by a win condition |
| `$other` | the seat after `$it` (when it is a seat), else after `$player`, else after `$actor` |
| `$it` | current item of a `forEach` (seat, object or card) |
| `$seat.<id>`, `$obj.<id>` | a seat; an object, zone or deck by id |
| `$param.<name>`, `$var.<name>` | an action parameter; a variable's current value |

Properties: on a seat `.hand` (its HandZone), `.<tag>` (the object or zone tagged `<tag>:<seatId>`, e.g. `$actor.deck` → tag `deck:seat_1`), `.id`, `.color`, `.res.<resource>`. On decks `.top`, `.bottom`, `.count`; on zones `.top`/`.bottom` (highest/lowest object in the zone), `.count`; on cards and objects `.meta.<key>`, `.name`, `.id`, `.faceUp`, `.value` (die). A reference to something that does not exist right now (the top of an empty deck) is null: comparisons with null are false except `!=`.

## Effects (v0)

Each effect is a single-key object, e.g. `{ "shuffle": { "object": "deck_main" } }`; effects without fields take `{}`.

| Effect | Fields |
|---|---|
| `move` | `object` (one thing), or `from` (deck or zone) + `take` (`top` default, `bottom`, `all`, `allButTop`) + `count` (default 1); `to` (deck, bag or zone); `at` (`top`/`bottom`, into decks); `faceUp` |
| `deal` | `from`, `count` per seat, `seats` (default all), exactly one of `toZone` (with `$it` = seat, e.g. `$it.hand`) or `toTag` (the deck tagged `<tag>:<seat>`), `faceDown` |
| `shuffle` / `flip` | `object` |
| `lock` | `object`, `locked` (default true) |
| `roll` | `object` (a die), `store` (variable for the value) |
| `setVariable` | `name`, exactly one of `value` (literal or reference) or `delta` |
| `setResource` | `player`, `resource`, exactly one of `value` or `delta`; clamped to `min`/`max` |
| `if` | `condition`, `then`, `else` |
| `forEach` | exactly one of `players: true` or `objectsIn` (zone, deck or bag); `do` (binds `$it`) |
| `message` | `text`; `{$reference}` is replaced by its value; goes to the session log |
| `endTurn` / `endPhase` | `{}` — take effect after the current effect list |
| `gotoPhase` | `phase` |
| `win` | `player` |

Moving cards into a deck makes them take the deck's orientation; into a zone they are laid out (hand slots for a HandZone, a stack at the centre otherwise). Not in v0: `spawn`, `destroy`, `prompt`, `reveal`/`hide`, `lua` (M5).

**Transactions:** a rules command either applies completely or not at all. Refusals before any change (wrong turn, failed precondition, bad parameter) leave the table untouched; if an effect fails midway, objects, zones, session and random state are restored and the result says `stateRestored`.

## Conditions (v0)

| Condition | Fields |
|---|---|
| `zoneCount` | `zone` (deck: cards; bag: contents; zone: objects inside, decks counting their cards), `op`, `value` |
| `compare` | `left`, `op`, `right` — literals or references; numbers compare numerically, anything else as text |
| `variable` | `name`, `op`, `value` |
| `resource` | `player`, `resource`, `op`, `value` |
| `isCurrentPlayer` | `player` |
| `objectIn` | `object`, `zone` (zone, deck or bag) |
| `hasTag` | `object`, `tag` |
| `and` / `or` | an array of conditions: `{ "and": [ ..., ... ] }` |
| `not` | one condition: `{ "not": { "isCurrentPlayer": { "player": "$actor" } } }` |

`op` is one of `==`, `!=`, `<`, `<=`, `>`, `>=`.

## Events

```jsonc
"events": {
  "onGameStart": [ { "shuffle": { "object": "deck_main" } }, { "deal": { "from": "deck_main", "count": 26, "toTag": "deck", "faceDown": true } } ],
  "onTurnStart": [],
  "onTurnEnd":   [ { "setVariable": { "name": "battles", "delta": 1 } } ]
}
```

v0 events: `onGameStart`, `onTurnStart`, `onTurnEnd`. Zone and physical events (`onEnterZone`, `onDrop`, ...) come with the Lua work (M5); the engine already emits `ZoneEntered`/`ZoneLeft`.

## Win conditions

```jsonc
"winConditions": [
  { "when": { "zoneCount": { "zone": "$player.hand", "op": "==", "value": 0 } }, "winner": "$player", "checkAfter": "anyCommand" }
]
```

`when` is checked once per seat with `$player` bound to it; the first that holds ends the game with `winner` (a reference or seat id). `checkAfter`: `anyCommand` (after every action, and after physical commands while a game runs) or `turnEnd`. A `win` effect ends the game directly.

## Session (written by the engine)

`StartGameCommand` adds a `session` object: `started`, `over`, `winner`, `turn`, `currentSeat`, `phase`, `acted`, `actionCounts`, `variables`, `resources`, `log` (last 50 messages) and `randomState`. It is saved with the table, so a saved game resumes exactly, including the shuffle sequence. An authored game file has no session.

## UI

```jsonc
"ui": { "panels": [
  { "id": "score", "title": "War", "anchor": "topRight", "visibleTo": [],
    "elements": [
      { "type": "label",   "text": "Battle {$var.battles}" },
      { "type": "counter", "text": "South's deck", "value": "$seat.seat_south.deck.count" },
      { "type": "button",  "text": "South: play top card", "action": "play_top_card", "seat": "seat_south", "params": {} },
      { "type": "image",   "asset": "logo" }
    ] }
] }
```

Panels are rendered with UI Toolkit (T4.6) on top of the table. `anchor`: `topLeft`, `top`, `topRight`, `left`, `center`, `right`, `bottomLeft`, `bottom`, `bottomRight`. `visibleTo`: seat ids (empty: everyone). Texts are templates: `{$reference}` is replaced by its value as the viewing seat sees it; `$viewer` is that seat. A `counter` shows `text` and the value of its `value` reference. A `button` performs `action` as `seat` (default `$viewer`) with `params`, and is disabled when that action is not legal right now. The validator checks button actions, references in texts and values, and image assets.

## Validation errors (format for an LLM reader)

```json
{ "path": "actions[0].effects[0].shufle", "code": "unknown_effect", "message": "Unknown effect 'shufle'.", "suggestion": "Did you mean 'shuffle'?" }
```

Always a `path`, a stable `code` (`Core.Rules.ErrorCodes`; never renamed), a one-sentence `message`, and a `suggestion` when one is computable. All errors are returned in one call. Rules codes: `unknown_effect`, `unknown_condition`, `unknown_phase`, `unknown_action`, `unknown_variable`, `unknown_resource`, `unknown_event`, `invalid_reference`, `invalid_operator`, `player_count`, plus the table codes. Engine refusals use the same shape: `no_rules`, `game_already_started`, `game_not_started`, `game_over`, `action_not_available`, `not_your_turn`, `not_your_seat` (online: a player sent a rules action for a seat that is not theirs), `limit_reached`, `precondition_failed`, `missing_param`, `invalid_param`, `rules_loop`, `rule_failed`.

The machine-readable schema is `docs/rules-schema.v0.json` (JSON Schema 2020-12), generated from the C# model by `SchemaGenerator`; a test fails when it is stale (`TW_UPDATE_SCHEMA=1 dotnet test` rewrites it). It checks shape only; references and ids are checked by the validator.

## Examples

`games/war.json` and `games/uno-lite.json` (generated by `tools/samples/make_games.py`). War is fully declarative: per-seat decks tagged `deck:<seat>` and battle piles tagged `battle:<seat>`, a `resolve` phase that compares `$seat.<id>.battle.top.meta.rank`, and ties handled with `gotoPhase` back to `battle` (no Lua needed). House rule: after 500 battles the player with more cards wins. Simplified UNO uses the standard deck: match the suit or rank of the top discard, or draw one (reshuffling the discard pile when the draw pile is empty); first to empty their hand wins.

## Resolved questions (T1.3, 2026-10-22)

- **`deal` target:** no magic. `deal.to` is `allPlayers` or a list of seat ids, and the destination per seat is explicit: `toZone` (a zone id or `$seat.hand`) **or** `toTag` (the object tagged `<tag>:<seat id>`, e.g. `deck` → `deck:seat_1`). Exactly one of the two is required.
- **Deck card order:** `cards[0]` is the top. TTS uses the same order: `DeckIDs[0]` / `ContainedObjects[0]` is the top card (`takeObject` index 0); the local saves agree (the only deck whose contained cards keep their stacking heights has the highest card at index 0). The importer copies the order as is.
- **Per-phase `actions`:** a whitelist in v0. Actions do not declare phases.
