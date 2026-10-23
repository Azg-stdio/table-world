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
| Reset camera | Space | KB:basic | ✅ (the start view is behind your own seat: your seat online, the first seat alone) | play, input |
| Camera mode (3rd person, 1st person, top-down) | P | KB:basic, KB:adv | ✅ 3rd + top-down (first person ⏭ M6) | play, input |
| Save / load camera | Ctrl+1…9 / Shift+1…9 | KB:menu | ✅ | play, input |
| Turn camera | Q/E when not holding | — (ours) | ✅ | input |

## 2. Picking up and moving

| Interaction | TTS binding | Source | Status | Test |
|---|---|---|---|---|
| Pick up / drop | LMB press / release | KB:basic | ✅ | play, input |
| Drop onto deck/card/bag joins it | release over it | unverified | ✅ (a card or deck covering ≥ 15 % of a card or deck joins it; anything covering ≥ 30 % of a bag goes in) | play |
| Held objects lift, then hover above what is under them (Auto Raise, on by default in TTS) | automatic while holding | KB:menu | ✅ (a held object never sweeps its neighbours) | play |
| Snap to snap points when dropped | release near point | KB:menu (Snap toggle) | ✅ | play |
| Take top card by dragging a deck; move whole deck | press and drag at once = take the top card/item; press and hold still ≈0.35 s = lift the whole deck or bag; click = select only | TTS (Miguel, 2026-10-23) | ✅ (Shift+drag also lifts the whole container) | input |
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
| Hands toggle (can go to hand) | menu toggle | KB:menu | ⏭ polish (every card can go to a hand today) | — |
| Hand view: your cards stand at your table edge facing you, others see backs; others' hand cards cannot be picked up, and their name never shows | automatic in a hand zone | KB:basic | ✅ (view only: every client still receives the whole table) | play |
| Reorder your hand | drag a card within your hand | KB:basic | ✅ | play |

## 4b. Playing a rules game by hand (Table World; TTS has no rules engine)

While a game with rules runs, gestures become rules actions, so turns, limits and win checks apply without the button list:

| Gesture | Becomes | Status | Test |
|---|---|---|---|
| Drag a card out of your hand and drop it anywhere outside the hand | the legal action that takes that card as a parameter (UNO: play it); not legal → back to the hand with the reason | ✅ | core, play |
| Drag from a pile the rules draw from | that legal draw action (UNO: draw and end the turn; War: play your top card); not legal → refused with the reason | ✅ | core, play |
| Drag from a pile the rules only gather (UNO's discard) | refused: piles the rules use cannot be taken from freely | ✅ | core, play |
| Anything else (dice, free tokens, cards on the table) | free play, as before | ✅ | — |

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

## Physics behaviour (Table World failsafes, 2026-10-23)

Measured by `PhysicsDropTests` (careless drops, the way people play). Each must come to rest within 1.5 s, at most 5 cm from where it was let go, flat (≤ 3°), on the table, with the state recording the rest pose:

| Drop | Result |
|---|---|
| Still on felt / from 20 cm up | stops at once, flat |
| Thrown fast / thrown at the rim | slides 4.5 cm, stops inside the rim (drags never leave the play area) |
| Half on a card / ≥ 15 % on a deck | joins it (TTS) |
| 8 % over a deck's edge, half on a die | laid flat beside it (two-stage lay-flat failsafe) |
| On a hand zone's edge, sloppily into a hand with cards | joins the hand; standing hand cards are triggers, so nothing collides with them |
| Mostly over a bag | goes into the bag |

Also: overlapping bodies separate at ≤ 0.25 m/s instead of exploding apart; no object exceeds 3 m/s; anything that falls off or through the table is put back on it; a body that is nearly still for 0.25 s counts as resting (so a slow-creeping body is not left unrecorded); a body released, laid flat or teleported is always recorded when it rests, even if it falls asleep within one slow frame.
