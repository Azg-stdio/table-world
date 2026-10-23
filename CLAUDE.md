# CLAUDE.md — Table World (working title)

This file is the permanent project context for Claude Code. Read it fully before working. If a decision recorded here changes, update this file in the same commit.

---

## 1. What we are building

A 3D board game simulator, positioned as "a more advanced Tabletop Simulator, with the option to connect an AI agent".

- **Game-creator agent (core of the product):** the host provides the rulebook and the assets of their game; the agent builds the logic and sets up the table, asking questions when the rulebook is ambiguous.
- **The creator is the host** of the session. Players receive the assets when they join.
- **Physics characters** Peak / REPO style as a hook (later phase).
- **DnD assistant** to plan sessions and set the mood (lights, VFX, SFX, music). Live actions are suggestions the DM confirms, never automatic changes (later phase).
- **Long term:** DnD campaigns with full 3D immersion. Out of current scope.

**Initial audience:** indie board game designers who prototype and playtest online.

## 2. Stack and closed decisions

| Topic | Decision |
|---|---|
| Name | **Table World** (working title, may change). Repo `table-world` |
| Engine | **Unity 6.6** (Supported). Migrate to **6.7 LTS** when released. Do not use 6.0 or 6.3 (TTS itself runs on 6000.3.10f1 today; irrelevant for us) |
| Platform | **Windows Build Support** (.exe for Steam). NOT UWP. Mac and Linux later |
| Distribution | **Steam first**, but the Steam Direct fee is paid only after the Playtest MVP (M3-lite) has been played by two friends. Friends test over **Unity Relay** join codes; Steam transport is tested on **Steam test app ID 480** before owning an app |
| Scripting backend | **Mono** for development. IL2CPP evaluated for release builds |
| Render | **URP**. TTS AssetBundles are remapped to `URP/Lit` at load time (ADR 0001) |
| Physics | **PhysX** (built in) |
| Language | C# |
| JSON | **Newtonsoft.Json 13.x** in `core/` (Unity provides it via `com.unity.nuget.newtonsoft-json`). Not System.Text.Json |
| UI | **UI Toolkit** (UXML/USS) for the game UI, following the `unity:ui-uitk` skill conventions. Game-defined UI is our own declarative JSON format, not TTS XmlUI; an XmlUI translator belongs to the importer (M7) |
| AI | The game exposes an **MCP server**; the user drives it with their own Claude Code / Claude / Codex. No third-party subscription login and no API keys inside the game for now (Anthropic and OpenAI policies changed several times in 2026; review before adding that option) |
| Game rules | **Own declarative format** (JSON with schema) interpreted by our rules engine. For logic that does not fit: **sandboxed Lua** (MoonSharp 2.0.0, verified on .NET Standard 2.1), with access only to the API we expose |
| Mods / catalog | After the prototype. Steam Workshop via Steamworks.NET (mod.io as fallback) |
| TTS importer | After the prototype. See section 6 |
| Documentation | **English** for docs and code identifiers |

## 3. Architecture

```
/
├── CLAUDE.md
├── core/                   # Pure C# library (.NET Standard 2.1). ZERO Unity dependencies
│   ├── Rules/              # Rules format schema, table model, validation
│   ├── Engine/             # Rules engine: state, turns, phases, actions, conditions
│   ├── Scripting/          # Lua sandbox and exposed API
│   └── Tts/                # TTS format reader and mapping to our model (later phase)
├── core.tests/             # xUnit tests for core. Run with `dotnet test`, no Unity needed
├── mcp-server/             # MCP server (separate process). Speaks in board game terms
├── unity/                  # Unity project: render, physics, network, UI, puppets
│   └── Assets/
│       ├── _Project/       # All our code and assets live here
│       │   ├── Scripts/
│       │   ├── Prefabs/
│       │   ├── Scenes/
│       │   ├── Settings/
│       │   ├── UI/         # UI Toolkit: UXML, USS, PanelSettings
│       │   └── Art/ThirdParty/<pack>/   # CC0 packs, each with its LICENSE.txt
│       └── Plugins/        # core.dll and dependencies
├── tools/                  # Helper scripts (TTS analysis, Unity probes, converters)
└── docs/                   # plan.md (context), milestones/ (tasks + status), adr/, rules schema, TTS mapping
```

**Principles:**

