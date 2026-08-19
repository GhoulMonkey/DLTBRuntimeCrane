# CRANE Script SDK

Authoring kit for Lua mods that run through CRANE and DLTBRuntimeBridge.

This download is for script authors. Players installing CRANE mods do not need it.
It contains no game runtime, ASI, loader, native header, address or game file.

## Start here

1. Copy `templates/minimal` or `templates/configurable` into your own folder.
2. Rename the `.lua` file and edit its `@name`, `@description` and optional `@param` lines.
3. Open the SDK folder in an editor. `.luarc.json` and `library/crane.lua` provide
   Lua Language Server completion for the global `bridge` and `params` tables.
4. Validate:

   ```powershell
   .\tools\validate.ps1 .\MyMod.lua
   ```

5. Add the script with Crane Manager and test it. Start with script writes disabled.
6. Package a Vortex-ready archive:

   ```powershell
   .\tools\package.ps1 .\MyMod.lua -Version 1.0.0
   ```

The produced ZIP contains only `ph_ft/work/bin/x64/scripts/MyMod.lua`. Never bundle
CRANE, DLTBRuntimeBridge, an ASI loader, CRANE configuration or another author's files.

## Compatibility

- SDK: 2.0.0
- Target CRANE Lua API: 2.0.0
- Requires: CRANE 2.0.0 and DLTBRuntimeBridge 3.0.3 or newer
- Catalog source: DLTBRuntimeBridge ABI 3, released 3.0.3, for game 1.6.4
- Lua language: 5.4, with only base, table, string, math and UTF-8 libraries exposed

The bundled catalog is a development aid. The installed Bridge remains authoritative;
use `bridge.describe`, `bridge.list` and ordinary error handling at runtime.

Read `docs/QUICKSTART.md` next. `docs/CRANE-LUA-API.md` is the normative Lua reference.
