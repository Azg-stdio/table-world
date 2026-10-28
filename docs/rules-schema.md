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
  "roles":    { /* hidden roles, dealt at the start (see Roles) */ },
  "translations": { /* the game in other languages (see Languages) */ },
  "session":  { /* written by the engine once a game starts */ }
}
```

Ids are strings matching `[a-z0-9_-]+`. **Objects, deck cards and zones share one id namespace** (effects can target either); assets and seats have their own. Cross-references use ids. Unknown keys are a validation error, keys are case-sensitive, and comments (`//`) are allowed in the JSON.

The table part of this document (`schemaVersion`, `assets`, `objects`, `zones`, `players`) is implemented by `Core.Rules.TableState` (T2.1) and is also the **save-file format**. Serialization omits nulls, defaults and empty lists; `TableJson.Hash` is the SHA-256 of the compact JSON and is the state identity used by save/load tests.

## Environment

Optional. The room around the table, so a game (a mod) can set its own place. Omitted means the default room. Every value is optional; unset values keep the room preset's.

```json
"environment": {
  "room": "gameNight",                 // gameNight (default: dim room, warm lamp over the table) | none (plain background, sun light) | karaoke (sofas, a big screen, a stage light)
  "roomAsset": "my_room",              // model or assetBundle asset that replaces the built-in room (loads with M4)
  "wallColor": [0.16, 0.12, 0.1], "floorColor": [0.22, 0.14, 0.09],   // colours 0–1
  "lighting": { "ambientIntensity": 0.3, "ambientColor": [0.6, 0.6, 0.62], "lampIntensity": 3.2, "lampColor": [1, 0.83, 0.62],
                "tableLightIntensity": 2.6, "tableLightColor": [1, 0.99, 0.97] },   // intensities 0–8
  "table": { "feltColor": [0.09, 0.27, 0.19], "feltAsset": "felt_image", "woodColor": [0.36, 0.22, 0.12] }
}
```

**Light on game pieces is kept neutral.** The lamp, its fill and the room colours set the mood and light only the room and the table's wooden frame; game pieces and the felt are lit only by the table light (neutral white by default), so card art keeps its true colours whatever mood a game picks. Ambient light reaches everything, so keep it low and neutral. A game can still tint the table light on purpose.

## Free-play turns

`"freeTurns": { "enabled": true, "seats": ["seat_south", "seat_north"], "current": "seat_north", "reverse": false, "skipEmptyHands": true, "count": 4 }` (top level, absent when off). Turns for tables without rules (TTS Turns). `seats` empty means the seat list's order. `SetTurnOrderCommand` (host only; refused while a rules game runs) switches them on or off and keeps the current seat when only the settings change; `EndTurnCommand`, sent by the current seat (`not_your_turn` otherwise), passes play to the next seat, over seats whose hand zone is empty when `skipEmptyHands` (a seat without a hand zone is never skipped, and if every hand is empty the next seat plays anyway).

## Physics mode

`"physics": "full" | "semiLock" | "locked"` (top level, default `full`, omitted when full). Semi-lock: objects slide and fall but cannot tip over (dice still tumble). Locked: nothing moves on its own; a dropped object is set down where it was released. Changed with `SetPhysicsCommand`, which only the host may send (`host_only` for a client). TTS keeps this as a host option, not in the save; we save it so a game can ship with the mode it needs.

