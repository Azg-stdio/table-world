# Rules and scripting for designers

Your agent writes a game's rules for you, but you can read and change them yourself. This page explains the rules
format in game terms, from a simple game to a complex one, using the two sample games: War
([`games/war.json`](../../games/war.json)) and Match the Pile, a simplified UNO
([`games/uno-lite.json`](../../games/uno-lite.json)). The second half covers Lua, for the logic the format cannot express.

Exact field lists are in the [rules format reference](../reference/rules-format.md) and the
[design notes](../rules-schema.md); this page is the tour.

## 1. A game is one JSON file

A game file holds the table and the rules together:

| Part | What it is | War | Match the Pile |
|---|---|---|---|
| `game` | name, player count, description | War, 2–2 | Simplified UNO, 2–4 |
| `assets` | images, models, sounds | the built-in card images | the same |
| `objects` | what is on the table at the start | the main deck and each player's two piles | the draw and discard piles |
| `zones` | areas of the table: hands, hidden areas, snap points | none | the players' hands |
| `players` | seats: colour and hand | south (red), north (blue) | the same, each with a hand |
| `variables`, `resources` | numbers the rules keep | `battles` | none |
| `turn`, `phases`, `actions` | how play goes | battle, then resolve | one phase: play or draw |
| `events` | what happens at fixed moments | shuffle and deal at the start | shuffle, deal 7, turn up a card |
| `winConditions` | how the game ends | out of cards; 500 battles | empty hand |
| `ui` | panels on the screen | battle number and pile sizes | the top discard and the draw pile |

It is ordinary JSON, and `//` comments are allowed. Every id is lowercase letters, digits, `_` or `-`.

## 2. Turns and phases

A turn is a list of phases played in order. In each phase, only the actions it lists can be performed, and its
`advanceWhen` rule says when it ends.

```mermaid
flowchart LR
    start([onGameStart]) --> ts[onTurnStart]
    ts --> p1[first phase]
    p1 -->|advanceWhen| p2[next phase]
    p2 -->|after the last phase| te[onTurnEnd and win checks]
    te -->|next seat| ts
```

Match the Pile has one phase. The current player either plays or draws, and both actions end the turn with
`endTurn`:

```json
"turn": { "order": "clockwise", "startingPlayer": "first", "phases": ["play"] },
"phases": [ { "id": "play", "name": "Play", "actions": ["play_card", "draw_card"] } ]
```

With no `advanceWhen`, the phase ends when an action says so (`endTurn` or `endPhase`). The other rules are:

| `advanceWhen` | The phase ends |
|---|---|
| `{ "immediate": true }` | as soon as its `onEnter` effects have run |
| `{ "allPlayersActed": true }` | when every seat has performed an action in it |
| `{ "condition": { ... } }` | when the condition holds, checked after every action |

War needs both players to act in the same turn. Its battle phase waits for everyone, then a resolve phase compares
the two cards straight away:

```json
{ "id": "battle", "actions": ["play_top_card"], "advanceWhen": { "allPlayersActed": true } },
{ "id": "resolve", "advanceWhen": { "immediate": true }, "onEnter": [ /* compare and take the cards */ ] }
```

## 3. Actions

An action is something a player can do. It has a `name` shown on buttons, `actor` (`current` for the player whose turn
it is, `any` for anyone), optional `params`, `preconditions` that must all hold, and `effects` that run in order.

War's only action takes no parameters:

```json
{ "id": "play_top_card", "name": "Play top card", "actor": "any", "limitPerPhase": 1,
  "preconditions": [ { "zoneCount": { "zone": "$actor.deck", "op": ">", "value": 0 } } ],
  "effects": [ { "move": { "from": "$actor.deck", "to": "$actor.battle", "faceUp": true } } ] }
```

Match the Pile's play action asks which card. The parameter is any card in the player's own hand, and the
precondition keeps only the cards that match:

```json
{ "id": "play_card", "name": "Play a card", "actor": "current",
  "params": [ { "name": "card", "type": "object", "filter": { "inZone": "$actor.hand", "type": "Card" } } ],
  "preconditions": [ { "or": [
    { "compare": { "left": "$param.card.meta.suit", "op": "==", "right": "$obj.discard.top.meta.suit" } },
    { "compare": { "left": "$param.card.meta.rank", "op": "==", "right": "$obj.discard.top.meta.rank" } } ] } ],
  "effects": [ { "move": { "object": "$param.card", "to": "discard" } }, { "endTurn": {} } ] }
```

The game works out every legal action with every legal choice. The HUD shows one button per action the player may
take now; an action with several choices opens a picker, where card choices show the cards themselves (pick a
character, buy from the shop). Your agent sees the same in `get_table_state`. Mark an action `"button": false` when
players do it with the pieces (a card dragged from the hand), or only scripts and agents use it: the game still knows
it, and the HUD stays short.

**An action happens completely or not at all.** If any effect fails, the table goes back to how it was.

**Secret choices.** Many games have everyone choose at once and then show: pick a space, bid, play a card face down.
Mark the action `"secret": true`. Performing it records the player's choice and runs nothing; the others see only that
the player has chosen. A `reveal` effect later shows every recorded choice and runs each one's effects, in turn order
from the current player, as if that player had just performed it. The usual shape is a phase everyone acts in, which
reveals when it ends:

```json
{ "id": "pick_space", "name": "Pick a space", "actor": "any", "secret": true, "limitPerPhase": 1,
  "params": [ { "name": "space", "type": "choice", "options": ["dungeon", "shop", "tower", "event"] } ],
  "effects": [ { "move": { "object": "$actor.figure", "to": "$param.space" } } ] }

{ "id": "choose", "actions": ["pick_space"], "advanceWhen": { "allPlayersActed": true },
  "onExit": [ { "reveal": {} } ] }
```

`{ "reveal": { "action": "pick_space" } }` shows only that action's choices. A choice is checked when it is made (its
parameters and preconditions), so a player cannot pick something illegal and be found out later.

`$secrets` counts the choices waiting for the reveal (`$secrets.pick_space` only that action's), and
`$viewer.chose.pick_space` is the waiting choice of the player looking at the screen, so a panel can remind each
player what they picked while the others see nothing (section 6).

**What players read.** Options keep short ids for the rules, and `labels` give the words players see on buttons, in the
picker and in the log:

```json
"params": [ { "name": "space", "type": "choice", "options": ["dungeon", "shop"],
              "labels": { "dungeon": "Mazmorra", "shop": "Tienda" } } ]
```

**Moving pieces instead of pressing buttons.** Any zone can carry rules. `onEnter` runs when a player puts a piece in
the zone during a game, `onLeave` when a piece is taken out; `$it` is the piece and `$actor` the player who moved it.
A `refuse` effect undoes the move and tells the player why:

```json
{ "id": "dungeon", "type": "ScriptingZone", "transform": { "position": [0.4, 0.05, 0.2] }, "size": [0.2, 0.1, 0.2],
  "onEnter": [
    { "if": { "condition": { "variable": { "name": "blocked", "op": "==", "value": "dungeon" } },
              "then": [ { "refuse": { "reason": "The roulette blocked the dungeon." } } ] } },
    { "setVariable": { "name": "fighters", "delta": 1 } } ] }
```

**Dice thrown on the table.** A zone with `"diceTotal": true` is an arena: while dice lie in it, the game shows their
total over it, and each player's share in their colour (a die belongs to the seat its `owner:<seat>` tag names;
`new_game` with `dicePerSeat` gives every seat its own coloured dice). Rules read `$diceTotal.arena` and
`$diceTotal.arena.<seat>` (or `.actor`), Lua `Game.getDiceTotal("arena", seat)`, so a duel can be decided from the dice
the players actually threw.

