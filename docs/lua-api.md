# Lua API (M5)

Scripts are TTS-style Lua run by `core/Scripting/ScriptHost` on the host only (ADR 0003). Names, argument orders and units
follow Tabletop Simulator: positions are in TTS units with the table surface at y = 0.96 (one unit = 3 cm,
`TtsUnits.TtsUnitsToMeters`), rotations in degrees. Methods work with `obj.method(...)` and `obj:method(...)`.

Where scripts live: the table's global script is `scripts["global"]`; an object's script is its `script` field; a zone's
(TTS scripting triggers have them) is the zone's `script`. Inside an object or zone script, `self` is that object or zone.
An object inside a bag or deck is not in play: its script starts when it comes out (and runs `onLoad`) and stops when it
goes back in, as in TTS. Scripts may move, scale and bag locked objects (`setPosition`, `setRotation`, `scale`, `putObject`); players cannot.

## Limits

A call still running after 5 seconds is stopped as an endless loop; `string.rep` builds at most 1 000 000 characters; scripts reacting to
their own changes stop 4 levels deep; `io`, `os` (except time), `load`, `dofile` and `require` do not exist. Errors are
logged on the host and never stop the game.

## Globals

`Vector(x, y, z)` / `Vector({ x, y, z })` and `Color(r, g, b[, a])` / `Color.Red` / `Color.fromHex('#ff0000')` work as in
TTS (operators `+ - * /`, `v[1]`, `copy`, `add`, `sub`, `scale`, `magnitude`, `normalized`, `distance`, `dot`, `cross`,
`angle`, `lerp`, `rotateOver`, `heading`; `toHex`, `toString`, `equals`, `lerp`), and positions, rotations and tints the
API returns are Vectors and Colors.

| Function | Does |
|---|---|
| `getObjectFromGUID(guid)` | the object or zone with that id, or nil; nil for an object inside a bag, as in TTS |
| `getObjects()`, `getAllObjects()` | every object on the table (not inside containers) |
| `getObjectsWithTag(tag)`, `getObjectsWithAllTags(tags)`, `getObjectsWithAnyTags(tags)` | objects on the table with those tags |
| `Timer.create({ identifier, function_name, function_owner, parameters, delay, repetitions })`, `Timer.destroy(identifier)` | TTS's older timers, on Wait's clock (`repetitions = 0` repeats forever) |
| `broadcastToAll(text)`, `printToAll(text)`, `broadcastToColor(text, color)`, `printToColor(text, color)` | a line in every player's chat (the colour variants also go to everyone for now) |
| `print(...)` | a line in the host's log |
| `JSON.encode(table)`, `JSON.decode(text)` | JSON text and Lua tables |
| `Wait.time(fn, seconds[, repetitions])` | calls `fn` later; `-1` repeats forever; returns an id |
| `Wait.frames(fn, frames)` | calls `fn` after that many frames (1/60 s each) |
| `Wait.condition(fn, condition[, timeout, on_timeout])` | calls `fn` once `condition()` is true (checked every frame); after `timeout` seconds without it, calls `on_timeout` instead (if given) and never `fn` |
| `Wait.stop(id)`, `Wait.stopAll()` | cancels |
| `Global.call(name, value)`, `Global.getVar(name)`, `Global.setVar(name, value)`, `Global.getTable`, `Global.setTable` | the global script's functions and variables; only plain values (numbers, strings, booleans, tables of them) cross between scripts |
| `startLuaCoroutine(owner, name)` | runs the owner's function a frame at a time (`coroutine.yield(0)` waits a frame) |
| `destroyObject(obj)` | `Delete` |
| `spawnObject({ type, position, rotation, scale, callback_function })` | a new object (`Spawn`, checked like a loaded table): `Die_4`…`Die_20`, `Custom_Token`, chips, `Custom_Tile`, `Custom_Board`, bags, `Notecard`, `Counter`, `BlockSquare`, `BlockRectangle`, checkers, `PlayerPawn`; the callback gets it on the next frame. Types needing custom files (`setCustomObject`) are not supported yet |
| `getSeatedPlayers()` | the seats' colours |
| `Player[color]`, `Player.Red`, `Player.getPlayers()`, `Player.getAvailableColors()` | players: `color`, `steam_name`, `seated`, `getHandObjects()`, `broadcast(text)`, `print(text)`; camera calls (`lookAt`) are accepted without effect |
| `getNotebookTabs()`, `addNotebookTab({title, body, color})`, `editNotebookTab({index, title, body, color})`, `removeNotebookTab(index)` | the notebook (G25); colour "Grey" is everyone's page |
| `log(...)`, `JSON.encode_pretty(table)` | host log; JSON |
| `MusicPlayer.*` | the table's music (G27): `play`, `pause`, `skip`/`back`, `getPlaylist`/`setPlaylist`, `getCurrentAudioclip`/`setCurrentAudioclip` (`{url, title}`), members `player_status` ("Ready", "Play", "Pause"), `playlist_index`/`playlistIndex`, `repeat_track`, `shuffle`, `loaded`; every machine plays it, the host moves to the next track |
| `Physics.cast({ origin, direction, type, size, orientation, max_distance })` | the host's physics: `type` 1 ray, 2 sphere, 3 box; with no distance or direction, what overlaps the shape. Returns `{ hit_object, point, normal, distance }`, nearest first, objects only |

