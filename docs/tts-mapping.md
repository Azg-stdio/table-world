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
| `Table`, `TableURL` | `environment.table` | The table is sized to the layout (98 % of the pieces in play, plus 15 cm; never below our 1.6 × 1.0 m), because TTS tables are far bigger than ours: Pokémon spreads ±2.3 m, Gloomhaven ±4.4 m. `Table_None` → `hidden` (no table of ours; the game's own models are the surface), the median height of the pieces in play becomes our table height, and `lowestY` is 30 cm under the lowest piece. Table images (`TableURL`) not mapped yet |
| `Sky`, `SkyURL`, `Lighting` | ambience | M6, ignored before |
| `Hands.HandTransforms[]` | `HandZone` per `Color` | `Hands.Enable`, `Hands.DisableUnused` |
| `SnapPoints[]` (global) | `SnapPoint` zones | `Position`, `Rotation`, `Rotation_snap` |
| `LuaScript`, `LuaScriptState`, `XmlUI` | `scripts.global`, saved state, UI (translated in T8.3) | `#include` already resolved in saves |
| `ObjectStates[]` | `objects` + `zones` | recursive via `ContainedObjects` and `States` |
| `VectorLines` | `drawings` | `points3` × the unit constant → `points`; `color` → `color`; `thickness` → `thickness`; `rotation`, `loop`, `square` kept in `ttsExtra` (none in the local mods) |
| `TabStates` | `notebook` | each tab: `title`, `body`; `color` "Grey" → no colour (everyone), another colour → that seat's colour; empty per-colour tabs can be dropped |
| `Notes`, `Decals`, `Grid`, `Turns` | notes, decals, `grid`, turn order | `Turns.TurnOrder`, `Turns.Enable` → `turn`. `Grid`: `Type` (0 box, 1 hex) → `shape`; `Lines` → `lines`; `Snapping` → `snapping` (verify TTS's values: off / lines / center / both); `xSize`, `ySize` × the unit constant → `sizeX`, `sizeZ`; `PosOffset` → offsets; `Color`, `Opacity`. Each object's `Grid` flag → `grid` (false opts out) |

## Object types

TTS `Name` → our `type`. Prefix `Custom_` means the mesh/image comes from a URL.

| TTS `Name` | Ours | Fields to carry |
|---|---|---|
| `Card`, `CardCustom` | `Card` | `CardID` = deckKey × 100 + index; `CustomDeck[deckKey]` gives `FaceURL`, `BackURL`, `NumWidth`, `NumHeight`, `BackIsHidden`, `UniqueBack`; `SidewaysCard`, `HideWhenFaceDown` |
| `Deck`, `DeckCustom` | `Deck` | `DeckIDs[]`, `ContainedObjects[]` (full cards), `CustomDeck{}`; `DeckIDs[0]` is the **top** card, same as our `cards[0]`. Contained cards' `Transform` is stale (their last position before joining) and is ignored |
| `Custom_Token` | `Token` (shape `image`: cut along the image's alpha by `Silhouette`, holes filled, as TTS cuts them) | `CustomImage.ImageURL`, `ImageSecondaryURL`, `ImageScalar`, `WidthScale`, `CustomToken.Thickness`, `MergeDistancePixels`, `StandUp`, `Stackable`; `Number` (pieces in a stack) → `count` *(verify in TTS that stacks save this way; no local mod has a saved stack)*; `size` = 4.6 units on the longer side at scale 1, whatever the image's pixels *(estimate from the snap points of 8 board-sized tokens, `TtsUnits.CustomTokenSize`; verify with getBounds() in TTS)* |
| `Custom_Tile` | `Tile` | `CustomImage.CustomTile.Type` (0 box, 1 hex, 2 circle, 3 rounded), `Thickness`, `Stackable`, `Stretch`; `Number` → `count`. Size: 2 units at scale 1 (`size` 0.06 m); stretched (default) it takes its image's proportions: depth = 2 × scaleZ, width = 2 × scaleX × aspect, for wide and tall images alike (tall ones checked against Pokémon Sinnoh's panels, which then sit edge to edge) (checked against Riftbound: 10 000 × 6 050 mats at scale 12.62 sit exactly 41.7 units apart). The importer reads the image size from the TTS cache; otherwise the game applies it when the image loads (`keepAspect`). Locked pieces that sink into a bigger locked surface below them are placed on its top (TTS draws coplanar mats; we would hide them) |
| `Die_4/6/8/10/12/20`, `Die_6_Rounded` | `Die` | `faces`; `RotationValues[]` → `labels`: each entry labels the face its rotation turns up (not its list position: `Die_6_Rounded` lists them in another order). Our d6 numbers faces as TTS does (1 +Z, 2 +Y, 3 −X, 4 +X, 5 −Y, 6 −Z), so a save's rotation shows the same value. Labels that are just the face's number are dropped |
| `Notecard` | `Notecard` | `Nickname` → `name` (the title), `Description` → `description` (the text) |
| `Custom_Dice` | `Die` | `CustomImage.CustomDice.Type` (0 d4, 1 d6, 2 d8, 3 d10, 4 d12, 5 d20); `ImageURL` → `asset`. The d6 image is the TTS template: a square, top third unused, then two rows of three cells `2 4 5` / `1 3 6`, the 1 and 6 printed upside down (checked against a local mod's die, 2026-10-23; `builtin:d6_template` is a sample). Templates for other dice are not mapped yet (no local mod uses them): they draw with the builtin atlas layout |
| `BlockSquare`, `BlockRectangle`, `Checker_*`, `Chess_Pawn` | `Model` with `builtin:block_square`, `builtin:block_rectangle`, `builtin:checker` (kind chip), `builtin:pawn`; `ColorDiffuse` → `tint` |
| `Bowl` | `Model` with `builtin:bowl` | a dish about 3.6 units across (estimate, verify in TTS); pieces fall in and stay (base plus a ring of wall colliders); `ColorDiffuse` → tint |
| `Custom_Model` | `Model` | `CustomMesh.MeshURL` (OBJ in TTS units: the mesh asset gets `meshScale` 0.03; loaded by `ObjMesh`, X mirrored into Unity's axes as TTS does), `DiffuseURL`, `NormalURL`, `ColliderURL`, `Convex`, `MaterialIndex` (0 plastic, 1 wood, 2 metal, 3 cardboard, 4 glass), `TypeIndex` (0 generic, 1 figurine, 2 dice, 3 coin, 4 board, 5 chip, 6 bag, 7 infinite bag), `CastShadows` |
| `Custom_Model_Bag`, `Custom_Model_Infinite_Bag` | `Bag` / `InfiniteBag` with a `Model` look | as `Custom_Model` + `ContainedObjects` |
| `Custom_Assetbundle` | `Model` (source = bundle) | `CustomAssetbundle.AssetbundleURL`, `AssetbundleSecondaryURL`, `MaterialIndex`, `TypeIndex`, `LoopingEffectIndex`, `TriggerEffectIndex`; load via ADR 0001 remap or placeholder |
| `Custom_Assetbundle_Bag`, `Custom_Assetbundle_Infinite_Bag` | `Bag` / `InfiniteBag` with a bundle look | as above + `ContainedObjects` |
| `Bag`, `Infinite_Bag` | `Bag` / `InfiniteBag` | `ContainedObjects` |
| `Custom_Board` | `Board` | `CustomImage.ImageURL`, `WidthScale` |
| `Figurine_Custom` | `Figure` (a standee cut along the image's alpha, stand under the cut's lowest point) | `CustomImage.ImageURL` (standee) |
| `Counter` | `Counter` | `Counter.value` → `value` |
| `Digital_Clock` | `Clock` | `Clock.ClockMode` (`Current` → `clock`, `Stopwatch`, `Timer`), `Clock.SecondsPassed` → `seconds`, `Clock.Paused` → `running` (negated) |
| `Checker_*`, `Chess_*`, `BlockSquare`, `BlockRectangle`, `Bowl`, `Domino`, `Go_*` | `Token` / `Model` with built-in presets | map by name to the default asset kit |
| `Notecard`, `Counter`, `Tablet`, `Custom_Text` | `Notecard`, `Counter` (UI), out of v0 for Tablet/Text | keep as placeholder with notes |
| `Custom_PDF` | `Pdf` (asset type `pdf`, file in `pdfs/`) | `CustomPDF.PDFUrl` → asset, `PDFPage` → `page`, `PDFPageOffset` → `pageOffset`; a 1-unit square stretched by the scale, as TTS does; `PDFPassword` is not used (noted). Rendered by PDFium (ADR 0007) |
| `ScriptingTrigger` | `ScriptingZone` | transform + scale is the box; `LuaScript` / `LuaScriptState` → the zone's `script` / `scriptState`, `XmlUI` → its `scriptUi` |
| `HandTrigger` | `HandZone` | `FogColor` = owner color; hidden to others |
| `FogOfWarTrigger` | `HiddenZone` | fog of war proper (revealers) not built yet (P3); no local mod uses one |
| `RandomizeTrigger` | `RandomizeZone` | transform + scale is the box |
| `LayoutZone` | `LayoutZone` | `LayoutZone.Options`: `ManualOnly` → `manualOnly`; `MaxObjectsPerNewGroup` → `maxPerRow` *(verify)*; `Direction`, `NewObjectFacing` → `direction`, `facing` *(verify the TTS numbers in TTS)*; the rest (`MeldDirection`, `MeldSort`, `CombineIntoDecks`, `SplitAddedDecks`, paddings, `StickyCards`, ...) kept in `ttsExtra` |

## Common object fields

| TTS | Ours |
|---|---|
| `GUID` (6 hex) | `id` = the GUID itself (scripts compare `getGUID()` with literal GUIDs, so a prefix would break them); a repeated GUID gets `_2`, `_3`… |
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
| `AttachedSnapPoints[]` | `SnapPoint` zones parented to the object (those of a non-active state are dropped for now: the state is not on the table) |
| `Hands` (bool) | `hideInHand` semantics |
| `Hands` | `hands` (false: never joins a hand) |
| `HideWhenFaceDown` | `hideWhenFaceDown` (omit when it matches the default: true for cards, false otherwise) |
| `Grid` | `grid` (false: ignores grid snapping) |
| `SidewaysCard` | the card's (or deck's) `size` with width and height swapped: a landscape card |
| `Snap`, `Tooltip` | `snap`, `tooltip` (false: ignores snap points / shows no hover text) |
| `Sticky`, `IgnoreFoW`, `MeasureMovement`, `DragSelectable`, `Autoraise` | interaction flags, kept verbatim in `ttsFlags` until we need them |
| `AltLookAngle`, `Number` (except a token or tile stack's count), `Text`, `Bag.Order` | kept in `ttsExtra` |

## Sprite sheets

TTS decks: `NumWidth` × `NumHeight` cells, row-major, index 0 at top-left. Default custom deck layout is 10 × 7 (70 cards). Our image assets carry the same grid (`columns` × `rows`); each TTS `CustomDeck` key becomes one face asset (and one back asset), and cards reference them by `faceAsset` + `faceIndex` (`CardID % 100`). `UniqueBack` means the back URL is also a sheet with per-card backs.

## Assets and URLs

- Cache key in TTS: URL with every non-alphanumeric character removed, plus extension (`.unity3d`, `.png`/`.jpg`, `.obj`, `.pdf`), under `Mods/{Assetbundles,Images,Models,PDF}` (`TtsImporter.CacheKey`). The importer lists each asset with its URL, cache key and package path (`images/`, `models/`, `bundles/` + key + extension) so the package can be filled from the TTS cache first.
- Every imported asset keeps its address (`url`): when the package lacks the file, each machine looks in its own copies (our URL cache, then the player's TTS cache) and else fetches it, as TTS does. Script UI may also name images by address.
- Custom UI images (`CustomUIAssets`) become image assets named `ui_<name in lower case, other characters as _>` (`TtsImporter.UiAssetId`); script UI looks images up by that name too.
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
| Card base size in TTS units → `TtsUnitsToMeters` | **Bounded:** ≤ 1.93 units wide (Pokémon Sinnoh's unlocked decks at scale 1.5 lie 2.894 apart, and TTS saves positions after physics), ≤ 3.2 long. Imported cards use 1.92 × 2.69 units (`TtsUnits.CardSize`, poker proportions); an exact `getBounds()` from TTS would settle it |
| `RotationValues` on dice, `Custom_Dice` value ↔ rotation | d6 settled (G21, above); other dice open → T8.1 |
