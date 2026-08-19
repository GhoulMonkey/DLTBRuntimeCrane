# Changelog

## 2.0.0

First release under the DLTBRuntimeCrane name, continuing LuaHost 1.0.x.

## 1.0.2

- Adds `bridge.list(prefix)`, returning the Bridge state paths matching a prefix
  along with the true total match count.
- Makes the reflected families enumerable from Lua, including the 3111 named
  PlayerVariables reachable as `var.<Name>`.
- Declares `operation:state.enumerate` as a required Bridge feature.
- A listing that exceeds the 4096-record ceiling returns the matches it
  retrieved, reports the real total as its second return value, and logs the
  shortfall.

## 1.0.1

No changelog recorded.

## 1.0.0

Released as the LuaHost optional file on the DLTBRuntimeBridge page.

- Adds a contained Lua 5.4 inspection client for DLTBRuntimeBridge 1.0.0 or
  newer.
- Exposes read-only Bridge state queries, path descriptions, scoped event
  observation, merged logging, hot-reloaded `startup.lua` scripts, and a
  one-line named-pipe console.
- Enforces a 100,000-instruction call budget and a 2 MiB interpreter memory cap.
- Does not expose game addresses, raw memory, file or process automation, state
  writes, leases, inventory actions, modifiers, or Survivor Sense mutations.
- Includes the Lua 5.4 MIT license required by the statically linked
  interpreter.
