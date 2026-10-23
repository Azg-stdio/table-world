# M5 — Sandboxed Lua

**Goal:** logic that does not fit the rules JSON runs in Lua with access only to our API.
**Done when:** per-object and global scripts react to `onLoad`, `onDrop`, `onEnterZone`, `onTurnStart`; a hostile script (infinite loop, `os`, `io`, 1 GB of strings) cannot take the host down.
**In the MVP:** no. **Status:** todo. **Depends on:** M2 (engine events), playtest gate.

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T6.1 | MoonSharp 2.0.0 in `core/Scripting`: `Preset_SoftSandbox`, step limit per call (`IDebugger` pattern verified 2026-10-22), memory limit, max time per event | S | T4.2 | Hostile-script tests green | todo |
| T6.2 | Exposed API, typed and documented in `docs/lua-api.md`: objects, zones, players, turns, UI, `Wait` | M | T6.1, T4.6 | One test per function | todo |
| T6.3 | TTS compatibility layer in real usage order: `UI.setAttribute`, `getName`, `getObjectFromGUID`, `getLuaScript`, `broadcastToAll`, `getObjects`, `getGMNotes`, `UI.getAttribute`, `createButton`, `getPosition`, `editButton`, `onLoad`, `setValue`, `Wait.time`, `setRotation`, `getRotation`, `getGUID`, `JSON.decode`, `call`, `Wait.condition` | M | T6.2 | Each function has a test mirroring TTS semantics | todo |
| T6.4 ⚗ | Run the global scripts of the 6 local mods in the sandbox; count missing functions | S | T6.3 | Coverage number recorded | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | MoonSharp sandbox on netstandard2.1 | 7 tests green |
| — | T6.4 coverage | — |