The TTS importer maps `Sky` → `room`/`roomAsset`, `Table` + `TableURL` → `table.feltAsset`, and `Lighting` → `lighting`. Player avatars join this block when physics characters exist (M6).

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
| `Token` | `asset` (image), `shape` (`round` \| `square` \| `image`), `thickness` (m), `stackable`, `count` (pieces in the stack, 1–500, default 1; above 1 only when stackable) |
| `Tile` | `asset`, `shape` (`square` \| `hex` \| `circle` \| `rounded`), `thickness`, `stackable`, `count` |
| `Die` | `faces` (4, 6, 8, 10, 12, 20; default 6), `labels` (optional, one per face), `value` (1..faces, null before the first roll), `asset` (custom dice image) |
| `Model` | `asset` (`model` or `assetBundle`), `diffuseAsset`, `collider` (`convex` \| `box` \| `mesh`), `kind` (`generic` \| `figurine` \| `dice` \| `coin` \| `board` \| `chip`), `material` (`plastic` \| `wood` \| `metal` \| `cardboard` \| `glass`) |
| `Bag` / `InfiniteBag` | `asset` (optional model look); contents are the objects whose `container` is this id; `InfiniteBag` clones its template on take |
| `Board` | `asset`, `size` |
| `Figure` | `asset`, `height` (m) |
| `Counter` | `value` (−99 999..999 999). `SetCounterCommand` sets or adds, locked or not; typing digits over it sets it |
| `Clock` | `mode` (`clock` time of day, `stopwatch`, `timer`), `seconds` (reading when last started or stopped), `duration` (what a timer was set to; Reset returns to it), `running`. Each machine counts from when it saw the start; the host stops a timer at zero for everyone. `SetClockCommand` changes any of them; a pause carries the sender's shown reading |

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
- **New ids:** a card leaving a deck without an id becomes `<deckId>-<n>`; infinite-bag clones become `<templateId>-<n>`; decks formed from loose cards become `deck-<n>`. Always the first free `n`, so ids are deterministic too. On a table with rules or roles (secret cards), a card leaving a deck is always named `<deckId>-<n>` on the table, so other machines cannot tell which card it is; it keeps its own id as `entryId`. Rules and scripts run on the host and still use the card's own id: `$….id`, `object: "<own id>"`, Lua `getGUID()` and `getObjectFromGUID("<own id>")`, an object a player picks (`ctx.params`) and `ctx.it`. A proxy's `guid` field keeps the table id.
- **Locked objects** cannot be moved, flipped, rolled, grouped or inserted, but you can still take from, shuffle, search and deal from a locked deck or bag (TTS behaviour).
- **Deal** goes round robin from the top into each seat's `HandZone`, face up, laid out along the zone's local X; it stops early if the deck runs out.
- **Events:** `ObjectAdded`, `ObjectRemoved`, `ObjectUpdated`, `ContainerChanged`, `Shuffled`, `DieRolled`, `Searched`, `ZoneEntered`, `ZoneLeft`. The view re-reads the object from the state on each event.
- **Zones (T2.6):** an object is in a zone when its centre lies inside the zone's box (`size` × `scale`, rotated with the zone). Objects inside bags or decks are in no zone; snap points contain nothing. After every successful command the engine appends `ZoneEntered`/`ZoneLeft` for each object the command touched (the rule events `onEnterZone`/`onLeaveZone` build on them). `IsHiddenFrom(object, seat)`: true inside another seat's `HandZone`, or inside a `HiddenZone` whose `visibleTo` lacks the seat (`host` for the host). `HiddenZoneHiding(object, seat, host)` gives the hidden zone (not hands) that keeps an object from a viewer at that seat, hosting or not; the view does not draw such objects (G17). Every client still receives the full state.

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

### Eyes closed and voice

A phase can close eyes (ADR 0012): `"awake"` names who stays awake (`nobody`, `team:<id>`, `role:<id>`, a seat, or a
list such as `$var.night_order`); everyone else sleeps for the phase.

```jsonc
{ "id": "night_wolves", "awake": "team:wolves", "sleepText": "Night {$var.night}. Eyes closed.", "voice": "awake",
  "actions": [ "bite" ] }
```

- Sleepers take no actions, see a dark screen with `sleepText` (default "Eyes closed."), and do not see the awake
  players' pointers or held pieces; their chalkers sleep on the awake players' screens.
- Each machine tells who is awake from what it knows: a villager cannot tell who the wolves are by who seems awake.
- `voice`: `all` (default), `awake` (default in an eyes-closed phase: the awake hear each other, the sleepers hear and
  are heard by nobody), `team` (each team hears only itself), `none`. Spectators and a host without a seat see and
  hear everyone.

### Gestures

`events.onGesture` runs when a player makes a gesture at someone: a wink (K), an emote from the wheel. `$actor` made
it, `$param.gesture` is its name (`wink`, `wave`, ...), `$param.target` the seat whose face they were looking at
(missing when nobody). The rules decide what it means:

