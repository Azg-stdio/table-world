# Rules format

> Generated from `docs/rules-schema.v0.json` by `core.tests/Docs/DocsReferenceTests.cs`. Do not edit: run
> `TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference`.

The JSON document that holds a table and its rules. Every type of the schema, with its fields. For the ideas
behind it (phases, actions, effects, conditions) read [rules-schema.md](../rules-schema.md).

## Types

[TableState](#tablestate) · [Asset](#asset) · [TableObject](#tableobject) · [Card](#card) · [TableTransform](#tabletransform) · [JointDef](#jointdef) · [ButtonDef](#buttondef) · [InputDef](#inputdef) · [UiNode](#uinode) · [CardText](#cardtext) · [CardStyle](#cardstyle) · [Deck](#deck) · [DeckCard](#deckcard) · [Token](#token) · [Tile](#tile) · [Die](#die) · [Model](#model) · [Bag](#bag) · [InfiniteBag](#infinitebag) · [Board](#board) · [Figure](#figure) · [Counter](#counter) · [Clock](#clock) · [Notecard](#notecard) · [Pdf](#pdf) · [Zone](#zone) · [HandZone](#handzone) · [Effect](#effect) · [MoveEffect](#moveeffect) · [DealEffect](#dealeffect) · [ShuffleEffect](#shuffleeffect) · [FlipEffect](#flipeffect) · [SetVariableEffect](#setvariableeffect) · [SetResourceEffect](#setresourceeffect) · [IfEffect](#ifeffect) · [Condition](#condition) · [ZoneCountCondition](#zonecountcondition) · [CompareCondition](#comparecondition) · [VariableCondition](#variablecondition) · [ResourceCondition](#resourcecondition) · [IsCurrentPlayerCondition](#iscurrentplayercondition) · [ObjectInCondition](#objectincondition) · [HasTagCondition](#hastagcondition) · [ForEachEffect](#foreacheffect) · [MessageEffect](#messageeffect) · [EndTurnEffect](#endturneffect) · [EndPhaseEffect](#endphaseeffect) · [GotoPhaseEffect](#gotophaseeffect) · [WinEffect](#wineffect) · [LockEffect](#lockeffect) · [RollEffect](#rolleffect) · [RevealEffect](#revealeffect) · [LuaEffect](#luaeffect) · [RefuseEffect](#refuseeffect) · [BlindRoundEffect](#blindroundeffect) · [ShowEffect](#showeffect) · [HiddenZone](#hiddenzone) · [ScriptingZone](#scriptingzone) · [SnapPoint](#snappoint) · [LayoutZone](#layoutzone) · [RandomizeZone](#randomizezone) · [Players](#players) · [Seat](#seat) · [EnvironmentDef](#environmentdef) · [LightingDef](#lightingdef) · [TableLookDef](#tablelookdef) · [FreeTurns](#freeturns) · [BlindRound](#blindround) · [GridDef](#griddef) · [MusicState](#musicstate) · [MusicTrack](#musictrack) · [NoteTab](#notetab) · [DrawingDef](#drawingdef) · [GameInfo](#gameinfo) · [ResourceDef](#resourcedef) · [TurnDef](#turndef) · [PhaseDef](#phasedef) · [AdvanceRule](#advancerule) · [ActionDef](#actiondef) · [ParamDef](#paramdef) · [ParamFilter](#paramfilter) · [WinCondition](#wincondition) · [UiDef](#uidef) · [PanelDef](#paneldef) · [UiElementDef](#uielementdef) · [Session](#session) · [ZoneMove](#zonemove) · [SecretChoice](#secretchoice)

### TableState

| Field | Type | Required |
|---|---|---|
| `schemaVersion` | string |  |
| `assets` | array of [Asset](#asset) |  |
| `objects` | array of [TableObject](#tableobject) |  |
| `zones` | array of [Zone](#zone) |  |
| `players` | [Players](#players) |  |
| `environment` | [EnvironmentDef](#environmentdef) |  |
| `freeTurns` | [FreeTurns](#freeturns) |  |
| `physics` | one of `full`, `semiLock`, `locked` |  |
| `blind` | [BlindRound](#blindround) |  |
| `grid` | [GridDef](#griddef) |  |
| `music` | [MusicState](#musicstate) |  |
| `notebook` | array of [NoteTab](#notetab) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `drawings` | array of [DrawingDef](#drawingdef) |  |
| `game` | [GameInfo](#gameinfo) |  |
| `resources` | array of [ResourceDef](#resourcedef) |  |
| `variables` | map of any |  |
| `turn` | [TurnDef](#turndef) |  |
| `phases` | array of [PhaseDef](#phasedef) |  |
| `actions` | array of [ActionDef](#actiondef) |  |
| `events` | map of array of [Effect](#effect) |  |
| `winConditions` | array of [WinCondition](#wincondition) |  |
| `ui` | [UiDef](#uidef) |  |
| `globalScriptState` | string |  |
| `scripts` | map of string |  |
| `session` | [Session](#session) |  |

### Asset

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | one of `image`, `model`, `assetBundle`, `audio`, `pdf` |  |
| `path` | string |  |
| `hash` | string |  |
| `columns` | integer |  |
| `rows` | integer |  |
| `meshScale` | number |  |
| `url` | string |  |

### TableObject

[Card](#card) or [Deck](#deck) or [Token](#token) or [Tile](#tile) or [Die](#die) or [Model](#model) or [Bag](#bag) or [InfiniteBag](#infinitebag) or [Board](#board) or [Figure](#figure) or [Counter](#counter) or [Clock](#clock) or [Notecard](#notecard) or [Pdf](#pdf)

### Card

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `faceAsset` | string |  |
| `faceIndex` | integer |  |
| `backAsset` | string |  |
| `backIndex` | integer |  |
| `size` | array of number |  |
| `meta` | object |  |
| `text` | [CardText](#cardtext) |  |
| `style` | [CardStyle](#cardstyle) |  |

### TableTransform

| Field | Type | Required |
|---|---|---|
| `position` | array of number |  |
| `rotation` | array of number |  |
| `scale` | array of number |  |

### JointDef

| Field | Type | Required |
|---|---|---|
| `to` | string |  |
| `type` | one of `fixed`, `hinge`, `spring` |  |
| `collision` | boolean |  |
| `attached` | boolean |  |

### ButtonDef

| Field | Type | Required |
|---|---|---|
| `label` | string |  |
| `click` | string |  |
| `owner` | string |  |
| `position` | array of number |  |
| `rotation` | array of number |  |
| `width` | number |  |
| `height` | number |  |
| `fontSize` | number |  |
| `color` | array of number |  |
| `fontColor` | array of number |  |
| `tooltip` | string |  |

### InputDef

| Field | Type | Required |
|---|---|---|
| `label` | string |  |
| `value` | string |  |
| `inputFunction` | string |  |
| `owner` | string |  |
| `position` | array of number |  |
| `rotation` | array of number |  |
| `width` | number |  |
| `height` | number |  |
| `fontSize` | number |  |
| `color` | array of number |  |
| `fontColor` | array of number |  |
| `tooltip` | string |  |
| `alignment` | integer |  |
| `validation` | integer |  |

### UiNode

| Field | Type | Required |
|---|---|---|
| `tag` | string |  |
| `attributes` | map of string |  |
| `value` | string |  |
| `children` | array of [UiNode](#uinode) |  |
| `id` | string |  |
| `active` | boolean |  |

### CardText

| Field | Type | Required |
|---|---|---|
| `title` | string |  |
| `kind` | string |  |
| `body` | string |  |
| `footer` | string |  |
| `corner` | string |  |
| `art` | string |  |
| `artIndex` | integer |  |

### CardStyle

| Field | Type | Required |
|---|---|---|
| `paper` | array of number |  |
| `ink` | array of number |  |
| `accent` | array of number |  |
| `backPaper` | array of number |  |
| `backInk` | array of number |  |
| `backLabel` | string |  |

### Deck

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `faceAsset` | string |  |
| `backAsset` | string |  |
| `backIndex` | integer |  |
| `size` | array of number |  |
| `style` | [CardStyle](#cardstyle) |  |
| `cards` | array of [DeckCard](#deckcard) |  |

### DeckCard

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `faceAsset` | string |  |
| `faceIndex` | integer |  |
| `backAsset` | string |  |
| `backIndex` | integer |  |
| `meta` | object |  |
| `script` | string |  |
| `ttsExtra` | object |  |
| `text` | [CardText](#cardtext) |  |
| `style` | [CardStyle](#cardstyle) |  |

### Token

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `shape` | one of `round`, `square`, `image` |  |
| `thickness` | number |  |
| `size` | number |  |
| `stackable` | boolean |  |
| `count` | integer |  |

### Tile

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `shape` | one of `square`, `hex`, `circle`, `rounded` |  |
| `size` | array of number |  |
| `keepAspect` | boolean |  |
| `thickness` | number |  |
| `stackable` | boolean |  |
| `count` | integer |  |

### Die

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `faces` | integer |  |
| `labels` | array of string |  |
| `value` | integer |  |
| `asset` | string |  |

### Model

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `diffuseAsset` | string |  |
| `collider` | one of `convex`, `box`, `mesh` |  |
| `kind` | one of `generic`, `figurine`, `dice`, `coin`, `board`, `chip` |  |
| `material` | one of `plastic`, `wood`, `metal`, `cardboard`, `glass` |  |

### Bag

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `diffuseAsset` | string |  |

### InfiniteBag

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `diffuseAsset` | string |  |

### Board

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `size` | array of number |  |

### Figure

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `height` | number |  |

### Counter

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `value` | integer |  |

### Clock

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `mode` | one of `clock`, `stopwatch`, `timer` |  |
| `seconds` | number |  |
| `duration` | number |  |
| `running` | boolean |  |

### Notecard

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |

### Pdf

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `description` | string |  |
| `gmNotes` | string |  |
| `tags` | array of string |  |
| `invisibleTo` | array of string |  |
| `meshHidden` | boolean |  |
| `locked` | boolean |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `container` | string |  |
| `snap` | boolean |  |
| `tooltip` | boolean |  |
| `hands` | boolean |  |
| `hideWhenFaceDown` | boolean |  |
| `grid` | boolean |  |
| `tint` | array of number |  |
| `loopingEffect` | integer |  |
| `joint` | [JointDef](#jointdef) |  |
| `states` | map of [TableObject](#tableobject) |  |
| `script` | string |  |
| `scriptState` | string |  |
| `buttons` | array of [ButtonDef](#buttondef) |  |
| `inputs` | array of [InputDef](#inputdef) |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `ttsExtra` | object |  |
| `asset` | string |  |
| `page` | integer |  |
| `pageOffset` | integer |  |
| `size` | array of number |  |

### Zone

[HandZone](#handzone) or [HiddenZone](#hiddenzone) or [ScriptingZone](#scriptingzone) or [SnapPoint](#snappoint) or [LayoutZone](#layoutzone) or [RandomizeZone](#randomizezone)

### HandZone

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `tags` | array of string |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `size` | array of number |  |
| `parent` | string |  |
| `script` | string |  |
| `scriptState` | string |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `onEnter` | array of [Effect](#effect) |  |
| `diceTotal` | boolean |  |
| `onLeave` | array of [Effect](#effect) |  |
| `owner` | string |  |

### Effect

object or object or object or object or object or object or object or object or object or object or object or object or object or object or object or object or object or object or object or object

### MoveEffect

| Field | Type | Required |
|---|---|---|
| `object` | string |  |
| `from` | string |  |
| `take` | string |  |
| `count` | integer |  |
| `to` | string |  |
| `at` | string |  |
| `faceUp` | boolean |  |

### DealEffect

| Field | Type | Required |
|---|---|---|
| `from` | string |  |
| `count` | integer |  |
| `seats` | array of string |  |
| `toZone` | string |  |
| `toTag` | string |  |
| `faceDown` | boolean |  |

### ShuffleEffect

| Field | Type | Required |
|---|---|---|
| `object` | string |  |

### FlipEffect

| Field | Type | Required |
|---|---|---|
| `object` | string |  |

### SetVariableEffect

| Field | Type | Required |
|---|---|---|
| `name` | string |  |
| `value` | any |  |
| `delta` | number |  |

### SetResourceEffect

| Field | Type | Required |
|---|---|---|
| `player` | string |  |
| `resource` | string |  |
| `value` | any |  |
| `delta` | number |  |

### IfEffect

| Field | Type | Required |
|---|---|---|
| `condition` | [Condition](#condition) |  |
| `then` | array of [Effect](#effect) |  |
| `else` | array of [Effect](#effect) |  |

### Condition

object or object or object or object or object or object or object or object or object or object

### ZoneCountCondition

| Field | Type | Required |
|---|---|---|
| `zone` | string |  |
| `op` | string |  |
| `value` | any |  |

### CompareCondition

| Field | Type | Required |
|---|---|---|
| `left` | any |  |
| `op` | string |  |
| `right` | any |  |

### VariableCondition

| Field | Type | Required |
|---|---|---|
| `name` | string |  |
| `op` | string |  |
| `value` | any |  |

### ResourceCondition

| Field | Type | Required |
|---|---|---|
| `player` | string |  |
| `resource` | string |  |
| `op` | string |  |
| `value` | any |  |

### IsCurrentPlayerCondition

| Field | Type | Required |
|---|---|---|
| `player` | string |  |

### ObjectInCondition

| Field | Type | Required |
|---|---|---|
| `object` | string |  |
| `zone` | string |  |

### HasTagCondition

| Field | Type | Required |
|---|---|---|
| `object` | string |  |
| `tag` | string |  |

### ForEachEffect

| Field | Type | Required |
|---|---|---|
| `players` | boolean |  |
| `objectsIn` | string |  |
| `do` | array of [Effect](#effect) |  |

### MessageEffect

| Field | Type | Required |
|---|---|---|
| `text` | string |  |

### EndTurnEffect

| Field | Type | Required |
|---|---|---|

### EndPhaseEffect

| Field | Type | Required |
|---|---|---|

### GotoPhaseEffect

| Field | Type | Required |
|---|---|---|
| `phase` | string |  |

### WinEffect

| Field | Type | Required |
|---|---|---|
| `player` | string |  |

### LockEffect

| Field | Type | Required |
|---|---|---|
| `object` | string |  |
| `locked` | boolean |  |

### RollEffect

| Field | Type | Required |
|---|---|---|
| `object` | string |  |
| `store` | string |  |

### RevealEffect

| Field | Type | Required |
|---|---|---|
| `action` | string |  |

### LuaEffect

| Field | Type | Required |
|---|---|---|
| `call` | string |  |
| `args` | object |  |

### RefuseEffect

| Field | Type | Required |
|---|---|---|
| `reason` | string |  |

### BlindRoundEffect

| Field | Type | Required |
|---|---|---|
| `on` | boolean |  |

### ShowEffect

| Field | Type | Required |
|---|---|---|
| `object` | string |  |

### HiddenZone

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `tags` | array of string |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `size` | array of number |  |
| `parent` | string |  |
| `script` | string |  |
| `scriptState` | string |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `onEnter` | array of [Effect](#effect) |  |
| `diceTotal` | boolean |  |
| `onLeave` | array of [Effect](#effect) |  |
| `visibleTo` | array of string |  |

### ScriptingZone

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `tags` | array of string |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `size` | array of number |  |
| `parent` | string |  |
| `script` | string |  |
| `scriptState` | string |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `onEnter` | array of [Effect](#effect) |  |
| `diceTotal` | boolean |  |
| `onLeave` | array of [Effect](#effect) |  |

### SnapPoint

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `tags` | array of string |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `size` | array of number |  |
| `parent` | string |  |
| `script` | string |  |
| `scriptState` | string |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `onEnter` | array of [Effect](#effect) |  |
| `diceTotal` | boolean |  |
| `onLeave` | array of [Effect](#effect) |  |
| `rotationSnap` | boolean |  |

### LayoutZone

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `tags` | array of string |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `size` | array of number |  |
| `parent` | string |  |
| `script` | string |  |
| `scriptState` | string |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `onEnter` | array of [Effect](#effect) |  |
| `diceTotal` | boolean |  |
| `onLeave` | array of [Effect](#effect) |  |
| `direction` | one of `rightThenDown`, `downThenRight`, `leftThenDown`, `downThenLeft` |  |
| `maxPerRow` | integer |  |
| `spacing` | number |  |
| `facing` | one of `keep`, `faceUp`, `faceDown` |  |
| `manualOnly` | boolean |  |

### RandomizeZone

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `type` | any | yes |
| `name` | string |  |
| `tags` | array of string |  |
| `transform` | [TableTransform](#tabletransform) |  |
| `size` | array of number |  |
| `parent` | string |  |
| `script` | string |  |
| `scriptState` | string |  |
| `scriptUi` | array of [UiNode](#uinode) |  |
| `onEnter` | array of [Effect](#effect) |  |
| `diceTotal` | boolean |  |
| `onLeave` | array of [Effect](#effect) |  |

### Players

| Field | Type | Required |
|---|---|---|
| `seats` | array of [Seat](#seat) |  |

### Seat

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `color` | string |  |
| `handZone` | string |  |

### EnvironmentDef

| Field | Type | Required |
|---|---|---|
| `room` | one of `gameNight`, `none` |  |
| `roomAsset` | string |  |
| `skyAsset` | string |  |
| `wallColor` | array of number |  |
| `floorColor` | array of number |  |
| `lighting` | [LightingDef](#lightingdef) |  |
| `table` | [TableLookDef](#tablelookdef) |  |

### LightingDef

| Field | Type | Required |
|---|---|---|
| `ambientIntensity` | number |  |
| `ambientColor` | array of number |  |
| `lampIntensity` | number |  |
| `lampColor` | array of number |  |
| `tableLightIntensity` | number |  |
| `tableLightColor` | array of number |  |
| `lightScale` | number |  |
| `ambientScale` | number |  |
| `lightTint` | array of number |  |

### TableLookDef

| Field | Type | Required |
|---|---|---|
| `size` | array of number |  |
| `hidden` | boolean |  |
| `lowestY` | number |  |
| `feltColor` | array of number |  |
| `feltAsset` | string |  |
| `woodColor` | array of number |  |

### FreeTurns

| Field | Type | Required |
|---|---|---|
| `enabled` | boolean |  |
| `seats` | array of string |  |
| `current` | string |  |
| `reverse` | boolean |  |
| `skipEmptyHands` | boolean |  |
| `count` | integer |  |

### BlindRound

| Field | Type | Required |
|---|---|---|
| `movedBy` | map of string |  |
| `before` | map of array of string |  |

### GridDef

| Field | Type | Required |
|---|---|---|
| `shape` | one of `square`, `hex` |  |
| `lines` | boolean |  |
| `snapping` | one of `off`, `centers`, `corners`, `both` |  |
| `sizeX` | number |  |
| `sizeZ` | number |  |
| `offsetX` | number |  |
| `offsetZ` | number |  |
| `color` | array of number |  |
| `opacity` | number |  |
| `hexRow` | number |  |

### MusicState

| Field | Type | Required |
|---|---|---|
| `playlist` | array of [MusicTrack](#musictrack) |  |
| `current` | [MusicTrack](#musictrack) |  |
| `index` | integer |  |
| `playing` | boolean |  |
| `repeat` | boolean |  |
| `shuffle` | boolean |  |
| `starts` | integer |  |

### MusicTrack

| Field | Type | Required |
|---|---|---|
| `title` | string |  |
| `url` | string |  |

### NoteTab

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `title` | string |  |
| `body` | string |  |
| `color` | string |  |

### DrawingDef

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `points` | array of array of number |  |
| `color` | array of number |  |
| `thickness` | number |  |
| `arrow` | boolean |  |
| `seat` | string |  |

### GameInfo

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `name` | string |  |
| `description` | string |  |
| `minPlayers` | integer |  |
| `maxPlayers` | integer |  |

### ResourceDef

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `name` | string |  |
| `initial` | number |  |
| `min` | number |  |
| `max` | number |  |

### TurnDef

| Field | Type | Required |
|---|---|---|
| `order` | one of `clockwise`, `counterclockwise` |  |
| `startingPlayer` | string |  |
| `phases` | array of string |  |

### PhaseDef

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `name` | string |  |
| `actions` | array of string |  |
| `onEnter` | array of [Effect](#effect) |  |
| `onExit` | array of [Effect](#effect) |  |
| `advanceWhen` | [AdvanceRule](#advancerule) |  |

### AdvanceRule

| Field | Type | Required |
|---|---|---|
| `immediate` | boolean |  |
| `allPlayersActed` | boolean |  |
| `currentPlayerEnds` | boolean |  |
| `condition` | [Condition](#condition) |  |

### ActionDef

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `name` | string |  |
| `actor` | one of `current`, `any` |  |
| `params` | array of [ParamDef](#paramdef) |  |
| `preconditions` | array of [Condition](#condition) |  |
| `effects` | array of [Effect](#effect) |  |
| `limitPerPhase` | integer |  |
| `secret` | boolean |  |
| `button` | boolean |  |

### ParamDef

| Field | Type | Required |
|---|---|---|
| `name` | string |  |
| `type` | one of `object`, `seat`, `number`, `choice` |  |
| `filter` | [ParamFilter](#paramfilter) |  |
| `options` | array of string |  |

### ParamFilter

| Field | Type | Required |
|---|---|---|
| `inZone` | string |  |
| `type` | string |  |
| `hasTag` | string |  |

### WinCondition

| Field | Type | Required |
|---|---|---|
| `when` | [Condition](#condition) |  |
| `winner` | string |  |
| `checkAfter` | one of `anyCommand`, `turnEnd` |  |

### UiDef

| Field | Type | Required |
|---|---|---|
| `panels` | array of [PanelDef](#paneldef) |  |

### PanelDef

| Field | Type | Required |
|---|---|---|
| `id` | string |  |
| `title` | string |  |
| `anchor` | one of `topLeft`, `top`, `topRight`, `left`, `center`, `right`, `bottomLeft`, `bottom`, `bottomRight` |  |
| `visibleTo` | array of string |  |
| `elements` | array of [UiElementDef](#uielementdef) |  |

### UiElementDef

| Field | Type | Required |
|---|---|---|
| `type` | one of `label`, `counter`, `button`, `image` |  |
| `text` | string |  |
| `value` | string |  |
| `action` | string |  |
| `seat` | string |  |
| `params` | map of string |  |
| `asset` | string |  |

### Session

| Field | Type | Required |
|---|---|---|
| `started` | boolean |  |
| `over` | boolean |  |
| `winner` | string |  |
| `turn` | integer |  |
| `players` | array of string |  |
| `currentSeat` | string |  |
| `phase` | string |  |
| `acted` | array of string |  |
| `actionCounts` | map of integer |  |
| `variables` | map of any |  |
| `resources` | map of map of number |  |
| `heldZoneMoves` | array of [ZoneMove](#zonemove) |  |
| `secrets` | array of [SecretChoice](#secretchoice) |  |
| `log` | array of string |  |
| `randomState` | integer |  |

### ZoneMove

| Field | Type | Required |
|---|---|---|
| `object` | string |  |
| `zone` | string |  |
| `seat` | string |  |
| `enter` | boolean |  |

### SecretChoice

| Field | Type | Required |
|---|---|---|
| `seat` | string |  |
| `action` | string |  |
| `params` | map of string |  |
