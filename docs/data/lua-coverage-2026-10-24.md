# Lua API coverage (T6.4)

Source: `docs/data/mod-requirements-2026-10-23.json`. Calls weighted by how often the mods' scripts make them.

| Mod | Calls | Covered | Share |
|---|---|---|---|
| Pokemon Master Trainer 3D (7 Generations) | 337 | 300 | 89% |
| Gloomhaven - Fantasy Setup (Scripted UI) | 8051 | 7873 | 98% |
| The Chimera Table | 40 | 33 | 82% |
| Anna's Roundtable - The FE Board Game Fan Project | 0 | 0 | 100% |
| Pokémon: Legends of Sinnoh | 6973 | 6935 | 99% |
| Riftbound - Basic Table | 5648 | 5543 | 98% |
| **All** | 21049 | 20684 | **98%** |

## Most used missing

| Call | Uses |
|---|---|
| `onCollisionEnter` | 99 |
| `Physics.cast` | 62 |
| `setCustomObject` | 44 |
| `heading` | 24 |
| `editInput` | 22 |
| `spawnObject` | 14 |
| `getInputs` | 13 |
| `onNumberTyped` | 10 |
| `onChat` | 5 |
| `layout` | 4 |
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
| `unregisterCollisions` | 1 |
| `getTransformUp` | 1 |
| `getTriggerEffects` | 1 |
| `getLoopingEffects` | 1 |
| `Notes.addNotebookTab` | 1 |
| `onObjectCollisionEnter` | 1 |
| `onObjectHover` | 1 |
| `onObjectDestroy` | 1 |
| `onObjectSpawn` | 1 |
| `changeColor` | 1 |
| `setAngularVelocity` | 1 |
