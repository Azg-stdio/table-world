# M1 — Basic 3D table

**Goal:** a solo-playable table with physical cards, decks, tokens and dice, saved and loaded from the `core/` model as JSON.
**Done when:** deal a hand from a sprite-sheet deck, flip, shuffle, roll dice, save, close, load and see it unchanged. 500 objects at 60 fps (MVP); 2 000 later (T2.8).
**In the MVP:** yes. **Status:** reopened 2026-10-22 for T2.11 (TTS interaction parity); T2.1–T2.10 done (manual checklist pending), T2.8 deferred. **Depends on:** M0.

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
| T2.9 ⚗ | PlayMode smoke test: load sample table, run 50 random commands | S | T2.5 | `unity command test run --mode PlayMode` green | done 2026-10-22 |
| T2.10 | HUD v0 with UI Toolkit under `_Project/UI/` (UXML/USS, one `PanelSettings`): turn indicator, seat list, context actions for the selected object. Load the `unity:ui-uitk` skill first | S | T2.5 | HUD visible; buttons issue `Command`s | done 2026-10-22 |

### T2.11 — TTS interaction parity (added 2026-10-22 after Miguel's playtest)

Spec and status per interaction: [../tts-interactions.md](../tts-interactions.md). Every row gets a test; rows marked unverified there are checked against TTS by Miguel.

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T2.11a | Core commands: scale, delete, clone (copy/paste), set state, cut, split, spread, set die value, set name/description/tint; `CreatedIds` on results | M | T2.2 | One xUnit test per command, including failures; random-command property test covers them | done 2026-10-22 |
| T2.11b | Rotate held or hovered (Q/E, Alt axis), flip by F / MMB click / Alt+MMB, scale (+/−), place under (U), nudge (N), tap down (RMB while holding), Alt+RMB drop bottom card | M | T2.11a | PlayMode tests per gesture | done 2026-10-22 |
| T2.11c | Looking: Alt zoom, Alt+Shift peek, hover tooltip | S | T2.10 | PlayMode tests | done 2026-10-22 |
| T2.11d | Number-row draw with 1 s buffer (multi-digit), digits switch states, G group, box select + pick up selection + RMB add to held group | M | T2.11a | PlayMode tests | done 2026-10-22 |
| T2.11e | Context menus (object and global) with the per-type actions: flip, rotate, lock, copy, delete, name, tint, deck draw/deal/search/cut/split/spread/shuffle, die roll/set, bag search/shuffle, states | M | T2.11a | PlayMode tests click every entry | done 2026-10-22 |
| T2.11f | Copy/paste (Ctrl+C/V), delete (Del), undo/redo (Ctrl+Z/Y), camera: Space reset, Z/MMB quick zoom, P top-down, Ctrl/Shift+digit save/load; Tab ping and measure | M | T2.11a | PlayMode tests | done 2026-10-22 |
| T2.11g | Input binding tests: simulated keyboard and mouse (Input System test fixture) for every binding in the spec | M | T2.11b–f | `test run --mode PlayMode` green with one test per binding | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| — | T2.8 2 000 objects | — (deferred) |
| 2026-10-22 | T2.11b-f PlayMode tests (`Tests/PlayMode/TtsInteractionTests.cs`, real Table scene) | 19/19 plus smoke 1/1. Found and fixed: an Alt look opened by code was cancelled on the next frame by the key loop; nudge at 0.35 m/s moved a token only 5 mm on felt (now 0.8 m/s) |
| 2026-10-22 | MVP bar: 500 generated objects (300 cards, 100 tokens, 50 d6, 50 hex tiles) in Editor play mode, unfocused with CLI auto-tick, Windows 11 + NVIDIA GPU | sample table 195.9 fps; 500 resting 88.8 fps (1 350 frames / 15.2 s); 500 dropped from 10 cm, averaged over 27 s incl. settling, 87.0 fps; Editor memory 971 → 1 070 MB. Above the 60 fps bar without instancing; a player build should be faster |
| 2026-10-22 | T2.9 smoke test (`Tests/PlayMode/SmokeTests.cs`, seed 2026) | 1/1 passed: 50 commands (10 of them real grab/drag/drop gestures), 19 succeeded, 31 rejected with codes as expected; 25 objects on the table at the end, views = state, 0 errors |
| 2026-10-22 | M1 scenario in play mode: deal 5, flip, shuffle, roll d20, save, exit play mode, start, load, wait 6 s | hash identical after load and after 6 s of physics (`sha256:5824b774…`); first attempt drifted 2–30 mm because old views overlapped new ones for one physics step, fixed by deactivating before `Destroy` |

## Assumptions recorded during implementation

