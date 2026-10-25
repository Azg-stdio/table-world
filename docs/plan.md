# Project plan — context, decisions, risks

The work itself (milestones and tasks) lives in **[milestones/README.md](milestones/README.md)**, one file per milestone. This file holds what does not change per task: how to work, decisions, what is verified, risks.

Last updated: 2026-10-23 (working rule 9: polish). Planning closed 2026-10-22.

## For the implementing agent (read first)

1. Read `CLAUDE.md`, then this file, then `milestones/README.md`, then the milestone you are working on. Design drafts: `rules-schema.md`, `tts-mapping.md` (you own them from T1.3 on).
2. **The target is the full game at TTS quality** (Miguel, 2026-10-23): a real Tabletop Simulator game loads and plays in Table World, and it feels as polished as TTS. There is no reduced first version. Work in the dependency order of `milestones/README.md`. The friends playtest (T3.6) is a test whenever there is something worth playing, and the condition for paying the Steam fee; it does not block other work.
3. A task is done only when its **done check** passes. Record the date in the milestone file's status column and in `milestones/README.md`. If a check was skipped, write that instead of `done`.
4. Logic lives in `core/` with an xUnit test first. Unity is view, physics, network and UI. If something in `core/` needs Unity, stop and redesign.
5. Scenes, prefabs and assets are created from editor scripts or the Unity CLI, never by hand-editing serialized files. If a step is only possible in the Editor UI, write exactly what the human must click and continue.
6. One commit per task, message starting with the task id (`T1.1: ...`). Architecture changes get an ADR in `adr/` in the same commit.
7. Ask the human only for: decisions that change the plan, anything that costs money, anything needing their accounts, and playtests. Otherwise decide, record the assumption in the milestone file, continue.
8. Run each milestone's ⚗ simulation before building on its result. Record numbers in the milestone's simulation table, not adjectives.
9. **Finished means polished.** What we build must feel and look as good as TTS. Going back to fix earlier tasks costs more than finishing them properly the first time (added 2026-10-23, after Miguel's own playtest found a wrong camera side, no hand view, no obvious way to play, console errors and a hard-to-read HUD). Before marking a task done:
   - **Play it like a player:** real gestures (mouse and keyboard, not only code calls), in the built exe as well as the Editor, and with two clients when networking is involved.
   - **Look at it:** take screenshots and read them. Text is legible, nothing overlaps, nothing is missing or misleading.
   - **Clean console:** zero errors and no new warnings in the Editor and in `Player.log`.
   - **Think like a TTS player:** compare against what Tabletop Simulator does for the same situation, not only the task's literal text. Missing pieces become rows in the task's file, not silent gaps.
   - **Honest status:** ✅ only for what was exercised. "Partial" with the reason otherwise.
   - **People-only checks:** anything automated tests cannot judge (real networks, real mice, feel) is added as a line to `playtest.md` §5 in the same commit, so the friends session has a complete checklist.

## Decisions (2026-10-22)

| # | Question | Decision |
|---|---|---|
| Q1 | Rules + MCP before multiplayer? | **Yes.** M1 is built as intents → engine → state so M3 is a transport layer. |
| Q2 | Distribution | **Steam**, fee paid only after two friends have played over Relay (T3.6). Friends test over **Unity Relay** join codes. Steam transport tested on **test app ID 480** before owning an app. |
| Q3 | JSON in `core/` | **Newtonsoft.Json 13.0.3**; Unity provides it via `com.unity.nuget.newtonsoft-json` 3.2.2. |
| Q4 | UI | **Own declarative JSON UI rendered with UI Toolkit**. TTS XmlUI gets a translator in M7. |
| Q5 | Docs language | **English**, code identifiers too. |
| Q6 | Name | **Table World** (working title). Repo `table-world`. |
| Q7 | Hours per week | Unknown; estimates given per band in `milestones/README.md`. |
| Q8 | First deliverable | ~~Playtest MVP (M0–M3-lite)~~ → **the full game** (2026-10-23): TTS-grade parity, then the TTS games themselves (assets, Lua, importer). |

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
| Multiplayer is a long pole | cross-machine bugs are wall-clock bound | T3.5 latency simulation before friends are involved |
| Market: TTS content moat, free tools (Screentop.gg) | — | gate on real playtests with designers; the agent workflow is the differentiator |
| Big tables (Gloomhaven 6 433 objects) | — | T2.8 performance pass; instancing from M1 |
| Anthropic/OpenAI third-party client policies | changed several times in 2026 | MCP only; user brings their own client |
| Copyrighted test content (Pokémon, Gloomhaven) | in the local collection | local only; never in repo or builds |

## Ideas (Miguel, 2026-10-24; from `new-ideas.txt`)

Ideas to shape later milestones; none is scheduled before the milestone it belongs to.

| Idea | Where it goes | First notes |
|---|---|---|
| **Chalkers**: the player characters are simple figures with a chalkboard face. The face shows the personality the player picks; the mouth moves when they talk; the eyes and face follow the player's point of view | M6 (T7.1 spike, T7.3) | A chalkboard face is a texture we draw at runtime (eyes, mouth, chalk strokes): cheap, readable at a distance and easy to customise. The mouth follows voice level (Vivox or the transport's voice), the head and eyes the camera's aim, sent with presence (G1, `core/Net/Presence`) |
| **Emotes** | M6 (T7.4) | A small set drawn on the face plus a body pose; a wheel on a key; sent like presence |
| **Gift link**: buy the game for a friend and send a link that installs and joins (easy playtests) | Steam distribution (T0.6 onward) | Steam gifting exists; a join link can open the game through a `steam://` URL or Steam's invite (rich presence "Join game"). The Relay join code already works; a link wraps it. Check Steam's rules for a "friend's pass" before promising one |
| **Karaoke room**: a couple of sofas and a big TV playing YouTube videos, timed with each player's ping so everyone hears it together | After M6, a social space | YouTube's terms forbid extracting the audio or video stream; an embedded official player (a web view) is the allowed way, so check terms and a Unity web view for Windows first. Sync: the host sends "playing video X from t at server time T", each client seeks to t + its clock offset (the music player's starts already go through the host, G27) |
| **Host-led teaching** (Miguel, 2026-10-25): the host still explains the game; the game only helps them. Pointing and showing first: the host spotlights a card, zone or token and everyone's focus goes to it; the host can guide the first rounds with these tools | After the importer is finished; tools are generic (any table), rules-aware parts use M2 | Spotlight (a glow on every screen, the rest dims a little), "follow the host's view" as an invitation, the host's chalker pointing at the spotlighted thing. Later, all under the host's control: a private teaching outline with ticks, host-only reminders of teachable moments, a teaching round (undo, nothing counts), a "?" marker players leave on what confuses them, "why not" messages and legal-move highlights the host switches on. Not wanted: tutorials or bots that replace the host |
| **Party games** such as Keep Talking and Nobody Explodes or Avalon, fully playable | M2 rules (hidden roles, secret votes, timers), M6 voice | The rules engine needs per-seat hidden information beyond hands (roles), secret simultaneous votes and timers; Keep Talking needs per-player different screens (one sees the bomb, others the manual), which hidden zones and per-seat UI can give |

