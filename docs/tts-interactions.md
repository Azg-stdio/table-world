# TTS interaction parity

The list of player interactions Table World must reproduce, taken from Tabletop Simulator. It is the spec for task T2.11 and its tests. Behaviour is copied, not code or assets (CLAUDE.md §5–7).

**Sources.** `KB:basic` = kb.tabletopsimulator.com/player-guides/basic-controls, `KB:adv` = …/advanced-controls, `KB:menu` = …/context-menu, `KB:states` = …/host-guides/creating-states, `KB:zones` / `KB:line` / `KB:flick` / `KB:joint` / `KB:decal` = …/game-tools/… (fetched 2026-10-22). The KB's full key list is only an image in its Help Menu, and per-object menus (deck, dice, bag) are not documented as text: rows marked **unverified** come from playing TTS and must be checked against the game (open TTS, press `?` for the Help Menu, right-click each object type) before they are considered final.

**Core commands (T2.11a, done):** `Scale`, `Delete`, `Clone`, `SetState`, `Cut`, `Split`, `Spread`, `SetDieValue`, `SetProperties` exist in `core` with xUnit tests; the rows below track the in-game gestures.

**Status:** ✅ done and tested · 🟡 partial · ❌ missing · ⏭ later milestone (with reason).
**Test:** `core` = xUnit command test; `play` = PlayMode test through `TableInteraction`'s public gestures; `input` = PlayMode test with simulated keyboard/mouse (Input System test fixture), which checks the binding itself.

## 1. Camera

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Orbit | hold RMB on empty table + move | KB:basic | ✅ | input |
| Pan | hold MMB + move; WASD | KB:basic | ✅ | input |
| Zoom | mouse wheel | KB:basic | ✅ | input |
| Quick zoom to pointer / back | press MMB (no drag) on empty table; Z | KB:basic | ✅ | play, input |
| Reset camera | Space | KB:basic | ✅ | play, input |
| Camera mode (3rd person, 1st person, top-down) | P | KB:basic, KB:adv | ✅ 3rd + top-down (first person ⏭ M6) | play, input |
| Save / load camera | Ctrl+1…9 / Shift+1…9 | KB:menu | ✅ | play, input |
| Turn camera | Q/E when not holding | — (ours) | ✅ | input |

## 2. Picking up and moving

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Pick up / drop | LMB press / release | KB:basic | ✅ | play, input |
| Drop onto deck/card/bag joins it | release over it | unverified | ✅ | play |
| Snap to snap points when dropped | release near point | KB:menu (Snap toggle) | ✅ | play |
| Take top card by dragging a deck; move whole deck | drag; **unverified which is default in TTS** | unverified | ✅ (drag = take one, Shift+drag = whole deck) | play |
| Box select | LMB drag on empty table, release highlights | KB:basic | ✅ | play, input |
| Pick up a selection (all highlighted) | LMB on one of them | KB:basic | ✅ | play |
| Add to held group | while holding, RMB over another object | KB:basic | ✅ | play |
| Tap down | RMB while holding | KB:basic | ✅ (lowers the held object while RMB is down) | play |
| Drop last grabbed / bottom card of held deck | Alt+RMB | KB:adv | ✅ | play |
| Rotate held or hovered | Q / E (15°) | unverified (KB: Q/E in Alt zoom) | ✅ | play, input |
| Rotate on another axis | Alt+Q / Alt+E / Alt+wheel | KB:adv | ✅ (Alt+wheel turns about the vertical) | play |
| Flip | F; MMB click on object; menu "Flip" | KB:basic | ✅ | play, input |
| Flip on another axis | Alt+MMB | KB:adv | ✅ | play |
| Scale | hold + / − over object or selection | KB:adv | ✅ | core, play |
| Place under | U over a target | KB:adv ("not meant for decks") | ✅ | play |
| Nudge | hold N and move the pointer: pushes objects away | KB:adv | ✅ | play |
| Raise/lower held object | wheel while holding, **unverified** | unverified | ✅ | play |
| Throw | release while moving | — | ✅ (dice spin, T2.5 fix) | play |

