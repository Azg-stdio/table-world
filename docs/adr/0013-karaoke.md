# ADR 0013 — Karaoke: one singer in time for every listener, and where the songs come from

Status: **proposed** (2026-10-27, M11; from Miguel's ideas: "just a couple of sofas and a big TV, you can play YouTube
videos, it takes account of ping so the song doesn't sound out of phase for everyone"). Needs Miguel's decisions,
marked **Decision**.

## Context

Miguel asked two questions:
- Can ping be accounted for so that everyone can sing without it sounding terrible?
- Can we play YouTube videos?

Voice already travels over the session: Opus frames to the host, which relays them; each listener plays them out of a
60 ms jitter buffer (T7.3). Nothing yet plays video or shared music in time across machines.

The simulation was run before any design (`SingAlongSimulationTests`). Its network:
- each machine reaches the host in 10–80 ms one way;
- paths up to 30 % asymmetric, jitter averaging 8 ms;
- clocks compared by 20 pings.

It measured 500 rooms of 6:

| Case | How far voice and music are apart at a listener |
|---|---|
| **One singer**, each listener playing the song late by the voice's measured delay | median 8 ms, p95 26 ms, worst 42 ms |
| **Two singers**: the second voice against the music, at a listener in time for the first | median 22 ms, p95 63 ms, worst 91 ms |
| **Two singers hearing each other** | late by median 148 ms, p95 205 ms |

Lip-sync tolerance is about 45 ms of sound late and 15 ms early (ITU-R BT.1359), and music is less forgiving than
speech.

## Decision

### 1. One singer at a time, in time for everyone

- **The singer** sings to the song as it plays on their own machine, with no delay.
- **Each listener** plays the song late by exactly the time the singer's voice takes to reach them, measured from the
  voice itself:
  - every Opus frame carries the singer's send time;
  - clocks are compared with the host by ping, the fastest samples kept (`SingAlong.ClockOffset`);
  - the median of recent frames sets the delay (`SingAlong.SongDelay`).
  - For every listener, voice and music then land together: within 26 ms for 95 % of listeners in the simulation.
- **Changing singer** re-times the room. Between turns the song is paused, or restarts from the chorus.
- **Everyone else's voice stays live** during a song, so they can cheer. It is not in time with the music, so a
  "quiet while singing" toggle mutes the others for the listeners.
- **Duets:** the second voice is out of time, 22–63 ms in the simulation. Offered as "duet (loose)", or not at all:
  **Decision**.

### 2. Singing together live is not possible over the internet

- **Why:** two singers hear each other 150–200 ms late. No sync can remove that: it is the time the sound takes to
  travel.
- **What works instead:** "everyone sings along locally". Each player hears only the song and their own voice, and
  the room hears one featured singer. That is how online karaoke apps do group songs.

### 3. Where the songs come from — **Decision**

| Option | Seek and delay per listener | Cost and risk |
|---|---|---|
| **A. Local video files** the host adds to the game (Unity `VideoPlayer`, sent like other assets) | Yes: frame-exact | Free, built in. The host provides the songs, so their rights are the host's |
| **B. YouTube in an embedded browser** (YouTube's IFrame player inside a web-view plugin, e.g. Vuplex 3D WebView) | Yes, through the player API (seek, pause, current time) | A paid third-party plugin. Every player needs internet access to YouTube. Within YouTube's terms only through the official player (no downloading) |
| **C. Downloading YouTube videos** | Yes | Against YouTube's terms of service. **Rejected** |

- **Recommended:** A first, which needs nothing new, then B if Miguel wants YouTube. B needs the plugin bought and
  checked against Unity 6.6 and URP before it is planned.
- **Rights:** song rights stay with whoever provides the songs (CLAUDE.md §7). Table World ships no songs.

### 4. The room

- **The scene:** a "karaoke" environment preset: sofas facing a big screen, with the chalkers seated on the sofas
  (the same seats and chairs as T11.10). Lyrics come with the video (karaoke videos have them burned in).
- **The singer's chalker** holds a microphone, and the stage light follows them (`SetLightCommand`).
- **The queue** is a list variable of seats (the M10 lists), and rules can drive it like any game: whose turn, next
  song.

## Consequences

- One singer in time for everyone needs:
  - a send time in each voice frame (two more bytes per 20 ms frame, as a frame counter);
  - a ping-based clock offset per machine;
  - a per-listener playback delay on the shared player.
  None of it changes the table protocol.
- A shared video player is a new kind of object, a screen, synced like music: play, pause, seek, position at a host
  time. Each listener applies its own delay.
- Two singers, or live group singing, stay out of time. The UI says so rather than pretending.

## Alternatives considered

- **Delaying the singer's own song by the round trip** (the singer sings late on purpose): it helps one listener only,
  and the singer hears the song lagging behind their own voice. Rejected.
- **Server-side mixing** (the host mixes voices with the song and streams the result): adds the host's round trip for
  everyone, does not fix duets, and costs the host's upload. Rejected.
