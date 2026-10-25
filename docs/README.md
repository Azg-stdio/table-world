# Table World docs

| Folder | For | What is there |
|---|---|---|
| [user/](user/README.md) | Players and game designers | Playing, hosting, building a game with your agent, importing Tabletop Simulator games, the game brief template |
| [dev/](dev/README.md) | Miguel and implementing agents | The system map, one page per flow, recipes for common changes |
| [reference/](reference/README.md) | Both | Generated from the code: MCP tools, rules format, Lua API, error codes. Never edited by hand |
| [milestones/](milestones/README.md) | Planning | Milestones, tasks and their status (the source of truth for status) |
| [adr/](adr/) | Decisions | Architecture decisions and why |
| [plan.md](plan.md) | Context | Decisions, risks, working rules for agents, ideas |

Other pages at this level: [rules-schema.md](rules-schema.md) (the rules format), [tts-mapping.md](tts-mapping.md) (how
Tabletop Simulator saves map to our model), [tts-interactions.md](tts-interactions.md) (TTS parity checklist),
[lua-api.md](lua-api.md) (the Lua API and its TTS coverage).

## Writing these docs

- **Audience per folder.** `user/` speaks to a player or designer who has never seen the code: no file paths, no class
  names. `dev/` speaks to someone changing the code: every box in a diagram and every step names its files.
- **Diagrams are Mermaid**, written in the page. GitHub turns off Mermaid links, so every diagram is followed by a table
  that maps each box to its files, as relative links (`[TableEngine.cs](../../core/Engine/TableEngine.cs)`).
- **Relative links only** between pages and to code, so they work on GitHub, in an editor and offline.
- **A page for a feature still in progress** starts with a status line: `> Status: in progress — what works, what
  does not yet.`
- **Written after the behaviour is done and played.** A change to documented behaviour updates its page in the same
  commit.
- **Checked:** `python -I tools/docs/check_docs.py` fails on a link to a missing file or heading, and on a Mermaid
  block that does not parse (when the Mermaid CLI `mmdc` is installed). CI runs it.
- **Generated pages** live in `reference/` and are rewritten by a test (`TW_UPDATE_DOCS=1 dotnet test --filter
  DocsReference`); the test fails when they drift from the code.
