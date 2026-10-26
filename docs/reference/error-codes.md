# Error codes

> Generated from `ErrorCodes` in `core/Rules/ValidationError.cs` by `core.tests/Docs/DocsReferenceTests.cs`. Do not edit: run
> `TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference`.

Every refusal from a command, the validator or an MCP tool carries one of these codes. They never change
name: agents and tests match on them.

| Code | Constant |
|---|---|
| `action_not_available` | `ActionNotAvailable` |
| `container_cycle` | `ContainerCycle` |
| `container_empty` | `ContainerEmpty` |
| `duplicate_id` | `DuplicateId` |
| `game_already_started` | `GameAlreadyStarted` |
| `game_not_started` | `GameNotStarted` |
| `game_over` | `GameOver` |
| `host_only` | `HostOnly` |
| `index_out_of_range` | `IndexOutOfRange` |
| `invalid_container` | `InvalidContainer` |
| `invalid_die_faces` | `InvalidDieFaces` |
| `invalid_id` | `InvalidId` |
| `invalid_json` | `InvalidJson` |
| `invalid_operator` | `InvalidOperator` |
| `invalid_param` | `InvalidParam` |
| `invalid_path` | `InvalidPath` |
| `invalid_reference` | `InvalidReference` |
| `invalid_target` | `InvalidTarget` |
| `invalid_value` | `InvalidValue` |
| `limit_reached` | `LimitReached` |
| `missing_field` | `MissingField` |
| `missing_param` | `MissingParam` |
| `no_hand_zone` | `NoHandZone` |
| `no_rules` | `NoRules` |
| `not_on_table` | `NotOnTable` |
| `not_playing` | `NotPlaying` |
| `not_your_seat` | `NotYourSeat` |
| `not_your_turn` | `NotYourTurn` |
| `object_locked` | `ObjectLocked` |
| `player_count` | `PlayerCount` |
| `precondition_failed` | `PreconditionFailed` |
| `refused` | `Refused` |
| `rule_failed` | `RuleFailed` |
| `rules_loop` | `RulesLoop` |
| `script_failed` | `ScriptFailed` |
| `unknown_action` | `UnknownAction` |
| `unknown_asset` | `UnknownAsset` |
| `unknown_command` | `UnknownCommand` |
| `unknown_condition` | `UnknownCondition` |
| `unknown_container` | `UnknownContainer` |
| `unknown_effect` | `UnknownEffect` |
| `unknown_event` | `UnknownEvent` |
| `unknown_key` | `UnknownKey` |
| `unknown_object` | `UnknownObject` |
| `unknown_phase` | `UnknownPhase` |
| `unknown_resource` | `UnknownResource` |
| `unknown_seat` | `UnknownSeat` |
| `unknown_type` | `UnknownType` |
| `unknown_variable` | `UnknownVariable` |
| `unknown_zone` | `UnknownZone` |
| `unsupported_schema_version` | `UnsupportedSchemaVersion` |
| `wrong_asset_type` | `WrongAssetType` |
| `wrong_object_type` | `WrongObjectType` |
