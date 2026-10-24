# Lua API coverage (T6.4)

Source: `docs/data/mod-requirements-2026-10-23.json`. Calls weighted by how often the mods' scripts make them.

| Mod | Calls | Covered | Share |
|---|---|---|---|
| Pokemon Master Trainer 3D (7 Generations) | 337 | 219 | 65% |
| Gloomhaven - Fantasy Setup (Scripted UI) | 8051 | 3849 | 48% |
| The Chimera Table | 40 | 17 | 42% |
| Anna's Roundtable - The FE Board Game Fan Project | 0 | 0 | 100% |
| Pokémon: Legends of Sinnoh | 6973 | 4257 | 61% |
| Riftbound - Basic Table | 5648 | 3660 | 65% |
| **All** | 21049 | 12002 | **57%** |

## Most used missing

| Call | Uses |
|---|---|
| `UI.setAttribute` | 2181 |
| `playLoopingEffect` | 921 |
| `playTriggerEffect` | 605 |
| `UI.getAttribute` | 590 |
| `createButton` | 533 |
| `UI.setValue` | 432 |
| `setLuaScript` | 302 |
| `editButton` | 296 |
| `getTable` | 276 |
| `UI.setAttributes` | 260 |
| `getZones` | 229 |
| `onPickUp` | 172 |
| `getBoundsNormalized` | 149 |
| `setColorTint` | 138 |
| `getBounds` | 133 |
| `startLuaCoroutine` | 117 |
| `setScale` | 114 |
| `onClick` | 101 |
| `clearButtons` | 99 |
| `onCollisionEnter` | 99 |
| `getScale` | 96 |
| `onSave` | 91 |
| `getNotebookTabs` | 89 |
| `setVelocity` | 77 |
| `positionToWorld` | 75 |
| `isSmoothMoving` | 68 |
| `Physics.cast` | 62 |
| `destroyObject` | 60 |
| `setCustomObject` | 44 |
| `reload` | 41 |
| `UI.setXml` | 36 |
| `log` | 36 |
| `onObjectEnterScriptingZone` | 30 |
| `getCustomObject` | 30 |
| `onObjectLeaveScriptingZone` | 25 |
| `setInvisibleTo` | 24 |
| `heading` | 24 |
| `Global.getTable` | 23 |
| `editInput` | 22 |
| `setState` | 21 |