1. **Core does not know Unity.** The rules format, the engine and the MCP server must work and be tested with `dotnet test`. Unity only renders, simulates physics, handles network and UI. If something in core needs Unity, it is designed wrong.
2. **The MCP server speaks board games, not Unity.** Tools like `create_deck`, `define_zone`, `add_phase`, `set_win_condition`, `place_object`, never `create_gameobject` or `set_transform`. It talks to Unity over a local WebSocket.
3. **Authoritative host.** The host runs the rules engine; clients send intents and receive state. The Unity view never mutates state on its own: every interaction becomes a `Command` that core executes.
4. **User- or agent-generated C# is never executed in the public build.** Only validated JSON and sandboxed Lua.
5. **TTS-compatible in the data model, not a clone in behaviour.** Primitives must map one to one with TTS's (section 6), but we do not copy its exact physics, its full Lua API or its interface.

## 4. Rules for working with agents in Unity

- **Never hand-edit `.unity`, `.prefab`, `.asset` or `.meta` files.** Create and modify scenes and prefabs from code (editor scripts in `Assets/_Project/Editor/`) or through the **Unity CLI** (`unity command <group> <action>`, e.g. `unity command scene get_hierarchy`) with the Editor open. `unity commands --grep <term>` to search commands. If something can only be done in the editor, say so and ask the human to do it.
- **Agent tooling for Unity:** Unity CLI (`winget install Unity.CLI`) + `com.unity.pipeline` package in the project + the official `unity@unity-agent-plugin` Claude Code plugin (enabled in `.claude/settings.json`). Prefer direct CLI commands over `unity mcp` (faster, fewer tokens). `unity command script eval --code "..."` and `script eval_file --file x.cs` run C# as a **method body**: no `using` directives, fully-qualified names (`System.IO.Directory`, `UnityEngine.Rendering.GraphicsSettings`), ending with `return <string>`. Use them for read-only experiments; for project changes use editor scripts or the `asset`/`scene`/`prefab` commands. Examples in `tools/unity-probes/`. The CLI and the package are beta/experimental: read the release notes before updating. CLI analytics disabled (`unity analytics opt-out`).
- **Driving Play mode from the CLI:** an unfocused Editor does not advance Play mode (stays at frame 1). Run `unity command editor.playmode set_run_in_background --enable true` first (builds keep the project value). Drive gestures through the public methods of `TableInteraction` (`Grab`, `DragTo`, `Release`, `Flip`, ...) with `script eval_file`, one call per step, sleeping between calls so physics runs. `capture game_view --save_path` always writes under `Assets/` (use `Temp/x.png` → `Assets/Temp/`, git-ignored) and delete it afterwards with `asset delete`. PlayMode tests: `unity command test run --mode PlayMode --async_tests true`, then poll `unity command test status` (the synchronous form reports `0/0`).
- **UI:** load the `unity:ui-uitk` skill before writing UXML/USS or UI Toolkit C#. Styling only in USS (no inline `style=`), one `PanelSettings` asset under `_Project/UI/`.
- Before proposing a package, verify it is compatible with Unity 6.6 and URP.
- Unity uses **Fast Enter Play Mode** by default in 6.6: do not use static state that depends on domain reload. Initialize with `[RuntimeInitializeOnLoadMethod]` or in `Awake`.
- Put game logic in `core/` and write its test before touching Unity.
- Naming: PascalCase for types and methods, camelCase with `_` prefix for private fields, English everywhere.
- Small, descriptive commits. An architecture change comes with an ADR in `docs/adr/`.

## 5. Table model primitives (v0)

They must cover TTS's so the importer is a direct translator:

- **Objects:** `Card`, `Deck` (with sprite sheet: columns × rows, face and back), `Token`, `Tile`, `Die` (n faces), `Model` (glTF), `Bag`, `InfiniteBag`, `Board`, `Figure`
- **Zones:** `HandZone` (per player, hidden), `HiddenZone`, `ScriptingZone`, `SnapPoint`
- **Common properties:** stable `id`, `name`, `description`, `gmNotes`, `tags`, `locked`, `states` (several states per object), `transform`, `container` (parent object), `script` (optional Lua)
- **Players:** player colors, seats, roles (host, player, spectator)
- **Rules:** phases, turns, actions allowed per phase, resources, win conditions, events (`onTurnStart`, `onDrop`, `onEnterZone`, etc.)
- **Standard table actions:** flip, shuffle, search, deal, lock, group, measure, roll dice
- **Declarative UI:** panels, buttons, counters, text (our equivalent of TTS XmlUI), rendered with UI Toolkit
- **Units:** define ONE conversion constant with TTS, documented in `docs/tts-mapping.md`, and similar default sizes (standard card, token, die)

