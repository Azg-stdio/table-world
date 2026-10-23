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
| Held objects lift, then hover above what is under them (Auto Raise, on by default in TTS) | automatic while holding | KB:menu | ✅ and a held object collides with nothing (its own layer), so even pressed down it never shoves dice or cards | play |
| Snap to snap points when dropped | release near point | KB:menu (Snap toggle) | ✅ | play |
| Take top card by dragging a deck; move whole deck | press and drag at once = take the top card/item; press and hold still ≈0.35 s = lift the whole deck or bag; click = select only | TTS (Miguel, 2026-10-23) | ✅ (Shift+drag also lifts the whole container) | input |
| Box select | LMB drag on empty table, release highlights | KB:basic | ✅ | play, input |
| Pick up a selection (all highlighted) | LMB on one of them | KB:basic | ✅ | play |
| Add to held group | while holding, RMB over another object | KB:basic | ✅ | play |
| Tap down | RMB while holding | KB:basic | ✅ (lowers the held object while RMB is down) | play |
| Drop last grabbed / bottom card of held deck | Alt+RMB | KB:adv | ✅ | play |
| Rotate held or hovered | Q / E: tap = next 15° step, snapped (from 67°, E → 75°, Q → 60°); hold = turns smoothly (120°/s) and stops wherever released | Miguel (TTS), 2026-10-23 | ✅ animated; one command per gesture | play, input |
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
| Alt zoom (close-up of hovered object, best for cards) | hold Alt | KB:basic, KB:adv | ✅ flat objects (cards, decks, tokens, tiles, boards) are always shown square on to their face, even standing in a hand | play, input |
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
| Hand view: your cards stand at your table edge facing you, others see backs; others' hand cards cannot be picked up, and their name never shows | automatic in a hand zone | KB:basic | ✅ leaning 40° (faces the default camera), shown 3 cm toward your table edge, gliding into place (≈0.15 s); hiding is view only (every client still receives the whole table) | play |
| Hand bar: your hand at the bottom of the screen, whatever the camera does | automatic | KB:basic | ✅ resting cards show only their top (corner index and pip); hover raises and enlarges a card (×1.6); press a card to pick it up; release over the bar (or over your hand zone) to put a card straight into your hand at that spot, without a physics fall; **Hide hand / Show hand** sinks it to a thin strip (remembered on this machine) | input |
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

## 7. Gaps found in the 2026-10-23 re-audit

**Target: complete TTS parity** (Miguel, 2026-10-23), so a real TTS game can be played in Table World. Every mechanic below gets built, at TTS quality; the priority column only orders the work. Sections 1–6 covered single-player gestures; this list is what was still missing, checked in the code, not assumed. Rows marked *verify* come from memory of TTS and are checked in TTS before building. Each becomes ✅ only after the polish checks in `plan.md` rule 9 (real input, built exe, two clients, screenshots, clean console).

