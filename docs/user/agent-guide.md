# Building a game with an AI agent

Table World exposes an MCP server. You bring your own agent (Claude Code, Claude Desktop, Codex): you give it a rulebook in plain text, it builds the table and the rules through the server, asks you when the rulebook is unclear, and then you play the game in the Table World window.

## 1. Set up (once)

Requirements: .NET SDK 9, the repository, and Unity 6.6 with the project open (or a game build) if you want to see and play the table.

Build the server:

```bash
dotnet build mcp-server -c Release
```

Do not launch it with `dotnet run`: build output on stdout would corrupt the MCP channel.

**Claude Code**, inside this repository: the project's `.mcp.json` already declares the server (Claude Code asks you to approve it the first time). Elsewhere:

```bash
claude mcp add table-world -- dotnet "<repo>/mcp-server/bin/Release/net9.0/TableWorld.Mcp.dll" --file "<repo>/games/my-game.json"
```

**Codex** (`~/.codex/config.toml`):

```toml
[mcp_servers.table-world]
command = "dotnet"
args = ["<repo>/mcp-server/bin/Release/net9.0/TableWorld.Mcp.dll", "--file", "<repo>/games/my-game.json"]
```

`--file` is optional: with it, every change is saved to that file and it is reloaded next time.

**The game window:** open `unity/` in Unity, open `Assets/_Project/Scenes/Table.unity` and press Play (or run a build). The window connects to the agent's server by itself within a couple of seconds; `get_table_state` then reports `gameWindow: connected`. Without a window the server still works: games run headless and you can inspect them with `get_table_state`.

## 2. What to ask

Paste the rulebook and ask for the game, for example:

> Here is the rulebook of my card game. Build it in Table World with the table-world tools: read get_rules_reference first, set up the table and the rules, validate them, and ask me (ask_host) whenever the rulebook is ambiguous instead of guessing. Then start a game so I can play it in the window.

Sample rulebooks to try: `docs/rulebooks/war.txt` and `docs/rulebooks/uno-lite.txt`. Finished versions of both are in `games/` (compare with what the agent builds).

The server also offers ready-made **prompts**, which most agents show as slash commands or a prompt picker:

- `review_rulebook`: reads your rulebook and lists what is ambiguous or missing, with the questions to answer, before
  anything is built.
- `build_game_from_brief`: builds the game from a filled [game brief](game-brief-template.md) or a rulebook, validates
  it and plays a few turns.

And **resources** an agent can read on its own: the rules reference, the game brief template and this guide.

## 3. How the agent works (for the agent)

