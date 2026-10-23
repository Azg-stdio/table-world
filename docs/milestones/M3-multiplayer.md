# M3 — Multiplayer

**Goal:** authoritative host; clients send intents and receive state; players receive the host's assets on join.
**Done when (M3-lite, MVP):** two friends on different networks join by Relay code and play War; a client without local images sees the full table.
**Done when (full M3):** 3 players (2 virtual + 1 build) with 200 ms simulated latency and no desync; two machines connect through Steam.
**In the MVP:** M3-lite only. **Status:** in progress (T3.1, T3.2 done). **Depends on:** M2 (War playable).

**M3-lite = T3.1, T3.2, T3.3, T3.4 (images only), T3.6. Full M3 adds T3.5, T3.7 and glTF streaming.**

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T3.1 ⚗ | Netcode spike, one week max: Netcode for GameObjects 2.13 vs FishNet vs Mirror on Unity 6.6. Criteria: compiles on 6.6, Unity Transport + Relay, Steam transport exists (Steamworks.NET / Facepunch, FishySteamworks, community NGO transports), ownership model fits "host rules". Default if inconclusive: NGO | M | M2 | `docs/adr/0002-netcode.md` with a criteria table | done 2026-10-23 (NGO as message pipe, ADR 0002) |
| T3.2 | Network layer: client sends `Command`, host validates with `core`, broadcasts `StateDelta`; no game logic in network code | M | T3.1 | Multiplayer Play Mode, 2 virtual players, state hash equal after 100 commands | done 2026-10-23 (two processes instead of MPPM virtual players; see record) |
| T3.3 **human** (dashboard) + agent | Lobby-lite: Unity Gaming Services project link + anonymous auth; host gets a Relay join code; join by code; seats, colors, roles | S | T3.2 | Two Editor instances on one machine join by code | todo |
| T3.4 | Asset sync: manifest with hashes, chunked transfer, client cache. MVP: images only; glTF after M4 | M | T3.2 | Client with empty cache receives all images | todo |
| T3.5 ⚗ | Multiplayer Play Mode 2 virtual players + Unity Transport latency/loss simulator (200 ms, 2 % loss); 500 random commands | S | T3.2 | State hash equal on all peers | todo (full M3) |
| T3.6 **human** | Windows `.exe` (Mono); playtest War with two friends over Relay. **Playtest gate**: triggers T0.6 (Steam fee) and unlocks M4+ | S | T3.3, T3.4 | Playtest notes filed; gate recorded in README | todo |
| T3.7 | Steam transport on test app ID 480 (`steam_appid.txt` = 480, Steam running on both ends); swap transports without touching the game layer | S | T3.6 | Two machines connect through Steam | todo (full M3) |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-23 | T3.1 spike | NGO 2.13.3 + Transport 6.6.0 + MPPM 3.0.0 install and compile on 6.6; host starts in Play mode; JSON command over a named message received (see ADR 0002) |
| — | T3.5 latency run | — |
| 2026-10-23 | T3.2 protocol, simulated peers () | host + 2 clients, 3 seeds x 300 random commands with interleaved delivery: all peers identical, 0 resyncs; conflict (delete vs flip of the same die): host order wins, loser told; tampered client resyncs; UNO played across the protocol |
| 2026-10-23 | T3.2 two processes: Editor host (Play mode) + Windows player client (), both playing 100 random commands concurrently | identical final hash ; host ordered 120 commands (39 host, 35 client, 46 physics pose reports); client seat . First attempt failed: NGO refuses a NetworkManager nested under another object |

## Assumptions recorded during implementation

| Task | Assumption |
|---|---|
| T3.2 | Protocol in  (ADR 0002): the host orders commands; clients predict their own and rebuild when the host orders differently; remote commands apply in place when nothing is pending. Physics: the host reports poses for every object; a client only for objects its player touched in the last 4 s. Undo/redo are local-only (disabled while networked). Seats: host plays the first seat, each joining client the next free one. Join by address in the HUD (Host / Join, default 127.0.0.1:7777) until Relay join codes (T3.3). The done check used two real processes (Editor + built player) instead of Multiplayer Play Mode virtual players, which can only be enabled from the MPPM window. |
