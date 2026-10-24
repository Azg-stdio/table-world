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
| `Global.call(name, value)`, `Global.getVar(name)`, `Global.setVar(name, value)`, `Global.getTable`, `Global.setTable` | the global script's functions and variables; only plain values (numbers, strings, booleans, tables of them) cross between scripts |
| `startLuaCoroutine(owner, name)` | runs the owner's function a frame at a time (`coroutine.yield(0)` waits a frame) |
| `destroyObject(obj)` | `Delete` |
| `getSeatedPlayers()` | the seats' colours |
| `Player[color]`, `Player.Red`, `Player.getPlayers()`, `Player.getAvailableColors()` | players: `color`, `steam_name`, `seated`, `getHandObjects()`, `broadcast(text)`, `print(text)`; camera calls (`lookAt`) are accepted without effect |
| `getNotebookTabs()`, `addNotebookTab({title, body, color})`, `editNotebookTab({index, title, body, color})`, `removeNotebookTab(index)` | the notebook (G25); colour "Grey" is everyone's page |
| `log(...)`, `JSON.encode_pretty(table)` | host log; JSON |

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
| `getLuaScript()`, `setLuaScript(source)` | its script; setting it reloads that script (`SetProperties`) |
| `getTable(name)`, `setTable(name, table)` | that object's script tables (copied) |
| `getColorTint()`, `setColorTint(color)` | tint (`SetProperties`) |
| `getScale()`, `setScale(scale)` | uniform scale from x (`Scale`) |
| `getBounds()`, `getBoundsNormalized()` | `{ center, size, offset }` from the footprint |
| `getZones()` | zones it lies in |
| `positionToWorld(local)`, `positionToLocal(world)` | between its local axes and the table, TTS units |
| `setState(n)`, `getStates()` | switches state and returns the object in its new state (`SetState`) |
| `getCustomObject()` | its image or mesh paths |
| `isSmoothMoving()`, `reload()` | false; itself |
| `playLoopingEffect`, `playTriggerEffect`, `setVelocity`, `getVelocity`, `addForce`, `addTorque`, `highlightOn`, `highlightOff` | accepted, no effect yet (asset-bundle effects come with M4; physics is not script-driven) |

### Buttons

`createButton(params)`, `editButton(params)` (with `index`, 0-based; fields left out keep their value),
`removeButton(index)`, `clearButtons()`, `getButtons()` (nil when there are none). Parameters as in TTS: `label`,
`click_function`, `function_owner` (an object, or Global by default), `position` and `rotation` in the object's local
TTS units, `width`, `height`, `font_size` (about 500 per local unit: TTS's default 100 is 6 mm), `color`, `font_color`
(`{ r, g, b[, a] }`, `{ 1, 1, 1 }` or a player colour name), `tooltip`. A click calls
`click_function(obj, player_color, alt_click)` in the owner's script; a right click is the alt click. A button 0 wide or
high is a label only. Buttons are table state (every player sees and clicks them; only the host's scripts make them),
and scripts make them again in `onLoad`, so a loaded table drops the saved ones first.

*To check against TTS:* the 500-per-unit size and whether TTS mirrors a button's local x.

### Script UI (ADR 0004)

`UI` is the global screen UI; `obj.UI` the UI drawn on an object. Both have `setAttribute(id, name, value)`,
`setAttributes(id, table)`, `getAttribute(id, name)`, `getAttributes(id)`, `setValue(id, text)`, `getValue(id)`,
`show(id)`, `hide(id)`, `setXml(xml)`, `getXml()`, `setXmlTable(t)`, `getXmlTable()` (`{ tag, attributes, value,
children }`), `getCustomAssets()`; `setCustomAssets` is accepted without effect (images come with the table's assets).
Values are strings; booleans read `True` / `False` as in TTS. Elements are found by `id`, depth first.

Handlers named in the XML (`onClick="fn"`, `"Global/fn"`, `"<guid>/fn"`, `"fn(arg)"`) run as `fn(player, value, id)`:
`player` is `{ color, steam_name, seated, ... }`, `value` the argument in brackets, else the mouse button for clicks
(`"-1"` left, `"-2"` right, `"-3"` middle) or the new value of an input, toggle (`True` / `False`), slider or dropdown
(the option's text); `onEndEdit` fires when an input loses the keyboard. Without an owner, a global UI's handler is in
the global script and an object UI's in that object's.

Drawn elements: `Panel`, `Text`, `Image`, `Button`, `HorizontalLayout`, `VerticalLayout`, `GridLayout`, `TableLayout` /
`Row` / `Cell`, `InputField`, `Toggle`, `Slider`, `Dropdown` / `Option`, `ProgressBar`, scroll views. Layout: free
elements by `rectAlignment` + `offsetXY` (y up) with `width` / `height` in pixels or % (default: the whole parent) on a
1920 × 1080 canvas; in layout groups `width` / `height` are the preferred size and `childForceExpand*` (default true)
shares the rest. `visibility` (`Red|Blue|Host`) hides an element from other seats; `active` switches it.

UI on objects: each active top-level element is drawn lying on the object at its `position` (x y z in UI pixels,
100 per local TTS unit; z negative is up, x mirrored, so text reads upside down from the front unless rotated
`0 0 180`, as TTS mods do), with its `rotation` and `scale`. Clicks on objects reach `onClick`; inputs on objects show
their text but take no typing yet.

*To check against TTS:* the object UI frame and its 100 pixels per unit, Text's default alignment (upper left here), a clear Panel letting the pointer through (it does
here), the default colours of ProgressBar fills, how XmlUI scales with screen size (match 0.5 here). Not yet:
`allowDragging`, show / hide animations, tooltips, `Defaults` changed after load.

Zones (`getObjectFromGUID` of a zone): `getGUID()`, `getName()`, `getObjects()`, `getPosition()`, `hasTag(tag)`.

## Events

Defined in the global script or any object script; colours are seat colours capitalised (`"Red"`).

| Event | When |
|---|---|
| `onLoad(saved)` | after the table loads (global first, then objects); `saved` is what `onSave` returned last time, or "" |
| `onSave()` | before the table is saved: return a string to keep (TTS `script_state`) |
| `onObjectPickUp(color, obj)`; own `onPickUp(color)` | a player picked it up |
| `onObjectDrop(color, obj)`; in the object's script `onDrop(color)` | a player let go of the object |
| `onObjectEnterZone(zone, obj)`, `onObjectLeaveZone(zone, obj)` | an object entered or left a zone (also `onObjectEnterScriptingZone` / `onObjectLeaveScriptingZone` for scripting zones) |
| `onObjectFlip(color, obj)`; own `onFlip(color)` | flipped |
| `onObjectRandomize(obj, color)`; own `onRandomize(color)` | shuffled or rolled |
| `onPlayerTurn(color)` | free-play turns moved to that seat |

## Not yet (T6.3)

Typing into inputs drawn on objects, `createInput`, `Turns`, `spawnObject`, physics casts,
`setInvisibleTo`, joints, music, collision events. The coverage of the local mods is measured by
`tools/tts-analysis/lua_coverage.py` (T6.4).