```jsonc
"events": { "onGesture": [ { "if": {
  "condition": { "and": [ { "compare": { "left": "$param.gesture", "right": "wink" } },
                          { "compare": { "left": "$actor.role", "right": "murderer" } } ] },
  "then": [ { "message": { "text": "You have been winked at. Count to five, then fall.", "to": "$param.target" } } ] } } ] }
```

Gestures pass between two players: the host runs them, and the other players' machines never learn whom one was aimed
at. Whoever looks at the winker's face sees the wink, as at a real table.

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

### Votes

An action with a `vote` block is a vote (ADR 0012): each voter's choice is secret until the last voter votes, then
the votes are counted, the count is logged, and `onResult` runs.

```jsonc
{ "id": "team_vote", "name": "Team vote", "actor": "any",
  "vote": {
    "options": [ "yes", "no" ], "labels": { "yes": "approve", "no": "reject" },   // or "seats": true to vote for a player
    "who": "all",              // all (default), team:<id>, role:<id>, a seat, or a list of seats ($var.quest_team)
    "reveal": "named",         // named (who voted what) | count (only how many chose each) | result (only the outcome)
    "decide": "majority",      // most (default) | majority (more than half) | unanimous | none (the rules judge the counts)
    "otherwise": "no",         // the result on a tie or when nothing reaches what decide needs; "tie" when left out
    "onResult": [ { "if": { "condition": { "compare": { "left": "$vote.result", "right": "yes" } }, "then": [ /* ... */ ] } } ]
  } }
```

- The choice is the action's `choice` parameter (`perform_action params {"choice": "yes"}`); the HUD shows one button
  per option to the seats that vote, and "Team vote: 2 of 5 voted (you: approve)" while it runs.
- A vote has no `secret` and no `effects`: it is secret by itself, and what follows goes in `onResult`.
- `$vote.result`, `$vote.total`, `$vote.<option>` (how many chose it), `$vote.of.<seat>` (named votes only) read the
  vote counted last; `$vote.<action>.result` and so on read a given one.
- `closeVote { action }` counts a vote now with the votes cast so far (a rule calling it).
- `"seconds": 30` closes the vote that long after its first ballot, counting what was cast; everyone reads the time
  left, and the host's game window keeps the clock.
- `"decide": "none"` announces no result and leaves `$vote.result` empty: for votes whose outcome the rules work out
  from the counts (a quest fails on one fail card), where a majority line would contradict them.
- A list in a text reads as names: `{$var.team}` shows "Red, Green and Blue".
- `"private": true` keeps a vote among its voters (the wolves choosing a victim): "voted" and the count are told to
  them alone, and no other machine learns that a ballot was cast.

### Secret variables

`"secretVariables": [ "victim", "saved" ]` lists variables only the host's rules read: their values never reach the
players' machines (the wolves' victim stays hidden until dawn), and a command that reads or sets one runs on the host.
Because no player's machine holds them, they cannot decide a player's own buttons: what one player alone may know about
themselves (winked at, poisoned) belongs in a hidden trait, a role `set` such as `fate`, which reaches that player only.
`set_variables` takes them as `secret`.

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

Lists (a variable holding `[...]`) have `.count` and items by position (`$var.team.0`). A seat id held in a variable or a vote result is followed like the seat (`$var.victim.team`, `$vote.result.name`). `$alive.count`, `$alive.team.<id>`, `$alive.role.<id>` count the seats still in the game; a seat's `.out` is true once eliminated. Properties: on a seat `.role` and `.team` (its hidden role and team, where this machine knows them; see Roles), `.hand` (its HandZone), `.<tag>` (the object or zone tagged `<tag>:<seatId>`, e.g. `$actor.deck` → tag `deck:seat_1`), `.id`, `.color`, `.res.<resource>`. On decks `.top`, `.bottom`, `.count`; on zones `.top`/`.bottom` (highest/lowest object in the zone), `.count`; on cards and objects `.meta.<key>`, `.name`, `.id`, `.faceUp`, `.value` (die). A reference to something that does not exist right now (the top of an empty deck) is null: comparisons with null are false except `!=`.

## Effects (v0)

Each effect is a single-key object, e.g. `{ "shuffle": { "object": "deck_main" } }`; effects without fields take `{}`.

