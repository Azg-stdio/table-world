# M12 — Atmosphere: rooms, music, sound cues, coloured words, themed HUD

Planned 2026-10-29 for the Avalon playtest that evening (Miguel). Goal: the table feels like a place. A game picks a room, a
soundtrack, a look for its panels, and marks its moments with light and sound. Everything here is a **sandbox option any
game can use** ("more options is a good thing if they make sense for future games too", Miguel 2026-10-29); Avalon is the
first game that uses them, and the example the docs show.

Read first: `CLAUDE.md`, `plan.md` ("For the implementing agent"), `M10-social-games.md` (Avalon), the memory handoff of
2026-10-28, and `docs/dev/recipes.md`. Branch `chalkers-voice`. No player builds until Miguel asks (he will, for the
playtest; remind him that Tom the lizard ships in every build).

## Order and priority

Work top down. **P0** must be in before tonight's playtest, **P1** if there is time, **P2** afterwards. Each task is one
commit (`T13.x: ...`), tested as the done check says, and updates its docs page in the same commit. The PlayMode suite is
run through `python -I tools/unity/playmode_tests.py` (one `--filter` name per run; never recompile in Play mode).

| Id | Size | Depends on | Task | Done check | Priority | Status |
|---|---|---|---|---|---|---|
| T13.1 | S | — | CC0 art brought in with its licences (parchment, retro textures, UI sheets); no music in the repo or the build | Folders under `Art/ThirdParty/` with a `LICENSE.txt` each; nothing from `free-assets/` committed whole; sizes in the table below | P0 | todo |
| T13.2 | S | — | Light changes fade, and Avalon's ritual darkens the room | `light` effects in `build_avalon.py`; EnvironmentView lerps lights over ~1.5 s (PlayMode test); screenshot of the dark ritual | P0 | todo |
| T13.3 | M | — | Music: the local music library (packs imported from zips or folders), playlists in the game file, autoplay, crossfade, a Music panel to listen and pick, MCP tools to browse and use it | core tests on `MusicLibrary`, `MusicDef`, `MusicEffect`; PlayMode test: a `library:` track resolves to the local file and plays; a joiner without the pack receives the track from the host, a joiner with it plays its own copy; Avalon starts its tavern playlist, switches to the ritual one and back; panel and MCP tools work | P0 | todo |
| T13.4 | M | T13.1 | Sound cues: the `sound` effect, built-in cue bank, Avalon's cues | core test: `sound` → `PlaySoundCommand` with audience; PlayMode: `TableSounds.Played["cue:gong"]` increments on the command; every Avalon moment below has a cue | P0 | todo |
| T13.5 | M | — | The tavern room preset (candles, fireplace, stone, beams) | `room: "tavern"` builds it (PlayMode test counts its lights and parts, all on the room layer except as noted); Avalon uses it; screenshots from the seat and from above; frame time within 1 ms of game night at Medium | P0 | todo |
| T13.6 | M | — | Coloured words: seats, teams, vote options and roles in colour in the log, role line, banner and chat relay | core: `RichText` tags on every seat/team/option name, `RichText.Strip` used by MCP and tests; HUD renders colours (PlayMode test reads a `<color` in the label text); chat typed by players stays plain | P0 | todo |
| T13.7 | M | T13.1 | HUD theme per game: `parchment` (medieval) beside `default` | `environment.theme` toggles a root USS class; parchment panels, banner and buttons use the 9-sliced frames; Cinzel headings; Spanish glyphs render; screenshots of both themes | P1 | todo |
| T13.8 | S | — | Declutter: zone outlines only when useful; quest results in the game panel | Rules games hide zone outlines unless hovering, dragging or the host toggles "Show zones"; Avalon's quest row shows in colour in the game panel | P1 | todo |
| T13.9 | S | — | `set_environment` MCP tool (room, theme, lighting, table, music) | contract test; `build_avalon.py` uses it; `docs/reference/mcp-tools.md` regenerated | P1 | todo |
| T13.10 | — | — | Release script for itch.io | — | — | dropped (Miguel 2026-10-29: work only in the game for now) |
| T13.11 | ⚗ | T13.2–T13.6 | Avalon played through in the Editor with everything on, then over Relay with friends | Hot-seat playthrough with a driver script, console clean; playtest notes in this file | P0 | todo |

Done checks are played like a player: load Avalon in the Editor, sit at a seat, run the ritual, watch and listen.

