# ADR 0013 — Karaoke: one singer in time for every listener, and where the songs come from

Status: **accepted in part** (2026-10-27: songs from YouTube, free exploration, queue, stage zone; see section 5),
proposed in the rest (M11; from Miguel's ideas: "just a couple of sofas and a big TV, you can play YouTube
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

### 5. Miguel's decisions after trying the first room (2026-10-27)

The first room (sofas round a game table, the turn holder singing, "Pass the microphone") felt like a board game, not a
karaoke night. Miguel decided:

- **No table: free exploration.** People walk around the room in **first person** and sit on any sofa; others see their
  chalker walk and sit. This is the first real piece of moving characters (M6); how characters feel physically (T7.1)
  stays open, so walking is a plain character controller, no ragdoll.
- **Songs from YouTube** (option B), through a **free, open-source embedded Chromium** (UnityWebBrowser's CEF engine is
  the first candidate) running YouTube's official embed player. No paid plugin, no downloads. Checked on Unity 6.6 + URP
  by an experiment before it is built on.
- **A shared song queue**: while someone sings, the others find and add their next song.
- **No mic passing: whoever stands near the screen sings.** The stage is a zone in front of the screen; walking into it
  makes you the singer (microphone in hand, stage light, every listener's song delayed by your voice); walking out
  ends it. Two people on the stage sing together, out of time as section 2 explains.

### 6. The embedded browser: what we use, and the rules it runs under (2026-10-27)

- **Now:** UnityWebBrowser 2.2.8 (MIT, CEF 143, `dev.voltstro.unitywebbrowser*` from its own registry). Checked:
  open source and active (releases 2022–2026, 7 open issues, none about security). Unity calls every non-Unity package
  "unsigned"; its Chromium binaries are built by its maintainer. On Unity 6.6 it needs UniTask 2.5.11 (2.5.10 does not
  compile). An issue reports it failing in builds, so T12.9 includes a Windows build.
- **Behind our own interface:** the game talks to "a screen that plays a YouTube video at a time" (load, play, pause,
  seek, current time, ready, ended), never to the plugin directly, so the engine under it can change.
- **Before release:** replace it with a thin host of our own on CefSharp's off-screen mode (a separate process sending
  frames through shared memory; CefSharp follows Chrome releases closely), or, if that proves harder, a fork of
  UnityWebBrowser whose Chromium we build and sign ourselves. Not a Chromium integration written from zero.
- **Safety rules, whatever the engine:** the screen loads only YouTube embed URLs (any other navigation, pop-up or
  download is refused); no saved logins or cookies between sessions; the Chromium version is kept current before each
  release; YouTube's terms: the official player, ads play (the room waits for everyone's player to be ready), no
  downloading. A video that will not embed, or is blocked in a player's country, is shown as such in the queue.
- **Song titles** come from YouTube's public oEmbed address (`youtube.com/oembed`, no key), asked once by whoever adds
  the song; its answer also says when the owner blocks embedding (401) or the video is gone (404), so the queue refuses
  it before it reaches anyone's screen.
- **Song search (Miguel, 2026-10-27):** YouTube's own results page (`youtube.com/results`, the player's words plus
  "karaoke") in a second browser, open only while the search panel is. It is the one exception to "only our page": it
  may show YouTube's results and its cookie consent pages, nothing else. A click on a video is caught on the page and
  never opens it: the video goes through the same oEmbed check as a pasted link and joins the queue; a video page
  reached any other way counts as that pick and the browser goes back to the results; every result is checked with
  oEmbed as it appears (answers remembered for the session, four questions at a time) and those whose owner blocks
  playing outside YouTube, or that are gone, are hidden from the page, the panel saying how many (Miguel, 2026-10-27:
  many karaoke uploads, Sing King's among them, block it). oEmbed does not tell everything: some videos (label-claimed
  music mostly) pass it and still refuse every embedded player with error 150 ("Video unavailable"). So a picked video
  is first tried in the official player on our own page, muted, inside the search browser, before it is queued; one the
  player refuses is hidden and the panel says why. Then every result is tried that way as it appears, in a third,
  hidden and muted browser that runs only while the panel is open, so what can be clicked plays (measured on one
  Spanish search: 37 of 50 results played, 13 were refused with error 150). Sponsored results are neither tried nor
  queued (an ad's video is not a song); they stay on the page as YouTube placed them. Whatever still fails on the screen (a pasted link, a country block) is
  skipped by the host, so "Video unavailable" does not stay up for everyone. Ads are left as YouTube shows them (hiding them would be ad
  blocking); any other page (a channel,
  sign-in, another site) is refused back to the results; ads open in a new window, which is ignored. It is muted (a
  hovered result plays a preview). Rejected: YouTube's Data API (needs a key that would ship inside the game, with a
  shared quota of about 100 searches a day) and unofficial search (scraping, mirrors such as Invidious: against
  YouTube's terms and fragile).
- **Each browser runs apart:** its own pair of loopback ports to talk to its engine and its own cache folder (Chromium
  will not start on a folder another of its processes holds), so the screen and the search, or two copies of the game
  on one machine, never clash. What the pages print to their own console is logged as plain lines, not as the game's
  errors.

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
