# Lua API coverage (T6.4)

Source: `docs/data/mod-requirements-2026-10-23.json`. Calls weighted by how often the mods' scripts make them.

| Mod | Calls | Covered | Share |
|---|---|---|---|
| Pokemon Master Trainer 3D (7 Generations) | 337 | 232 | 69% |
| Gloomhaven - Fantasy Setup (Scripted UI) | 8051 | 4794 | 60% |
| The Chimera Table | 40 | 20 | 50% |
| Anna's Roundtable - The FE Board Game Fan Project | 0 | 0 | 100% |
| Pokémon: Legends of Sinnoh | 6973 | 6574 | 94% |
| Riftbound - Basic Table | 5648 | 4372 | 77% |
| **All** | 21049 | 15992 | **76%** |

## Most used missing

| Call | Uses |
|---|---|
| `UI.setAttribute` | 2181 |
| `UI.getAttribute` | 590 |
| `createButton` | 533 |
| `UI.setValue` | 432 |
| `editButton` | 296 |
| `UI.setAttributes` | 260 |
| `onClick` | 101 |
| `clearButtons` | 99 |
| `onCollisionEnter` | 99 |
| `Physics.cast` | 62 |
| `setCustomObject` | 44 |
| `UI.setXml` | 36 |
| `setInvisibleTo` | 24 |
| `heading` | 24 |
| `editInput` | 22 |
| `onValueChanged` | 21 |
| `onEndEdit` | 19 |
| `spawnObject` | 14 |
| `deal` | 13 |
| `getInputs` | 13 |
| `onNumberTyped` | 10 |
| `lookAt` | 9 |
| `isDestroyed` | 8 |
| `clone` | 7 |
| `Timer.create` | 7 |
| `getStateId` | 6 |
| `Timer.destroy` | 6 |
| `addTag` | 6 |
| `removeButton` | 5 |
| `onChat` | 5 |
| `getHandObjects` | 4 |
| `UI.show` | 4 |
| `UI.hide` | 4 |
| `getObjectsWithAllTags` | 4 |
| `getObjectsWithAnyTags` | 4 |
| `layout` | 4 |
| `removeTag` | 4 |
| `MusicPlayer.getPlaylist` | 4 |
| `spawnObjectData` | 4 |
| `getComponent` | 4 |
