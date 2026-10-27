# M11 — Karaoke

A karaoke room: friends on sofas in front of a big screen, one singer at a time in time for everyone, the others
cheering. Design: [ADR 0013](../adr/0013-karaoke.md) (proposed; the song source and duets are Miguel's call).

**Done when:** four friends over Relay take turns singing a song, and each listener hears the singer in time with the
music.

Sizes: S ≤ 1 day, M 2–4 days, L 1–2 weeks. Status values as in the [overview](README.md).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T12.1 ⚗ | Timing simulation: one singer, a duet, singers hearing each other, on a simulated internet | S | — | Numbers recorded here and in ADR 0013 | done 2026-10-27: `SingAlongSimulationTests` (500 rooms of 6): one singer p50 8 ms, p95 26 ms, max 42 ms; a duet's second voice p50 22 ms, p95 63 ms; singers hear each other 148–205 ms late. `core/Net/SingAlong.cs` (clock offset by fastest pings, song delay by median frame) |
| T12.2 **human** | Decisions: the song source (local files, YouTube through a web-view plugin) and whether duets are offered as loose | S | T12.1 | Recorded in ADR 0013 | todo |
| T12.3 | Voice frames carry a send time (frame counter); every machine keeps a clock offset with the host by ping | M | T12.1 | Core tests: offset within the simulation's error; frames from a fake microphone measured over the PlayMode session | todo |
| T12.4 | A screen object: a shared video player (play, pause, seek, position at a host time) with a per-listener delay; local video files first | L | T12.2 | PlayMode: two clients and the host show the same frame within the delay they were given | todo |
| T12.5 | Sing mode: the featured singer, the listeners' song delay from the singer's voice, "quiet while singing", the queue as a list of seats | M | T12.3, T12.4 | PlayMode with a fake microphone: the listener's song delay follows the measured voice delay | todo |
| T12.6 | The karaoke room: an environment preset with sofas and a big screen, chalkers seated on the sofas, a stage light on the singer, a microphone in the singer's hand | M | T11.10 | Screenshots; PlayMode: seats are on the sofas | todo |
| T12.7 | YouTube through a web-view plugin, if T12.2 chooses it: the plugin checked on Unity 6.6 + URP, its player driven like the screen object | L | T12.2, T12.4 | A YouTube video plays in time on two machines | todo |
| T12.8 | Player docs: hosting a karaoke night, adding songs, why duets drift | S | T12.5 | `check_docs.py` clean | todo |
