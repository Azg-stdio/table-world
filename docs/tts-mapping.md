# TTS equivalence table — v0

Status: **v0**, checked against the 6 local saves on 2026-10-22 (T1.3). The importer (M7) implements it; the table model in `core/` (T2.1) must be able to hold every field listed here. The implementing agent owns this file.

## Units and axes

- Table World native unit: **meters**, Y up, left-handed (same as Unity, same as TTS). No axis flip on import.
- **ONE constant:** `TtsUnitsToMeters = 0.03` (3 cm per TTS unit), defined once in `core/Tts`. Rationale: a TTS `Card` at scale 1 is approximately 2 × 3 TTS units and a real poker card is 63.5 × 88.9 mm; 0.03 gives 60 × 90 mm, close enough for physics and legible defaults. Our own default card is 63.5 × 88.9 mm regardless of this constant.
  - Data check (T1.3): the Pokémon Master Trainer mat has card snap slots 4.40 units apart for cards at scale 1.48, so a scale-1 card is at most 2.97 units wide: consistent with ~2 units. The exact mesh bounds cannot be read from a save; T8.1 measures them once with `getBounds()` in a running TTS and adjusts this constant if it is off by more than 10 %.
- **Table surface:** `posY ≈ 0.96` TTS units on the default tables (all global snap points lying on the bare table in two mods have `y = 0.96`). The importer maps `ourY = (ttsY − 0.96) × TtsUnitsToMeters`, so the table surface is Y = 0 like in our model. Custom tables (`Table_Custom`) may differ; the importer reads the table preset's height when it is not a default table.
- Rotation: TTS `rotX/rotY/rotZ` are Euler degrees, same convention as Unity. Flipped cards have `rotZ ≈ 180`.
- Scale: TTS `scaleX/Y/Z` multiply the base mesh; import as `transform.scale` and let the object type define the base size.

## Save file top level (what we read)

| TTS field | Ours | Notes |
|---|---|---|
| `SaveName`, `GameMode`, `VersionNumber` | `game.name`, metadata | — |
| `Table`, `TableURL` | table preset | `Table_None`, `Table_RectangleGlass`, etc. → nearest preset; custom URL → image on default table |
| `Sky`, `SkyURL`, `Lighting` | ambience | M6, ignored before |
| `Hands.HandTransforms[]` | `HandZone` per `Color` | `Hands.Enable`, `Hands.DisableUnused` |
| `SnapPoints[]` (global) | `SnapPoint` zones | `Position`, `Rotation`, `Rotation_snap` |
| `LuaScript`, `LuaScriptState`, `XmlUI` | `scripts.global`, saved state, UI (translated in T8.3) | `#include` already resolved in saves |
| `ObjectStates[]` | `objects` + `zones` | recursive via `ContainedObjects` and `States` |
| `TabStates`, `Notes`, `Decals`, `Grid`, `Turns` | notes, decals, grid settings, turn order | `Turns.TurnOrder`, `Turns.Enable` → `turn` |

## Object types

TTS `Name` → our `type`. Prefix `Custom_` means the mesh/image comes from a URL.

| TTS `Name` | Ours | Fields to carry |
|---|---|---|
| `Card`, `CardCustom` | `Card` | `CardID` = deckKey × 100 + index; `CustomDeck[deckKey]` gives `FaceURL`, `BackURL`, `NumWidth`, `NumHeight`, `BackIsHidden`, `UniqueBack`; `SidewaysCard`, `HideWhenFaceDown` |
| `Deck`, `DeckCustom` | `Deck` | `DeckIDs[]`, `ContainedObjects[]` (full cards), `CustomDeck{}`; `DeckIDs[0]` is the **top** card, same as our `cards[0]`. Contained cards' `Transform` is stale (their last position before joining) and is ignored |
| `Custom_Token` | `Token` | `CustomImage.ImageURL`, `ImageSecondaryURL`, `ImageScalar`, `WidthScale`, `CustomToken.Thickness`, `MergeDistancePixels`, `StandUp`, `Stackable` |
| `Custom_Tile` | `Tile` | `CustomImage.CustomTile.Type` (0 box, 1 hex, 2 circle, 3 rounded), `Thickness`, `Stackable`, `Stretch` |
| `Die_4/6/8/10/12/20`, `Die_6_Rounded` | `Die` | `RotationValues[]` (value ↔ rotation), `faces` |
| `Custom_Dice` | `Die` | `CustomImage.CustomDice.Type` (0 d4, 1 d6, 2 d8, 3 d10, 4 d12, 5 d20), image |
| `Custom_Model` | `Model` | `CustomMesh.MeshURL` (OBJ), `DiffuseURL`, `NormalURL`, `ColliderURL`, `Convex`, `MaterialIndex` (0 plastic, 1 wood, 2 metal, 3 cardboard, 4 glass), `TypeIndex` (0 generic, 1 figurine, 2 dice, 3 coin, 4 board, 5 chip, 6 bag, 7 infinite bag), `CastShadows` |
| `Custom_Model_Bag`, `Custom_Model_Infinite_Bag` | `Bag` / `InfiniteBag` with a `Model` look | as `Custom_Model` + `ContainedObjects` |
| `Custom_Assetbundle` | `Model` (source = bundle) | `CustomAssetbundle.AssetbundleURL`, `AssetbundleSecondaryURL`, `MaterialIndex`, `TypeIndex`, `LoopingEffectIndex`, `TriggerEffectIndex`; load via ADR 0001 remap or placeholder |
| `Custom_Assetbundle_Bag`, `Custom_Assetbundle_Infinite_Bag` | `Bag` / `InfiniteBag` with a bundle look | as above + `ContainedObjects` |
| `Bag`, `Infinite_Bag` | `Bag` / `InfiniteBag` | `ContainedObjects` |
| `Custom_Board` | `Board` | `CustomImage.ImageURL`, `WidthScale` |
| `Figurine_Custom` | `Figure` | `CustomImage.ImageURL` (standee) |
| `Checker_*`, `Chess_*`, `BlockSquare`, `BlockRectangle`, `Bowl`, `Domino`, `Go_*` | `Token` / `Model` with built-in presets | map by name to the default asset kit |
| `Notecard`, `Counter`, `Custom_PDF`, `Tablet`, `Custom_Text` | `Notecard`, `Counter` (UI), out of v0 for PDF/Tablet/Text | keep as placeholder with notes |
| `ScriptingTrigger` | `ScriptingZone` | transform + scale is the box |
| `HandTrigger` | `HandZone` | `FogColor` = owner color; hidden to others |
| `FogOfWarTrigger`, `RandomizeTrigger`, `LayoutZone` | `HiddenZone`, (randomize zone), (layout zone) | randomize/layout: placeholder zones in v0 |

