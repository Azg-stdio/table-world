# M9 — Languages

Table World's interface in English and Spanish, and games that carry several languages, the host picking one per
session. Design: [ADR 0011](../adr/0011-languages.md).

Sizes: S ≤ 1 day, M 2–4 days, L 1–2 weeks. Status values as in the [overview](README.md).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T10.1 | String tables in `core/` (`Strings`: load, look up with fallback, fill placeholders); the table check test (keys match across languages) | S | — | Core tests: lookup, fallback, placeholders, a missing or extra key fails the check | todo |
| T10.2 | Table World's interface through string tables: every HUD, menu, tooltip and status text keyed in `en.json`; language setting in the Escape menu (system default); Spanish `es.json` | L | T10.1 | No player-facing literal left in `View/Hud` (test); PlayMode test switches language and the HUD redraws; screenshots of every panel in both languages | todo |
| T10.3 | The engine's log lines and refusal messages through string tables (`game.messages` still overrides the log lines) | M | T10.1 | Core tests: a refused move and a win read in Spanish with the interface in Spanish | todo |
| T10.4 | Game translations model: `game.language`, `game.languages`, `translations` with path keys and `strings.<key>`, `@` texts, per-language asset paths; validation (unknown keys are errors, missing keys warnings) | M | T10.1 | Core tests for every key kind; schema and reference docs regenerated | todo |
| T10.5 | Session language: chosen at Start (HUD) and `start_game language=`, stored in the session; names, labels, panels, phases, log lines and `@` texts read it; Lua `Game.getLanguage()`, `Game.text(key, fills)` | M | T10.4 | Core and PlayMode tests: one game started in each language, every text kind checked; two clients read the host's language | todo |
| T10.6 | Text cards and images per language (localized asset paths, text cards redrawn) | S | T10.5 | PlayMode test: a card image and a text card change with the session's language | todo |
| T10.7 | MCP: `translate_game` (missing keys with base text), `set_translations`; guide and rules-and-scripting pages | M | T10.4 | Contract tests; guide examples run | todo |
| T10.8 | ⚗ Simulation: an agent translates Tornaris to English with the tools; play a round in each language; count texts left untranslated and fix the gaps | S | T10.5, T10.7 | Numbers recorded here; nothing untranslated on screen in a round | todo |
| T10.9 | Player docs: playing in your language, translating a game (`docs/user/`) | S | T10.2, T10.7 | `check_docs.py` clean | todo |