## 6. What we know about TTS mods

Analysis of the local collection (6 Workshop mods, 14 676 objects counting those contained in bags/decks; script `tools/tts-analysis/analyze_mods.py`, 2026-10-22):

- **AssetBundles are not decoration:** 4 of 6 mods use them and in 3 they are the game's figures (2 690 `Custom_Assetbundle` objects + 168 `Custom_Assetbundle_Bag`, which is container and model at once). The two Pokémon mods reference 1 026 and 902 unique bundle URLs; only 65 are cached locally: the importer downloads on demand.
- **Most frequent types:** `Card` 5 568, `Custom_Assetbundle` 2 690, `Custom_Model` 2 351, `Custom_Tile` 1 693, `Custom_Token` 759, `Custom_Model_(Infinite_)Bag` 542, `Deck` 226, `ScriptingTrigger` 219, `CardCustom` 124, `Bag` 80, `Infinite_Bag` 62, `Figurine_Custom` 48, `HandTrigger` 30, `Custom_Dice` 25.
- **Global Lua in 5 of 6, XmlUI in 6 of 6.** Snap points in 5 of 6 (up to 1 633 in Gloomhaven), zones in 5 of 6.
- **Most used API functions (occurrences):** `UI.setAttribute` 2 230, `getName` 1 120, `getObjectFromGUID` 1 062, `getLuaScript` 816, `broadcastToAll` 683, `getObjects` 677, `getGMNotes` 635, `UI.getAttribute` 590, `createButton` 565, `getPosition` 530, `editButton` 519, `onLoad` 481, `setValue` 432, `Wait.time` 431. That is the priority order for the Lua compatibility layer. UI (XmlUI + `UI.*`) weighs more than assumed.
- **86 % of URLs** are on `cloud-3.steamusercontent.com` and 9.5 % on `steamusercontent-a.akamaihd.net`; 4.5 % on pastebin (scripts). The importer must download and cache assets locally.
- **Bundles:** 69 of 70 were built with Unity 5.6.x and 1 with 2019.1. **They load in Unity 6.6 + URP**; materials render pink until remapped to `URP/Lit` (0 magenta pixels after the remap). Unity marks the pre-2019.1 format as deprecated. TTS itself runs on Unity 6000.3.10f1 today. Details and evidence: `docs/adr/0001-assetbundles.md`.
- The sample is small and biased towards heavy mods. Pending: analyze 20–30 varied mods (T8.6).

**Importer rules (when it is built):**
- Runs locally over files the user already has. The result is private by default.
- Never scrapes or republishes the Workshop.
- Every AssetBundle that cannot be converted is replaced by a placeholder that **keeps** id, name, GM notes, container, position and script.
- AssetBundles depend on the Unity version and pipeline they were created with; see ADR 0001.

## 7. Copyright and content

- Layered catalog: official/licensed (promoted), original (main niche), community (report and takedown, no algorithmic promotion).
- Publishing to the catalog requires declaring authorship or rights. Playing privately does not.
- Mention "Tabletop Simulator" only to describe format compatibility; no logos, no implied partnership.
- No asset from the TTS install or Workshop is ever copied into the repo or a build. Third-party packs must be CC0 or equivalently licensed, with a `LICENSE.txt` next to them.
- Before launch: legal review.

## 8. Roadmap

- **Milestones and tasks:** `docs/milestones/README.md` (overview, order, gates, estimates) and one file per milestone `docs/milestones/M0-…M7-*.md` with task tables (id, size, depends on, done check, status). Those files are the source of truth for status.
- **Context, decisions, risks, agent working rules:** `docs/plan.md`. Read its "For the implementing agent" section before the first task.
- **Design drafts:** `docs/rules-schema.md`, `docs/tts-mapping.md`.

Planning closed on 2026-10-22. Implementation starts at T1.1 and follows the **Playtest MVP** (M0 → M1 → M2 → M3-lite). This section is only a summary.

