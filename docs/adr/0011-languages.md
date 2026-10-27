# ADR 0011 — Languages: Table World's interface and games in several languages

Status: **proposed** (2026-10-26, M9; asked for by Miguel: "a system to translate Table World, and also a system for
people to make games to have them in different languages").

## Context

Every text a player reads is written into the code in English: HUD and menus (C# and UXML), the engine's log lines
("Red wins.", "Blue chose in secret."), the reasons a move is refused (validation and command errors). A game is
written in one language, its designer's. `game.messages` (T4.17) lets a game reword the engine's log lines, and choice
`labels` separate what players read from the ids rules use, but there is no way to ship one game in several languages.

Miguel's decisions (2026-10-26):
- The interface ships in **English and Spanish** first.
- In a game with several languages, **the host picks one language for the whole session**; every player reads that
  one.
- Design first (this ADR and the milestone tasks), then build.

## Decision

### One format for both: string tables

A string table is a JSON object, key → text, with `{name}` placeholders filled in at use:

```json
{ "hud.start": "Start game ({count} players)", "engine.win": "{seat} wins." }
```

The same format serves Table World's interface and a game's own texts. A translation is a text file anyone can edit,
without Unity.

### Table World's interface

- `core/Text/Strings`: loads `strings/<language>.json`, looks keys up with fallback to English, then to the key itself
  (so a missing string shows as something readable while testing, and a check lists it). Pure C#, so the engine's own
  lines use it too.
- Unity ships the tables in `StreamingAssets/Strings/` (`en.json`, `es.json`). UXML texts get keys set from code at
  start; C# texts call `Strings.Get("key", fills)`.
- **Language setting** in the Escape menu, default the system language when a table exists for it, else English.
  Changing it redraws the HUD at once.
- What is translated, in order: HUD and menus; the engine's log lines (`Names.Message` keys, which `game.messages` still
  overrides); the refusal messages players see in the status line. Docs, the MCP server's replies and tool
  descriptions stay English (agents translate for their user).
- Fonts: Inter covers Latin, Greek and Cyrillic. Chinese, Japanese and Korean need a fallback font asset; out of scope
  until someone asks for one of them.
- A test fails when `es.json` (or any table) lacks a key `en.json` has, or has one `en.json` does not.

### Games

A game declares its base language and carries tables for the others:

```json
"game": { "id": "tornaris", "name": "Tornaris", "language": "es", "languages": ["es", "en"] },
"translations": {
  "en": {
    "game.name": "Tornaris",
    "objects.ficha_nyra.name": "Nyra's token",
    "cards.eq_12_espada.name": "Sword",
    "cards.eq_12_espada.text.body": "Attack +1.",
    "actions.elegir.name": "Choose a space",
    "params.elegir.casilla.mazmorra": "Dungeon",
    "phases.elegir.name": "Choose a space",
    "ui.estado.title": "Status",
    "ui.estado.0.text": "Day {$var.dia}",
    "messages.win": "{seat} wins!",
    "notebook.reglas.body": "…",
    "strings.noche_cerrada": "{space} is a day space. The token goes back to its mat."
  }
}
```

- **Path keys** name the field they replace (`objects.<id>.name`, `.description`, `cards.<id>.name`, `.text.<field>`,
  `actions.<id>.name`, `params.<action>.<param>.<option>`, `phases.<id>.name`, `ui.<panel>.title`,
  `ui.<panel>.<index>.text`, `seats.<id>.name`, `messages.<key>`, `notebook.<tab>.title/body`). A game is translated
  without touching its base text, and the base language stays the source of truth.
- **`strings.<key>`** is the game's own free text: a text that starts with `@` (`"text": "@noche_cerrada"`) is looked
  up there, in a `message` or `refuse` effect, a label, a panel. Lua reads them with `Game.text("noche_cerrada",
  { space = … })` and the session's language with `Game.getLanguage()`.
- **Images with words in them** can name one file per language: `"localized": { "en": "cards_en.png" }` on an asset;
  the base path is the fallback. Text cards are drawn by the game, so they translate for free.
- **The session's language** is chosen when the game starts (the HUD's Start, `start_game language=`), defaulting to
  the host's interface language when the game has it, else the game's base language. It is stored in the session, so
  every player and every save sees the same one. The host may change it between games.
- **Resolution happens where text is made:** `Names.Of`, labels, panel texts, the engine's log lines and `@` strings
  read the session's language through one lookup (`Translations.Text(state, key, baseText)`). Because the whole
  session reads one language, the log keeps finished sentences: nothing per viewer, nothing new to sync.
- **Validation:** a key that names nothing (`objects.missing.name`) is an error; a declared language missing keys the
  base language has is a warning that lists them, so "translate everything" has a visible end.
- **For agents:** `translate_game` lists every translatable text of a language that is missing (base text beside each
  key); `set_translations` writes a batch; the guide shows the loop. "Translate my game to English" is one task.
- Imported TTS games keep the language they were made in.

### Addendum (T10.1, 2026-10-27): where the tables live

Table World's own tables are `core/Text/strings/<language>.json`, built into Core.dll as resources (`Strings.BuiltIn`,
`Strings.For`), not shipped in `StreamingAssets/Strings/`. The engine's log lines and refusals (T10.3) come from
`core/`, which cannot read Unity's folders, and one place for every table keeps the check test simple. They stay text
files anyone edits in the repo. A player can still add a language or fix a text without a rebuild: the game reads every
`<language>.json` in a folder (`Strings.LoadFolder`, in Unity the player's `Strings` folder) on top of the built-in
tables. Placeholders are `{name}`; `{{` and `}}` write a brace. The check also fails a translation that fills other
placeholders than English does.

## Consequences

- One lookup per text shown; a game without `translations` reads exactly as today.
- Every interface text moves into string tables, a large but mechanical change: new UI code must use keys from then on
  (a test can flag string literals passed to `Label`/`Button` in `View/Hud`).
- The session's language is part of the state hash (it lives in the session), so peers cannot disagree on it.
- Per-player languages (each screen in its own language) stay possible later: the path keys and `@` strings do not
  change; only the log would have to store templates instead of sentences.

## Alternatives considered

- **Unity's Localization package** for the interface: good tooling, but the engine's messages live in `core/`, which
  must not depend on Unity, and two formats would split the work. Rejected.
- **Each player in their own language** at one table: chosen against for now (Miguel); see Consequences.
- **Translate by duplicating the game file per language**: simple, but every rule fix must be made in every copy.
  Rejected.
