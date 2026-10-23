# M1 — Basic 3D table

**Goal:** a solo-playable table with physical cards, decks, tokens and dice, saved and loaded from the `core/` model as JSON.
**Done when:** deal a hand from a sprite-sheet deck, flip, shuffle, roll dice, save, close, load and see it unchanged. 500 objects at 60 fps (MVP); 2 000 later (T2.8).
**In the MVP:** yes. **Status:** in progress. **Depends on:** M0.

**Binding design rule:** the Unity view never mutates state on its own. Every interaction becomes a `Command` that `core` executes; the view reacts to `StateEvent`s. Physics only moves free objects and reports the final pose as a `Command`. This is what makes M3 a transport layer instead of a rewrite.

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T2.1 | `core/Rules`: table model v0 (`TableState`, object types from CLAUDE.md §5, zones, seats, transform, container, `states`), JSON serialization, validation | M | T1.1, T1.3 | Tests: JSON round trip, unique ids, acyclic containers, unknown-key rejection | done 2026-10-22 |
| T2.2 | `core/Engine`: `Command`, `CommandResult`, `StateEvent`; commands move, flip, shuffle, search, deal, lock, group, roll; seeded RNG | M | T2.1 | One test per command; same seed → same shuffle and rolls | todo |
| T2.3 | `Table` scene built by an editor script: table mesh + collider, lighting, orbit/zoom/pan camera | S | T1.2 | Scene regenerable from scratch by running the script | todo |
| T2.4 | Code-generated prefabs: `Card`/`Deck` from sprite sheet (columns × rows, face/back, 10×7 default), `Token`, `Tile`, `Die` d4–d20; generated placeholder sheet for the standard deck | M | T2.1, T2.3 | A sample table with all types renders; cards show the right cell | todo |
| T2.5 | Interaction: raycast pick, grab/drop with PhysX, lift height, rotate in hand, flip, snap to `SnapPoint`; every gesture ends in a `Command` | M | T2.2, T2.4 | Manual check list in the PR + T2.9 | todo |
| T2.6 | Zones: `HandZone` per seat (hidden from others, visual only in solo), `ScriptingZone`, `SnapPoint`; enter/leave events | S | T2.5 | Tests in core for zone membership; visual check | todo |
| T2.7 | Save/load table to the `core` JSON | S | T2.1, T2.4 | Save → reload → identical state hash | todo |
| T2.8 ⚗ | Performance: generated scene with 2 000 cards + 200 tokens; measure fps and memory; instancing/atlas if needed | S | T2.4 | Numbers recorded below | deferred past MVP (500 objects is the MVP bar) |
| T2.9 ⚗ | PlayMode smoke test: load sample table, run 50 random commands | S | T2.5 | `unity command test run --mode PlayMode` green | todo |
| T2.10 | HUD v0 with UI Toolkit under `_Project/UI/` (UXML/USS, one `PanelSettings`): turn indicator, seat list, context actions for the selected object. Load the `unity:ui-uitk` skill first | S | T2.5 | HUD visible; buttons issue `Command`s | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| — | T2.8 2 000 objects | — |
| — | T2.9 smoke test | — |
