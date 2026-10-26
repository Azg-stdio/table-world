# ADR 0010 — Hidden play, and Lua inside the rules

Status: **accepted** (2026-10-25, M2 / T4.12–T4.13; asked for by Miguel: "as many options as possible for the general
rules of all games", "Lua should give a LOT of freedom").

## Context

Two things Tornaris needs, and many games share, did not fit the rules format:
- **Simultaneous hidden choices.** Everyone picks a space in secret, then all are revealed. On a physical table this is
  arrows placed face down, or chips in a closed fist.
- **Logic too rich for declarative effects.** A group dice total plus 42 equipment cards against a monster's health;
  20 monster abilities.

Lua already ran on the host (ADR 0003), but only as TTS-style table scripts, blind to the rules engine.

## Decision

**Hidden play**, at two levels:
- **Rules:** an action marked `secret` records the seat's choice (`Session.Secrets`) instead of running it. A `reveal`
  effect runs every recorded choice in turn order from the current seat, binding `$actor` and `$param` to it. Choices
  are checked when made.
- **Table:**
  - A **blind round** (`TableState.Blind`, `BlindRoundCommand`): while it runs, whatever a seat moves, takes, flips,
    drops or deals onto the table becomes invisible to the other seats. Cards in hands and pieces in bags are left
    alone. The reveal restores each piece's previous `invisibleTo`.
  - **Hide from others** sets a single piece's `invisibleTo` (TTS's own primitive).
  - Hidden pieces are neither drawn nor pickable for those seats (existing view rule).

**Lua inside the rules:**
- **The `lua` effect** calls a global function with `{actor, params, args, it}`. It runs inside the action's
  transaction. If the function fails, or returns `false` or a reason string, the whole action is refused and rolled back.
- **The Lua `Game` table** reads the game (resources, variables, seats, phase, legal actions) and changes it through
  `ScriptRulesCommand`, a host-only command.
  - Inside a `lua` effect, the command (and any table command the script issues) applies directly to the transaction.
  - Elsewhere (buttons, events) it is a command of its own, sent to every peer like any other.
  - `Game.roll` uses its own random generator, so it never shifts the table's deterministic shuffles.
- **Networking:** only the host runs scripts. A command that ran Lua sets `CommandResult.ScriptsRan`, and the host
  attaches its state hash to that order. Peers replaying it skip the `lua` effect, see the hash differ and ask for the
  table (one resync per Lua action).
- **MCP:**
  - The server runs the table's scripts for headless games.
  - `set_script` stores Lua after a syntax check.
  - `run_lua` runs code against a copy of the table, so the agent can test a function without changing the game.
  - `validate_rules` checks that each called function is defined in the global script (a text search, not a parse).

## Addendum (2026-10-26): moves the rules react to

Clicking a button to go to a space felt wrong in Tornaris; players move the pawn. Zones now carry rules:
- `onEnter` / `onLeave` effects run during a game when a piece is put in a zone or taken out (`$it` the piece,
  `$actor` the mover).
- A `refuse` effect undoes the move with a reason, and the view snaps the piece back.
- During a blind round, these effects are held in `Session.HeldZoneMoves` (the last move of each piece). At the reveal
  they run in turn order, for pieces still where they were put, followed by the `onReveal` event.
- `blindRound` effects start and reveal a round from the rules.

On a table with zone rules, a table command during a game runs as a rules transaction. That costs a snapshot of the
objects per move, which is fine for rules games, and keeps a refused move atomic. Transactions now also restore
`TableState.Blind`.

## Consequences

- Secret choices and blind moves are hidden by the view, not by the protocol: peers receive the full table, as they
  already do for hand cards. This is honest play, as in TTS. A modded client could read them.
- Each Lua-driven action costs peers one snapshot. That is fine at board-game pace and on rules-sized tables. A heavy
  imported table (2 700 objects) would feel it; the fix then is a state diff instead of a snapshot.
- `Game.perform` cannot start an action from inside another action's Lua. It is refused, with a hint to change the game
  directly.
- `Wait.time` timers do not tick in headless MCP games (no clock); scripts that need them are tested in the game window.
