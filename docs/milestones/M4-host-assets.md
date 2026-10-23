# M4 — Host assets

**Goal:** the host brings their own models and images; private sessions; local cache.
**Done when:** a glTF and a PNG dropped on the table appear as `Model` and `Card`, are saved with the table, and clients receive them.
**In the MVP:** no. **Status:** todo. **Depends on:** playtest gate (T3.6).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T5.1 | glTFast 6.20 at runtime from local files; size/triangle limits; automatic convex colliders with a cap | S | T3.6 | 10 CC0 glTF models load with colliders | todo |
| T5.2 | Runtime images (PNG/JPG/WebP), mipmaps, resolution limit, atlas for sprite sheets | S | T3.6 | A 10×7 sheet becomes a working deck | todo |
| T5.3 | Content-hash cache in `%LocalAppData%/TableWorld/cache`; cleanup policy | S | T5.1 | Second load hits the cache | todo |
| T5.4 | AssetBundle loader with URP material remap (ADR 0001) and placeholder on failure; shader fallback table; unknown shaders logged | M | T5.1 | The 5 ADR bundles load with 0 magenta pixels in a build | todo |
| T5.5 ⚗ | Load 50 varied CC0 glTF models and 20 local bundles; measure time and memory | S | T5.4 | Numbers recorded; none pink | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | 5 bundles, URP remap (Editor) | 0 magenta pixels |
| — | T5.5 | — |
