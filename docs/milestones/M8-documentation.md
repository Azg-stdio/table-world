# M8 — Documentation (developer and player)

**Goal:** two sets of docs that stay true to the code. Developer docs (for Miguel and implementing agents): wireframes of how the system works, every box linked to the files that implement it. Player docs: how to play, host and join, how the MCP server and the agent build a game, what the importer does, and a game brief template that lets a designer hand their agent everything it needs at once.
**Done when:** a person who has never seen the project installs the game, builds a small original card game with their own agent from a filled game brief, imports a local TTS mod and understands its import notes, using only the player docs; a fresh agent session answers "where does a dropped card become a Command, and how does it reach the other players?" from the developer docs alone, with correct file links. CI fails on a broken link or a generated reference that drifted from the code.
**Status:** in progress (T9.10 started 2026-10-24). **Depends on:** nothing to start; each page waits for the feature it describes (column "Depends on").

Docs are written **after** the behaviour they describe is done and played (working rule 9), and every later task that changes a documented behaviour updates the page in the same commit. Pages for features still in progress say so at the top.

## Layout

```
docs/
├── dev/                      # for Miguel and agents (T9.1)
│   ├── README.md             # index + system map
│   ├── flows/                # one page per flow, diagram + file table
│   └── recipes/              # "how to add an effect / MCP tool / object type ..."
├── user/                     # for players and designers (T9.1)
│   ├── README.md             # index
│   ├── game-brief-template.md
│   └── examples/             # filled briefs
├── reference/                # generated from code, never hand-edited (T9.6)
└── adr/, milestones/, ...    # unchanged
```

Diagrams are Mermaid in Markdown (GitHub and VS Code render them; text, so they diff and agents can edit them). GitHub turns off Mermaid `click` links, so **every diagram is followed by a table that maps each box to its files** (`[TableEngine.cs](../../core/Engine/TableEngine.cs)`); the link checker (T9.2) keeps those tables honest.