| Effect | Fields |
|---|---|
| `move` | `object` (one thing), or `from` (deck or zone) + `take` (`top` default, `bottom`, `all`, `allButTop`) + `count` (default 1); `to` (deck, bag or zone); `at` (`top`/`bottom`, into decks); `faceUp` |
| `deal` | `from`, `count` per seat, `seats` (default all), exactly one of `toZone` (with `$it` = seat, e.g. `$it.hand`) or `toTag` (the deck tagged `<tag>:<seat>`), `faceDown` |
| `shuffle` / `flip` | `object` |
| `lock` | `object`, `locked` (default true) |
| `roll` | `object` (a die), `store` (variable for the value) |
| `setVariable` | `name`, exactly one of `value` (literal or reference), `delta`, or for a list `add` / `remove` (one item; `"value": []` empties it) |
| `setResource` | `player`, `resource`, exactly one of `value` or `delta`; clamped to `min`/`max` |
| `if` | `condition`, `then`, `else` |
| `forEach` | exactly one of `players: true` or `objectsIn` (zone, deck or bag); `do` (binds `$it`) |
| `message` | `text`; `{$reference}` is replaced by its value; goes to the session log, or with `to` (a seat, `team:<id>`, `role:<id>`) privately to those players only |
| `endTurn` / `endPhase` | `{}` — take effect after the current effect list |
| `gotoPhase` | `phase` |
| `win` | `player`, or `team` (hidden roles: every seat of that team wins; the log names them) |
| `eliminate` | `seat`, `revealRole` (show its role to all): the seat is out — no actions or votes, asleep at night, not heard, skipped by turns and seat choices |
| `revealRole` | `seat`; `to` (a seat, `team:<id>`, `role:<id>`; every seat when left out or `all`); `teamOnly` (show only the team); `set` (show a trait of an extra role set) |
| `setRole` | `seat`, `role` (an id or a reference), `set`, `tell` (let the player know); the new role brings its team |
| `swapRoles` | `a`, `b`, `set`, `tell`; each seat's team goes with its role |
| `setTeam` | `seat`, `team`, `tell`; the role stays |

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
| `inList` | `list` (a list, e.g. `$var.team`), `value`: the list holds it |

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

With hidden roles a win condition can name a team instead of a winner: `{ "when": { "compare": { "left":
"$alive.team.wolves", "right": 0 } }, "team": "village", "checkAfter": "anyCommand" }` is checked once, not per seat.

## Roles

Hidden-role games (Avalon, Werewolf, Wink Murder; ADR 0012) declare teams, the roles dealt at random when the game
starts, and who knows whom:

```jsonc
"roles": {
  "teams": [ { "id": "good", "name": "Loyal servants of Arthur" }, { "id": "evil", "name": "Minions of Mordred" } ],
  "list": [
    { "id": "merlin",   "name": "Merlin",   "team": "good" },              // count 1 when left out
    { "id": "percival", "name": "Percival", "team": "good", "optional": true },
    { "id": "servant",  "team": "good", "count": "fill" },                  // the seats left in its team
    { "id": "assassin", "team": "evil" },
    { "id": "oberon",   "team": "evil", "optional": true },
    { "id": "minion",   "team": "evil", "count": "fill" }
  ],
  "byPlayers": { "5": { "good": 3, "evil": 2 }, "6": { "good": 4, "evil": 2 } },
  "knows": [
    { "role": "merlin", "sees": { "team": "evil", "except": [ "mordred" ] } },              // as their team
    { "team": "evil",   "sees": { "team": "evil", "except": [ "oberon" ] } },
    { "role": "oberon", "sees": {} },                                                       // a role's own rules replace its team's
    { "role": "percival", "sees": { "roles": [ "merlin", "morgana" ] }, "as": "oneOf" },   // one of those, not which
    { "role": "dictator", "sees": { "roles": [ "fascist" ] }, "maxPlayers": 6 }            // only in small games
  ]
}
```

- **The deal:** the host ticks optional roles when starting (`StartGameCommand.roles`, `start_game roles=`). With
  `byPlayers`, each team gets its size for the number of players: its fixed roles first, its `fill` role the rest.
  Without it, the fixed counts stand and a single `fill` role takes the rest. A mix that does not add up refuses the
  start with `role_count` and the reason.
