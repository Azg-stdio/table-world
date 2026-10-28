# Flow: loading assets

Images, glTF and OBJ models and Unity asset bundles named by a table. Every machine loads them on its own; nothing waits for
them (pieces show plain colours until their image lands) and nothing loads twice.

```mermaid
flowchart TD
    need["A view needs asset X<br/>(Texture, Mesh, Bundle)"] --> pkg{"In the game package?"}
    pkg -- yes --> decode
    pkg -- no --> client{"Client: in the host's<br/>asset sync (chunks)?"}
    client -- yes --> decode
    client -- no --> url{"Asset has a web address?"}
    url -- no --> grey["Placeholder"]
    url -- yes --> local{"URL cache or the<br/>player's TTS cache?"}
    local -- yes --> decode
    local -- no --> web["Download (4 at a time):<br/>Steam's old host rewritten to the CDN"]
    web --> decode["Decode off the main thread<br/>(2 images at a time, 4 on big machines)"]
    decode --> bake["Bake in Burst jobs: at most 4096 px,<br/>mipmaps, BC1 (opaque) or BC3"]
    bake --> upload["Upload: a few per frame,<br/>within 4 ms"]
    upload --> redraw["Decoded → redraw only the views<br/>that use X (AssetUse.Uses)"]
```

| Box | Files |
|---|---|
| Asset library: package files, URL cache, TTS cache, web, decode | [AssetLibrary.cs](../../../unity/Assets/_Project/Scripts/View/AssetLibrary.cs) (`Texture`, `Mesh`, `Bundle`, `PackageFile`, `LocalCopy`, `FetchFromWeb`, `Decode`, `Fit` for images that arrive as bytes, `Progress`, `Translucent`) |
| Bake and upload | [TextureBaker.cs](../../../unity/Assets/_Project/Scripts/View/TextureBaker.cs), jobs in [TextureJobs.cs](../../../unity/Assets/_Project/Scripts/View/TextureJobs.cs) |
| Loading screen (steps, then images and downloads left) | [LoadingScreen.cs](../../../unity/Assets/_Project/Scripts/View/Hud/LoadingScreen.cs), `TableView.LoadInSteps` |
| Where to fetch an address from today | [TableState.cs](../../../core/Rules/TableState.cs) (`Asset.FetchAddress`) |
| Which views an asset concerns | [AssetUse.cs](../../../core/Rules/AssetUse.cs), used by `TableView.ShowDecoded` |
| Files for clients (manifest, chunks, cache) | [AssetSync.cs](../../../core/Net/AssetSync.cs) |
| Asset bundles and the URP remap | [Bundles.cs](../../../unity/Assets/_Project/Scripts/View/Bundles.cs) (`Prefab`, `Attach`, `Remap`), [ADR 0001](../../adr/0001-assetbundles.md) |
| OBJ models | [ObjMesh.cs](../../../core/Models/ObjMesh.cs) |
| glTF and GLB models (glTFast in the background, URP Lit materials, convex colliders, size limits) | [GltfModels.cs](../../../unity/Assets/_Project/Scripts/View/GltfModels.cs) (`Template`, `Attach`), `AssetLibrary.GltfTemplate` |
| Download in bulk after an import | [AssetDownloader.cs](../../../core/Tts/AssetDownloader.cs) |

## Rules worth knowing

- **The address is the name.** An asset with a `url` keeps that address as its identity (the URL cache and the TTS
  cache are keyed by it, `TtsImporter.CacheKey`); only the download goes to Steam's current host
  (`cloud-3.steamusercontent.com` answers 403).
- **Two decodes at a time** (four with 12 GB of memory and 6 cores). Each holds the full-size pixels until it is
  baked (a 7 151 × 3 253 TTS panel is 93 MB); decoding a heavy table's images together ran the Editor out of memory.
- **The main thread never compresses.** `Texture2D.Compress` took 100 ms and more per sprite sheet on the main thread;
  the baker's Burst jobs resize, make mipmaps and encode BC1/BC3 blocks on worker threads, and the main thread only
  uploads finished textures within `TextureBaker.UploadBudgetMs`. Opaque images are BC1, half the memory of BC3.
- **A decoded image redraws only the views that use it.** Redrawing every view for each of hundreds of images stalled
  loading.
- **Bundles built for Unity 5.6–2019 load in 6.6**; their materials are remapped to URP Lit at load time.

## Where to put a breakpoint

| Question | Breakpoint |
|---|---|
| Where did this image come from? | `AssetLibrary.Texture` |
| Why is it still grey? | `AssetLibrary.FetchFromWeb` (the warning names the failure) |
| Why is a bundle pink? | `Bundles.Remap` |
