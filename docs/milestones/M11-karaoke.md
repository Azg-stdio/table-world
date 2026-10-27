# M11 — Karaoke

A karaoke room: friends on sofas in front of a big screen, one singer at a time in time for everyone, the others
cheering. Design: [ADR 0013](../adr/0013-karaoke.md) (proposed; the song source and duets are Miguel's call).

**Done when:** four friends over Relay take turns singing a song, and each listener hears the singer in time with the
music.

Sizes: S ≤ 1 day, M 2–4 days, L 1–2 weeks. Status values as in the [overview](README.md).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T12.1 ⚗ | Timing simulation: one singer, a duet, singers hearing each other, on a simulated internet | S | — | Numbers recorded here and in ADR 0013 | done 2026-10-27: `SingAlongSimulationTests` (500 rooms of 6): one singer p50 8 ms, p95 26 ms, max 42 ms; a duet's second voice p50 22 ms, p95 63 ms; singers hear each other 148–205 ms late. `core/Net/SingAlong.cs` (clock offset by fastest pings, song delay by median frame) |
| T12.2 **human** | Decisions: the song source (local files, YouTube through a web-view plugin) and whether duets are offered as loose | S | T12.1 | Recorded in ADR 0013 | done 2026-10-27 (ADR 0013 §5): YouTube through a free open-source embedded Chromium; free exploration in first person, no table; a song queue; whoever stands near the screen sings (two on the stage sing together, out of time) |
| T12.3 | Voice frames carry a send time (frame counter); every machine keeps a clock offset with the host by ping | M | T12.1 | Core tests: offset within the simulation's error; frames from a fake microphone measured over the PlayMode session | in progress (2026-10-27): voice frames are version 2 with a send time on the host's clock (4 bytes; version 1 still decodes); `HostClock` (pings every 2 s, the 20 latest kept, the fastest 5 used); every machine keeps each speaker's median voice delay (`NetworkTableSession.VoiceDelayOf`). Core tests: the codec both versions and the wrap, the offset within 10 ms over a lopsided path. Left: measuring it between two machines (a build; Miguel decides when) |
| T12.4 | A screen object: a shared video player (play, pause, seek, position at a host time) with a per-listener delay; local video files first | L | T12.2 | PlayMode: two clients and the host show the same frame within the delay they were given | todo |
| T12.5 | Sing mode: the featured singer, the listeners' song delay from the singer's voice, "quiet while singing", the queue as a list of seats | M | T12.3, T12.4 | PlayMode with a fake microphone: the listener's song delay follows the measured voice delay | todo |
| T12.6 | The karaoke room: an environment preset with sofas and a big screen, chalkers seated on the sofas, a stage light on the singer, a microphone in the singer's hand | M | T11.10 | Screenshots; PlayMode: seats are on the sofas | done 2026-10-27: room `karaoke` (`RoomPreset.Karaoke`, also `new_game room:`): the game night room in party colours with a 16:9 screen on the far wall (`EnvironmentView.Screen`, dark until T12.4 plays on it), speakers, magenta and blue accent lights and a stage light (`AimStage`); `ChalkerSofa` along every side someone sits on, the seats of a side next to each other in their usual order; `ChalkersView.Singer` puts a microphone in that chalker's right hand (aimed at the mouth) and the stage light on them. `KaraokeRoomTests` (4), screenshots from `tools/unity-probes/KaraokeRoomShot.cs`. Sample game `games/karaoke-night.json` (built by `tools/mcp/build_karaoke.py`, in Games): until sing mode (T12.5), whoever's turn it is in a game in the karaoke room sings, and "Pass the microphone" moves it on |
| T12.7 | YouTube through a web-view plugin, if T12.2 chooses it: the plugin checked on Unity 6.6 + URP, its player driven like the screen object | L | T12.2, T12.4 | A YouTube video plays in time on two machines | todo |
| T12.8 | Player docs: hosting a karaoke night, adding songs, why duets drift | S | T12.5 | `check_docs.py` clean | todo |

**Rework after Miguel tried the first room (2026-10-27, ADR 0013 §5).** T12.6's room stays (sofas, screen, lights) but
loses the table; T12.4, T12.5 and T12.7 are replaced by the tasks below.

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T12.9 ⚗ | Web view on Unity 6.6: UnityWebBrowser (CEF) installed, a YouTube embed playing on a texture, driven from C# (load, play, pause, seek, current time), its sound, its cost per frame | S | T12.2 | Numbers and a screenshot recorded here; go or no-go for the candidate | todo |
| T12.10 | First-person walking: a character controller (WASD, mouse look, collisions with the room and sofas), sit on any sofa place and stand up, a walking chalker for others (presence carries position, facing, sitting); rooms without a table | L | T11.10 | PlayMode: walk, collide, sit, stand; two clients see each other walk (simulation) | todo |
| T12.11 | The YouTube screen: the big screen shows the embedded player; play, pause and seek follow the host's clock, each listener delayed by the singer's voice delay | M | T12.9, T12.3 | PlayMode: the player's time follows the host time minus the delay | todo |
| T12.12 | The song queue: anyone adds a song (a YouTube link, or one found by searching in the embedded browser), sees the queue, removes their own; the host can reorder and skip; the next song starts when the stage is free | M | T12.11 | Core tests for the queue; PlayMode: add, play next | todo |
| T12.13 | The stage: a zone in front of the screen; standing in it makes you the singer (microphone, stage light, listeners delayed by your voice), leaving it ends it | M | T12.10, T12.11 | PlayMode: walking in and out changes the singer | todo |
| T12.14 | Karaoke night as a game: the room without a table, walking, the queue panel, the screen; replaces the turn-based sample | S | T12.10–T12.13 | Played with friends over Relay | todo |
