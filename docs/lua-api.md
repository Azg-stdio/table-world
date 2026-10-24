# Lua API (M5)

Scripts are TTS-style Lua run by `core/Scripting/ScriptHost` on the host only (ADR 0003). Names, argument orders and units
follow Tabletop Simulator: positions are in TTS units with the table surface at y = 0.96 (one unit = 3 cm,
`TtsUnits.TtsUnitsToMeters`), rotations in degrees. Methods work with `obj.method(...)` and `obj:method(...)`.

Where scripts live: the table's global script is `scripts["global"]`; an object's script is its `script` field. Inside an
object script, `self` is that object.

## Limits

Each call runs at most 2 000 000 Lua instructions; `string.rep` builds at most 1 000 000 characters; scripts reacting to
their own changes stop 4 levels deep; `io`, `os` (except time), `load`, `dofile` and `require` do not exist. Errors are
logged on the host and never stop the game.

## Globals

| Function | Does |
|---|---|
| `getObjectFromGUID(guid)` | the object or zone with that id, or nil |
| `getObjects()`, `getAllObjects()` | every object on the table (not inside containers) |
| `getObjectsWithTag(tag)` | objects on the table with that tag |
| `broadcastToAll(text)`, `printToAll(text)`, `broadcastToColor(text, color)`, `printToColor(text, color)` | a line in every player's chat (the colour variants also go to everyone for now) |
| `print(...)` | a line in the host's log |
| `JSON.encode(table)`, `JSON.decode(text)` | JSON text and Lua tables |
| `Wait.time(fn, seconds[, repetitions])` | calls `fn` later; `-1` repeats forever; returns an id |
| `Wait.frames(fn, frames)` | calls `fn` after that many frames (1/60 s each) |
| `Wait.condition(fn, condition[, timeout])` | calls `fn` once `condition()` is true (checked every frame) |
| `Wait.stop(id)`, `Wait.stopAll()` | cancels |
| `Global.call(name, value)`, `Global.getVar(name)`, `Global.setVar(name, value)` | the global script's functions and variables; only plain values (numbers, strings, booleans, tables of them) cross between scripts |

## Objects

| Method | Does (command issued) |
|---|---|
| `getGUID()`, `guid`, `type` | its id and type |
| `getName()`, `setName(text)` | name (`SetProperties`) |
| `getDescription()`, `setDescription(text)` | description (`SetProperties`) |
| `getGMNotes()`, `setGMNotes(text)` | GM notes (host data, no command) |
| `getTags()`, `hasTag(tag)` | tags |
| `getPosition()`, `setPosition(pos)`, `setPositionSmooth(pos)` | position in TTS units (`Move`) |
| `getRotation()`, `setRotation(rot)`, `setRotationSmooth(rot)` | rotation in degrees (`Move`) |
| `flip()` | `Flip` |
| `shuffle()`, `randomize()`, `roll()` | `Shuffle` / `Roll` |
| `lock()`, `unlock()`, `setLock(bool)`, `getLock()` | `Lock` |
| `is_face_down` | face down? (a function here) |
| `getValue()`, `setValue(n)` | a counter's number or a die's face (`SetCounter`, `SetDieValue`) |
| `getQuantity()` | cards in a deck, items in a bag, pieces in a stack; -1 otherwise |
| `getObjects()` | a deck's cards or a bag's items as `{ name, guid, index }`; on a zone, the objects in it |
| `takeObject({ position, rotation, guid, index, top })` | takes from a deck, bag or stack and returns it (`Take`) |
| `putObject(obj)` | puts another object into this one (`Insert`) |
| `destruct()` | `Delete` |
| `call(name, value)`, `getVar(name)`, `setVar(name, value)` | that object's script |
| `getLuaScript()` | its script's source |

Zones (`getObjectFromGUID` of a zone): `getGUID()`, `getName()`, `getObjects()`, `getPosition()`, `hasTag(tag)`.

## Events

Defined in the global script or any object script; colours are seat colours capitalised (`"Red"`).

| Event | When |
|---|---|
| `onLoad()` | after the table loads (global first, then objects) |
| `onObjectDrop(color, obj)`; in the object's script `onDrop(color)` | a player let go of the object |
| `onObjectEnterZone(zone, obj)`, `onObjectLeaveZone(zone, obj)` | an object entered or left a zone |
| `onObjectFlip(color, obj)`; own `onFlip(color)` | flipped |
| `onObjectRandomize(obj, color)`; own `onRandomize(color)` | shuffled or rolled |
| `onPlayerTurn(color)` | free-play turns moved to that seat |

## Not yet (T6.3)

`UI.*` and XmlUI, `createButton` / `editButton` / inputs, `Player[color]`, `Turns`, `script_state` and `onSave`,
`spawnObject`, physics casts, `setInvisibleTo`, highlights, joints, music. The coverage of the local mods is measured by
`tools/tts-analysis/lua_coverage.py` (T6.4).