## Common object fields

| TTS | Ours |
|---|---|
| `GUID` (6 hex) | `id` = `tts_<guid>`; collisions get a numeric suffix |
| `Nickname` | `name` |
| `Description` | `description` |
| `GMNotes` | `gmNotes` |
| `Tags[]` | `tags` |
| `Locked` | `locked` |
| `Transform` | `transform` (position: subtract table height on Y, then × `TtsUnitsToMeters`; scale as is) |
| `ColorDiffuse {r,g,b}` | `tint` |
| `States {n: object}` | `states` |
| `ContainedObjects[]` | children with `container` = this id |
| `LuaScript`, `LuaScriptState` | `script`, saved state |
| `XmlUI` | per-object UI (T8.3) |
| `AttachedSnapPoints[]` | `SnapPoint` zones parented to the object |
| `Hands` (bool) | `hideInHand` semantics |
| `Sticky`, `Tooltip`, `Grid`, `Snap`, `IgnoreFoW`, `MeasureMovement`, `DragSelectable`, `Autoraise`, `HideWhenFaceDown` | interaction flags, kept verbatim in `ttsFlags` until we need them |
| `AltLookAngle`, `Number`, `Text`, `Bag.Order` | kept in `ttsExtra` |

## Sprite sheets

TTS decks: `NumWidth` × `NumHeight` cells, row-major, index 0 at top-left. Default custom deck layout is 10 × 7 (70 cards). Our `Deck.spriteSheet` uses the same convention. `UniqueBack` means the back URL is also a sheet with per-card backs.

## Assets and URLs

- Cache key in TTS: URL with every non-alphanumeric character removed, plus extension (`.unity3d`, `.png`/`.jpg`, `.obj`, `.pdf`), under `Mods/{Assetbundles,Images,Models,PDF}`. The importer looks there first (`tools/tts-analysis/analyze_mods.py` has the key function).
- 86 % of URLs on `cloud-3.steamusercontent.com`, 9.5 % on `steamusercontent-a.akamaihd.net`. Download on demand with a concurrency limit, store by content hash on our side.
- OBJ models (`Custom_Model`) → convert to glTF at import (M7); AssetBundles → ADR 0001.

## Lua compatibility priority (from real usage)

`UI.setAttribute`, `getName`, `getObjectFromGUID`, `getLuaScript`, `broadcastToAll`, `getObjects`, `getGMNotes`, `UI.getAttribute`, `createButton`, `getPosition`, `editButton`, `onLoad`, `setValue`, `Wait.time`, `setRotation`, `getRotation`, `getGUID`, `JSON.decode`, `call`, `Wait.condition`, `setLuaScript`, `setPosition`, `Wait.frames`, `takeObject`, `getDescription`, `getTable`, `getZones`, `shuffle`, `deal`, `destruct`.

Re-run `python -I tools/tts-analysis/analyze_mods.py` after adding mods to refresh this list.

## Verification status

| Item | Status |
|---|---|
| Deck order | **Resolved (T1.3):** `DeckIDs[0]` is the top |
| Table surface height | **Resolved (T1.3):** 0.96 units on default tables |
| Card base size in TTS units → `TtsUnitsToMeters` | **Bounded (T1.3):** ≤ 2.97 units wide; exact `getBounds()` in T8.1 |
| `RotationValues` on dice, `Custom_Dice` value ↔ rotation | Open → T8.1 (no dice logic needs it before the importer) |