## 3. Looking

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Alt zoom (close-up of hovered object, best for cards) | hold Alt | KB:basic, KB:adv | ✅ | play, input |
| Zoom further / rotate in Alt zoom | wheel / Q E | KB:basic | ✅ | play |
| Peek (see the underside; others are alerted) | hold Alt + Shift | KB:adv | ✅ (alerting others: M3) | play |
| Magnify area | M (+ wheel) | KB:adv | ⏭ polish (after playtest) | — |
| Tooltip with name, then description | hover ~1 s | KB:menu (Tooltip toggle) | ✅ (name after 0.6 s, description after 1.6 s) | input |
| Hide name/description of face-down objects | menu toggle "Hide Face Down" | KB:menu | 🟡 honoured from `ttsExtra.HideWhenFaceDown`; no toggle yet | — |

## 4. Cards, decks, containers, dice

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Draw N to own hand | type N on number row over a deck/stack/bag (1 s delay; "1","2" quickly = 12) | KB:adv | ✅ | play, input |
| Shuffle deck / bag, roll die | R | unverified | ✅ | play, input |
| Deal to all players / to a seat | deck menu "Deal" | unverified | ✅ (deal 1 to everyone; draw 1 or 5 to own hand) | core, play |
| Search a deck/bag and take a card | menu "Search" | unverified | ✅ | core, play |
| Cut a deck (top half onto the table) | menu "Cut" | unverified | ✅ | core, play |
| Split a deck into N piles | menu "Split" | unverified | ✅ (2 to 4 from the menu) | core, play |
| Spread cards in a row | menu "Spread" | unverified | ✅ | core, play |
| Group (cards into a deck, objects into stacks) | G | KB:adv | ✅ cards and decks (token stacks: later) | core, play |
| Set a die's value | menu, **unverified** | unverified | ✅ | core, play |
| Switch object state | number keys over an object with states (KB example: key 2 opens a book); menu | KB:states | ✅ | core, play |
| Create states from a selection | menu "Create States" | KB:states | ⏭ authoring (M7 / MCP) | — |
| Lock | menu toggle "Lock" (L: unverified) | KB:menu | ✅ (L) | play |
| Hands toggle (can go to hand) | menu toggle | KB:menu | ⏭ M3 (hidden hands) | — |

## 5. Editing the table

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Context menu on objects | RMB on object | KB:menu | ✅ | play |
| Global context menu (camera mode, save/load camera, blindfold, paste) | RMB on empty table | KB:menu | ✅ (blindfold: M3) | play |
| Copy / paste | menu; Ctrl+C / Ctrl+V | KB:menu | ✅ | core, play, input |
| Delete | menu; Delete key (unverified) | KB:menu | ✅ | core, play |
| Undo / redo | Ctrl+Z / Ctrl+Y, **unverified** | unverified | ✅ (50 steps) | play |
| Name and description | menu | KB:menu | ✅ | core, play |
| Tint color | menu | KB:menu | ✅ | core, play |
| Toggles: Lock, Snap, Grid, Auto Raise, Sticky, Hands, Hide Face Down, Tooltip | menu | KB:menu | 🟡 Lock only | core |
| Save object to chest | menu | KB:menu | ⏭ M7 (catalog) | — |

## 6. Tools and multiplayer

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Ping (arrow in player color + chime) | Tab + click | KB:line | ✅ ring in seat colour (no sound yet) | play |
| Measure (distance while dragging) | Tab + drag | KB:line | ✅ cm and TTS inches | input |
| Blindfold | B | KB:adv | ⏭ M3 (needs other players) | — |
| Flick tool | tool | KB:flick | ⏭ after playtest | — |
| Zone tools (hidden, randomize, hand, layout, fog of war, scripting) | tool | KB:zones | ⏭ agent defines zones via MCP; in-game tool M7 | — |
| Joint, decal, drawing, text, gizmo, snap point tools | tools | KB:joint, KB:decal, site nav | ⏭ M6/M7 (host authoring) | — |
| Number pad scripting hotkeys | numpad | KB:adv | ⏭ M5 (Lua) | — |
| First-person camera, physics characters | P, WASD | KB:basic | ⏭ M6 | — |

## Notes

- **Conflicts we resolve on purpose:** TTS uses MMB *hold* to pan and MMB *click* to flip; we do the same (a click is a press-and-release without moving). Number keys draw over decks and switch states over objects with states (TTS does both). Shift+1…9 loads cameras; over a deck, plain digits draw.
- **Q/E:** rotate the held object, otherwise the hovered object; turn the camera only when the pointer is over nothing.
- Everything that changes the table goes through a core command (CLAUDE.md §3), so every row in sections 2–5 also works for an MCP agent and, later, over the network.
