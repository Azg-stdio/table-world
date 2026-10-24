# Lua API coverage (T6.4)

Source: `docs/data/mod-requirements-2026-10-23.json`. Calls weighted by how often the mods' scripts make them.

| Mod | Calls | Covered | Share |
|---|---|---|---|
| Pokemon Master Trainer 3D (7 Generations) | 337 | 296 | 88% |
| Gloomhaven - Fantasy Setup (Scripted UI) | 8051 | 7845 | 97% |
| The Chimera Table | 40 | 33 | 82% |
| Anna's Roundtable - The FE Board Game Fan Project | 0 | 0 | 100% |
| Pokémon: Legends of Sinnoh | 6973 | 6884 | 99% |
| Riftbound - Basic Table | 5648 | 5537 | 98% |
| **All** | 21049 | 20595 | **98%** |

## Most used missing

| Call | Uses |
|---|---|
| `onCollisionEnter` | 99 |
| `Physics.cast` | 62 |
| `setCustomObject` | 44 |
| `setInvisibleTo` | 24 |
| `heading` | 24 |
| `editInput` | 22 |
| `spawnObject` | 14 |
| `deal` | 13 |
| `getInputs` | 13 |
| `onNumberTyped` | 10 |
| `isDestroyed` | 8 |
| `clone` | 7 |
| `Timer.create` | 7 |
| `getStateId` | 6 |
| `Timer.destroy` | 6 |
| `addTag` | 6 |
| `onChat` | 5 |
| `getObjectsWithAllTags` | 4 |
| `getObjectsWithAnyTags` | 4 |
| `layout` | 4 |
| `removeTag` | 4 |
| `MusicPlayer.getPlaylist` | 4 |
| `spawnObjectData` | 4 |
| `getComponent` | 4 |
| `set` | 4 |
| `getData` | 4 |
| `scale` | 3 |
| `onObjectLeaveContainer` | 3 |
| `reset` | 3 |
| `registerCollisions` | 2 |
| `getHoldingObjects` | 2 |
| `jointTo` | 2 |
| `onObjectEnterContainer` | 2 |
| `addAttachment` | 2 |
| `removeAttachments` | 2 |
| `setPage` | 2 |
| `createInput` | 2 |
| `onScriptingButtonDown` | 2 |
| `onUpdate` | 2 |
| `magnitude` | 2 |
