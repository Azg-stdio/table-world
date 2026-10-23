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

## 3. How the agent works (for the agent)

1. `get_rules_reference` — read it once; it is the format (objects, zones, phases, actions, effects, conditions, `$` references, events, win conditions, UI, error codes).
2. `new_game` — name, number of seats. Seats are `seat_south`, `seat_north`, `seat_east`, `seat_west`, each with a hand zone `hand_<side>` (so `$actor.hand` works).
3. Table: `create_deck` (`standard52` / `standard54` give the builtin French deck with `meta.rank` 2–14, aces high, `meta.suit`, `meta.color`), `place_object`, `define_zone`. Per-seat piles: tag them `<role>:<seatId>` (e.g. `deck:seat_south`) and the rules can say `$actor.deck`, `$it.deck`, `$seat.seat_north.deck`.
4. Rules: `set_variables`, `add_resource`, `add_action` (what a player can do; preconditions and effects), `add_phase` (which actions are allowed and when the phase ends), `add_event` (`onGameStart` for shuffling and dealing), `set_win_condition`, optionally a `ui` panel through `set_rules_document`.
5. `validate_rules` until it returns no problems. Every problem has a path, a code and usually a suggestion ("Did you mean 'shuffle'?"). Edits with shape errors are not applied; edits that leave the document incomplete are kept and listed as problems.
6. `start_game`, then `get_table_state`: it lists every seat's legal actions with their parameter choices. `perform_action` plays one; the human can also play in the window (HUD buttons, or the table itself).
7. When the rulebook does not say something (tie rules, what happens when the draw pile runs out, aces high or low), call `ask_host` and ask the human. Record the answer in the rules, e.g. as a `message` at game start or in `game.description`.

## 4. Things that trip agents up

- Effects and conditions are single-key objects: `{ "move": { "from": "deck", "to": "$actor.hand" } }`, not `{ "type": "move", ... }`.
- `and` / `or` take an array; `not` takes one condition.
- An action is only allowed in the phases that list it.
- `advanceWhen` takes exactly one rule. `currentPlayerEnds` needs an `endTurn` or `endPhase` effect somewhere.
- Cards moved into a deck take that deck's orientation; create discard piles with `faceUp: true`.
- A deck stays on the table when empty, so `$actor.deck` keeps working.
- During play the rules are locked: `reset_game` returns to the setup.

## 5. Troubleshooting

| Symptom | Fix |
|---|---|
| `gameWindow: not connected` | The Table scene is not in Play mode, or the window started before the server: it retries every 2 s. The server writes its address to `%LOCALAPPDATA%/TableWorld/bridge.json`. |
| `game_unreachable` | The window stopped answering (Play mode exited). Restart Play mode, or close it to play headless. |
| The agent cannot see the tools | Rebuild the server (`dotnet build mcp-server -c Release`) and restart the agent session. |
| A long game (War) | The sample War ends after 500 battles (more cards wins); ask the agent for the same house rule if your game can loop. |
