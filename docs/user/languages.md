# Languages

Table World speaks English and Spanish, and a game can carry as many languages as its designer gives it. There are two
separate things: the language of Table World itself (menus, buttons, the controls list), which each player picks, and
the language of a game (its cards, actions, panels, the lines in the log), which the host picks for the whole table.

## Playing in your language

**Table World's own words.** Escape → Settings → You → **Language**. "System" follows your computer's language when
Table World speaks it, else English. The change shows at once, without restarting.

**The game's words.** A game in several languages shows one button per language beside **Start** before the game
begins, each named in its own language ("English", "Español"). The host picks one; everyone at the table reads the game
in it, including the lines Table World writes in the log ("Red wins.", "Rojo gana."). A game in one language has no
buttons; its own words show as written, and Table World's lines follow the host's language.

What a player may refuse to do ("It is Blue's turn") reads in that player's language for Table World, with the game's
names in the game's language.

## Translating a game

You write the game in your language; each translation is a list of the texts it replaces. Missing texts read as you
wrote them, so a half-finished translation still plays.

The quickest way is to ask your agent: "translate the game to English". It uses `translate_game` (what is left to
translate, with your text beside each) and `set_translations` (the translated batch), and repeats until nothing is left.
The format, if you edit it by hand, is in [rules and scripting](rules-and-scripting.md#6-events-the-end-of-the-game-and-the-screen):
`game.language` says which language you wrote it in, and `translations` holds the others, by what each text is
(`actions.<id>.name`, `cards.<id>.text.body`, `ui.<panel>.title`, …).

Three things need a little care:

- **Sentences your rules write** (a `message`, a `refuse` reason) translate when written as `@key` with
  `strings.<key>` holding the sentence, first in your own language.
- **Sentences your Lua writes** translate when wrapped in `Game.tr("...")`; the translation lists them by their words.
  A sentence built from pieces needs its fixed pieces wrapped.
- **Images with words on them** can name a file per language: `"localized": { "en": "cards_en.png" }` on the asset.
  Cards whose words are written as text (text cards) translate like any other text.

## Translating Table World itself

Table World's own words live in `core/Text/strings/<language>.json` in its source: `en.json` and `es.json` today. To add
a language or fix a word on your computer without rebuilding, put a `<language>.json` (for example `fr.json`) in the
`Strings` folder next to your settings (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Table World\Strings` on Windows today; the company part changes once
the game has its publisher name): it is
read on top of the built-in tables and the language appears in Settings. The keys are `ui:` followed by the English text
for the interface, `engine.*` and `log.*` for the lines Table World writes during a game, `refusal.*` for refused moves,
`color.*` for seat colours; copy `es.json` and translate its values. `{name}` placeholders must stay as they are.