| Task | Assumption |
|---|---|
| T2.2 | Decks stay decks at 0–1 cards; dice values come from the engine RNG, not physics; flipping a deck reverses it. Details in `docs/rules-schema.md` → "Table commands". |
| T2.3 | Table is 1.6 × 1.0 m with a 2 cm rim, surface at Y = 0. Small-object physics: contact offset 0.001 m, fixed step 0.01 s, solver 12/4 iterations, sleep threshold 0.0005; URP shadow distance 6 m. Applied by `ProjectSettingsSetup` (menu **Table World → Apply Project Settings**). Camera: right drag orbit, middle or Shift+right drag pan, wheel zoom, WASD pan, Q/E turn. |
| T2.4 | No prefab assets: `ObjectViewFactory` builds every type from code at runtime (sizes are real-world: 63.5 × 88.9 × 0.4 mm cards, 16 mm d6, 25 mm tokens). `builtin:standard_deck` / `standard_back` / `dice_numbers` are drawn at runtime with a 5×7 pixel font (`BuiltinTextures`). State positions are object centres; y = 0 means "resting on the table" and the view lifts the object onto it. A die's stored value wins over its stored rotation. Known limits: d4 reads the bottom face and its numbers are not at the vertices; `Model` renders a grey placeholder until M4. Sample table: `tools/samples/make_sample_table.py` → `Resources/Tables/sample.json` (checked by a core test). |
| T2.5 | Controls: left drag grabs; on a non-empty deck or a bag it takes one item (top card / first object), Shift + left drag moves the container. Release over a card, deck or bag inserts (card on card forms a deck); within 4 cm of a snap point it snaps (and takes its yaw if `rotationSnap`). F flip, R roll/shuffle, L lock, Q/E rotate held object 15°, 1–9 over a deck deals to the local seat (solo: first seat). Physics sync: a free body that falls asleep (or stays awake 2 s) away from its stored pose (> 0.5 mm or 0.5°) sends a `Move`. Dice: a 0.55 s kinematic tumble lands on the engine's value, then reports its pose. |
| T2.7 | Saves are the core table JSON in `<persistentDataPath>/Saves/<name>.json` (F5 quick save, F9 quick load). `Save` first records every live physics pose. Asset paths in a save resolve against the package the table was first loaded from. The RNG state is not saved: a reloaded table shuffles differently. |
| T2.9 | Player setting **Run In Background = on** (kept on purpose after the CLI override wrote it): the authoritative host in M3 must keep simulating while its window is unfocused, and it lets the CLI drive Play mode. |
| T2.10 | HUD: `UI/Hud/Hud.uxml` + `Hud.uss`, one `UI/PanelSettings.asset` (made by `HudBuilder`), own theme `UI/TableWorld.tss` (only stretches the document root; no Unity default theme). The controller lives in `Scripts/View/Hud/` instead of next to the UXML so it stays in the `TableWorld.Runtime` asmdef. Seat colours are USS modifier classes. Clicks over opaque HUD elements never reach the table. Turn line says "Free play" until the rules engine (M2) provides turns. |
| T2.5 (fix) | Dice physics after Miguel's playtest: shaking a held die spins it (rolls in the drag direction, ≤ 40 rad/s) and releasing throws it with that spin (≤ 1.2 m/s); physics decides the face (`MoveCommand.dieValue`). Dice use continuous collision, a plastic material (bounce 0.25, friction 0.35/0.5), max spin 50 rad/s, and a settle assist that damps slide and vertical-axis spin once a die rests flat on a face (PhysX barely resists that spin, so dice slid in circles for 6+ s). Pose reports from an object's own physics no longer snap it (that zeroed its velocity every 2 s). Measured: 8 shaken throws of d4–d20 all land on a face and sleep 0.6–2.1 s after release. |
| T2.5 (fix 2) | Flipping (or any command on an object) right after a drop snapped it back to where it was picked up: a settling body has not reported its pose yet. `TableView.Execute` now records the live pose of every object a command targets before running it. |

## Manual checklist (T2.5, needs a human with a mouse)

Open `Assets/_Project/Scenes/Table.unity`, press Play, and check:

- [ ] Right drag orbits, middle drag pans, wheel zooms, WASD pans, Q/E turns the camera.
- [ ] Left drag on the blue deck pulls the top card; it hovers and follows the cursor; releasing drops it on the felt.
- [ ] Shift + left drag on the deck moves the whole deck.
- [ ] Dropping a card on another card makes a two-card deck; dropping it on a deck adds it on top.
- [ ] F over a card flips it; F over the face-up clubs deck flips it and shows the other end.
- [ ] R over a die tumbles it and it lands showing a number; R over the deck shuffles it.
- [ ] Grab a die and shake the mouse: it spins; release while moving: it is thrown, tumbles, lands flat and stops within ~2 s; the HUD shows that number.
- [ ] Hold a card and press Q/E: it rotates in 15° steps; the camera does not turn.
- [ ] Press 5 over the deck: five cards appear face up in the south hand area.
- [ ] L over the d20 locks it; dragging it does nothing; L again unlocks.
- [ ] Dropping a card near the empty spot right of the decks snaps it there.
- [ ] Drag from the brown bag: a green token comes out. Drag from the purple bag: a new coin every time.
- [ ] Click the deck: the HUD panel bottom-left shows its actions; "Deal 5 to me" works; clicking a HUD button does not grab the object behind it.
- [ ] Save (F5), move things around, Load (F9): the table returns to the saved state.
