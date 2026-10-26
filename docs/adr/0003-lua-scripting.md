# ADR 0003 — Lua scripts run on the host only and change the table through commands

Status: **proposed** (2026-10-24, M5 / T6.1–T6.2; written by the agent overnight, to be confirmed by Miguel).

## Context

TTS games are scripts as much as objects: 5 of the 6 local mods have a global Lua script, and the most used API calls
(`UI.setAttribute`, `getName`, `getObjectFromGUID`, `getObjects`, `broadcastToAll`, `createButton`, `Wait.time`, …) are
listed in CLAUDE.md §6. CLAUDE.md fixes the interpreter (MoonSharp 2.0.0, sandboxed, only our API) and the network model
(authoritative host, ADR 0002: clients send commands, the host orders them and broadcasts the result with a state hash).

Two questions are open: **where** scripts run, and **how** they change the table.

## Options

| | Run on every peer | Run on the host only |
|---|---|---|
| Determinism | every peer must run the same scripts in the same order with the same timers and random numbers, or hashes diverge | not needed: peers receive the commands the scripts issued |
| Timers (`Wait.time`) | frame and clock differences between peers break lockstep | one clock, the host's |
| Hostile or buggy scripts | every peer exposed | only the host; peers only see validated commands |
| Latency | none for local effects | a client's action that triggers a script waits one round trip for the script's effects (TTS behaves the same: scripts run on the host) |

| | Scripts mutate state directly | Scripts issue commands |
|---|---|---|
| Sync | needs a new "state diff" message | none: commands already travel, are hashed and logged |
| Validation | scripts can produce invalid tables | the engine validates every change |
| Game log, undo, rewind, saves | bypassed | work unchanged |

## Decision

- Scripts run **on the host only**, in `core/Scripting/ScriptHost` (no Unity), one MoonSharp `Script` per script
  (the global one, `scripts["global"]`, and each object's `script`), with `CoreModules.Preset_SoftSandbox` (no `io`, no
  `os` beyond time, no `load`, `dofile` or `require`).
- Every call (top level, event, timer, `call` between scripts) runs as a coroutine that yields every 10 000 instructions
  and is abandoned after 5 s of running (2 000 000 instructions until 2026-10-25, which stopped Pokémon Sinnoh's real
  Start Game setup halfway); `string.rep` refuses strings over 1 000 000 characters; scripts reacting to the
  commands their own commands caused stop 4 levels deep. Errors and stops are logged, never thrown into the host.
- Scripts **read** the host's table directly and **change** it only by issuing ordinary commands (`setPosition` → `MoveCommand`,
  `flip` → `FlipCommand`, `takeObject` → `TakeCommand`, …) through the host's route, so changes are validated, ordered,
  hashed, broadcast, logged and saved like a player's. The one exception, `setGMNotes`, writes host-only data (no command
  carries GM notes yet).
- The API follows TTS names, argument orders and units (TTS units, the table surface at y = 0.96), and accepts both
  `obj.method()` and `obj:method()` as TTS does. It is documented in `docs/lua-api.md`.
- Pure announcements a script must hear but that change nothing (a player let go of an object: `DropCommand`) are commands
  with no state effect, so they reach the host the same way.

## Consequences

- Clients stay simple and safe; scripts cannot desync the table.
- A client sees a script's effects one round trip after its own action triggers them.
- Script state (Lua variables) lives only on the host: saving it needs TTS's `onSave` / `script_state` (planned with T6.3).
  Rewinding or loading a table reloads the scripts and runs `onLoad`.
- Script UI (`createButton`, `UI.*`) must also be host-owned and described as table state (`ui` in the rules schema) so
  clients draw it; that is T6.3 work.
- Memory is bounded only indirectly (step budget, string cap). A script can still build large tables within its budget;
  if mods show it, add a per-script allocation estimate.
