# Milestones overview

One file per milestone. Each task has an id, a size (S ≤ 1 day, M 2–4 days, L 1–2 weeks), its dependencies, and a done check that must pass before the status changes. Update the status column here and in the milestone file in the same commit.

Status values: `todo` · `in progress` · `blocked (why)` · `done YYYY-MM-DD` · `dropped (why)` · `deferred → Mx`

| # | Milestone | Goal in one line | MVP | Tasks | Status |
|---|---|---|---|---|---|
| [M0](M0-foundations.md) | Foundations | `core/` tested with `dotnet test`, Unity loads `core.dll`, CI green | yes | T0.1–T0.6, T1.1–T1.6 | in progress (all done except T1.4: CI workflow written, needs a push for a green run) |
| [M1](M1-basic-table.md) | Basic 3D table | Solo table: cards, decks, dice, tokens; grab, flip, shuffle, deal; save/load | yes | T2.1–T2.11 | done 2026-10-23 (manual checklist and unverified TTS rows pending; T2.8 deferred) |
| [M2](M2-rules-and-mcp.md) | Rules + engine + MCP | Claude Code builds War and simplified UNO from a text rulebook | yes | T4.1–T4.8 | in progress (T4.1–T4.6 done) |
| [M3](M3-multiplayer.md) | Multiplayer | Two friends play over Relay (M3-lite); later Steam, asset streaming | lite only | T3.1–T3.7 | in progress (T3.1, T3.2, T3.4 done) |
| [M4](M4-host-assets.md) | Host assets | glTF and images at runtime, cache, bundle loader with URP remap | no | T5.1–T5.5 | todo |
| [M5](M5-lua-sandbox.md) | Sandboxed Lua | Scripts react to events; hostile scripts cannot hurt the host | no | T6.1–T6.4 | todo |
| [M6](M6-characters-and-dnd.md) | Physics characters + DnD | Active ragdolls, DnD assistant | no | T7.1–T7.2 | todo |
| [M7](M7-catalog-and-importer.md) | Catalog + TTS importer | Import local TTS mods privately; layered catalog | no | T8.1–T8.7 | todo |

## Order and gates

```
M0 ──► M1 ──► M2 ──► M3-lite ──► [PLAYTEST GATE: two friends over Relay] ──► Steam fee (T0.6)
                                                   │
                                                   ├──► M3 full (Steam transport, asset streaming)
                                                   ├──► M4 ──► M5 ──► M7
                                                   └──► M6 (after M3 full)
```

- Nothing past M3-lite starts before the playtest gate.
- Each milestone has at least one **simulation** task (marked ⚗). Run it before building on the result and record numbers in the milestone file.
- Tasks marked **human** need Miguel (accounts, money, clicks only possible in the Editor UI, playtests).

## Estimates for the MVP (M0 + M1 + M2 + M3-lite)

| Human time | Calendar |
|---|---|
| 10 h/week | 10–14 weeks |
| 25 h/week | 5–7 weeks |
| Full time | 3–5 weeks |

Context, decisions and risks: [../plan.md](../plan.md). Permanent rules: [../../CLAUDE.md](../../CLAUDE.md).
