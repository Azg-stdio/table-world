# Milestones overview

One file per milestone. Each task has an id, a size (S ≤ 1 day, M 2–4 days, L 1–2 weeks), its dependencies, and a done check that must pass before the status changes. Update the status column here and in the milestone file in the same commit.

**Target:** the full game at TTS quality (2026-10-23). A real Tabletop Simulator game loads and plays in Table World with friends, and feels as polished as TTS. There is no reduced first version; the order below follows dependencies.

Status values: `todo` · `in progress` · `blocked (why)` · `done YYYY-MM-DD` · `dropped (why)` · `deferred → Mx`

| # | Milestone | Goal in one line | Tasks | Status |
|---|---|---|---|---|
| [M0](M0-foundations.md) | Foundations | `core/` tested with `dotnet test`, Unity loads `core.dll`, CI green | T0.1–T0.6, T1.1–T1.6 | in progress (all done except T1.4: CI workflow written, needs a push for a green run) |
| [M1](M1-basic-table.md) | Table (TTS parity) | Everything a TTS player does at the table, at TTS quality | T2.1–T2.12 | in progress (T2.12 parity: gaps in `docs/tts-interactions.md` §7) |
| [M2](M2-rules-and-mcp.md) | Rules + engine + MCP | Claude Code builds games from a text rulebook | T4.1–T4.8 | in progress (T4.1–T4.6 done) |
| [M3](M3-multiplayer.md) | Multiplayer | Friends play together over Relay, then Steam; everything streams | T3.1–T3.8 | in progress (T3.1–T3.5 done; T3.6 friends playtest and T3.7 Steam need Miguel) |
| [M4](M4-host-assets.md) | Host assets | glTF and images at runtime, cache, bundle loader with URP remap | T5.1–T5.5 | in progress (T5.3 done; T5.2, T5.4 in progress; T5.1 done) |
| [M5](M5-lua-sandbox.md) | Sandboxed Lua | Scripts react to events; the TTS API layer; hostile scripts cannot hurt the host | T6.1–T6.4 | in progress (T6.1, T6.4 done; T6.2, T6.3 in progress) |
| [M6](M6-characters-and-dnd.md) | Physics characters + DnD | Active ragdolls (Chalkers), emotes, DnD assistant | T7.1–T7.4 | in progress (T7.3 voice and placeholder chalkers) |
| [M7](M7-catalog-and-importer.md) | TTS importer + catalog | Import local TTS mods and play them; layered catalog | T8.1–T8.8 | in progress (importer and downloads working on the 6 local mods; see the milestone file) |
| [M8](M8-documentation.md) | Documentation | Developer wireframes linked to code; player docs for play, MCP, importer; game brief template | T9.1–T9.17 | in progress (T9.10 template written) |
| [M9](M9-languages.md) | Languages | Table World's interface in English and Spanish; games in several languages, the host picking one (ADR 0011) | T10.1–T10.9 | todo (design proposed 2026-10-26) |
| [M10](M10-social-games.md) | Social games | Avalon, Werewolf and Wink Murder with secrets per seat, roles, eyes-closed phases, votes and seated chalkers (ADR 0012) | T11.1–T11.17, T11.4b, T11.15b | in progress (2026-10-26: all built and tested headless; left: playtests with friends over Relay, voice on two machines, the artist's chalker model) |
| [M11](M11-karaoke.md) | Karaoke | One singer at a time in time for every listener, a karaoke room, songs from local files (YouTube if Miguel chooses; ADR 0013) | T12.1–T12.8 | todo (2026-10-27: timing simulated; waiting on the song-source decision) |
| [M12](M12-atmosphere.md) | Atmosphere | Rooms with candles and fire, soundtracks and sound cues per game, coloured words in the log, a themed HUD (parchment) | T13.1–T13.11 | todo (planned 2026-10-29 for the Avalon playtest) |

## Order

```
M0 ──► M1 (TTS parity) ──► M2 ──► M3 ──► M4 ──► M5 ──► M7 (play a real TTS game)
                                   │
                                   ├──► friends playtest (T3.6) ──► Steam fee (T0.6, money)
                                   └──► M6 (after M3)

M8 (docs) runs alongside: each page follows the feature it describes.
M9 (languages) needs only M2's rules format; it can start any time.
M10 (social games) needs M3's sync and M6's chalkers; its two tracks (secrets, seated chalkers) can start now.
M11 (karaoke) needs M3's voice and M10's seated chalkers.
M12 (atmosphere) needs only the environment and effects of M2; its tasks are independent of each other except as their table says.
```

- The order is dependencies, not scope: M4, M5 and M7 are what "play a real TTS game" needs.
- The friends playtest is a test, run whenever there is something worth playing; it gates only the Steam fee.
- Each milestone has at least one **simulation** task (marked ⚗). Run it before building on the result and record numbers in the milestone file.
- Tasks marked **human** need Miguel (accounts, money, clicks only possible in the Editor UI, playtests).
- Estimates were made for a reduced first version and no longer apply; they will be redone per milestone as each one starts.

Context, decisions and risks: [../plan.md](../plan.md). Permanent rules: [../../CLAUDE.md](../../CLAUDE.md).
