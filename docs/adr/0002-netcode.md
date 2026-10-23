# ADR 0002 — Netcode: Netcode for GameObjects as a message pipe, game sync in core

Status: **accepted** (2026-10-23, T3.1). Revisit at T3.7 (Steam transport).

## Context

The host is authoritative (CLAUDE.md §3): clients send intents (`Command`s), the host executes them with `core`, everyone else follows. M1–M2 already made every change a serializable core command, the engine deterministic (seeded PCG32, same commands → same state hash) and the whole state a JSON document. So the network library does not need to replicate objects, own transforms or run prediction for us: it needs to carry **reliable, ordered messages** between a host and a few clients, over **Unity Relay** (M3-lite, join codes) and later **Steam** (full M3, test app 480).

## Options and criteria

| Criterion | Netcode for GameObjects 2.13.3 | FishNet | Mirror |
|---|---|---|---|
| Installs and compiles on Unity 6.6 | **verified 2026-10-23** (registry package, with Unity Transport 6.6.0 and Multiplayer Play Mode 3.0.0) | not tested (Asset Store / git) | not tested (Asset Store / git) |
| Reliable ordered messages without game objects | **verified**: named messages via `CustomMessagingManager`, `ReliableFragmentedSequenced`; a JSON `FlipCommand` round trip on a host in Play mode | yes (broadcasts) — docs | yes (`NetworkMessage`) — docs |
| Unity Relay (join codes) | first-party: Unity Transport + Relay; Unity's Relay docs recommend NGO for most cases | community transport | community transport (Unity docs describe Mirror + Relay as possible) |
| Steam transport | community transports (Facepunch, SteamNetworkingSockets) in Unity's community-contributions repo; **support for NGO 2.x not confirmed** | FishySteamworks (community) | FizzySteamworks (community) |
| Local multi-instance testing | **Multiplayer Play Mode 3.0.0** (first-party; the CLI lists its virtual players) | MPPM works with any library | MPPM works with any library |
| Fits "host runs the rules" | yes: we use it only as a pipe | yes | yes |
| Cost / licence | free, Unity package | free core, paid Pro tier | MIT |

Sources: Unity package registry (`unity command package search`, 2026-10-22), the Play-mode spike below, Unity Relay docs page "NGO vs Mirror for Relay", Unity community-contributions repository, community forum comparisons (Unity Discussions threads on NGO vs FishNet vs Mirror). Web results for 2026 were thin; nothing found contradicts the table.

## Decision

Use **Netcode for GameObjects** with **Unity Transport**, but only as a transport for our own messages:

- One `NetworkManager`; **no NetworkObjects, no NetworkTransforms, no NetworkVariables**. Messages are named messages carrying JSON.
- The synchronization protocol lives in **`core/Net`** (no Unity): the host sequences commands and broadcasts them with the resulting state hash; clients apply them in host order to a confirmed engine, predict their own commands locally, and rebuild on mismatch or resync from a snapshot. It is unit-tested with simulated peers (T3.2).
- Unity code only moves bytes between `core/Net` and NGO, plus Relay/lobby (T3.3).

## Consequences

- Switching library or transport touches one small adapter, not the game: the protocol is library-agnostic. This contains the main risk (no confirmed NGO 2.x Steam transport): if T3.7 finds no working NGO Steam transport, the fallback is a thin adapter on Steamworks.NET's networking sockets, or swapping NGO for FishNet/Mirror under the same `core/Net` protocol.
- We give up NGO features we do not need (object spawning, ownership, NetworkTransform interpolation). Physics is not replicated: each peer simulates locally and only final poses travel, as `Move` commands, which is what M1 already does.
- Bandwidth: commands are small; full snapshots (join, resync) are the table JSON (~30–60 KB for a 52-card game) and go through fragmented reliable delivery.

## Spike record (2026-10-23)

| Check | Result |
|---|---|
| `com.unity.netcode.gameobjects@2.13.3`, `com.unity.transport@6.6.0`, `com.unity.multiplayer.playmode@3.0.0` added to the project | installed, project compiles, 0 errors |
| Play mode: `NetworkManager` + `UnityTransport` (127.0.0.1:7777), `StartHost()` | `IsServer`, `IsListening` true; 1 connected client (the host) |
| Named message `tw.command` with a JSON `FlipCommand`, `ReliableFragmentedSequenced`, sent to the server | received by the handler with the full payload, sender 0 |
| FishNet, Mirror | not installed: the decision does not depend on their object models, and both can carry the same messages if needed |
