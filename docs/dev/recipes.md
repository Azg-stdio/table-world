# Recipes

Checklists for the changes that come up most. Each names the files in the order to touch them and the tests that
prove the change. Write the core test first (CLAUDE.md §4), and finish with the full suites:
`dotnet test TableWorld.sln` and `python -I tools/unity/playmode_tests.py`.

## Add a rules effect or condition

1. The data: a class in [Effects.cs](../../core/Rules/Effects.cs) (`…Effect : Effect` or `…Condition : Condition`) and
   its JSON name in `EffectKinds.ByName` (`ConditionKinds` for conditions, same file).
2. Validation: a `case` in [RulesValidator.cs](../../core/Rules/RulesValidator.cs) that checks its references and
   values, with an error code from `ErrorCodes` ([ValidationError.cs](../../core/Rules/ValidationError.cs)).
3. Meaning: a `case` in `GameEngine.Run` (effects) or `GameEngine.Holds` (conditions), in
   [GameEngine.cs](../../core/Engine/GameEngine.cs). Change the table only through `TableCommand(...)`, never by editing
   objects directly, so events and multiplayer stay right.
4. Tests: a rules game in `core.tests/Engine` that uses it, including the refusal when it is wrong.
5. Regenerate the schema (`TW_UPDATE_SCHEMA=1 dotnet test core.tests --filter SchemaFile`) and the references
   (`TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference`).
6. Document it in [rules-schema.md](../rules-schema.md): agents read that page through the MCP server's
   `get_rules_reference`, so an undocumented effect does not exist for them.

## Add an MCP tool

1. A static method with `[McpServerTool(Name = "verb_noun")]` and `[Description(...)]` on it and on every parameter, in
   [GameTools.cs](../../mcp-server/GameTools.cs) (authoring and play) or [TableTools.cs](../../mcp-server/TableTools.cs)
   (table actions). Board-game words only (CLAUDE.md principle 2).
2. It works on [GameSession.cs](../../mcp-server/GameSession.cs) and returns JSON with `ok`, and `errors` or `problems`.
3. A contract test in [McpContractTests.cs](../../mcp-server.tests/McpContractTests.cs) (the server is started over stdio).
4. Regenerate [reference/mcp-tools.md](../reference/mcp-tools.md):
   `TW_UPDATE_DOCS=1 dotnet test mcp-server.tests --filter DocsReference`.

## Add an object type

1. The class in [TableObjects.cs](../../core/Rules/TableObjects.cs) and its name in `TableObjectTypes.ByName`.
2. Its footprint in [Footprint.cs](../../core/Rules/Footprint.cs) (zones, layout, settling use it) and its checks in
   [TableValidator.cs](../../core/Rules/TableValidator.cs).
3. Schema and references regenerated (as above).
4. The view: a `case` in `ObjectViewFactory.Refresh`
   ([ObjectViewFactory.cs](../../unity/Assets/_Project/Scripts/View/ObjectViewFactory.cs)); flat image pieces use the
   `MeshFactory.Image*` shapes (ADR 0005).
5. TTS: the importer's mapping in `TtsImporter.Object` ([TtsImporter.cs](../../core/Tts/TtsImporter.cs)), its `tag` in
   `ScriptApi.TtsTag` and `spawnObject` in `ScriptApi.SpawnObject` ([ScriptApi.cs](../../core/Scripting/ScriptApi.cs)),
   a row in [tts-mapping.md](../tts-mapping.md).
6. One of it on the sample table (`tools/samples/make_sample_table.py`, then run it) so PlayMode tests and the
   default scene show it.

## Add a Lua API function

1. A global: in `ScriptApi.InstallApi`. An object member: a `case` in `ScriptApi.ObjectMethod` (made on first use); a
   value that changes (TTS member variable): `ScriptApi.LiveProperty`. Both accept `obj.f()` and `obj:f()` (the `Self`
   helper skips the object).
2. Change the table only through commands (`Do(new …Command { … })`), so the host orders it and clients get it.
3. A test in [ScriptCompatTests.cs](../../core.tests/Scripting/ScriptCompatTests.cs) that calls it as a TTS script would.
4. Regenerate [reference/lua-api.md](../reference/lua-api.md) (`DocsReference`), update [lua-api.md](../lua-api.md),
   and rerun `python -I tools/tts-analysis/lua_coverage.py` when it is a TTS function the local mods call.
5. On a real mod: `TW_MOD=<name> TW_TTS_DIR=<TTS folder> dotnet test core.tests --filter LocalModInteractionsTests`
   and read its report (errors, slow clicks).

## Add a HUD panel

1. Load the `unity:ui-uitk` skill first.
2. Elements in [Hud.uxml](../../unity/Assets/_Project/UI/Hud/Hud.uxml), styles in
   [Hud.uss](../../unity/Assets/_Project/UI/Hud/Hud.uss) only (no inline `style=`), a controller class in
   `Scripts/View/Hud/` created by [HudController.cs](../../unity/Assets/_Project/Scripts/View/Hud/HudController.cs).
3. Buttons send commands through `TableView.Execute` or `TableInteraction`, never change state themselves.
4. New UI symbols (a glyph the font lacks): add them to `HudBuilder.Characters()` and run
   *Table World → Rebuild UI Font Atlases*. Commit atlas growth; never revert it under a running Editor.
5. A PlayMode test that opens the panel and presses its buttons (see below).

## Write a PlayMode test or a Unity probe

- **PlayMode test**: a class in `unity/Assets/_Project/Tests/PlayMode/` that loads the `Table` scene in
  `[UnitySetUp]` and drives gestures through `TableInteraction` (`Grab`, `DragTo`, `Release` ...), like
  [ReconcileTests.cs](../../unity/Assets/_Project/Tests/PlayMode/ReconcileTests.cs). Run it only with
  `python -I tools/unity/playmode_tests.py --filter <Name>` (it refreshes, recompiles and watches the run).
- **Probe** (a read-only experiment in the running Editor): a `.cs` method body in [tools/unity-probes/](../../tools/unity-probes/)
  run with `unity command script eval_file --file <probe> --timeout 120`. Fully qualified names, no `using`, end with
  `return <string>`. A probe meant for Play mode starts with `if (!UnityEngine.Application.isPlaying) return "not playing";`.
