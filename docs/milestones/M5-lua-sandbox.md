# M5 — Sandboxed Lua

**Goal:** logic that does not fit the rules JSON runs in Lua with access only to our API.
**Done when:** per-object and global scripts react to `onLoad`, `onDrop`, `onEnterZone`, `onTurnStart`; a hostile script (infinite loop, `os`, `io`, 1 GB of strings) cannot take the host down.
**Status:** in progress (2026-10-24): sandbox and first API in core (ADR 0003, `docs/lua-api.md`); the host (or a player alone) runs a loaded table's scripts in the game; drops, zones, flips, rolls, turns and timers reach them, `broadcastToAll` reaches chat. **Depends on:** M2 (engine events).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T6.1 | MoonSharp 2.0.0 in `core/Scripting`: `Preset_SoftSandbox`, step limit per call (`IDebugger` pattern verified 2026-10-22), memory limit, max time per event | S | T4.2 | Hostile-script tests green | done (2026-10-24): `core/Scripting/ScriptHost`, soft sandbox, coroutine step budget (10k per slice, 2M per call), `string.rep` cap, depth limit; 13 tests |
| T6.2 | Exposed API, typed and documented in `docs/lua-api.md`: objects, zones, players, turns, UI, `Wait` | M | T6.1, T4.6 | One test per function | in progress: objects, zones, events, `Wait`, `JSON`, `Global`, cross-script calls, running in the game on the host (PlayMode test); script UI next |
| T6.3 | TTS compatibility layer in real usage order: `UI.setAttribute`, `getName`, `getObjectFromGUID`, `getLuaScript`, `broadcastToAll`, `getObjects`, `getGMNotes`, `UI.getAttribute`, `createButton`, `getPosition`, `editButton`, `onLoad`, `setValue`, `Wait.time`, `setRotation`, `getRotation`, `getGUID`, `JSON.decode`, `call`, `Wait.condition` | M | T6.2 | Each function has a test mirroring TTS semantics | in progress: object, zone, notebook, state, scale, tint, bounds, coroutine, save/load state and button calls done (buttons drawn on objects, clicked by any player); `UI.*` with XmlUI on screen and on objects done (ADR 0004); next physics casts, collision events, spawnObject |
| T6.4 ⚗ | Run the global scripts of the 6 local mods in the sandbox; count missing functions | S | T6.3 | Coverage number recorded | first measure 2026-10-24: 57 % of the local mods' calls (weighted) covered; `tools/tts-analysis/lua_coverage.py`, `docs/data/lua-coverage-2026-10-24.md` |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | MoonSharp sandbox on netstandard2.1 | 7 tests green |
| 2026-10-24 | T6.4 coverage (static, from the requirement report) | 12 002 of 21 049 calls (57 %) → 15 992 (76 %) after the compatibility additions; left: `UI.*` (3 467), buttons (928), physics casts, collision events, `spawnObject` |