### Task 0 — AssetBundle and pipeline test — **DONE (2026-10-22)**, see `docs/adr/0001-assetbundles.md`
Result: URP stays; bundles load in 6.6 and are remapped to `URP/Lit` at runtime; AssetRipper remains an option for the importer (M7).

### M0 / Task 1 — Repository skeleton
- `core/` (.NET Standard 2.1) with the rules schema v0 and its validation, `core.tests/` with xUnit, and a build step that copies `core.dll` to `unity/Assets/Plugins/`.
- Unity 6.6 + URP project with the `_Project/` folder, Unity `.gitignore` and `.editorconfig`.
- `docs/rules-schema.md` and `docs/tts-mapping.md` promoted from v0 drafts to what `core/` implements.
- Pinned packages: `com.unity.nuget.newtonsoft-json@3.2.2`, `com.unity.test-framework@1.8.0`; NuGet `Newtonsoft.Json` 13.0.3, `MoonSharp` 2.0.0, `ModelContextProtocol` 2.3.0.

### M1 / Task 2 — Basic 3D table
Cards, sprite-sheet decks, tokens and dice with PhysX. Grab, drop, flip, shuffle. Table camera. HUD v0 in UI Toolkit.

### M2 / Task 4 — Rules format + MCP server
Rules engine in `core/`. MCP server with the tools from section 3. Goal: Claude Code builds a simple card game (e.g. "War" or "simplified UNO") end to end from a plain-text rulebook, asking about ambiguities.

### M3 / Task 3 — Multiplayer
Authoritative host. Evaluate Netcode for GameObjects vs FishNet vs Mirror (6.6 compatibility, Relay and Steam transport). M3-lite for the MVP: Relay join codes, image assets only. Full M3: lobby, asset streaming, Steam transport (test app ID 480 first).

### M4 / Task 5 — Host assets
Runtime glTF (glTFast) and image loading, local cache, private sessions, AssetBundle loader with URP remap.

### M5 / Task 6 — Sandboxed Lua
MoonSharp with a restricted API. Compatibility subset of the most used TTS functions.

### M6 / Task 7 — Physics characters
Active ragdoll, talking interaction. DnD assistant.

### M7 / Task 8 — Catalog, mods and TTS importer

## 9. Commands

```bash
# Core (build copies Core.dll to unity/Assets/Plugins/Core/; commit it when core/ changes)
dotnet build TableWorld.sln
dotnet test TableWorld.sln

# Unity: open unity/ with Unity Hub (6.6). Builds with Windows Build Support (Mono).

# Tools
python -I tools/tts-analysis/analyze_mods.py ["<Tabletop Simulator folder>"] --json docs/data/mods-<date>.json   # local mod analysis
python -I tools/tts-analysis/bundle_headers.py "<folder>/Mods/Assetbundles"                # Unity version of each bundle

# Unity CLI (with the Editor open)
unity status                 # connected Editors
unity pipeline list          # must show "Pipeline: Installed"
unity command                # list Editor commands
unity doctor                 # diagnostics
unity command script eval_file --file tools/unity-probes/ProbeBundles.cs --timeout 120   # read-only experiment
```

# MCP server (stdio). Build it, then run the DLL (never `dotnet run`: build output on stdout breaks MCP).
# .mcp.json registers it for Claude Code in this repo; docs/agent-guide.md covers other agents.
dotnet build mcp-server -c Release
dotnet mcp-server/bin/Release/net9.0/TableWorld.Mcp.dll --file games/my-game.json   # --no-bridge: headless only
dotnet test mcp-server.tests          # contract tests: starts the server over stdio

# Multiplayer (T3.2): build the player, then host in the Editor (HUD "Host") and join from the build, or script both:
unity command build run --target StandaloneWindows64 --outputPath Builds/Win64/TableWorld.exe --scenes Assets/_Project/Scenes/Table.unity --confirm true
unity/Builds/Win64/TableWorld.exe -tw-join 127.0.0.1:7777 -tw-autoplay 100 -tw-report out.json -tw-quit   # also: -tw-host [port]

# Long simulations and generated files
TW_GAMES=1000 dotnet test core.tests --filter GamePlayTests   # 1 000 random games of War and UNO
TW_UPDATE_SCHEMA=1 dotnet test core.tests --filter SchemaFile # regenerate docs/rules-schema.v0.json
python -I tools/samples/make_games.py                         # regenerate games/war.json, games/uno-lite.json
