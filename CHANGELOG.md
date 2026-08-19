# Changelog

## 2.1.0

- Adds `sdk/`, the script authoring kit: the Lua API reference, two templates,
  four worked examples, language-server annotations, the Bridge path and event
  catalog, and the validation and packaging tools.

## 2.0.0

First release under the DLTBRuntimeCrane name, continuing LuaHost 1.0.x.

## 1.0.2

- Adds `bridge.list(prefix)`, returning matching Bridge state paths and the
  total match count.
- Makes the reflected families enumerable from Lua, including the 3111 named
  PlayerVariables as `var.<Name>`.
- Requires the `operation:state.enumerate` Bridge feature.
- Listings past the 4096-record ceiling return what was retrieved, report the
  real total, and log the shortfall.

## 1.0.1

No changelog recorded.

## 1.0.0

First release, as the LuaHost optional file on the DLTBRuntimeBridge page.

- Read-only Lua 5.4 client: state queries, path descriptions, scoped event
  observation, merged logging, hot-reloaded scripts, and a named-pipe console.
- Caps scripts at 100,000 instructions per call and 2 MiB of interpreter memory.
- Includes the Lua 5.4 MIT license required by the statically linked
  interpreter.
