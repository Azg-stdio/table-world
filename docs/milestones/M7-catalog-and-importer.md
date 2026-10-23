# M7 — Catalog, mods and TTS importer

**Goal:** import a local TTS mod into our format privately; layered catalog; mods.
**Done when:** "The Chimera Table" and "Riftbound" import completely (objects, zones, snap points, Lua, translated UI); the Pokémon mods import with placeholders where a bundle is missing.
**In the MVP:** no. **Status:** todo. **Depends on:** M4, M5.

Design reference: [../tts-mapping.md](../tts-mapping.md).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T8.1 | `core/Tts`: TTS save reader → our model (units, `States`, `ContainedObjects`, snap points, zones). First measure a scale-1 card with `getBounds()` in a running TTS and the dice `RotationValues` (open items in `tts-mapping.md`) | M | T5.4, T6.3 | Chimera Table and Riftbound round-trip into `TableState` with 0 unmapped objects | todo |
| T8.2 | Download and cache referenced assets (95 % Steam CDN), never the Workshop; concurrency limit; resume | M | T8.1, T5.3 | Pokémon mod assets download with a cap and resume after kill | todo |
| T8.3 | XmlUI → declarative UI translator | S | T4.6, T8.1 | Riftbound UI renders | todo |
| T8.4 ⚗ | AssetRipper (external, GPL) and GPU buffer readback evaluation for bundle → glTF conversion | S | T5.4 | ADR with recommendation | todo |
| T8.5 | Catalog: official / original / community layers, authorship declaration, report and takedown; Steam Workshop via Steamworks.NET | M | T0.6 | Publish and retrieve a test mod | todo |
| T8.6 ⚗ | Analyze 20–30 varied mods with `analyze_mods.py`; revalidate priorities | S | — | `docs/data/` updated; priorities adjusted | todo |
| T8.7 **human** | Legal review before publishing the catalog | — | T8.5 | Sign-off recorded | todo |