1. `get_rules_reference` — read it once; it is the format (objects, zones, phases, actions, effects, conditions, `$` references, events, win conditions, UI, error codes).
2. `new_game` — name, number of seats (1–12). Up to four seats are `seat_south`, `seat_north`, `seat_east`, `seat_west`, each with a hand zone `hand_<side>` (so `$actor.hand` works); five or more are `seat_1`…`seat_12` clockwise from the south side with hands `hand_<n>`, on a table that grows to fit them. One table serves every player count: give it the most seats the game takes and set `minPlayers`; `start_game` (`seats` or `players`) picks who plays, and empty seats are skipped by turns, deals and `forEach`.
3. Table: `add_asset` for your own images (card art, sprite sheets with `columns`/`rows`, boards, tokens), models, music and PDFs, copied next to the game file; `create_deck` (its `faceAsset`/`backAsset` take those ids; `standard52` / `standard54` give the builtin French deck with `meta.rank` 2–14, aces high, `meta.suit`, `meta.color`), `place_object`, `define_zone`. Per-seat piles: tag them `<role>:<seatId>` (e.g. `deck:seat_south`) and the rules can say `$actor.deck`, `$it.deck`, `$seat.seat_north.deck`.
   Pieces players handle: `create_supply` (coins, beads, gems, cubes, pawns as a bank, bag or pile), `new_game` `dicePerSeat` (each seat's dice in its colour), zones with `diceTotal` (the table shows the dice thrown in them, per player), `place_object` with `seat` (in front of a player, facing them), `create_text_deck` for cards made of words.
4. Rules: how much the game enforces or does by itself is the designer's choice; when the brief does not say, ask (`ask_host`). Besides actions, the format has zone `onEnter`/`onLeave` effects (moving a piece is the move; `refuse` undoes it), `secret` actions with `reveal`, `blindRound` effects, `show` (a card big on every screen), and `lua` for anything else. `set_variables`, `add_resource`, `add_action` (what a player can do; preconditions and effects), `add_phase` (which actions are allowed and when the phase ends), `add_event` (`onGameStart` for shuffling and dealing), `set_win_condition`, optionally a `ui` panel through `set_rules_document`.
   Hidden roles and social games (Avalon, Werewolf, Wink Murder): `set_roles` (teams, roles with counts or `fill`, optional roles, team sizes per player count, who knows whom), `$actor.role` / `$seat.<id>.team` in conditions, `revealRole` / `setRole` / `swapRoles` / `setTeam`, `message` with `to` for a private line, an action with a `vote` block, phases with `awake` (eyes closed) and `voice`, `eliminate` and `$alive` counts, `onGesture` (a wink at a face), `win` and win conditions with `team`, `set_variables` with `secret`. The format reference has a section on each (`get_rules_reference`: Roles, Votes, Eyes closed and voice, Gestures, Secret variables); in Table World's source, `games/avalon.json`, `werewolf.json`, `wink-murder.json` and `secret-dictator.json` are worked examples and `tools/mcp/build_*.py` the calls that built them.
5. Put the rules where players can read them: `set_notebook` pages (a page per chapter of the rulebook) or the designer's PDF as a `Pdf` object.
6. Look at what you placed: `screenshot` (`view` `top` for the layout, `seat` with a seat id for what that player sees, `object` with a `target` to read a card) whenever the game window is open. Check that things face their seat, do not overlap and are readable; you cannot see the table otherwise.
7. `validate_rules` until it returns no problems. Every problem has a path, a code and usually a suggestion ("Did you mean 'shuffle'?"). Edits with shape errors are not applied; edits that leave the document incomplete are kept and listed as problems.
8. `start_game`, then `get_table_state`: it lists every seat's legal actions with their parameter choices. `perform_action` plays one; the human can also play in the window (HUD buttons, or the table itself).
9. When the rulebook does not say something (tie rules, what happens when the draw pile runs out, aces high or low), call `ask_host` and ask the human. Record the answer in the rules, e.g. as a `message` at game start or in `game.description`.

## 4. Things that trip agents up

- Players read names, never ids: give every object, deck, card, zone, phase, action and resource a `name` in the game's own language ("Night events", "Eventos de noche"), with no underscores. Ids (`eventos_noche`) are for rules and scripts; where a name is missing the game turns the id into words, which reads worse.
- Effects and conditions are single-key objects: `{ "move": { "from": "deck", "to": "$actor.hand" } }`, not `{ "type": "move", ... }`.
- `and` / `or` take an array; `not` takes one condition.
- An action is only allowed in the phases that list it.
- `advanceWhen` takes exactly one rule. `currentPlayerEnds` needs an `endTurn` or `endPhase` effect somewhere.
- Cards moved into a deck take that deck's orientation; create discard piles with `faceUp: true`.
- A deck stays on the table when empty, so `$actor.deck` keeps working.
- During play the rules are locked: `reset_game` returns to the setup.
- A vote has no `secret` and no `effects`: its choice is the `choice` parameter, and what follows the count goes in `vote.onResult`.
- Every machine receives the game's variables. What players must not see goes in `set_variables` `secret`; what one player alone should know about themselves (winked at, poisoned) goes in a hidden trait, a role with a `set` (`$actor.role.fate`), since a secret variable reaches nobody, that player included.
- A vote's log lines are public unless it is `private`: a night vote among a team reveals that team otherwise.
- In a game with rules, a card drawn from a deck gets a new id on the table (its deck id would name it); read cards through parameters and `meta`, not by deck id.

## 5. Troubleshooting

| Symptom | Fix |
|---|---|
| `gameWindow: not connected` | The Table scene is not in Play mode, or the window started before the server: it retries every 2 s. The server writes its address to `%LOCALAPPDATA%/TableWorld/bridge.json`. |
| `game_unreachable` | The window stopped answering (Play mode exited). Restart Play mode, or close it to play headless. |
| The agent cannot see the tools | Rebuild the server (`dotnet build mcp-server -c Release`) and restart the agent session. |
| A long game (War) | The sample War ends after 500 battles (more cards wins); ask the agent for the same house rule if your game can loop. |
