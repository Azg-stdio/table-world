# M2 — Rules format, engine and MCP server

**Goal:** Claude Code (or Codex) builds a simple card game end to end from a plain-text rulebook, asking about ambiguities.
**Done when:** recorded demo: War rulebook as text → agent calls MCP tools → table set up in Unity → full game played with the engine enforcing the rules. Repeated with simplified UNO.
**In the MVP:** yes. **Status:** in progress (T4.1–T4.6 done). **Depends on:** M1 (T2.1, T2.2 at minimum).

Design reference: [../rules-schema.md](../rules-schema.md).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T4.1 | Rules schema v0: finalize `docs/rules-schema.md`, write `docs/rules-schema.v0.json` (JSON Schema), validator in `core/Rules` with LLM-readable errors (`path`, `code`, `message`, `suggestion`) | M | T2.1 | Validator tests: every error code has a test; War example validates | done 2026-10-22 |
| T4.2 | `core/Engine` rules interpreter: phases, turns, actions, preconditions, effects, conditions, events, win checks; transactional commands | L | T4.1, T2.2 | One test per effect and per condition; War plays headless | done 2026-10-22 |
| T4.3 ⚗ | War and simplified UNO as rules files; 1 000 headless games with random players | S | T4.2 | All games terminate; card count conserved; per-phase invariants hold | done 2026-10-22 |
| T4.4 | `mcp-server/` (net9, `ModelContextProtocol` 2.3.0, stdio): `create_deck`, `define_zone`, `add_phase`, `add_action`, `set_win_condition`, `place_object`, `deal`, `get_table_state`, `validate_rules`, `ask_host` → `needs_clarification` | M | T4.1 | Contract test per tool over stdio | done 2026-10-22 |
| T4.5 | WebSocket bridge Unity ↔ MCP (`ws://127.0.0.1:<port>`): commands, events, reconnection, session token; Unity renders the engine's state | M | T4.4, T2.7 | Place objects from the MCP server and see them in the Editor | done 2026-10-22 |
| T4.6 | Declarative UI v0 from the rules JSON rendered with UI Toolkit (generated `VisualElement` tree, shared USS theme): label, counter, button, image | M | T2.10, T4.2 | War score panel renders and the End Turn button works | done 2026-10-22 (War has no End Turn: its panel's "play top card" buttons were pressed instead) |
| T4.7 | `docs/agent-guide.md`: configuring the MCP server in Claude Code/Codex, what to ask, sample rulebooks | S | T4.4 | A fresh Claude Code session follows it and reaches a playable table | in progress (guide, `.mcp.json` and sample rulebooks written; the fresh-session check is part of T4.8) |
| T4.8 **human** + agent | End-to-end demo with Claude Code; log frictions and the questions the agent asked | S | T4.5, T4.7 | Demo recorded; friction list filed as tasks | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | MCP C# SDK stdio handshake | initialize / tools/list / tools/call OK |
| 2026-10-22 | T4.3 War, 1 000 games, random players (`TW_GAMES=1000`) | all terminate; 52 cards after every action; table valid at the end. First run: South won 609/391 and 90 % of games hit the 500-battle cap: the rule collected battle piles in fixed seat order. Fixed (winner's pile first): median 494 actions (~247 battles), p99 1 092, max 1 112; South 487 / North 513; 162 ended by the 500-battle house rule |
| 2026-10-22 | T4.3 simplified UNO, 1 000 games | all terminate; 52 cards after every action; median 546 actions, p99 3 521, max 4 405; South 516 / North 484 |
| 2026-10-22 | T4.5 bridge end to end (`TW_UNITY=1 dotnet test mcp-server.tests --filter BridgeEndToEnd`, Unity in Play mode, unfocused) | game window connected within 2 s of the server starting; new_game + create_deck + place_object appeared in the Editor; UNO-lite loaded, started and played 6 turns in the window (turn 7, hands 6 and 4); test 5 s |
| 2026-10-22 | War without a cap, seed 1 | still running after 9 951 battles (cards drift around 34/18): War is mean-reverting, so the sample file adds the 500-battle house rule |

## Assumptions recorded during implementation

| Task | Assumption |
|---|---|
| T4.1 | Effects and conditions are single-key objects. The rules live in the same document as the table (`TableState`), and game progress in its `session` block, so a save is a resumable game. `limitPerTurn` became `limitPerPhase` (War needs a second play in the same turn after a tie). `deal` uses `seats` + `toZone`/`toTag` instead of `to: allPlayers`. Not in v0: `spawn`, `destroy`, `prompt`, `reveal`/`hide`, `lua`, zone/physical events. `docs/rules-schema.v0.json` is generated from the model. |
| T4.2 | `GameEngine` wraps `TableEngine`: free-play commands still work during a game. Rollback snapshots objects + zones + session as JSON before each rules command (≈0.35 ms per action with 52 cards; fine for the MVP, revisit for big tables). |
| T4.3 | CI runs 200 games per sample (≈1 min); the 1 000-game run is `TW_GAMES=1000 dotnet test --filter GamePlayTests`. |
| T4.4 | 24 tools: `new_game`, `create_deck`, `place_object`, `define_zone`, `set_variables`, `add_resource`, `set_turn`, `add_phase`, `add_action`, `add_event`, `set_win_condition`, `remove`, `validate_rules`, `get_rules_document`, `set_rules_document`, `get_rules_reference`, `ask_host`, `start_game`, `perform_action`, `deal`, `reset_game`, `get_table_state`, `save_game`, `load_game`. Rule pieces are passed as JSON strings in the documented format (the agent reads `get_rules_reference`). Edits with shape errors are rejected; edits that leave the document incomplete are kept and the problems listed. `ask_host` returns `needs_clarification` and tells the agent to ask the human (MCP elicitation can replace it later). Until the Unity bridge (T4.5) the server plays games in-process. |
| T4.5 | The MCP server listens (127.0.0.1, random port) and writes `{port, token, pid}` to `%LOCALAPPDATA%/TableWorld/bridge.json`; the game polls it every 2 s and connects with `ClientWebSocket` (newest server wins; reconnects when a new agent session starts). The game is the authoritative host: authoring edits are pushed as `load` (full document), play commands (`start`, `perform`, `deal`) execute in the game and the reply carries the new document; changes made in the window are reported as `state` (batched 0.5 s). Without a window the server plays headless; `--no-bridge` disables the link (tests). The HUD shows the turn line, a Start button and one button per legal action (hot seat: the current seat). Known gap: hand slots are not re-laid out after a card leaves a hand. |
| T4.6 | `ui` is a typed model (`UiDef`): panels with label, counter, button, image. Rendered by `GameUiRenderer` into the HUD; styling only through Hud.uss classes, except images (runtime textures) which need `style.backgroundImage`. Buttons are enabled from `LegalActions`. Check (Play mode): War panel showed Battle/South's deck/North's deck live; pressing both buttons resolved a battle (South 27, North 25). |
