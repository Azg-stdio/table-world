# M1 — Basic 3D table

**Goal:** a solo-playable table with physical cards, decks, tokens and dice, saved and loaded from the `core/` model as JSON.
**Done when:** deal a hand from a sprite-sheet deck, flip, shuffle, roll dice, save, close, load and see it unchanged. 500 objects at 60 fps (MVP); 2 000 later (T2.8).
**In the MVP:** yes. **Status:** in progress. **Depends on:** M0.

**Binding design rule:** the Unity view never mutates state on its own. Every interaction becomes a `Command` that `core` executes; the view reacts to `StateEvent`s. Physics only moves free objects and reports the final pose as a `Command`. This is what makes M3 a transport layer instead of a rewrite.

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T2.1 | `core/Rules`: table model v0 (`TableState`, object types from CLAUDE.md §5, zones, seats, transform, container, `states`), JSON serialization, validation | M | T1.1, T1.3 | Tests: JSON round trip, unique ids, acyclic containers, unknown-key rejection | done 2026-10-22 |
| T2.2 | `core/Engine`: `Command`, `CommandResult`, `StateEvent`; commands move, flip, shuffle, search, deal, lock, group, roll; seeded RNG | M | T2.1 | One test per command; same seed → same shuffle and rolls | done 2026-10-22 |
| T2.3 | `Table` scene built by an editor script: table mesh + collider, lighting, orbit/zoom/pan camera | S | T1.2 | Scene regenerable from scratch by running the script | done 2026-10-22 (camera feel not yet hand-tested) |
| T2.4 | Code-generated prefabs: `Card`/`Deck` from sprite sheet (columns × rows, face/back, 10×7 default), `Token`, `Tile`, `Die` d4–d20; generated placeholder sheet for the standard deck | M | T2.1, T2.3 | A sample table with all types renders; cards show the right cell | done 2026-10-22 |
| T2.5 | Interaction: raycast pick, grab/drop with PhysX, lift height, rotate in hand, flip, snap to `SnapPoint`; every gesture ends in a `Command` | M | T2.2, T2.4 | Manual check list in the PR + T2.9 | done 2026-10-22 (scripted gesture run passed; manual mouse checklist below pending) |
| T2.6 | Zones: `HandZone` per seat (hidden from others, visual only in solo), `ScriptingZone`, `SnapPoint`; enter/leave events | S | T2.5 | Tests in core for zone membership; visual check | done 2026-10-22 |
| T2.7 | Save/load table to the `core` JSON | S | T2.1, T2.4 | Save → reload → identical state hash | done 2026-10-22 |
| T2.8 ⚗ | Performance: generated scene with 2 000 cards + 200 tokens; measure fps and memory; instancing/atlas if needed | S | T2.4 | Numbers recorded below | deferred past MVP (500 objects is the MVP bar) |
| T2.9 ⚗ | PlayMode smoke test: load sample table, run 50 random commands | S | T2.5 | `unity command test run --mode PlayMode` green | todo |
| T2.10 | HUD v0 with UI Toolkit under `_Project/UI/` (UXML/USS, one `PanelSettings`): turn indicator, seat list, context actions for the selected object. Load the `unity:ui-uitk` skill first | S | T2.5 | HUD visible; buttons issue `Command`s | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| — | T2.8 2 000 objects | — |
| — | T2.9 smoke test | — |
| 2026-10-22 | M1 scenario in play mode: deal 5, flip, shuffle, roll d20, save, exit play mode, start, load, wait 6 s | hash identical after load and after 6 s of physics (`sha256:5824b774…`); first attempt drifted 2–30 mm because old views overlapped new ones for one physics step, fixed by deactivating before `Destroy` |

## Assumptions recorded during implementation

| Task | Assumption |
|---|---|
| T2.2 | Decks stay decks at 0–1 cards; dice values come from the engine RNG, not physics; flipping a deck reverses it. Details in `docs/rules-schema.md` → "Table commands". |
| T2.3 | Table is 1.6 × 1.0 m with a 2 cm rim, surface at Y = 0. Small-object physics: contact offset 0.001 m, fixed step 0.01 s, solver 12/4 iterations, sleep threshold 0.0005; URP shadow distance 6 m. Applied by `ProjectSettingsSetup` (menu **Table World → Apply Project Settings**). Camera: right drag orbit, middle or Shift+right drag pan, wheel zoom, WASD pan, Q/E turn. |
| T2.4 | No prefab assets: `ObjectViewFactory` builds every type from code at runtime (sizes are real-world: 63.5 × 88.9 × 0.4 mm cards, 16 mm d6, 25 mm tokens). `builtin:standard_deck` / `standard_back` / `dice_numbers` are drawn at runtime with a 5×7 pixel font (`BuiltinTextures`). State positions are object centres; y = 0 means "resting on the table" and the view lifts the object onto it. A die's stored value wins over its stored rotation. Known limits: d4 reads the bottom face and its numbers are not at the vertices; `Model` renders a grey placeholder until M4. Sample table: `tools/samples/make_sample_table.py` → `Resources/Tables/sample.json` (checked by a core test). |
| T2.5 | Controls: left drag grabs; on a non-empty deck or a bag it takes one item (top card / first object), Shift + left drag moves the container. Release over a card, deck or bag inserts (card on card forms a deck); within 4 cm of a snap point it snaps (and takes its yaw if `rotationSnap`). F flip, R roll/shuffle, L lock, Q/E rotate held object 15°, 1–9 over a deck deals to the local seat (solo: first seat). Physics sync: a free body that falls asleep (or stays awake 2 s) away from its stored pose (> 0.5 mm or 0.5°) sends a `Move`. Dice: a 0.55 s kinematic tumble lands on the engine's value, then reports its pose. |

## Manual checklist (T2.5, needs a human with a mouse)

Open `Assets/_Project/Scenes/Table.unity`, press Play, and check:

- [ ] Right drag orbits, middle drag pans, wheel zooms, WASD pans, Q/E turns the camera.
- [ ] Left drag on the blue deck pulls the top card; it hovers and follows the cursor; releasing drops it on the felt.
- [ ] Shift + left drag on the deck moves the whole deck.
- [ ] Dropping a card on another card makes a two-card deck; dropping it on a deck adds it on top.
- [ ] F over a card flips it; F over the face-up clubs deck flips it and shows the other end.
- [ ] R over a die tumbles it and it lands showing a number; R over the deck shuffles it.
- [ ] Hold a card and press Q/E: it rotates in 15° steps; the camera does not turn.
- [ ] Press 5 over the deck: five cards appear face up in the south hand area.
- [ ] L over the d20 locks it; dragging it does nothing; L again unlocks.
- [ ] Dropping a card near the empty spot right of the decks snaps it there.
- [ ] Drag from the brown bag: a green token comes out. Drag from the purple bag: a new coin every time.
| T2.7 | Saves are the core table JSON in `<persistentDataPath>/Saves/<name>.json` (F5 quick save, F9 quick load). `Save` first records every live physics pose. Asset paths in a save resolve against the package the table was first loaded from. The RNG state is not saved: a reloaded table shuffles differently. |
