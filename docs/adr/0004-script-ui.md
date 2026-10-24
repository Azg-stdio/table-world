# ADR 0004 — Script UI is a host-owned element tree in table state, with XmlUI's vocabulary

Status: **proposed** (2026-10-24, M5 / T6.3; written by the agent overnight, to be confirmed by Miguel).

## Context

TTS games build much of their interface with XmlUI and drive it from Lua: `UI.setAttribute` is the single most used API
call in the local mods (2 181 uses), followed by `UI.getAttribute` (590) and `UI.setValue` (432). Gloomhaven carries
852 000 characters of XmlUI across its global UI and 106 objects; Riftbound builds its UI from Lua with `UI.setXml`.
The elements they use, by count: `Panel`, `Image`, `Button`, `Text`, `HorizontalLayout`, `InputField`, `VerticalLayout`,
`ProgressBar`, `Dropdown` / `Option`, plus a few tables, sliders and toggles; the attributes they change at run time are
mostly `active` (1 358), `image`, `text`, `width`, `textColor`, `percentage`, `position`, `visibility`.

CLAUDE.md says game UI is **our own declarative JSON format**, not TTS XmlUI, with the XmlUI translator belonging to the
importer. ADR 0003 says scripts run on the host only and change the table through commands, and that script UI must be
host-owned table state so clients draw it.

We already have a declarative UI (`ui` in the rules, T4.6): screen panels of labels, counters and buttons that perform
rules actions. It is made for agents writing rules, not for the free layouts TTS scripts build and edit element by element.

## Options

| | Extend the rules UI (`UiDef`) | A separate element tree with XmlUI's names | Store raw XmlUI text |
|---|---|---|---|
| Expressiveness for TTS games | needs most XmlUI features added to a format designed for something else | covers them | covers them |
| Translator (importer, `UI.setXml`) | heavy: every attribute renamed and reshaped | thin: parse, apply `<Defaults>`, canonical names | none |
| `UI.setAttribute` traffic | each change maps onto typed fields | one small command per change | re-send or diff the whole XML |
| Our format (JSON, validated, versioned) | yes | yes | no: XML text in the state |

## Decision

- Script UI is a tree of `UiNode` (`core/Rules/ScriptUi.cs`): `tag`, `attributes` (string values), `value` (text
  content), `children`. It lives in table state: `scriptUi` on the table (the global screen UI) and on each object (UI
  drawn on that object). It is JSON like the rest of the table, validated (size cap of 20 000 elements, value lengths),
  saved, synced and rewound with it.
- Element and attribute **names follow XmlUI** (`Panel`, `rectAlignment`, `offsetXY`, …) so the translator
  (`core/Tts/XmlUi.cs`) only parses, applies `<Defaults>` (tag, then class, then the element's own attributes), fixes
  attribute case and writes booleans as `True` / `False` (TTS scripts compare `getAttribute(id, "active") == "True"`).
  The rules UI (`ui`) stays as it is for agent-written games.
- Only the host's scripts change script UI, through two host commands: `SetScriptUi` (replace a tree: `UI.setXml`,
  `setXmlTable`) and `SetUiAttributes` (one element's attributes and text: `setAttribute`, `setAttributes`, `setValue`,
  `show`, `hide`). Players use it with `UiEvent` (a click, a changed or finished input), which changes nothing; the host's
  scripts run the handler as TTS does, `fn(player, value, id)`, in the owner named by `Global/fn` or `guid/fn`, else in the
  script the UI belongs to. Elements are found by their path of child indices, because many clickable elements have no id.
- Each machine draws the tree with UI Toolkit: the global tree on the screen, an object's tree on the object.
  `visibility` (colours) hides elements from other seats on their machines; like hidden zones, this is for honest play,
  not secrecy against a modified client (see G33).

## Consequences

- Clients draw whatever the host's scripts build, with no Lua on clients (ADR 0003 holds).
- Scripts that rebuild large trees often (`setXml` every frame) cost a full tree per command; the local mods call
  `setXml` rarely (36 uses) and `setAttribute` often, which is the cheap path.
- The layout rules of XmlUI (uGUI rect transforms and layout groups) are reproduced in UI Toolkit, not run as-is; where
  they differ, `docs/lua-api.md` lists what to check against TTS.
- Text typed into inputs stays on the typing machine until a script stores it (as in TTS, where scripts usually call
  `setAttribute(id, "text", value)` in `onEndEdit`).
