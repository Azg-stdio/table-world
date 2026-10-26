# Lua API

> Generated from a running script host (globals) and `core/Scripting/ScriptApi.cs` (object members, each checked on a real object) by `core.tests/Docs/DocsReferenceTests.cs`. Do not edit: run
> `TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference`.

What table scripts can call (M5, ADR 0003). Scripts run on the host only, in a sandbox without files, OS access
or code loading, with a step budget per call. The Lua standard library (`string`, `table`, `math`, `coroutine`)
is there without `io`, `os.execute`, `load` and `dofile`. Names follow Tabletop Simulator's; how much of TTS's
API the local mods' calls find is measured in [`docs/data/lua-coverage-*.md`](../data/).

## Globals

| Name | Kind | Functions |
|---|---|---|
| `addNotebookTab` | function |  |
| `assert` | function |  |
| `broadcastToAll` | function |  |
| `broadcastToColor` | function |  |
| `collectgarbage` | function |  |
| `Color` | table |  |
| `destroyObject` | function |  |
| `dynamic` | table | `eval`, `prepare` |
| `editNotebookTab` | function |  |
| `error` | function |  |
| `Game` | table | `addResource`, `endPhase`, `endTurn`, `getCurrentSeat`, `getDiceTotal`, `getLegalActions`, `getPhase`, `getResource`, `getRole`, `getSeatColor`, `getSeats`, `getTeam`, `getTurn`, `getVariable`, `getWinner`, `gotoPhase`, `isOver`, `isStarted`, `log`, `perform`, `revealRole`, `roll`, `setResource`, `setRole`, `setTeam`, `setVariable`, `win` |
| `getAllObjects` | function |  |
| `getmetatable` | function |  |
| `getNotebookTabs` | function |  |
| `getObjectFromGUID` | function |  |
| `getObjects` | function |  |
| `getObjectsWithAllTags` | function |  |
| `getObjectsWithAnyTags` | function |  |
| `getObjectsWithTag` | function |  |
| `getSeatedPlayers` | function |  |
| `Global` | table | `call`, `Call`, `getGUID`, `getTable`, `getVar`, `setTable`, `setVar` |
| `Grid` | table |  |
| `ipairs` | function |  |
| `JSON` | table | `decode`, `encode`, `encode_pretty` |
| `json` | table | `isnull`, `null`, `parse`, `serialize` |
| `Lighting` | table | `apply`, `getAmbientEquatorColor`, `getAmbientGroundColor`, `getAmbientSkyColor`, `getLightColor`, `setAmbientEquatorColor`, `setAmbientGroundColor`, `setAmbientSkyColor`, `setLightColor`, `setRoomLight`, `setTableLight` |
| `log` | function |  |
| `MusicPlayer` | table | `back`, `getCurrentAudioclip`, `getPlaylist`, `pause`, `play`, `setCurrentAudioclip`, `setPlaylist`, `skip`, `skipBack`, `skipForward` |
| `next` | function |  |
| `Notes` | table | `addNotebookTab`, `editNotebookTab`, `getNotebookTabs`, `removeNotebookTab` |
| `pack` | function |  |
| `package` | table |  |
| `pairs` | function |  |
| `pcall` | function |  |
| `Physics` | table | `cast`, `getGravity`, `setGravity` |
| `Player` | table | `getAvailableColors`, `getColors`, `getPlayers` |
| `print` | function |  |
| `printToAll` | function |  |
| `printToColor` | function |  |
| `probe` | table |  |
| `rawequal` | function |  |
| `rawget` | function |  |
| `rawlen` | function |  |
| `rawset` | function |  |
| `removeNotebookTab` | function |  |
| `select` | function |  |
| `self` | table | `call`, `Call`, `getGUID`, `getTable`, `getVar`, `setTable`, `setVar` |
| `setmetatable` | function |  |
| `spawnObject` | function |  |
| `spawnObjectData` | function |  |
| `startLuaCoroutine` | function |  |
| `Timer` | table | `create`, `destroy` |
| `tonumber` | function |  |
| `tostring` | function |  |
| `Turns` | table | `getNextTurnColor`, `getPreviousTurnColor` |
| `type` | function |  |
| `UI` | table | `getAttribute`, `getAttributes`, `getCustomAssets`, `getValue`, `getXml`, `getXmlTable`, `hide`, `setAttribute`, `setAttributes`, `setCustomAssets`, `setValue`, `setXml`, `setXmlTable`, `show` |
| `unpack` | function |  |
| `Vector` | table |  |
| `Wait` | table | `condition`, `frames`, `stop`, `stopAll`, `time` |
| `xpcall` | function |  |