For simultaneous secret moves with the pieces themselves, use a **blind round**: `{ "blindRound": { "on": true } }`
starts one (in a phase's `onEnter`, say), and while it runs every piece a player moves is hidden from the others and the
zones' effects wait. `{ "blindRound": { "on": false } }` reveals: the held zone effects run in turn order, for pieces
still where they were put, and then the `onReveal` event. Players can also start and reveal a blind round from the left
tool bar.

A zone drawn over a board (a space on a map) holds the pieces on it, never the board itself: counts, `onEnter` and
`forEach` over the zone see only the pieces.

**Hidden roles.** Avalon, Werewolf and the like deal each player a secret role when the game starts. `roles` lists the
teams, the roles (with how many seats get each: a number, or `"fill"` for the seats left in its team), the team sizes
for each number of players, and who knows whom:

```jsonc
"roles": {
  "teams": [ { "id": "village", "name": "Villagers" }, { "id": "wolves", "name": "Werewolves" } ],
  "list": [
    { "id": "seer", "name": "Seer", "team": "village" },
    { "id": "villager", "name": "Villager", "team": "village", "count": "fill" },
    { "id": "wolf", "name": "Werewolf", "team": "wolves", "count": 2 }
  ],
  "knows": [ { "team": "wolves", "sees": { "team": "wolves" } } ]
}
```

Each player is told privately who they are and what they see ("You are Werewolf (Werewolves).", "Blue is
Werewolves."); nobody else's screen, nor the shared log, shows it, and the other players' machines never receive it.
Roles marked `"optional": true` are ticked by the host when starting. Rules read roles with `$actor.role` or
`$seat.<id>.team` (an action only the seer may take has the precondition
`{ "compare": { "left": "$actor.role", "right": "seer" } }`), and `revealRole` shows one later:
`{ "revealRole": { "seat": "$param.target", "to": "$actor", "teamOnly": true } }` lets the seer look at a card, and
without `to` everyone sees it. The full format is in [the rules format](../rules-schema.md#roles).

## 4. Naming things: `$` references

Rules rarely name one card. They say "the actor's deck" or "the top card of the discard pile". A `$` reference names
what you mean:

| You write | It means |
|---|---|
| `$actor`, `$current` | the seat performing the action; the seat whose turn it is |
| `$actor.hand` | that seat's hand zone |
| `$actor.deck` | the object tagged `deck:<that seat>`, so each seat gets its own pile from one rule |
| `$obj.discard.top` | the top card of the object `discard` |
| `$param.card.meta.rank` | the rank written on the card the player chose |
| `$var.battles` | the variable `battles` |
| `$seat.seat_north.battle.count` | how many cards are in north's battle pile |
| `$player`, `$other` | in a win condition: the seat being checked, and the next one |
| `$viewer` | in a panel: the player looking at it (each player sees their own) |
| `$actor.name` | a seat's name: the one `new_game` gave it, or its colour |
| `$secrets`, `$viewer.chose.pick_space` | secret choices waiting for the reveal; one player's own waiting choice |

`meta` is free data you put on cards (rank, suit, cost, colour). The built-in deck gives every card `rank` (2–14,
aces high), `suit` and `color`. A reference to something that does not exist right now, such as the top of an empty
pile, is empty, and comparisons with it are false.

## 5. Effects and conditions

**Effects** change the table: `move`, `deal`, `shuffle`, `flip`, `lock`, `roll`, `setVariable`, `setResource`,
`message`, `endTurn`, `endPhase`, `gotoPhase` and `win`, plus `if` and `forEach` to build logic from them, `reveal` for
secret choices, `refuse` and `blindRound` for pieces moved by hand (section 3), `show` to put a card big on every
screen so it is read out (an event card drawn on your turn; players can do the same from the card's menu), and `lua`
to call a script (section 8).

**Conditions** ask questions: `zoneCount`, `compare`, `variable`, `resource`, `isCurrentPlayer`, `objectIn` and
`hasTag`, combined with `and`, `or` and `not`.

Each one is an object with a single key:

```json
{ "shuffle": { "object": "draw" } }
{ "not": { "isCurrentPlayer": { "player": "$actor" } } }
```

Match the Pile's draw action shows the usual pattern for a rulebook's "if the pile runs out" sentence:

```json
"effects": [
  { "if": { "condition": { "zoneCount": { "zone": "draw", "op": "==", "value": 0 } },
            "then": [ { "move": { "from": "discard", "take": "allButTop", "to": "draw" } },
                      { "shuffle": { "object": "draw" } } ] } },
  { "if": { "condition": { "zoneCount": { "zone": "draw", "op": ">", "value": 0 } },
            "then": [ { "move": { "from": "draw", "to": "$actor.hand", "faceUp": true } } ] } },
  { "endTurn": {} } ]
```

War's tie rule ("war!") puts one card face down for each player, then goes back to the battle phase with
`gotoPhase`. A player who cannot go to war loses through `win`:

```json
{ "forEach": { "players": true, "do": [
  { "if": { "condition": { "zoneCount": { "zone": "$it.deck", "op": "<", "value": 2 } },
            "then": [ { "win": { "player": "$other" } } ],
            "else": [ { "move": { "from": "$it.deck", "to": "$it.battle", "faceUp": false } } ] } } ] } },
{ "gotoPhase": { "phase": "battle" } }
```

## 6. Events, the end of the game, and the screen

`events` run at fixed moments: `onGameStart` (set up the table), `onTurnStart`, `onTurnEnd` and `onReveal` (after a
blind round is revealed). War counts its battles
in `onTurnEnd` with `{ "setVariable": { "name": "battles", "delta": 1 } }`.

`winConditions` are checked for each seat in turn (`$player`), either after every action (`anyCommand`) or at the end
of each turn (`turnEnd`):

```json
{ "when": { "zoneCount": { "zone": "$player.hand", "op": "==", "value": 0 } }, "winner": "$player", "checkAfter": "anyCommand" }
```

`ui` panels show labels, counters, images and buttons. Text in braces is filled in live, as each player sees it:

```json
{ "type": "label", "text": "Battle {$var.battles}" },
{ "type": "counter", "text": "South's deck", "value": "$seat.seat_south.deck.count" }
```

Each player sees a panel filled in for themselves through `$viewer`, and `visibleTo` keeps a panel, or one element of
it, to some seats. `"host"` in that list is the player running the game, seated or not, so the controls only the host
should press sit in the panel everyone reads (give such a button a `seat`, e.g. `"$current"`, for a host who only
watches):

```json
{ "id": "day", "title": "Day", "anchor": "top",
  "elements": [ { "type": "label", "text": "Day {$var.day}" },
                { "type": "button", "text": "Next day", "action": "next_day", "seat": "$current", "visibleTo": ["host"] } ] }
```

Hiding is honest play, as with hidden zones: it keeps the screen tidy, it does not stop a player who edits their game.
Lua screen panels do the same with XmlUI's `visibility="Host"`.

A reminder of your own secret pick, seen by you alone:

```json
{ "id": "my_pick", "title": "Your pick", "anchor": "right",
  "elements": [ { "type": "label", "text": "You chose: {$viewer.chose.pick_space}" },
                { "type": "label", "text": "Players who have picked: {$secrets}" } ] }
```

Players move each panel by its title bar and fold it to the bar; the game remembers where each player left it.

**The game's own words.** The lines the engine writes in the log ("Red wins.", "Blue chose in secret.") can be
written in the game's language under `game.messages`; `{seat}` and the other names in braces are filled in, and a line
left out stays in English:

```json
"game": { "id": "tornaris", "name": "Tornaris",
          "messages": { "win": "¡{seat} gana!", "choseInSecret": "{seat} eligió en secreto.", "chose": "{seat} eligió {option}." } }
```

The keys are `win`, `choseInSecret`, `chose` (`{seat}`, `{action}`, `{option}`), `blindRound`, `reveal` (`{count}`) and
`shows` (`{card}`).

**Light.** The room has two lights: the room's own (the lamp and its warm fill, on the walls, the floor and the table's
frame) and the table light on the felt and the pieces. The `light` effect changes one of them while playing, so night
can fall in the room while the cards stay readable. `scale` multiplies it (1 normal, 0 off) and `tint` colours it (three
numbers from 0 to 2); leaving both out puts it back to normal:

```json
{ "light": { "part": "room", "scale": 0.25, "tint": [0.6, 0.7, 1] } }
{ "light": { "part": "room" } }
```

Lua does the same with `Lighting.setRoomLight({ intensity = 0.25, color = { 0.6, 0.7, 1 } })` and
`Lighting.setTableLight({ ... })`. TTS's `Lighting.apply()` still scales both lights and the ambient at once.

## 7. Editing a rule by hand

1. Open the game file in any text editor.
2. Change the rule. For example, in `games/uno-lite.json`, deal 5 cards instead of 7: in `onGameStart`, change the
   `deal` effect's `"count": 7` to `"count": 5`.
3. Ask your agent to check it: "load_game games/uno-lite.json, then validate_rules". You can also start the MCP
   server with `--file` on that file and call `validate_rules`.
4. Fix what it reports. Every problem names where it is, what is wrong and usually how to fix it:

```json
{ "path": "events.onGameStart[1].dael", "code": "unknown_effect", "message": "Unknown effect 'dael'.", "suggestion": "Did you mean 'deal'?" }
```

5. Start the game (`start_game`) and play a few turns to see the change.

Rules are locked while a game is running; `reset_game` goes back to the setup. All the error codes are listed in
[error codes](../reference/error-codes.md).

## 8. When the format is not enough: Lua

Some rules are easier to write as a script: a card with a one-off power, a calculation, a custom panel. Table World
runs **Lua** scripts the way Tabletop Simulator does, so TTS scripting tutorials apply. The full list of functions,
with their TTS names, is in the [Lua API](../lua-api.md).

Where scripts live:

- The table's global script is `scripts.global`.
- An object's script is its `script` field; inside it, `self` is that object.
- A zone can have a script too.

```lua
function onLoad()
  self.createButton({ label = "Draw 2", click_function = "drawTwo", function_owner = self,
                      position = { 0, 0.2, 1.6 }, width = 900, height = 300, font_size = 150 })
end

function drawTwo(obj, color)
  self.deal(2, color)
  broadcastToAll(color .. " drew two cards")
end
```

**Events** call functions by name. The common ones are:

- `onLoad` and `onSave`;
- `onObjectDrop`, `onObjectPickUp` and `onObjectFlip`;
- `onObjectEnterZone` and `onObjectLeaveZone`;
- `onPlayerTurn`;
- `onChat`;
- collisions.

The [Lua API](../lua-api.md) lists them all.

**Script UI:** scripts can make buttons and text fields on objects (`createButton`, `createInput`), and screen panels
in TTS's XmlUI (`UI.setXml`, `UI.setAttribute`, `onClick` handlers).

### Lua inside the rules

A rule can hand a step to Lua with the `lua` effect. It calls a function of the global script with one table: `actor`
(who performs the action), `params` (the action's choices), `it` (inside a `forEach`) and `args` (from the effect):

```json
{ "id": "fight", "name": "Fight the monster", "actor": "current",
  "effects": [ { "lua": { "call": "fight", "args": { "health": 24 } } }, { "endTurn": {} } ] }
```

```lua
function fight(ctx)
  local dice = Game.roll(2, 6)
  local total = dice[1] + dice[2] + Game.getResource(ctx.actor, "strength")
  Game.log(ctx.actor .. " rolled " .. total)
  if total > ctx.args.health then
    Game.addResource(ctx.actor, "gold", 5)
  else
    Game.addResource(ctx.actor, "gold", -2)
  end
end
```

The function is part of the action: if it fails, or returns `false` or a reason (`return "Not enough gold."`), the
whole action is undone and the player sees the reason. The `Game` table reads and changes the game in play:

| Function | What it does |
|---|---|
| `Game.getResource(seat, id)`, `Game.setResource(seat, id, value)`, `Game.addResource(seat, id, delta)` | A player's resource (clamped to its min and max) |
| `Game.getVariable(name)`, `Game.setVariable(name, value)` | A game variable: numbers, text, or tables |
| `Game.getCurrentSeat()`, `Game.getPhase()`, `Game.getTurn()`, `Game.getSeats()`, `Game.getSeatColor(seat)` | Where the game is |
| `Game.endTurn()`, `Game.endPhase()`, `Game.gotoPhase(id)` | Move the game on (after the action's effects) |
| `Game.win(seat)`, `Game.isStarted()`, `Game.isOver()`, `Game.getWinner()` | The end of the game |
| `Game.perform(seat, action, params)` | Perform an action as a player, from a button or an event (not from inside another action) |
| `Game.getLegalActions(seat)` | The actions a player may perform now |
| `Game.getRole(seat)`, `Game.getTeam(seat)`, `Game.revealRole(seat, to, teamOnly)` | Hidden roles: read one, or show it to a seat (every seat when `to` is nil) |
| `Game.roll(count, sides)` | Dice for the script's own sums, as a list |
| `Game.log(text)` | A line in the game log |

The rest of the Lua API works too: a fight function can move a monster card, deal rewards or flip a token. Outside an
action (a button, `onObjectDrop`), the `Game` functions are moves of their own, sent to every player like any move.

Moving and finding objects works as in TTS:

```lua
local monster = getObjectFromGUID("monster_3")       -- an object by its id
monster.setPositionSmooth({ 0, 1, 5 })               -- TTS units, table surface at y = 0.96
monster.setRotation({ 0, 180, 0 })                   -- yaw 180: upright for the south seat
monster.setScale({ 1.5, 1, 1.5 })
for _, obj in ipairs(getAllObjects()) do              -- everything on the table
  if obj.hasTag("monster") then obj.flip() end
end
```

A card or board image reads upright for the player at the south seat when its yaw is 180 (the deck and card defaults);
each other seat turns with its hand. `place_object` with a `seat` does the turning for you.
Your agent sets scripts with `set_script` and tries them with `run_lua`, which runs against a copy of the table.

### The sandbox

Scripts are safe to run from a game someone sent you:

- They run only on the host, the player who loaded the game. Everyone else sees the result.
- They reach only the table: no files, no network, no other programs (`io`, `os`, `load` and `require` do not exist).
- A call still running after 5 seconds is stopped, so an endless loop cannot freeze the game. Errors are logged and
  never stop play.
- Everything a script does goes through the same commands a player's hands use, so it stays in sync for every
  player and can be saved and replayed.

### Differences from TTS Lua

- Positions are in TTS units (1 unit is about 3 cm) with the table surface at y = 0.96, so TTS scripts work
  unchanged.
- Not yet supported:
  - typing into XmlUI input fields drawn on objects;
  - scripted forces and velocities.

  These calls are accepted and do nothing.
- Player-colour chat (`broadcastToColor`) reaches everyone for now.
- Scripts never run on clients, so client-only tricks (a script reading what only one player sees) do not apply.

## 9. Rules format or Lua?

Use the rules format when you can. The game can then:

- list the legal moves;
- run thousands of test games headless;
- explain errors to your agent.

Use Lua for what the format cannot say yet, or when you import a TTS game, whose scripts come with it. A game can
use both: rules for the turn structure, Lua for a handful of special cards.