## What is in `free-assets/` (root, git-ignored, 3.1 GB)

| File | Contents | Licence | Use |
|---|---|---|---|
| `Fantasy RPG Music Pack Vol.3.zip` | 10 Ambient (4–5 min, mp3/ogg) + 10 Ambient **loops**, 5 Action + loops, 5 short Ambience, 5 short Dark (~1 min), 3 Fx (stingers, 0.1 MB) | AlkaKrab (PDF inside): royalty-free, commercial use, credit appreciated, **no redistribution of files as-is**, contact the author before open-sourcing | Imported into the music library (T13.3), never committed or built in. **Tavern playlist** (Ambient loops), **ritual playlist** (Dark), stingers (Fx) |
| `10 Ambient RPG Tracks.zip` | 10 Ambient (ogg 3–5 MB) | AlkaKrab | Second tavern playlist, or a `library` room later |
| `Free JRPG Music Pack.zip` | 12 tracks + 10 loops | AlkaKrab | Not now (brighter JRPG tone); keep for a "bright" playlist for other games |
| `8 Piano Tracks vol.2.zip`, `Pixel RPG Music Pack.zip`, `Sci-Fi Music Pack.zip` | Piano, chiptune, sci-fi | AlkaKrab | Not now; candidates for future rooms (sci-fi room, lounge) |
| `Grimoire0.png` | 3000×3000 parchment with a thin inner frame | **CC0** (Miguel, 2026-10-29) | Parchment theme: panel and banner background (downscaled to 1024, 9-sliced) |
| `AquilariusRetroTextures.zip` | 60 tileable 128×128 textures (stone, brick, metal, wood planks) + 3 preview sheets | **CC0** (Miguel, 2026-10-29) | Tavern walls and floor: pick a stone brick (t-files, check `pack1.png` for the overview), a wood plank for the floor, point filtering off (bilinear at 128 px looks soft, which suits a dim room) |
| `MegaPackFree.zip` | Pixel-art UI sheets 256 px: `MediavelFree.png` (wooden framed panel with chains, round wooden buttons, candles, daggers, a red bar), `freefantasy.png` (hanging wooden boards, arrows, candles, PLAY/LOAD/EXIT), `UiCozyFree.png`, `FreeHorrorUi.png`, environment sprites, animals, cats | **CC0** (Miguel, 2026-10-29) | Parchment/medieval theme: the framed wooden panel as a 9-slice frame for the banner and the game panel; the candle sprite beside the phase timer. The rest is not for us |

Licence rule (CLAUDE.md §7): nothing goes under `Art/ThirdParty/` without a `LICENSE.txt` beside it naming the author
and terms. The three art packs are CC0 by Miguel's word; their `LICENSE.txt` says "CC0 1.0, per the project owner,
2026-10-29" and the pack's name, and copy only the files used (not the cats, animals or cactus sheets). Never copy the
AlkaKrab PDF's email into docs; copy the terms.