## Object members

On every object a script gets (`getObjectFromGUID`, `self`, event arguments). Functions are called
`obj.name(...)` or `obj:name(...)`; values are read as `obj.name`.

| Member | Kind |
|---|---|
| `AssetBundle` | value |
| `UI` | value |
| `addAttachment` | function |
| `addForce` | function |
| `addTag` | function |
| `addTorque` | function |
| `call` | function |
| `clearButtons` | function |
| `clearInputs` | function |
| `clone` | function |
| `createButton` | function |
| `createInput` | function |
| `deal` | function |
| `destroyObject` | function |
| `destruct` | function |
| `editButton` | function |
| `editInput` | function |
| `flip` | function |
| `getAttachments` | function |
| `getBounds` | function |
| `getBoundsNormalized` | function |
| `getButtons` | function |
| `getChildren` | function |
| `getColorTint` | function |
| `getComponent` | function |
| `getComponents` | function |
| `getCustomObject` | function |
| `getData` | function |
| `getDescription` | function |
| `getGMNotes` | function |
| `getInputs` | function |
| `getInvisibleTo` | function |
| `getJoints` | function |
| `getLock` | function |
| `getLoopingEffects` | function |
| `getLuaScript` | function |
| `getName` | function |
| `getObjects` | function |
| `getPosition` | function |
| `getQuantity` | function |
| `getRotation` | function |
| `getScale` | function |
| `getStateId` | function |
| `getStates` | function |
| `getTable` | function |
| `getTags` | function |
| `getTransformForward` | function |
| `getTransformRight` | function |
| `getTransformUp` | function |
| `getTriggerEffects` | function |
| `getValue` | function |
| `getVar` | function |
| `getVelocity` | function |
| `getZones` | function |
| `hasAnyTag` | function |
| `hasTag` | function |
| `highlightOff` | function |
| `highlightOn` | function |
| `interactable` | value |
| `isDestroyed` | function |
| `isSmoothMoving` | function |
| `is_face_down` | value |
| `jointTo` | function |
| `loading_custom` | value |
| `lock` | function |
| `mass` | value |
| `playLoopingEffect` | function |
| `playTriggerEffect` | function |
| `positionToLocal` | function |
| `positionToWorld` | function |
| `putObject` | function |
| `randomize` | function |
| `registerCollisions` | function |
| `reload` | function |
| `removeAttachments` | function |
| `removeButton` | function |
| `removeInput` | function |
| `removeTag` | function |
| `reset` | function |
| `resting` | value |
| `roll` | function |
| `scale` | function |
| `setAngularVelocity` | function |
| `setColorTint` | function |
| `setCustomObject` | function |
| `setDescription` | function |
| `setGMNotes` | function |
| `setInvisibleTo` | function |
| `setLock` | function |
| `setLuaScript` | function |
| `setName` | function |
| `setPosition` | function |
| `setPositionSmooth` | function |
| `setRotation` | function |
| `setRotationSmooth` | function |
| `setScale` | function |
| `setState` | function |
| `setTable` | function |
| `setTags` | function |
| `setValue` | function |
| `setVar` | function |
| `setVelocity` | function |
| `shuffle` | function |
| `spawning` | value |
| `takeObject` | function |
| `tooltip` | value |
| `type` | value |
| `unlock` | function |
| `unregisterCollisions` | function |

## Zone members

On scripting zones, hand zones and the other zones.

| Member | Kind |
|---|---|
| `call` | function |
| `destruct` | value |
| `getBounds` | function |
| `getBoundsNormalized` | function |
| `getGUID` | function |
| `getLuaScript` | function |
| `getName` | function |
| `getObjects` | function |
| `getPosition` | function |
| `getRotation` | function |
| `getScale` | function |
| `getTags` | function |
| `getVar` | function |
| `guid` | value |
| `hasTag` | function |
| `setPosition` | function |
| `setPositionSmooth` | function |
| `setRotation` | function |
| `setRotationSmooth` | function |
| `setScale` | function |
| `tag` | value |
| `type` | value |
| `UI` | value |