- **What a seat knows** is worked out once at the deal from `knows`: the seats whose role matches `sees` (`team`,
  `roles`, minus `except`), shown `as` their `team` (default), their `role`, or `oneOf` the listed roles. Each seat is
  told privately ("You are Merlin (Loyal servants of Arthur).", "Red and Blue are Minions of Mordred."); those lines
  never reach the shared log.
- **Rules read roles** with `$actor.role`, `$seat.<id>.team` and so on: an assassin-only action is a precondition
  `{ "compare": { "left": "$actor.role", "right": "assassin" } }`. `revealRole` shows a role later.
- **Roles change during play:** `setRole` (a conversion), `swapRoles` (two seats trade, their teams with them),
  `setTeam` (a seat changes allegiance, keeping its role). With `tell` the player learns it; what others learnt
  before stays what they saw. `revealRole` and `message` with `to: "team:wolves"` tell a whole team privately ("the
  pack learns who the new wolf is").
- **More hidden traits:** roles with a `set` (`{ "id": "spy", "set": "objective" }`) form another deck dealt one per
  seat beside the role, from its own counts and one `fill`. Rules read them as `$actor.role.objective`; the player
  reads "Objective: Spy."; `revealRole`, `setRole` and `swapRoles` take the `set`.
- **Shown to a role:** a `HiddenZone`'s `visibleTo` and a notebook page's `for` take `role:<id>` and `team:<id>` as well
  as seats (`"visibleTo": [ "role:defuser" ]`: only the defuser sees the bomb, whoever is dealt it).
- **Secrecy:** `session.roles` (seat → role) and `session.knows` (seat → what it knows) reach each machine only for its
  own seat. A command that reads or changes a role runs on the host, which sends each player its result.

## Session (written by the engine)

`StartGameCommand` adds a `session` object: `language` (what players read, see Languages; English when unset), `started`, `over`, `winner`, `turn`, `currentSeat`, `phase`, `acted`, `actionCounts`, `variables`, `resources`, `log` (last 50 messages), `secrets` (secret choices waiting for a reveal), `roles`, `traits`, `teams` and `knows` (see Roles) and `randomState`. It is saved with the table, so a saved game resumes exactly, including the shuffle sequence. An authored game file has no session.

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

## Languages

A game is written in one language and can carry others (ADR 0011). `game.language` says which one it is written in;
`game.languages` lists those it can be played in, its own first (empty: its own plus every translation); `translations`
holds the others, language → path key → text:

```jsonc
"game": { "id": "tornaris", "name": "Tornaris", "language": "es", "languages": ["es", "en"] },
"translations": {
  "es": { "strings.noche": "Es de noche." },
  "en": { "objects.ficha.name": "Nyra's token", "params.elegir.casilla.mazmorra": "Dungeon", "strings.noche": "It is night." }
}
```

Path keys name the text they replace: `game.name`, `game.description`, `objects.<id>.name`, `.description`, a text
card's `objects.<id>.text.<title|kind|body|footer|corner>`, deck cards `cards.<id>...` the same way, `seats.<id>.name`,
`resources.<id>.name`, `actions.<id>.name`, choice options `params.<action>.<param>.<option>` (the label, else the
option), vote options `votes.<action>.<option>`, `phases.<id>.name`, `.sleepText`, panels `ui.<panel>.title` and
`ui.<panel>.<index>.text`, `messages.<key>`, `notebook.<tab>.title`, `.body`, `teams.<id>.name`, `roles.<id>.name`,
`.description`, `zones.<id>.name`. A sentence a Lua script writes is translated by its own words: the script wraps
it in `Game.tr("Evento de noche")` and the key is `tr:Evento de noche` (the scan finds literal `tr(...)` calls in every
script). `strings.<key>` is the game's own free text: a text that is exactly `@key` reads it (`@@` writes a
literal `@`); the game's own language holds its strings too. An image asset can name a file per language with
`"localized": { "en": "cards_en.png" }`; `path` is the fallback.

What players read is the session's language when the game has it, else the game's own; a text a translation lacks reads
as written. Validation: translations without `game.language`, a language the game does not list, a key that names no
text, the game's own language holding anything but strings, an `@key` with no string, are errors. Texts a language
lacks are not errors: `Translations.Missing` lists them, so an unfinished translation still plays.

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
