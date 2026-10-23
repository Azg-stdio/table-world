# Project plan — context, decisions, risks

The work itself (milestones and tasks) lives in **[milestones/README.md](milestones/README.md)**, one file per milestone. This file holds what does not change per task: how to work, decisions, what is verified, risks.

Last updated: 2026-10-22. Planning closed; implementation starts at T1.1.

## For the implementing agent (read first)

1. Read `CLAUDE.md`, then this file, then `milestones/README.md`, then the milestone you are working on. Design drafts: `rules-schema.md`, `tts-mapping.md` (you own them from T1.3 on).
2. Work the **Playtest MVP** in order: M0 → M1 → M2 → M3-lite. Nothing past M3-lite before the playtest gate (T3.6).
3. A task is done only when its **done check** passes. Record the date in the milestone file's status column and in `milestones/README.md`. If a check was skipped, write that instead of `done`.
4. Logic lives in `core/` with an xUnit test first. Unity is view, physics, network and UI. If something in `core/` needs Unity, stop and redesign.
5. Scenes, prefabs and assets are created from editor scripts or the Unity CLI, never by hand-editing serialized files. If a step is only possible in the Editor UI, write exactly what the human must click and continue.
6. One commit per task, message starting with the task id (`T1.1: ...`). Architecture changes get an ADR in `adr/` in the same commit.
7. Ask the human only for: decisions that change the plan, anything that costs money, anything needing their accounts, and playtests. Otherwise decide, record the assumption in the milestone file, continue.
8. Run each milestone's ⚗ simulation before building on its result. Record numbers in the milestone's simulation table, not adjectives.

## Decisions (2026-10-22)

| # | Question | Decision |
|---|---|---|
| Q1 | Rules + MCP before multiplayer? | **Yes.** M1 is built as intents → engine → state so M3 is a transport layer. |
| Q2 | Distribution | **Steam**, fee paid only after the playtest gate (T3.6). Friends test over **Unity Relay** join codes. Steam transport tested on **test app ID 480** before owning an app. |
| Q3 | JSON in `core/` | **Newtonsoft.Json 13.0.3**; Unity provides it via `com.unity.nuget.newtonsoft-json` 3.2.2. |
| Q4 | UI | **Own declarative JSON UI rendered with UI Toolkit**. TTS XmlUI gets a translator in M7. |
| Q5 | Docs language | **English**, code identifiers too. |
| Q6 | Name | **Table World** (working title). Repo `table-world`. |
| Q7 | Hours per week | Unknown; estimates given per band in `milestones/README.md`. |
| Q8 | First deliverable | **Playtest MVP**: M0 + M1 + M2 + M3-lite. Full scope stays as the long game. |

## Verified (simulations run on 2026-10-22)

| What | Result | Where |
|---|---|---|
| Unity version of the 70 local AssetBundles | 69× Unity 5.6.x, 1× 2019.1. TTS itself runs on 6000.3.10f1 | `adr/0001-assetbundles.md` |
| Bundles in Unity 6.6 + URP | All 5 sampled load; `Standard` renders pink; URP/Lit remap → 0 magenta pixels | ADR 0001, `../tools/unity-probes/` |
| 6 local mods | 14 676 objects; type and Lua API rankings | `../tools/tts-analysis/analyze_mods.py` |
| `core/` netstandard2.1 + xUnit net9 | green on SDK 9.0.305 | scratch → T1.1 |
| MoonSharp 2.0.0 on netstandard2.1 | sandbox blocks `os`/`io`/`load`; `IDebugger` step limit aborts loops; 7 tests | scratch → M5 |
| MCP C# SDK 2.3.0 stdio | initialize, tools/list, tools/call OK | scratch → M2 |
| Unity registry | glTFast 6.20.0, NGO 2.13.3, Transport 6.6.0, Multiplayer Play Mode 3.0.0, Multiplayer Tools 2.2.12, Multiplayer Center 2.1.0, Newtonsoft 3.2.2, Test Framework 1.8.0 | `unity command package search` |
| Unity CLI `script eval_file` | works as a method body: no `using`, qualified names, ends in `return` | `CLAUDE.md` §4 |
| Steam Direct timeline | paperwork 15–30 days; release ≥ 30 days after fee; reviews 1–5 days; coming-soon page 2 weeks | Q2 |

## Risks

| Risk | Signal | Mitigation |
|---|---|---|
| Unity drops the pre-2019.1 bundle format | deprecation warning in 6.6 | T8.4; bundles are never the final asset format |
| Unity CLI / `com.unity.pipeline` in beta | 1.0.0-beta.14 / 0.9.0-exp.1 | pinned; read release notes; everything must also be doable from editor scripts |
| Wrong netcode choice | — | T3.1 spike with written criteria; Relay and Steam transport both required |
| M3-lite is the MVP long pole | cross-machine bugs are wall-clock bound | T3.5 latency simulation before friends are involved |
| Market: TTS content moat, free tools (Screentop.gg) | — | gate on real playtests with designers; the agent workflow is the differentiator |
| Big tables (Gloomhaven 6 433 objects) | — | T2.8 after MVP; instancing from M1 |
| Anthropic/OpenAI third-party client policies | changed several times in 2026 | MCP only; user brings their own client |
| Copyrighted test content (Pokémon, Gloomhaven) | in the local collection | local only; never in repo or builds |
