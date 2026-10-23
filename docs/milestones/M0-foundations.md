# M0 — Foundations

**Goal:** repository with `core/` tested from `dotnet test`, Unity consuming `core.dll`, Task 0 decisions closed.
**Done when:** `dotnet test core.tests/` is green, a Unity EditMode test calls `core.dll`, CI runs the core tests, docs are on `main`.
**In the MVP:** yes. **Status:** in progress (Task 0 done 2026-10-22).

## Task 0 — AssetBundle and pipeline test

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T0.1 | Read the Unity version of the 70 local bundles | S | — | Table of versions in ADR 0001 | done 2026-10-22 |
| T0.2 ⚗ | Load 5 bundles in Unity 6.6 + URP, measure magenta before/after URP remap | S | T0.1 | 0 magenta pixels after remap, PNG evidence | done 2026-10-22 |
| T0.3 | Test the Built-in pipeline | S | T0.2 | — | dropped (remap solves it) |
| T0.4 | Evaluate AssetRipper as offline converter | S | T0.2 | — | deferred → M7 (T8.4) |
| T0.5 | Write `docs/adr/0001-assetbundles.md` | S | T0.2 | ADR on `main` | done 2026-10-22 |
| T0.6 **human** | Steamworks onboarding: partner account, Steam Direct fee (USD 100), identity/tax/bank forms (15–30 days) | S | playtest gate (T3.6) | App ID received | deferred → after T3.6 |

## Task 1 — Repository skeleton

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T1.1 | `core/Core.csproj` (netstandard2.1, LangVersion 9, nullable, `Newtonsoft.Json` 13.0.3), `core.tests/Core.Tests.csproj` (xunit, net9.0), `Directory.Build.props`, `TableWorld.sln`, `Core.Version` class, post-build copy of `Core.dll` to `unity/Assets/Plugins/Core/` | M | — | `dotnet test` green; `unity command script eval --code "return Core.Version.Get();"` returns the version | done 2026-10-22 |
| T1.2 | Unity: `_Project/{Scripts,Prefabs,Scenes,Settings,Editor,UI,Tests}` via CLI; add `com.unity.nuget.newtonsoft-json@3.2.2`, `com.unity.test-framework@1.8.0`; asmdefs `TableWorld.Runtime` (refs `Core.dll`), `TableWorld.Editor`, `TableWorld.Tests`; `.editorconfig`; move `InputSystem_Actions` and `SampleScene` under `_Project/` | S | T1.1 | `unity command test run --mode EditMode` runs a test calling `Core.Version.Get()` | done 2026-10-22 |
| T1.3 | Promote `docs/rules-schema.md` and `docs/tts-mapping.md` from v0 drafts to what `core/` will implement; verify the TTS unit constant and the deck top/bottom order against a local save | S | T1.1 | Both docs no longer say "draft"; "verify" notes resolved | todo |
| T1.4 | CI: GitHub Actions on `windows-latest` + `ubuntu-latest`, `dotnet build` + `dotnet test` on push | S | T1.1 | Green run on `main` | todo |
| T1.5 | `analyze_mods.py --json docs/data/mods-<date>.json`; commit the 6-mod result | S | — | File in `docs/data/` | todo |
| T1.6 **human** + agent | Default asset kit: confirm everything is procedural for the MVP; later source CC0 packs into `unity/Assets/_Project/Art/ThirdParty/<pack>/` with `LICENSE.txt` | S | — | Decision recorded below | todo |

## Asset kit decision (T1.6)

| Item | MVP | Later (CC0 sources) |
|---|---|---|
| Table | procedural plane | Poly Haven textures, Kenney, Quaternius |
| 52-card deck + jokers sheet (10×7 TTS layout) | generated placeholder sheet | Kenney playing cards |
| Dice d4–d20 | procedural polyhedra + generated numbers | Kenney, Quaternius |
| Chips, tokens, pawns, meeples, checkers, cubes | procedural primitives | Kenney, Quaternius |
| Tiles (square, hex) | procedural | — |
| Bags | procedural cylinder | Kenney, Quaternius |
| Chess, dominoes, go stones | — | M4+, Quaternius |
| Hand zone visual, notecard, counter, blocks | procedural | — |

Rule: nothing from the TTS install or Workshop is ever copied into the repo.

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | 70 bundle headers | 51× 5.6.2f1, 17× 5.6.5f1, 1× 2019.1.0f2, 1× 5.3.4f1 |
| 2026-10-22 | Render 3 bundle prefabs before/after URP remap | magenta 9 093 / 51 989 / 16 975 → 0 / 0 / 0 |
| 2026-10-22 | netstandard2.1 + xunit net9 scaffold | 1 test green |
| 2026-10-22 | MoonSharp sandbox | 7 tests green (os/io/load blocked, step limit aborts loop) |
| 2026-10-22 | MCP C# SDK stdio server | initialize, tools/list, tools/call OK |

## Assumptions recorded during implementation

| Task | Assumption |
|---|---|
| T1.1 | `Core.dll` (Debug, deterministic) is committed under `unity/Assets/Plugins/Core/` so a fresh clone opens in Unity without a build. Rebuild and commit it whenever `core/` changes. Newtonsoft is not copied: Unity's package provides it. |
| T1.2 | URP assets from `Assets/Settings/` were also moved to `_Project/Settings/` (GUID-preserving `asset move`). EditMode tests live in `_Project/Tests/EditMode/` (`TableWorld.Tests`, Editor only); PlayMode tests (T2.9) get their own asmdef. |