| # | Gap | What TTS does | Today | Priority |
|---|---|---|---|---|
| G1 | Seeing others move things | a held object moves live on every screen | ✅ presence frames (15/s, unreliable) carry held poses; others see it glide and fall when dropped (2026-10-23) | P1 |
| G2 | Other players' pointers | each player's hand cursor, in their colour, with their name | ✅ marker in the seat colour plus a name label | P1 |
| G3 | Who holds / selected what | outline in the holder's colour | ✅ for held objects (frame in the holder's colour; others cannot grab it: "seat_x is holding that"); others' box selections not shown yet | P1 |
| G4 | Controls help | `?` / Help menu lists every binding | ✅ F1, `?` or the Controls button; grouped list of every binding (`ControlsHelp.cs`, keep in step with this file); Escape closes; the wheel scrolls it, not the camera (2026-10-23) | P1 |
| G5 | Sound | pick up, drop, card slide, shuffle, dice, chips | ✅ Kenney CC0 packs: pickups by kind, landings from real impacts (louder for harder hits), shuffle, roll, deal, flip, ping; others' actions sound too; silent while a table settles after loading (2026-10-23). Volume setting comes with G13 | P1 |
| G6 | Deck count | hovering a deck or bag shows how many items | ✅ "52 cards", "3 items", "endless" under the name (2026-10-23) | P1 |
| G7 | Search shows cards | a grid of card faces, drag one out | ✅ grid of faces with names, top first; pressing a card picks it up so it follows the pointer (bag items as name tiles) | P1 |
| G8 | Hand card close-up | hovering a card in your hand raises and enlarges it | ✅ hand bar (2026-10-23) | P1 |
| G9 | Motion | flips, draws and deals animate | they snap | P2 |
| G10 | Seats | click a colour to sit there, spectator, change seat | assigned in join order | P2 |
| G11 | Chat and game log | chat box, messages and rule messages scroll back | only the last rules message | P2 |
| G12 | Lighting | even light around the table | ✅ game night room: warm lamp over the table plus a fill light (2026-10-23) | P2 |
| G13 | Escape menu | settings (mouse, volume, graphics), leave, quit | none | P2 |
| G14 | Token / chip stacks | same tokens stack into a counted stack, drag one off | tokens are separate | P2 |
| G15 | Blindfold, peek alert | B blindfolds; others see "X peeked" | ⏭ (needs G2/G11) | P3 |
| G16 | Toggles | Snap, Grid, Auto Raise, Sticky, Hands, Hide Face Down, Tooltip per object | Lock only | P3 |
| G17 | Hidden zones that hide | contents invisible to everyone but the zone's owner | ✅ seats the zone does not list see an opaque block tinted toward the seeing seat's colour; objects inside are not drawn and cannot be hovered or box selected; `host` in `visibleTo` means the hosting machine (2026-10-23). Clients still receive the hidden objects' data (as in TTS): honest play, not anti-cheat | P1 |
| G18 | Randomize, layout, fog-of-war zones | shuffle what is inside; auto-arrange contents; fog that players reveal | not implemented | P2 |
| G19 | Physics modes | host sets full, semi-lock (nothing topples) or locked | ✅ table menu (right-click the felt) → Physics: Full / Semi-lock / Locked, host only, saved with the table. Semi-lock freezes tipping (dice still tumble); locked sets drops down right where they are released, on the felt or on top of what is below (2026-10-23) | P1 |
| G20 | Several dice at once | select dice, R rolls them all | ✅ R (or the menu's "Roll N selected dice") rolls every selected or held die together, one throw sound, status line lists each value and the total (2026-10-23) | P1 |
| G21 | Custom dice | faces and rotation values from images (TTS `Custom_Dice`) | model only | P2 |
| G22 | Turns without rules | turn order, end turn button, skip empty hands | ✅ host: table menu → Turns: Off / In order / Reversed, Skip empty hands; the current seat gets End turn, its chip is outlined, online players are told "Your turn"; a hot-seat table hands the controls to whoever plays (2026-10-23). Unoccupied seats are not skipped yet (needs seat occupancy, G10) | P1 |
| G23 | Player management | colours and teams, Game Master (Black) sees everything, promote, kick, mute, per-player permissions | host and seats only | P2 |
| G24 | Chat commands and dice announcements | `/roll`, results in chat; per-team chat | none | P2 |
| G25 | Shared notebook | tabs per colour | none | P3 |
| G26 | Rewind time | host steps the table back (verify the window) | undo is local and off online | P2 |
| G27 | Built-in components | counter, calculator, timer/clock, notecard, tablet, music player | none | P2 (counter, timer P1) |
| G28 | Table tools | grid overlay and snapping, drawing, line/arrow, 3D text, decals, gizmo (precise move/rotate/scale), flick, joints | ping and measure only | P2 |
| G29 | Tables and environment | table types, backgrounds, lighting tool | one table | P3 |
| G30 | Saving | named saves, autosave, save objects to a chest | one quick save | P2 |
| G31 | Settings | rebindable keys, mouse sensitivity, volume, graphics | none | P2 (with G13) |
| G32 | Shaking a held bag or deck shuffles it | verify | no | P3 |
| G33 | Protocol-level hidden information | hands and hidden zones are not sent to players who cannot see them | every client receives the whole table | P2 |
| G34 | First person and physics characters | P toggles first person; characters (M6) | ⏭ M6 | — |

### Required by the local mods (Pokémon first)

**Acceptance: the two Pokémon mods (Master Trainer 3D, Legends of Sinnoh) play fully with friends in Table World**, then the other four local mods. The complete list of what each mod uses comes from `tools/tts-analysis/mod_requirements.py` → `docs/data/mod-requirements-2026-10-23.md` (types, per-object features, Lua events, every API call, XmlUI tags). Items not already in G1–G34:

| # | Needed by | What | Today |
|---|---|---|---|
| G35 | both Pokémon | AssetBundle models **with their animations and effects** (`playLoopingEffect` 920×, `playTriggerEffect` 604×) | bundles load (ADR 0001); effects not played |
| G36 | Sinnoh | PDF rulebooks on the table (`Custom_PDF`, page turning) | none |
| G37 | Master Trainer | Counter object (10), Notecard (1) | none (G27) |
| G38 | both | Primitive blocks (`BlockRectangle`, `BlockSquare`), custom models from OBJ (`Custom_Model` 1 937) | OBJ not loaded (M4 has glTF) |
| G39 | both | Snap points attached to objects (689 in Sinnoh), sideways cards (70), per-object `Hands=false` (3 655), `HideWhenFaceDown` (891), `Grid=false`, `GridProjection` | partial (model fields exist; behaviour per object not wired) |
| G40 | both | Joints (`jointTo`) | none |
| G41 | Sinnoh | Per-player visibility (`setInvisibleTo` 24×), highlights (`highlightOn/Off`), smooth moves (`setPositionSmooth`, `setRotationSmooth`, `isSmoothMoving`) | none (M5) |
| G42 | Master Trainer | `Physics.cast` (16×), collision events (`registerCollisions`, `onObjectCollisionEnter`) | none (M5) |
| G43 | Sinnoh | Scripts that rewrite scripts (`getLuaScript` 816×, `setLuaScript` 300×), `getVar`/`setVar`, coroutines (`startLuaCoroutine` 107×) | none (M5) |
| G44 | both | Environment from the save: custom table image (`Table_Custom` + `TableURL`), sky (`Sky_Museum`), lighting values, gravity (0.5), music playlist | 🟡 the `environment` block takes room, colours, lamp/ambient and a felt image (`docs/rules-schema.md` → Environment); the importer mapping, TTS sky presets, gravity and music are still to do |
| G45 | Sinnoh | Fog of war zones (6), layout zones (2), tags on everything (3 468), component tag labels (73) | tags exist; zones not (G18) |


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