## Objects

| Method | Does (command issued) |
|---|---|
| `getGUID()`, `guid`, `type` | its id and type |
| `getName()`, `setName(text)` | name (`SetProperties`) |
| `getDescription()`, `setDescription(text)` | description (`SetProperties`) |
| `getGMNotes()`, `setGMNotes(text)` | GM notes (host data, no command) |
| `getTags()`, `hasTag(tag)`, `addTag(tag)`, `removeTag(tag)`, `setTags(tags)` | tags (`SetProperties`) |
| `clone({ position })` | a copy, returned (`Clone`) |
| `deal(count[, color])` | deals from a deck to that seat or every seat (`Deal`) |
| `getStateId()` | the number of the state shown, -1 without states |
| `isDestroyed()` | true once it is gone |
| `setInvisibleTo(colors)`, `getInvisibleTo()` | colours that do not see it at all, on their machines (honest play, like hidden zones) |
| `getPosition()`, `setPosition(pos)`, `setPositionSmooth(pos)` | position in TTS units (`Move`) |
| `getRotation()`, `setRotation(rot)`, `setRotationSmooth(rot)` | rotation in degrees (`Move`) |
| `flip()` | `Flip` |
| `shuffle()`, `randomize()`, `roll()` | `Shuffle` / `Roll` |
| `lock()`, `unlock()`, `setLock(bool)`, `getLock()` | `Lock` |
| `is_face_down` | face down? (a function here) |
| `getValue()`, `setValue(n)` | a counter's number or a die's face (`SetCounter`, `SetDieValue`) |
| `getQuantity()` | cards in a deck, items in a bag, pieces in a stack; -1 otherwise |
| `getObjects()` | a deck's cards or a bag's items as `{ name, guid, index }`; on a zone, the objects in it |
| `takeObject({ position, rotation, guid, index, top, callback_function })` | takes from a deck, bag or stack and returns it (`Take`); `callback_function(obj)` runs on the next frame |
| `putObject(obj)` | puts another object into this one (`Insert`) |
| `destruct()` | `Delete` |
| `call(name, value)`, `getVar(name)`, `setVar(name, value)` | that object's script |
| `getLuaScript()`, `setLuaScript(source)` | its script; setting it reloads that script (`SetProperties`) |
| `getTable(name)`, `setTable(name, table)` | that object's script tables (copied) |
| `getColorTint()`, `setColorTint(color)` | tint (`SetProperties`) |
| `getScale()`, `setScale(scale)` | uniform scale from x (`Scale`) |
| `getBounds()`, `getBoundsNormalized()` | `{ center, size, offset }` of its colliders (normalized: as if unrotated); headless, from the footprint |
| `getTransformUp()`, `getTransformForward()`, `getTransformRight()` | its local axes in the world, unit length |
| `scale(n)` or `scale({x, y, z})` | multiplies its scale (scripts may scale, move and bag locked objects, as in TTS) |
| `resting` | false while it is held, falling, sliding or rolling, until the table has its settled pose |
| `getZones()` | zones it lies in |
| `positionToWorld(local)`, `positionToLocal(world)` | between its local axes and the table, TTS units |
| `setState(n)`, `getStates()` | switches state and returns the object in its new state (`SetState`) |
| `getCustomObject()`, `setCustomObject({ image, face, back, mesh, diffuse })` | its files, by web address (`SetCustom`; each machine fetches a new address itself, as in TTS). `spawnObject({ type = 'Custom_Model' })` then `setCustomObject` works; `reload()` is not needed |
| `isSmoothMoving()`, `reload()` | false; itself |
| `AssetBundle.playLoopingEffect(i)`, `playTriggerEffect(i)`, `getLoopingEffects()`, `getTriggerEffects()`, `getLoopingEffectIndex()`, and the same calls on the object | the bundle's effects (ADR 0008): a looping effect stays and is saved, a trigger plays once, for every player; the lists are `{ { index, name }, ... }`, empty while the bundle is not loaded |
| `getComponent(name).get / set`, `getComponents`, `getChildren` | accepted without effect (Unity components are not scripted) |
| `setVelocity`, `getVelocity`, `addForce`, `addTorque`, `highlightOn`, `highlightOff` | accepted, no effect yet (physics is not script-driven) |

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