## Tasks

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T9.1 | Docs structure and conventions: `docs/dev/`, `docs/user/`, `docs/reference/`, index pages, a short style guide (audience per folder, Mermaid + file table, relative links, "status" banner for unfinished features). Move `agent-guide.md` and `playtest.md` under `user/` with redirects (one-line stubs) | S | — | Index pages list every doc; old links still resolve | todo |
| T9.2 | `tools/docs/check_docs.py`: relative links and anchors exist, every file in a "files" table exists, Mermaid blocks parse (via `@mermaid-js/mermaid-cli` when installed, skipped otherwise); CI step | S | T9.1 | CI fails on a renamed file referenced by a doc; passes on main | todo |
| T9.3 | Dev: system map. Processes (game exe host and clients, MCP server, the user's agent), assemblies (`core`, `TableWorld.Runtime`, `McpServer`), the boundaries between them (local WebSocket bridge, NGO/Relay, stdio MCP) and which principle of CLAUDE.md §3 each boundary enforces | M | T9.1 | Miguel reads it and finds nothing wrong or missing; every box links to code | todo |
| T9.4 | Dev: flow pages, each a sequence or flow diagram + file table + "where to put a breakpoint": (a) input → `TableInteraction` → `Command` → `TableEngine` → state events → `TableView`; (b) rules game: `GameEngine` phases, legal actions, transactions and rollback; (c) MCP authoring and the Unity bridge (`bridge.json`, load/perform/state messages); (d) multiplayer: host authority, `TableSync`, presence, chat, asset sync and URL fetch order; (e) asset loading: package files, URL cache, TTS cache, web, image decode off the main thread, bundles and the URP remap; (f) TTS import: save → `TtsImporter` → `TableState` → package, placeholders, `import-notes.txt`; (g) Lua: `ScriptHost` sandbox, prelude, events, script UI; (h) UI: HUD, declarative game UI, script XmlUI | L | T9.3 | A fresh agent answers one question per flow from the page alone, with correct file links (record questions and answers in the simulation table) | todo |
| T9.5 | Dev recipes, each a checklist that names files and tests: add an effect or condition, add an MCP tool, add an object type (core model → validator → schema → view → importer mapping), add a Lua API function (and its TTS coverage entry), add a HUD panel (UI Toolkit rules), write a PlayMode test and a Unity probe | M | T9.1 | Each recipe followed once by an agent on a real change without asking where things go | todo |
| T9.6 | Generated references in `docs/reference/`: MCP tools (from the `[McpServerTool]` names, descriptions and parameters), rules format (from `rules-schema.v0.json`), Lua API with TTS coverage (from `ScriptApi` registrations), validation error codes. One command regenerates them (`TW_UPDATE_DOCS=1 dotnet test --filter DocsReference`, like the schema); the test fails when they drift | M | T9.1 | Adding a tool parameter without regenerating fails `dotnet test` | todo |
| T9.7 | User: getting started — install, first table, controls (from `ControlsHelp`), hosting and joining with a Relay code, saves, settings. Screenshots from a built exe | M | T3.6, T2.12 | A friend who has not played gets from download to a game with another person using only this page | todo |
| T9.8 | User: building a game with your agent — what MCP is in one paragraph, what the Table World server can and cannot do, setup for Claude Code, Claude Desktop and Codex, the build loop (reference → table → rules → validate → `ask_host` → play) as a diagram, a real annotated session transcript, prompts that work, what to do when the agent is stuck. Replaces `agent-guide.md` (its agent-facing part moves to an MCP resource, T9.11) | M | T4.8, T9.1 | A designer with their own agent reaches a playable table from the page alone | todo |
| T9.9 | User: MCP tool guide grouped by job (start a game, put things on the table, write rules, test, save and load), each tool with a short real example; links to the generated reference for exact parameters | S | T9.6, T9.8 | Every tool in the server appears; examples run against the server in a test | todo |
| T9.10 ⚗ | Game brief template (`docs/user/game-brief-template.md`) + filled examples: War and UNO-lite (from `docs/rulebooks/`) and one original game with custom card images. Simulation: fresh agent sessions build each game (a) from the rulebook alone and (b) from rulebook + brief; record tool calls, `ask_host` questions, validation rounds and whether the first started game was right | M | T4.7 | (b) needs fewer questions and validation rounds than (a) on all three games; template revised with what the agent still asked | in progress (2026-10-24): template written; examples and simulation todo |
| T9.11 | MCP prompts and resources: the server exposes the brief template, the rules reference and the agent guide as MCP resources, and prompts such as `build_game_from_brief` and `review_rulebook` (lists ambiguities and missing components before building), so any agent gets the right instructions without the repo | S | T9.10 | Contract test lists the resources and prompts; a session started with the prompt from Claude Code reaches a validated table | todo |
| T9.12 | User: importing Tabletop Simulator games — where saves are found, what imports (object types, zones, snap points, Lua, XmlUI), placeholders and why, reading `import-notes.txt`, downloading missing files, privacy (local only, never republished), known limits per feature. Glossary TTS term → Table World term | M | T8.1, T8.2, T8.3 | A player imports a local mod and explains its notes back correctly | todo |
| T9.13 | User: rules format for humans (concepts with diagrams, from simple to complex, using War and UNO-lite) and Lua scripting guide (sandbox limits, events, script UI, differences from TTS Lua) | M | T6.4, T9.6 | A designer edits a rule by hand and the validator accepts it on the first or second try | todo |
| T9.14 | User: preparing assets — sprite sheets (grid, index order, sizes TTS uses, DPI), card backs, tokens and tiles, glTF models (scale, pivot, triangle budget), where files live in a game package | S | T5.1, T5.2 | The original example game (T9.10) is built following it | todo |
| T9.15 | User: troubleshooting and FAQ (game window not connecting to the agent, join code fails, pink materials, missing files, slow loads) + an in-game Help entry in the Escape menu that opens the player docs | S | T9.7, T9.8 | Each FAQ entry reproduced once; Help opens the docs from a built exe | todo |
| T9.16 ⚗ | Publishing: decide how players read the docs (GitHub Markdown, a static site such as MkDocs Material on GitHub Pages, or shipped with the game), including offline and search. ADR | S | T9.7 | ADR with recommendation | todo |
| T9.17 **human** | Read-through of the player docs by a board game designer who has not seen the project; frictions filed as tasks | S | T9.8, T9.10, T9.12 | Notes recorded; tasks filed | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|

## Assumptions recorded during implementation

| Task | Assumption |
|---|---|
| T9.10 | The brief does not mirror the JSON schema field by field: a designer writes it in game terms and the agent maps it. It asks for what rulebooks usually leave out (enforcement level, edge cases, hidden information, component data as a table). |
