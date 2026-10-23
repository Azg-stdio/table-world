# M2 — Rules format, engine and MCP server

**Goal:** Claude Code (or Codex) builds a simple card game end to end from a plain-text rulebook, asking about ambiguities.
**Done when:** recorded demo: War rulebook as text → agent calls MCP tools → table set up in Unity → full game played with the engine enforcing the rules. Repeated with simplified UNO.
**In the MVP:** yes. **Status:** todo. **Depends on:** M1 (T2.1, T2.2 at minimum).

Design reference: [../rules-schema.md](../rules-schema.md).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T4.1 | Rules schema v0: finalize `docs/rules-schema.md`, write `docs/rules-schema.v0.json` (JSON Schema), validator in `core/Rules` with LLM-readable errors (`path`, `code`, `message`, `suggestion`) | M | T2.1 | Validator tests: every error code has a test; War example validates | todo |
| T4.2 | `core/Engine` rules interpreter: phases, turns, actions, preconditions, effects, conditions, events, win checks; transactional commands | L | T4.1, T2.2 | One test per effect and per condition; War plays headless | todo |
| T4.3 ⚗ | War and simplified UNO as rules files; 1 000 headless games with random players | S | T4.2 | All games terminate; card count conserved; per-phase invariants hold | todo |
| T4.4 | `mcp-server/` (net9, `ModelContextProtocol` 2.3.0, stdio): `create_deck`, `define_zone`, `add_phase`, `add_action`, `set_win_condition`, `place_object`, `deal`, `get_table_state`, `validate_rules`, `ask_host` → `needs_clarification` | M | T4.1 | Contract test per tool over stdio | todo |
| T4.5 | WebSocket bridge Unity ↔ MCP (`ws://127.0.0.1:<port>`): commands, events, reconnection, session token; Unity renders the engine's state | M | T4.4, T2.7 | Place objects from the MCP server and see them in the Editor | todo |
| T4.6 | Declarative UI v0 from the rules JSON rendered with UI Toolkit (generated `VisualElement` tree, shared USS theme): label, counter, button, image | M | T2.10, T4.2 | War score panel renders and the End Turn button works | todo |
| T4.7 | `docs/agent-guide.md`: configuring the MCP server in Claude Code/Codex, what to ask, sample rulebooks | S | T4.4 | A fresh Claude Code session follows it and reaches a playable table | todo |
| T4.8 **human** + agent | End-to-end demo with Claude Code; log frictions and the questions the agent asked | S | T4.5, T4.7 | Demo recorded; friction list filed as tasks | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | MCP C# SDK stdio handshake | initialize / tools/list / tools/call OK |
| — | T4.3 1 000 games | — |
