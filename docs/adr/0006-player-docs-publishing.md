# ADR 0006 — Player docs ship with the game as a static site

Status: **proposed** (2026-10-24, M8 / T9.16; written by the agent, to be confirmed by Miguel). Choosing a public
host is Miguel's call: it depends on whether the repository goes public, and on accounts.

## Context

The player and designer docs (`docs/user/`) are Markdown in the repository, checked by `tools/docs/check_docs.py`
(links, anchors, Mermaid) in CI. Players need to read them:

- **without the repository**: a player downloads the game from Steam, not the code;
- **offline**: the game itself works offline after an import;
- **with search**: the pages are long reference-style material (tool guide, import notes, glossary);
- **with the diagrams rendered**: several pages use Mermaid.

The Escape menu is to get a Help entry that opens these docs (T9.15).

The developer docs (`docs/dev/`, `docs/reference/`, ADRs, milestones) are for contributors and agents and are read in
the repository.

Options:

| Option | Without the repo | Offline | Search | Mermaid | Cost |
|---|---|---|---|---|---|
| A. GitHub Markdown only | only if the repository is public | no | GitHub's code search, poor for prose | yes | none |
| B. MkDocs Material site on GitHub Pages | yes | no | yes | yes | Pages on a private repository needs a paid GitHub plan |
| C. The same static site built into the game's folder | yes | yes | yes (offline search) | yes, if the Mermaid script is bundled | a build step |
| D. Docs drawn in game with UI Toolkit | yes | yes | to build | to build | a Markdown renderer to write and maintain |

## Decision

1. **Markdown in `docs/user/` stays the source.** The pages are written once, checked in CI, and read as they are by
   agents, which receive `agent-guide.md` and the brief template as MCP resources (T9.11).
2. **The player build ships a static site** (option C), built from `docs/user/` with MkDocs Material:
   - The `offline` plugin makes the site work from `file://`, search included.
   - The Mermaid script is bundled, not fetched from a CDN.
   - The site goes in the build next to the executable (e.g. `Docs/index.html`).
   - The Escape menu's Help entry opens it in the browser with `Application.OpenURL`. A missing site falls back to
     the online one, when there is one.
3. **The same site can be published online later** (option B) without changing the pages. That needs one of two
   things, both Miguel's decisions:
   - the repository goes public, and the site goes on GitHub Pages;
   - a host is chosen for the site.
4. Only `docs/user/` goes into the player site. Links from it into `docs/dev/` or the code are removed from the
   player pages, or point to the repository when it is public. A CI check (in `check_docs.py`) will flag them once the
   site exists.

## Consequences

- **The build needs a site step.** MkDocs (Python) runs before or after the Unity build. The build script copies the
  site into the output folder. T9.7's screenshots need a build anyway.
- **Repository paths change on the site.** Pages that name files players do not have (`games/war.json`,
  `mcp-server/…`) explain where those files are in the build, or say they are for people building from source.
- **Option D is not ruled out for short in-game help** (controls, first steps). The full docs stay in the browser,
  where reading long pages and searching them is easy.
- **No new runtime dependency:** the game only opens a URL.
- **To do when this is accepted:**
  - `mkdocs.yml`;
  - a `tools/docs/build_site.py` that bundles Mermaid;
  - the CI job;
  - the link check for player pages;
  - the Escape menu entry (T9.15).
