# Reference (generated)

These pages are written by a test from the code and must not be edited by hand. Regenerate them with

```bash
TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference
TW_UPDATE_DOCS=1 dotnet test mcp-server.tests --filter DocsReference
```

and the same tests fail when a page no longer matches the code.

| Page | Generated from |
|---|---|
| [MCP tools](mcp-tools.md) | The server's `[McpServerTool]` methods: names, descriptions, parameters |
| [Error codes](error-codes.md) | `ErrorCodes` in core: every code a command or the validator can return |
| [Lua API](lua-api.md) | The globals and object methods the script host registers, with their TTS names |
| [Rules format](rules-format.md) | `docs/rules-schema.v0.json`, the JSON schema of tables and rules |