**The music never enters the repo or the build** (Miguel, 2026-10-29: "a music library we already have in the game but
doesn't ship with the game per se"). It lives in each player's local music library (T13.3). Hosting the AlkaKrab files
for download ourselves would be "redistribution of the track as-is", which their licence forbids without the author's
permission; so each person gets the packs from the author's page (free) and imports the zip. If Miguel later wants a
one-click download from inside the game, he writes to AlkaKrab for permission first, then the same library gets a
"Download" button.

## Designs

### T13.1 Assets

- No music here: it goes through the library import (T13.3). On Miguel's machine, the agent imports his
  `free-assets/` zips with that import, as a player would.
- Cue clips for T13.4: Kenney's packs are already CC0 in the project (`KenneyCasinoAudio`, `KenneyImpactSounds`); for a
  gong, a chime, a drum hit and a bell, download Kenney "Interface Sounds" or "UI Audio" (CC0, kenney.nl) into
  `Art/ThirdParty/KenneyInterfaceSounds/` with its licence (the AlkaKrab Fx stingers can be used by a game from the
  library, but not built in). If nothing fits, make the gong and chime procedurally (an `AudioClip` built from sine
  partials with a decay) in `Builtin/Cues.cs`; that is acceptable for tonight and is noted as such.
- `Grimoire0.png`, textures and UI sheets (CC0). Downscale the parchment to 1024², keep the sheets at 1:1
  with point filtering, slice them with the Sprite Editor from an editor script (never hand-edit `.meta`): the framed
  wooden panel in `MediavelFree.png` is the top-left 64×64 at (0,0); candles are 8×16 each in the top-right.

### T13.2 Light fades and the ritual

- `EnvironmentView.ApplyLighting` currently sets intensities and colours at once. Keep a target per light and lerp
  intensity and colour towards it in `Update` with `FadeSeconds = 1.5` (a game can pass `"seconds"` on the effect:
  add `LightEffect.Seconds`, default 1.5, carried in `SetLightCommand`). Tests: a PlayMode test applies a scale of 0 and
  reads the lamp's intensity after 0.2 s (between) and after 2 s (at target).
- Avalon (`tools/mcp/build_avalon.py`): `ritual_close.onEnter` → `{"light": {"part": "room", "scale": 0.08, "tint": [0.6, 0.7, 1.0]}}`
  and `{"light": {"part": "table", "scale": 0.35, "tint": [0.7, 0.8, 1.0]}}`; `ritual_open.onEnter` → both parts back
  to normal (effects with no scale and tint). The awake see a cold, dark room with faces lit by the pieces' light; the
  sleepers see black anyway. Also dim the room during `assassinate` (scale 0.3, red tint [1, 0.6, 0.6]) and restore in
  the end phase.
- Rebuild `games/avalon.json` with the build script (`dotnet build mcp-server -c Debug` first); the Release server DLL is
  often locked (memory: tooling quirks).

### T13.3 Music

**The music library.** A folder per player, `%LOCALAPPDATA%/TableWorld/Music/` (`Application.persistentDataPath/Music`,
beside `Games`), outside the repo and the build. One subfolder per pack with a `pack.json`: pack name, author, licence
text, source page, and per track `id`, title, file, length, SHA-256 hash, and tags guessed from the file name
(ambient, dark, action, loop, fx). Core: `Core.Music.MusicLibrary` reads and writes `pack.json` and resolves
`library:<pack>/<track>` (pure C#, xUnit tests with a temp folder).

- **Import.** Music panel → **Import a pack**: a zip or a folder. It takes OGG files only when both OGG and MP3/WAV are
  present (the smallest), else MP3, else WAV; keeps `Loops/` as their own tracks tagged `loop`; copies any licence
  PDF/TXT into the pack folder and asks the importer to tick "I have the right to use this music". An MCP tool
  `import_music(path)` does the same for an agent. Headless importer in core so it is tested without Unity.
- **Listen.** The panel lists packs and tracks with a search box and tag chips; a play button previews a track **on this
  machine only** (a separate preview `AudioSource`, ducking the table music), then **Add to playlist** puts it into one of
  the game's playlists (host, or the game's author while building).
- **Agent.** MCP `list_music(tag?, search?)` returns packs and tracks with lengths and tags (so Claude can say "Dark 3 is
  a minute of low drums, good for the ritual"); `add_music(playlist, tracks[])` writes them into the game's `music`
  block; `play_music(track)` previews it on the host's machine through the bridge so the designer hears it while
  building.
- **Playing with friends.** A game references `library:` tracks with their hash. When the table loads, each client
  looks the hash up in its own library: found, it plays its copy; missing, the host serves the file through the existing
  asset chunks (the same path images take, `Host.Assets`) and the client keeps it in its cache, not in its library (it
  did not import the pack). Order: the current track first, the next one in the background, never the whole playlist at
  once: Relay gives about 200 KB/s per joiner, so a 3 MB OGG takes about 15 s. Until a track arrives the client stays
  silent and the panel shows "music arriving".
- **Missing on the host.** A game opened on a machine without its packs lists the missing tracks in the Music panel with
  the pack's source page; the game plays without them (no error).
- **Get packs.** The panel's empty state and `docs/user/music.md` (new page) link the authors' pages (AlkaKrab's itch
  page for these packs) and explain the import. No download button until a pack's licence allows us to host it.

**Game file.** New top-level `"music"` block (schema + `RulesValidator`):

```json
"music": {
  "playlists": {
    "tavern": ["library:alkakrab-fantasy-rpg-vol3/ambient-1-loop", "library:alkakrab-fantasy-rpg-vol3/ambient-2-loop"],
    "ritual": ["library:alkakrab-fantasy-rpg-vol3/dark-1", "library:alkakrab-fantasy-rpg-vol3/dark-2"]
  },
  "tracks": { "library:alkakrab-fantasy-rpg-vol3/dark-1": { "hash": "sha256:...", "title": "Dark 1" } },
  "start": "tavern",          // playlist started with the game (StartGameCommand) and on load if the table has no music yet
  "shuffle": true,
  "volume": 0.7               // the game's own level, under the player's Settings → Sound → Music
}
```

A playlist entry is a URL, an asset id (an audio asset added with `add_asset`, for music the designer owns and may
share with the game) or a `library:<pack>/<track>` track. `MusicTrack` gets `Title` from the library, else the file name.

**Effect.** `{"music": {"playlist": "ritual"}}`, `{"music": {"track": "library:..."}}`, `{"music": {"pause": true}}`,
`{"music": {"resume": true}}`, `{"music": {"volume": 0.4}}`, optional `"crossfade": 2` (seconds, default 2). It becomes a
`SetMusicCommand` (exists, host only) with the playlist loaded and `Playing = true`. Add `MusicState.Playlists`
(name → tracks) and `MusicState.PlaylistName`, `MusicState.Volume`, `MusicState.Crossfade`, so a client that joins late
plays the same list. Lua: `MusicPlayer.setPlaylist` already exists; add `MusicPlayer.play("ritual")` taking a playlist name.

**Unity.** `MusicPlayerView`: resolve `library:` through `MusicLibrary` (by hash) or the asset cache to a `file://`
path (the existing `UnityWebRequestMultimedia` path streams it; keep `Formats` fallback), resolve asset ids through
`AssetLibrary` to their local file. Two `AudioSource`s for a crossfade (fade the old one out over `Crossfade` seconds
while the new one fades in); loop a single-track playlist seamlessly (`AudioSource.loop` when the playlist has one track
or `repeat` is on). Volume = settings music volume × game volume. Show a small "♪ title" toast at the bottom left for 4 s
on track change (`HudController` status line, same fade as "...: done").

**Music panel.** A `Music` button in the top bar. For the host: now playing, play/pause, previous/next, a dropdown of
the game's playlists, shuffle and repeat toggles, and the library browser above (listen, add to playlist, import).
For everyone else: now playing, their own volume, and the library browser to import and preview their own packs (so
their machine never needs the host's copy). Built with UI Toolkit in `HudBuilder`
(load the `unity:ui-uitk` skill first), styles in `Hud.uss`.

**Avalon.** `start: "tavern"` (the ten Fantasy Ambient loops, shuffled); `ritual_close.onEnter` → playlist `ritual`
(Dark tracks, crossfade 3); `ritual_open.onEnter` → `tavern` again; `assassinate.onEnter` → `ritual`; the end phase →
`tavern`. Since nobody here can listen, keep the order by name and tell Miguel to reorder in the panel; whatever he
picks, write back into the build script.

**Docs.** New `docs/user/music.md` (the library: get packs, import, listen, use in a game, what friends download),
`docs/rules-schema.md` (Music section), `docs/user/rules-and-scripting.md`, `docs/user/mcp-tools-guide.md`,
`docs/reference/` regenerated (`TW_UPDATE_SCHEMA=1`, `TW_UPDATE_DOCS=1`), `docs/user/playtest.md` §5 (lines: everyone
hears the same track; the ritual changes it; a friend without the pack hears it after a few seconds).

**Tonight's shortcut.** If the full library does not fit before the playtest, land in this order: `MusicLibrary` +
import (agent imports Miguel's zips) → `library:` resolution and host-to-client serving → `music` block and effect →
the panel. The panel's browser can follow after the playtest; the Avalon playlists then come from `build_avalon.py`.

### T13.4 Sound cues

**Effect.** `{"sound": "builtin:gong", "volume": 0.8, "to": "awake"}` → `PlaySoundCommand { Sound, Volume, Audience }`
(host-run, logged like a message, not undoable). `to` takes the same audiences as `message` (a seat, `team:`, `role:`,
`awake`, `all`; default `all`). The sleepers' "no sound of the awake" rule applies only when `to` is `awake`.
Lua: `playSound(name, volume)` on the table API (not a TTS function; documented as ours).

**Cue bank.** `builtin:gong`, `builtin:chime`, `builtin:drum`, `builtin:bell`, `builtin:sting`, `builtin:fanfare`,
`builtin:page` (a page turn), `builtin:whoosh`; a `library:` track (an Fx stinger) or a game's audio asset id plays its file (loaded once through
`AssetLibrary`, cached). In `TableSounds`, a `Cue(string name, float volume)` plays 2D (not positional), counted in
`Played["cue:" + name]` for tests. Clips assigned by `TableSceneBuilder` like the other banks.

**Avalon cues.** Eyes close → gong; minions open → low drum; eyes open → chime; last vote card laid (they turn over
together) → drum; each quest card turned → drum, a sting if it is a Fail; three successes or three fails → fanfare or
sting; assassinate phase enters → sting; Merlin named → bell; roles revealed at the end → page. Add them where the Lua or
effects already do the flips (the Lua scripts in `build_avalon.py` issue commands through the host route).

### T13.5 The tavern room

`RoomPreset.Tavern` (schema: `"room": "tavern"`). Built in `EnvironmentView.BuildTavern` from primitives and, when
cleared, the retro textures; everything on `RoomLayers` so the pieces keep their colours, except as noted.

- **Walls**: stone brick texture tiled every 1 m (or a stone colour `[0.22, 0.2, 0.18]` until cleared), a dark wooden
  wainscot 0.9 m high; **ceiling**: dark planks with three beams across; **floor**: wide planks (texture or
  `[0.2, 0.13, 0.08]`), the rug stays, darker.
- **Chandelier** replacing the pendant: an iron ring (torus from a thin cylinder ring of boxes or a `MeshFactory` ring)
  0.9 m across at `LampHeight`, hung by three chains (thin cylinders), 8 candles on it (short cylinders, cream) each with a
  **flame**: a camera-facing quad with a procedurally drawn soft teardrop (additive, orange→yellow, generated in code as a
  64×64 texture, no asset). One `Light` at the ring's centre (warm `[1, 0.72, 0.45]`, intensity as the lamp, soft shadows
  like the lamp) does the lighting; the flames are only sprites.
- **Wall sconces**: two per long wall, one per short wall: an iron bracket, a candle, a flame, and a point light without
  shadows, range 3 m, intensity 0.6, room layer.
- **Fireplace** on the short wall away from the host's seat: a stone surround (boxes), a dark opening, an emissive quad
  with an orange gradient, three logs (cylinders), and a point light without shadows, orange, intensity 1.5, range 5,
  room layer. Optional glow particles are out of scope.
- **Flicker**: `CandleFlicker` component on every flame light and quad: `intensity = base × (1 + 0.12 × noise)` with
  `Mathf.PerlinNoise(t × 6 + seed, seed)` recentred, and the flame quad scale wobbling with the same noise. The fireplace
  flickers slower and deeper (×3, 0.2). Nothing allocates per frame.
- **Table**: felt `[0.3, 0.08, 0.08]` (dark red cloth) and wood `[0.25, 0.15, 0.08]` as the preset's defaults; a game
  overrides as today. Chairs keep their look.
- **Light defaults**: ambient 0.22 slightly warm `[0.6, 0.55, 0.5]`; table light intensity 2.4 colour `[1, 0.96, 0.9]`
  (a touch warm on purpose, within the "true colours" rule since a tavern game chose it).
- **Performance**: Forward+ renderer (PC) handles the 7 extra lights; only the chandelier casts shadows. Measure with
  `tools/unity-probes/FrameReport.cs` at Medium against game night and record the numbers here. `GraphicsPresets` Low
  turns sconce lights off (keeps flames) if the cost is above 1 ms.
- **Karaoke** keeps its own build; the tavern shares `BuildGameNight`'s helpers (`Box`, `Quad`, `SetLayers`).
- Avalon sets `"room": "tavern"` (and `wink-murder`, `werewolf` can too, Miguel's call; leave them).

### T13.6 Coloured words

**Core.** New `Core.Text.RichText` with two tags only, `<color=#rrggbb>…</color>` and `<b>…</b>`, and `Strip(string)`.
`Names.Of(state, seat)` wraps the name in the seat's colour (hex from `SeatLayout` palette, which already lives in core);
`Names.Team` (new) wraps team names in `TeamDef.Color` (new optional field, hex or a palette name; default: no colour);
`Names.Role` (new) wraps a role in its team's colour; vote option labels (`VoteDef.Labels`) get an optional
`VoteDef.Colors` map (option → colour); a `castBallot` option whose card has an `accent` uses that colour by default.
Every engine message built through `Names.Message` then carries colours wherever a seat, team, role or option is filled.
The role line ("You are Merlin... Red and Blue are Minions of Mordred") uses the same names. Plain text everywhere it
must stay plain: `Session.Log` keeps the rich string; MCP tools, `get_table_state`, the chat relay and the tests call
`RichText.Strip` (add a test that no MCP reply contains `<color`). Markdown docs show the tags.

**HUD.** `_gameLog`, `_gameRole`, the phase banner, the turn line, vote rows and the Log tab enable rich text for
engine lines after `RichText.Sanitize` (drops any tag but the two). Chat typed by players stays `enableRichText = false`.
Seat chips and the players panel already colour names from code; unchanged.

**Avalon.** `set_roles`: teams `good` colour `#4a8cff`, `evil` colour `#d8322a`; votes: approve `#3fae5a`, reject
`#d8322a`; quest cards: success `#3f7fd8`, fail `#1a1a1a` on the card and `#d8322a` in the log (dark is unreadable on the
panel; use the `Colors` map for the log). Example log line: "**Red** picked **Reject**" with Red in red and Reject in red.

### T13.7 HUD theme `parchment`

- `EnvironmentDef.Theme` (`"default"` | `"parchment"`; schema and validator). `HudController` sets `theme-parchment`
  on the root element when the table loads (and removes it on another table). All theme rules live in `Hud.uss` under
  `.theme-parchment ...` selectors; nothing inline.
- Parchment: panels (`.game-panel`, `.selection-panel`, `.phase-banner`, the chat and log tabs, the escape menu) get the
  parchment background (9-sliced `Grimoire0` at 1024, or until cleared a flat `rgb(222, 206, 166)` with a darker
  border), ink text `rgb(46, 34, 22)`, gold titles `rgb(140, 96, 30)`, buttons as the round wooden buttons or a wooden
  brown `rgb(96, 62, 34)` with cream text; the phase banner framed by the wooden panel 9-slice with chains (when
  cleared); the phase timer gets the pixel candle sprite beside it.
- Headings font **Cinzel** (SIL OFL, Google Fonts): download the TTF into `Art/ThirdParty/Cinzel/` with `OFL.txt`,
  create its SDF font asset from `HudBuilder` (same path as Inter: dynamic atlas, `HudBuilder.Characters()`, Spanish
  accents and ¡¿), use it for `.phase-banner`, `.game-role`, panel titles only; body text stays Inter. Memory rule:
  never revert the SDF assets under the running Editor.
- Pixel sprites: 9-slice through a sprite asset created by an editor script with `SpriteEditor`-equivalent borders
  (`TextureImporter.spriteBorder`), scaled 3× in USS (`-unity-slice-scale: 3`), point filtering.
- Done check includes a Spanish screenshot of the banner (the interface follows the system language here).

### T13.8 Declutter

- `ZoneViewFactory`: in a table with rules, zone outlines (layout zones, scripting zones, snap points) render only
  while the local player drags an object, hovers the zone, or the host's table menu has **Show zones** on (a new row
  beside Physics and Grid; free-play tables keep today's behaviour). Hand zones and hidden zones keep their outline.
- Avalon's quest track: the physical cards stay as they are (a real table reads sideways too). The game panel shows the
  quest row in colour: "Quests: ✓ ✓ ✗ · ·" built from the existing score panel data with the T13.6 colours.

### T13.9 `set_environment` MCP tool

`set_environment(room?, theme?, wallColor?, floorColor?, lighting?, table?, music?)`: merges into the game's
`environment` and `music` blocks, validates, replies with the result. Documented in `docs/reference/mcp-tools.md`
(regenerate) and `docs/user/mcp-tools-guide.md`. `build_avalon.py` uses it for the tavern, the theme and the playlists.

### T13.10 Release script (itch.io)

Dropped by Miguel on 2026-10-29. Builds go out as before (`docs/user/playtest.md` §1).

### T13.11 Playthrough

Drive Avalon in the Editor as the handoff memory describes (Start.cs, Step.cs in the scratchpad; join all seats by
reflection), with the tavern, music, cues, colours and theme on: watch the ritual from a seat (`TableCamera.TurnHead`),
screenshot each moment, keep the console clean, then run the full PlayMode suite (one filter at a time if the whole run
hangs). Record frame times (tavern vs game night) and anything left for Miguel to judge by ear.

## Simulation record

| Date | What | Numbers |
|---|---|---|
| | | |

## Playtest notes (2026-10-29 evening, Miguel and friends)

(filled after the session)
