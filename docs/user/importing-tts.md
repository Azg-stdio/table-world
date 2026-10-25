# Importing Tabletop Simulator games

> Status: the importer works with the Tabletop Simulator games on your computer. Some features are still missing;
> section 5 lists them.

Table World can open the games you have in Tabletop Simulator (TTS): Workshop games you subscribed to and tables you
saved. It reads them from your own computer, turns them into a Table World game, and keeps everything on your
computer. "Tabletop Simulator" is named here only to describe file compatibility; Table World is not affiliated with
it.

## 1. Import a game

1. Install Tabletop Simulator and open the game there at least once. This puts the game's images and models in TTS's
   cache, where Table World copies them from.
2. In Table World, open **Games**. Under **Import from Tabletop Simulator** you see every game TTS has on this
   computer, with where it comes from: **Workshop** (the game as published), **your save**, or **TTS autosave**, with
   the date. A save keeps the game as you left it (pages turned, pieces moved); each imports into its own game.
3. Click a game. Importing a big game takes a few seconds. When it is done, the status line says how many objects and
   files it brought in, and the game appears in the list of games.
4. If some files were not in the TTS cache, **Download its N missing files** appears under the game in the list. Click
   it to fetch them (see section 3). It stays there until every file has arrived, so you can do it later too.
5. Open the game from **Games**, like any other game. Re-importing the same game updates it. When Table World learns
   to import something better, games imported before show **Re-import it (made by an older importer)**.

**Where Table World looks:** `Documents/My Games/Tabletop Simulator`:

- `Mods/Workshop`: Workshop games you subscribed to;
- `Saves`: your own saves.

The images and models come from the cache folders next to them (`Mods/Images`, `Mods/Models`, `Mods/Assetbundles`).
TTS installed somewhere else is not found yet.

**Where the result goes:** each import becomes a folder in your Table World games folder (on Windows,
`%USERPROFILE%/AppData/LocalLow/DefaultCompany/Table World/Games/<game name>`). Each folder holds:

- `table.json`: the game;
- the files copied from the TTS cache;
- `import-notes.txt`.

## 2. What imports

| In TTS | In Table World |
|---|---|
| Cards and decks, with their sprite sheets, backs and sideways cards | Cards and decks, the same order (the top card stays on top) |
| Custom tokens, tiles, boards and figurines | Tokens, tiles, boards and figures, the same size and shape |
| Dice (standard and custom) | Dice, with the same face values |
| Custom models (OBJ) and AssetBundles | Models; AssetBundle materials are converted for Table World's renderer |
| Bags and infinite bags, with everything inside them | Bags and infinite bags |
| Blocks, checkers, chess pawns, counters, clocks and notecards | Built-in pieces of the same kind |
| Objects with several states | Objects with states (switch them the same way) |
| Hands, scripting zones, hidden zones, randomize zones and layout zones | The matching zones |
| Snap points, on the table and on objects | Snap points |
| Lua scripts, global and per object or zone, with their saved state | Lua scripts, run by Table World's script host |
| XmlUI, global and on objects | Script UI, drawn the way TTS draws it |
| Notebook, music player, grid, turns, vector drawings, the table and its image | The same features |

Ids stay the TTS GUIDs, so scripts that look objects up by GUID find them.

AssetBundle figures play their animations (idle, attacks, fainting) when the game's scripts ask for them. Some games
keep their 3D figures switched off until you turn them on, as they do in TTS: in Pokémon Master Trainer, click
**3D Model Options** on the options table at the right edge and tick **Enable Models**; a Pokémon chip taken out of
its bag and laid face up then gets its figure, and turning the chip face down puts the figure away.

## 3. Missing files

A game names its files by web address. When TTS has not cached a file, the game opens with a placeholder where that
file goes, and `import-notes.txt` lists it as `Not in the cache yet: <address>`.

**Download the missing files** fetches exactly the addresses the game names:

- 4 at a time, at most 300 MB per file;
- a download you stop resumes where it stopped the next time.

Files that cannot be fetched are listed in the log; the game still opens without them. Most files are on Steam's
content servers. Table World never downloads from the Workshop itself, and never downloads a game you do not have.

Files a game's scripts load while it plays (an image set by a script, for example) are fetched when they are needed.

## 4. Reading `import-notes.txt`

The notes say what the import did and what it could not do. An example, shortened:

```text
Imported from 2918471932.json on 2026-10-24 21:14.
5346 objects; 1180 of 1206 files found in the Tabletop Simulator cache.
Not in the cache yet: https://steamusercontent-a.akamaihd.net/ugc/…/
Tablet 4a1b2c ('Rules site') has no counterpart yet: kept as a placeholder.
```

| Line | What it means | What to do |
|---|---|---|
| `N objects; X of Y files found` | How much came in. Every object is in the game, even when its file is not | Nothing; or download the missing files |
| `Not in the cache yet: <address>` | A file TTS had not cached. A placeholder stands in for it | Download the missing files, or open the game in TTS once and import again |
| `… has no counterpart yet: kept as a placeholder` | An object Table World cannot show yet (a tablet, a text object) | See below |
| `… was inside a container; zones cannot be, so it lies on the table` | A zone was stored inside a bag in TTS | Nothing: it works on the table |
| `The XmlUI of … does not read (…); left out` | A UI document TTS accepted but that is not valid XML | Usually harmless; tell us which game |

**Why placeholders:** nothing is ever dropped. A placeholder is a plain block that keeps the object's id, name,
description, GM notes, what it was inside, its position and its script. Scripts that use it keep working, and it can
become the real object once Table World supports it.

## 5. Known limits

| Feature | Today |
|---|---|
| Tablets, text objects | Placeholders |
| Lua | More than 99.9 % of the calls in the tested games work. Missing: typing into text fields drawn on objects and scripted forces and velocities |
| Chat for one colour (`broadcastToColor`) | Reaches everyone |
| Fog of war that reveals around pieces | A hidden zone only |
| Physics | Table World's own: pieces behave like TTS's but not identically |
| Table size | Table World's table is fitted to where the pieces lie, so it is often smaller than TTS's |
| TTS installed outside `Documents/My Games` | Not found yet |

How much of each tested game's Lua works is in [the Lua coverage report](../data/lua-coverage-2026-10-25.md).

## 6. Privacy

- The import runs on your computer, over files you already have. Nothing is uploaded.
- The imported game is private: it is in your own games folder and appears nowhere else.
- When you host a session, the game's files are sent to the friends who join it, as with any game you host. They are
  not published anywhere.
- Table World never copies from or republishes the Workshop. Sharing an imported game is up to you and the game's
  rights holders.

## 7. Glossary

| TTS | Table World |
|---|---|
| Mod, save | Game (a folder with `table.json` and its files) |
| GUID | Object id |
| Custom Deck, `CustomDeck` sheet | Deck; the sheet is an image asset with columns × rows |
| Custom Token / Tile / Board / Figurine | Token / Tile / Board / Figure |
| Custom Model, AssetBundle | Model |
| Bag, Infinite Bag | Bag, InfiniteBag |
| Hand zone | HandZone |
| Scripting zone (ScriptingTrigger) | ScriptingZone |
| Hidden zone, fog of war | HiddenZone |
| Randomize zone, layout zone | RandomizeZone, LayoutZone |
| Snap point | SnapPoint |
| Global script, object script | `scripts.global`, an object's `script` |
| `script_state` (`onSave`) | Saved script state |
| XmlUI | Script UI |
| Player colour | Seat colour |
| TTS units | Table World metres: 1 TTS unit is about 3 cm |
