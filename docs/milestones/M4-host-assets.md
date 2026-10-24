# M4 — Host assets

**Goal:** the host brings their own models and images; private sessions; local cache.
**Done when:** a glTF and a PNG dropped on the table appear as `Model` and `Card`, are saved with the table, and clients receive them.
**Status:** todo. **Depends on:** M3 (asset sync).

| Id | Task | Size | Depends on | Done check | Status |
|---|---|---|---|---|---|
| T5.1 | glTFast 6.20 at runtime from local files; size/triangle limits; automatic convex colliders with a cap | S | T3.6 | 10 CC0 glTF models load with colliders | todo |
| T5.2 | Runtime images (PNG/JPG/WebP), mipmaps, resolution limit, atlas for sprite sheets | S | T3.6 | A 10×7 sheet becomes a working deck | in progress (2026-10-24): PNG and JPEG load with mipmaps, scaled to at most 4096 px on the longest side (sheets keep their grid), block-compressed when the size allows (DXT, a quarter of the memory or less); 10×7 sheets already work as decks (M1). Left: WebP (needs a decoder; no local mod uses it), a quality setting for the limit |
| T5.3 | Content-hash cache in `%LocalAppData%/TableWorld/cache`; cleanup policy | S | T5.1 | Second load hits the cache | done (2026-10-24): the content-addressed client cache of T3.4 (`%LocalAppData%/TableWorld/cache/assets`, second join fetches 0 bytes, measured in T3.4) now trims itself when a client connects: interrupted downloads older than a day, then least recently used files above 10 GB (`DirectoryAssetStore.Trim`, core test) |
| T5.4 | AssetBundle loader with URP material remap (ADR 0001) and placeholder on failure; shader fallback table; unknown shaders logged | M | T5.1 | The 5 ADR bundles load with 0 magenta pixels in a build | in progress (2026-10-24): `Bundles` loads each bundle once per session, puts its prefab on `Model` and `Bag` objects (scaled from TTS units, its colliders made convex, a box when it has none), remaps Standard / Legacy / Unlit / particle materials to URP (texture, colour, normal map, emission, smoothness, cutout and transparency), logs unknown shaders once, keeps the placeholder when a bundle is missing; bundles travel with the host's asset sync; PlayMode test with 3 local cached bundles (skipped without a TTS cache), Pokémon figures checked by eye. Left: the in-build check (no builds until Miguel asks), animated bundles and effects (`TTSAssetBundleEffects` is TTS's own component) |
| T5.5 ⚗ | Load 50 varied CC0 glTF models and 20 local bundles; measure time and memory | S | T5.4 | Numbers recorded; none pink | todo |

## Simulation record

| Date | Simulation | Result |
|---|---|---|
| 2026-10-22 | 5 bundles, URP remap (Editor) | 0 magenta pixels |
| — | T5.5 | — |
