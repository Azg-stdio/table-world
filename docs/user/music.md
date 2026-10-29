# Music: your library, a game's soundtrack, and sound cues

Table World plays music and sounds at the table, but it never ships anyone's music with the game. Each player keeps a
**music library** of packs they imported. A game names the tracks it wants. Whoever has the same pack plays their own
copy, and everyone else receives the track from the host while playing.

## Your library

The library is a folder on your computer: `%LOCALAPPDATA%\TableWorld\Music` (paste it into the Explorer address bar).
The game and your agent's MCP server both read it.

**To add a pack:**

1. Get a pack you may use. Many game-music packs are free with a licence that allows use in games; read it first.
2. Open **Music** in the top bar.
3. Either press **Music folder** and drop the zip (or folder) into the `Inbox` folder that opens, then close and reopen
   the Music panel, or paste the zip's path into the field at the bottom, tick **I may use this music** and press
   **Import a pack**.

The import keeps one file per track in its smallest format (OGG before MP3 before WAV), keeps loops as their own tracks,
and copies the pack's licence file next to them. Each track gets tags from its name and folders (ambient, dark, action,
loop, fx...) and its length when the file says it.

Your agent can do the same with `import_music`, after asking you whether the pack's licence lets you use it.

## Listening and picking

In the Music panel, search by words or tags and press **Listen** to hear a track on your computer only; the table's
music ducks under it. Your agent can play one for you with `preview_music`.

The host also sees the table's music: what plays, **◀ Pause ▶**, **Shuffle**, the game's playlists (one click switches
everyone to it), and **+ playlist** on each library track to add it to one of them.

## A game's soundtrack

A game has named playlists (`tavern`, `ritual`...) and the one it starts with. Phases switch between them and mark
moments with sounds:

```jsonc
"music": { "playlists": { "tavern": [ ... ], "ritual": [ ... ] }, "start": "tavern", "shuffle": true, "volume": 0.6 },
// in a phase:
"onEnter": [ { "music": { "playlist": "ritual", "crossfade": 3 } }, { "sound": { "play": "gong" } } ]
```

A playlist already playing is left as it is, so entering the same phase again does not restart the song. One track fades
into the next over `crossfade` seconds. See [the rules format](../rules-schema.md#music) for every field.

**Built-in cues**, made by the game itself, need no file: `gong`, `chime`, `bell`, `drum`, `sting`, `fanfare`, `page`,
`whoosh`, `knock`. A cue can also be a short library track (a pack's stinger) or one of the game's audio files. With
`"to": "awake"` (or a seat, `team:`, `role:`) only those players hear it.

## Playing with friends

- A friend who imported the same pack plays their own file: nothing is downloaded.
- A friend without it receives the tracks from you, after the table's pictures, the first track to play first. Relay
  moves about 200 KB per second to each friend, so a 3 MB track arrives in about 15 seconds; until then their music
  stays silent and the Music panel says the track is arriving.
- Each friend sets their own music volume in Escape → Settings → Sound.

## Why the music is not in the game

Most free music packs let you use their tracks in your games but not hand the files around as they are. Keeping the
music in each player's library, and sending a track only to play it at your table, keeps within those licences. A
one-click download of a pack from inside the game would need the author's permission first.
