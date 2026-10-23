# Rules format — v0

Status: **v0, the target `core/` implements** (table model in T2.1, rules in T4.1). When `core/Rules` implements something different, change this doc in the same commit. A machine-readable JSON Schema goes in `docs/rules-schema.v0.json` (T4.1).

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
  "turn":     { "order": "clockwise", "startingPlayer": "random", "phases": ["draw", "battle", "cleanup"] },
  "phases":   [ /* Phase */ ],
  "actions":  [ /* Action */ ],
  "events":   { /* event name -> Effect[] */ },
  "winConditions": [ /* WinCondition */ ],
  "ui":       { "panels": [ /* Panel */ ] },
  "scripts":  { "global": "-- optional Lua" }
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

`type`: `image` | `model` (glTF) | `assetBundle` | `audio`. Paths are relative to the game package; `builtin:<name>` is an asset the client generates (the MVP's procedural deck and dice). Hashes are filled by the tooling, not the agent. **The sprite-sheet grid belongs to the image** (`columns` × `rows`, default 1 × 1, row-major, index 0 top-left, same as TTS `NumWidth` × `NumHeight`), so one deck can mix cards from several sheets like a TTS `CustomDeck` with several keys.

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
- **Dice:** the engine picks the value; the view animates the roll and shows that face. Physics never decides a result.
- **Decks:** a deck stays a deck at 0 or 1 cards (unlike TTS, which turns the last card into a loose card), so rules can keep referring to `deck_p1`. Flipping a deck reverses its order like a real deck. Cards joining a deck take the deck's orientation.
- **New ids:** a card leaving a deck without an id becomes `<deckId>-<n>`; infinite-bag clones become `<templateId>-<n>`; decks formed from loose cards become `deck-<n>`. Always the first free `n`, so ids are deterministic too.
- **Locked objects** cannot be moved, flipped, rolled, grouped or inserted, but you can still take from, shuffle, search and deal from a locked deck or bag (TTS behaviour).
- **Deal** goes round robin from the top into each seat's `HandZone`, face up, laid out along the zone's local X; it stops early if the deck runs out.
- **Events:** `ObjectAdded`, `ObjectRemoved`, `ObjectUpdated`, `ContainerChanged`, `Shuffled`, `DieRolled`, `Searched`, `ZoneEntered`, `ZoneLeft`. The view re-reads the object from the state on each event.
- **Zones (T2.6):** an object is in a zone when its centre lies inside the zone's box (`size` × `scale`, rotated with the zone). Objects inside bags or decks are in no zone; snap points contain nothing. After every successful command the engine appends `ZoneEntered`/`ZoneLeft` for each object the command touched (the rule events `onEnterZone`/`onLeaveZone` build on them). `IsHiddenFrom(object, seat)`: true inside another seat's `HandZone`, or inside a `HiddenZone` whose `visibleTo` lacks the seat (`host` for the host); M3 uses it to filter what each client receives.

## Resources and variables

```jsonc
"resources": [ { "id": "score", "name": "Score", "perPlayer": true, "initial": 0, "min": 0 } ]
"variables": { "round": 0, "warDepth": 0 }
```

## Turn structure and phases

```jsonc
"turn": { "order": "clockwise", "startingPlayer": "random", "phases": ["play", "resolve"] },
"phases": [
  { "id": "play",    "name": "Play a card", "actions": ["play_top_card"], "onEnter": [], "onExit": [],
    "advanceWhen": { "allPlayersActed": true } },
  { "id": "resolve", "name": "Resolve", "actions": [], "onEnter": [ /* Effects */ ], "advanceWhen": { "immediate": true } }
]
```

`advanceWhen` options: `immediate`, `allPlayersActed`, `currentPlayerEnds` (an `endPhase` action), `condition` (a Condition).

## Actions

What a player may do. The engine exposes the legal actions for the current player and phase; the UI and the MCP `get_table_state` tool read the same list.

```jsonc
{
  "id": "play_top_card", "name": "Play top card", "actor": "current",      // current | any | owner
  "params": [],                                                            // or [{ "name": "card", "type": "object", "filter": { "inZone": "$actor.hand", "type": "Card" } }]
  "preconditions": [ { "zoneCount": { "zone": "$actor.deck", "op": ">", "value": 0 } } ],
  "effects": [ { "move": { "from": "$actor.deck", "take": "top", "to": "battlefield", "faceUp": true } } ],
  "limitPerTurn": 1
}
```

Param types: `object`, `zone`, `player`, `number`, `choice` (with `options`). Filters: `inZone`, `type`, `hasTag`, `ownedBy`, `faceUp`.

References starting with `$` resolve at runtime: `$actor`, `$current`, `$owner`, `$actor.hand`, `$actor.deck` (the zone/object tagged for that seat), `$param.card`, `$event.object`, `$event.zone`.

## Effects (v0 vocabulary)

| Effect | Fields |
|---|---|
| `move` | `object` or (`from`, `take`: `top` \| `bottom` \| `random` \| `all` \| n), `to` (zone or object id), `faceUp`, `position` |
| `flip` | `object` or `zone` |
| `shuffle` | `zone` or `object` (Deck/Bag) |
| `draw` | `from`, `to` (`$player.hand`), `count` |
| `deal` | `from`, `count`, `to`: `allPlayers` \| seat ids |
| `roll` | `object` (Die) or `faces`, `store`: variable name |
| `setResource` | `player`, `resource`, `value` or `delta` |
| `setVariable` | `name`, `value` or `delta` |
| `reveal` / `hide` | `object`, `to`: players |
| `lock` / `unlock` | `object` |
| `spawn` | `object` (inline TableObject), `at` |
| `destroy` | `object` |
| `if` | `condition`, `then`: Effect[], `else`: Effect[] |
| `forEach` | `players` \| `objectsIn`: zone, `do`: Effect[] (binds `$it`) |
| `prompt` | `player`, `text`, `choices`, `store` |
| `message` | `to`: `all` \| players, `text` (supports `{$var}` templates) |
| `endPhase` / `endTurn` | — |
| `win` / `lose` | `player` |
| `lua` | `function`: name in the global script, `args` |

Every effect is validated against the current state at authoring time where possible (`zone` exists, `resource` exists) and at runtime always. A failing effect aborts the whole command (transactional).

## Conditions (v0 vocabulary)

| Condition | Fields |
|---|---|
| `zoneCount` | `zone`, `op`, `value` |
| `resource` | `player`, `resource`, `op`, `value` |
| `variable` | `name`, `op`, `value` |
| `objectIn` | `object`, `zone` |
| `hasTag` | `object`, `tag` |
| `compare` | `left`, `op`, `right` (values or `$` references incl. `$card.rank`) |
| `isCurrentPlayer` | `player` |
| `and` / `or` / `not` | nested conditions |
| `lua` | `function` returning boolean |

`op` is one of `==`, `!=`, `<`, `<=`, `>`, `>=`.

Card metadata for comparisons comes from `Deck.cards[].tags` and an optional `meta` object (`{ "rank": 11, "suit": "hearts" }`).

## Events

```jsonc
"events": {
  "onGameStart":  [ { "shuffle": { "object": "deck_main" } }, { "deal": { "from": "deck_main", "count": 26, "to": "allPlayers" } } ],
  "onTurnStart":  [],
  "onDrop":       [ { "filter": { "zone": "battlefield", "type": "Card" }, "effects": [ /* ... */ ] } ],
  "onEnterZone":  [], "onLeaveZone": [], "onPhaseEnter": [], "onRoll": [], "onFlip": []
}
```

Events from physical interaction (`onDrop`) fire only on the host after the engine accepted the `Move` command.

## Win conditions

```jsonc
"winConditions": [
  { "when": { "zoneCount": { "zone": "$player.deck", "op": "==", "value": 52 } }, "winner": "$player", "checkAfter": "anyCommand" }
]
```

`checkAfter`: `anyCommand` | `turnEnd` | `phase:<id>`.

## UI

```jsonc
"ui": { "panels": [
  { "id": "score", "anchor": "top-right", "visibleTo": "all",
    "elements": [
      { "type": "label",   "text": "Round {$round}" },
      { "type": "counter", "bind": "resource:score", "player": "$viewer" },
      { "type": "button",  "text": "End turn", "action": "end_turn" }
    ] }
] }
```

Rendered with UI Toolkit (T4.6). `bind` values: `resource:<id>`, `variable:<name>`, `zoneCount:<zone>`.

## Validation errors (format for an LLM reader)

```json
{ "path": "actions[2].effects[0].move.to", "code": "unknown_zone", "message": "Zone 'discard' does not exist.", "suggestion": "Define it with define_zone or use one of: battlefield, hand_p1, hand_p2." }
```

Rules: always a `path`, a stable `code`, a one-sentence `message`, and a `suggestion` when one is computable. Multiple errors per validation call.

## Minimal example: War

```jsonc
{
  "schemaVersion": "0.1",
  "game": { "id": "war", "name": "War", "minPlayers": 2, "maxPlayers": 2 },
  "assets": [ { "id": "std_faces", "type": "image", "path": "assets/standard_faces.png", "columns": 13, "rows": 4 }, { "id": "std_back", "type": "image", "path": "assets/standard_back.png" } ],
  "objects": [
    { "id": "deck_main", "type": "Deck", "transform": { "position": [0, 0, 0] },
      "faceAsset": "std_faces", "backAsset": "std_back",
      "cards": [ { "faceIndex": 0, "name": "2 of clubs", "meta": { "rank": 2 } } /* ... 52 */ ] },
    { "id": "deck_p1", "type": "Deck", "tags": ["deck:seat_1"], "transform": { "position": [-0.3, 0, 0] }, "faceAsset": "std_faces", "backAsset": "std_back", "cards": [] },
    { "id": "deck_p2", "type": "Deck", "tags": ["deck:seat_2"], "transform": { "position": [0.3, 0, 0] },  "faceAsset": "std_faces", "backAsset": "std_back", "cards": [] }
  ],
  "zones": [ { "id": "battlefield", "type": "ScriptingZone", "transform": { "position": [0, 0, 0.2] }, "size": [0.4, 0.1, 0.2] } ],
  "players": { "seats": [ { "id": "seat_1", "color": "red" }, { "id": "seat_2", "color": "blue" } ] },
  "variables": { "warDepth": 0 },
  "turn": { "order": "clockwise", "startingPlayer": "seat_1", "phases": ["play", "resolve"] },
  "phases": [
    { "id": "play", "actions": ["play_top_card"], "advanceWhen": { "allPlayersActed": true } },
    { "id": "resolve", "actions": [], "advanceWhen": { "immediate": true },
      "onEnter": [ { "lua": { "function": "resolveBattle" } } ] }
  ],
  "actions": [ { "id": "play_top_card", "actor": "any", "limitPerTurn": 1,
    "preconditions": [ { "zoneCount": { "zone": "$actor.deck", "op": ">", "value": 0 } } ],
    "effects": [ { "move": { "from": "$actor.deck", "take": "top", "to": "battlefield", "faceUp": true } } ] } ],
  "events": { "onGameStart": [ { "shuffle": { "object": "deck_main" } }, { "deal": { "from": "deck_main", "count": 26, "to": "allPlayers" } } ] },
  "winConditions": [ { "when": { "zoneCount": { "zone": "$player.deck", "op": "==", "value": 0 } }, "winner": "$otherPlayer", "checkAfter": "turnEnd" } ],
  "scripts": { "global": "function resolveBattle() --[[ compare ranks on battlefield, move to winner's deck bottom, handle ties via warDepth ]] end" }
}
```

The tie-breaking of War is the kind of logic that is awkward declaratively; it is the first test case for the Lua escape hatch. If T4.2 finds a clean declarative form (`compare` on `$event.objects` with `forEach`), prefer it and drop the Lua.

## Resolved questions (T1.3, 2026-10-22)

- **`deal` target:** no magic. `deal.to` is `allPlayers` or a list of seat ids, and the destination per seat is explicit: `toZone` (a zone id or `$seat.hand`) **or** `toTag` (the object tagged `<tag>:<seat id>`, e.g. `deck` → `deck:seat_1`). Exactly one of the two is required.
- **Deck card order:** `cards[0]` is the top. TTS uses the same order: `DeckIDs[0]` / `ContainedObjects[0]` is the top card (`takeObject` index 0); the local saves agree (the only deck whose contained cards keep their stacking heights has the highest card at index 0). The importer copies the order as is.
- **Per-phase `actions`:** a whitelist in v0. Actions do not declare phases.
