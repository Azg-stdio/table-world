# M3 — Multiplayer

**Goal:** authoritative host; clients send intents and receive state; players receive the host's assets on join.
**Done when (M3-lite, MVP):** two friends on different networks join by Relay code and play War; a client without local images sees the full table.
**Done when (full M3):** 3 players (2 virtual + 1 build) with 200 ms simulated latency and no desync; two machines connect through Steam.
**In the MVP:** M3-lite only. **Status:** todo. **Depends on:** M2 (War playable).

**M3-lite = T3.1, T3.2, T3.3, T3.4 (images only), T3.6. Full M3 adds T3.5, T3.7 and glTF streaming.**

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T3.1 ⚗ | Netcode spike, one week max: Netcode for GameObjects 2.13 vs FishNet vs Mirror on Unity 6.6. Criteria: compiles on 6.6, Unity Transport + Relay, Steam transport exists (Steamworks.NET / Facepunch, FishySteamworks, community NGO transports), ownership model fits "host rules". Default if inconclusive: NGO | M | M2 | `docs/adr/0002-netcode.md` with a criteria table | todo |
| T3.2 | Network layer: client sends `Command`, host validates with `core`, broadcasts `StateDelta`; no game logic in network code | M | T3.1 | Multiplayer Play Mode, 2 virtual players, state hash equal after 100 commands | todo |
| T3.3 **human** (dashboard) + agent | Lobby-lite: Unity Gaming Services project link + anonymous auth; host gets a Relay join code; join by code; seats, colors, roles | S | T3.2 | Two Editor instances on one machine join by code | todo |
| T3.4 | Asset sync: manifest with hashes, chunked transfer, client cache. MVP: images only; glTF after M4 | M | T3.2 | Client with empty cache receives all images | todo |
| T3.5 ⚗ | Multiplayer Play Mode 2 virtual players + Unity Transport latency/loss simulator (200 ms, 2 % loss); 500 random commands | S | T3.2 | State hash equal on all peers | todo (full M3) |
| T3.6 **human** | Windows `.exe` (Mono); playtest War with two friends over Relay. **Playtest gate**: triggers T0.6 (Steam fee) and unlocks M4+ | S | T3.3, T3.4 | Playtest notes filed; gate recorded in README | todo |
| T3.7 | Steam transport on test app ID 480 (`steam_appid.txt` = 480, Steam running on both ends); swap transports without touching the game layer | S | T3.6 | Two machines connect through Steam | todo (full M3) |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| — | T3.1 spike | — |
| — | T3.5 latency run | — |