### Inputs

`createInput(params)`, `editInput(params)` (with `index`), `removeInput(index)`, `clearInputs()`, `getInputs()`: the
button parameters plus `input_function`, `value`, `alignment` (1 auto, 2 left, 3 center, 4 right) and `validation`
(2 integer, 3 decimal, 4 letters and digits). A player clicks the field to type in an editor that opens at the pointer;
each change calls `input_function(obj, player_color, value, true)` and Enter (or clicking away) calls it with `false`.
The text is table state, so everyone sees it; a string the function returns becomes the text.

Zones (`getObjectFromGUID` of a zone, or `self` in a zone script): `getGUID()`, `guid`, `getName()`, `getObjects()`,
`getPosition()`, `getRotation()`, `getScale()` (the box in TTS units), `getBounds()`, `getBoundsNormalized()`, `getTags()`,
`hasTag(tag)`, `getLuaScript()`, `getVar(name)`, `call(name, value)`, and `UI` (drawn at the zone, in its frame).

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
| `onObjectEnterContainer(container, obj)`, `onObjectLeaveContainer(container, obj)` | an object went into or came out of a bag or deck |
| `onChat(message, player)` | a player wrote in chat; returning false keeps the line from being shown |
| own `onCollisionEnter(info)` / `onCollisionExit(info)`; `onObjectCollisionEnter(obj, info)` / `onObjectCollisionExit` for objects that called `registerCollisions()` | physics contacts on the host; `info` is `{ collision_object, contact_points, relative_velocity }` (`collision_object` nil for the table) |

## Turns and grid

`Turns` is free-play turns (`freeTurns` in the table): `enable`, `order` (colours), `reverse_order`,
`skip_empty_hands` and `turn_color` (setting it passes the turn) can be read and set; `type` (1 auto, 2 when `order` is
set), `pass_turns` (always true) and `disable_interactations` (always false, not enforced) are read only;
`getNextTurnColor()` and `getPreviousTurnColor()`. Changes send `SetTurnOrder`; a turn change runs `onPlayerTurn`.

`Grid` is the table grid: `type` (1 squares, 2 hexes), `show_lines`, `color`, `opacity`, `snapping` (1 off, 2 corners,
3 centres, 4 both), `offsetX`, `offsetY`, `sizeX`, `sizeY` in TTS units; `thick_lines` reads false.

## Object data, spawning and per-frame events

`obj.getData()` returns the object as our own table JSON (what a save writes), plus the TTS names scripts read and
edit: `GUID`, `Nickname`, `Description`, `GMNotes`. `spawnObjectData({ data, position, rotation, scale,
callback_function })` spawns a new object from such data (changes to those names apply) under a new id; a deck's cards
get new ids too. Hand-made TTS data (`Name = 'Custom_Model'`, ...) is not read yet: it spawns nothing and logs why.
Joints: `obj.jointTo(other, { type = "Fixed", collision = false })` holds an object to another so they move
together; `obj.jointTo()` frees it; `obj.getJoints()` lists it. Hinge and spring hold like fixed for now.
Attachments: `obj.addAttachment(other)` makes `other` part of `obj` (grabbing it takes `obj`), `obj.getAttachments()`
lists them and `obj.removeAttachments()` frees them and returns them.

A PDF's `obj.Book.getPage(offset)` and `obj.Book.setPage(page, offset)` read and turn its page for everyone (with
`offset` true, page numbers count from the PDF's page offset). `bag.reset()` empties a bag or infinite bag and keeps it. A layout zone lays out with `zone.LayoutZone.layout()`.

`onUpdate()` runs every frame on the host. `onObjectSpawn(object)` runs for every new object and `onObjectDestroy(object)`
for every one removed (a card joining a deck too); the destroyed object is already gone, so it offers only `getGUID()`.

Player input: the numpad's 1–9 and 0 are TTS's scripting buttons 1–10, heard by `onScriptingButtonDown(index, color)` and
`onScriptingButtonUp(index, color)`. A number typed over an object whose script defines `onNumberTyped(color, number,
alt)` goes to the host first; unless the function returns true, the host then does what typing does (switch state,
draw from a deck, take from a bag into the hand). `Player[color].getHoldingObjects()` lists what that player carries.
`onPlayerConnect(player)` runs when someone joins at a seat.

## Not yet (T6.3)

Typing into XmlUI `InputField`s drawn on objects, and scripted forces and velocities. The
coverage of the local mods is measured by `tools/tts-analysis/lua_coverage.py` (T6.4).
