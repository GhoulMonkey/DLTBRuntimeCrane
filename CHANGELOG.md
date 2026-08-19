# Changelog

## 2.0.0

The Lua scripting host becomes a mod platform, and gains a manager, so nothing
here needs a text editor.

Drop a `.lua` file into `scripts\`, tick it in CraneManager, and it runs.
Scripts read and change game state through DLTBRuntimeBridge, so they get the
same arbitration every native client mod gets: values are leased, conflicts are
refused rather than silently overwritten, and everything a script held is put
back when it stops.

**Scripts that change the game are off by default.** Scripts may read, list and
observe until "Allow scripts to change the game" is turned on in Settings. That
switch takes effect without restarting, and turning it off releases everything
scripts are holding and restores what they changed.

- A script needs no cleanup code. Claim a value, write it, and DLTBRuntimeCrane
  restores
  the original when the script reloads, is disabled, is deleted, or the game
  exits.
- Order decides who wins. If two enabled scripts want the same value, the one
  higher in the list gets it and the other is refused with a message naming the
  holder.
- Runaway scripts are contained. An infinite loop is stopped by an instruction
  budget, the game keeps running, and the lease that script held is released.

**CraneManager**, a separate window deployed to `ph_ft`, manages all of it
without opening a file:

- tick a script on or off, reorder it, delete it, or drag a new one in;
- edit a script's settings as labelled controls, grouped, each with its range
  and a sentence explaining it, read from the script's own header comments;
- edit the settings of native Bridge client mods the same way, for any mod
  whose INI describes itself;
- DLTBRuntimeCrane's and DLTBRuntimeBridge's own settings, under Settings;
- see which scripts the game actually loaded, and whether it is running.

The script list and every tuned value live in files DLTBRuntimeCrane creates
rather than
files the archive ships, so a mod manager purge or redeploy cannot revert them.

**Quick Hands** ships with this, switched off. It shortens the hold time for
six
loot interaction classes, each separately toggleable, adjustable from 0 to 800
percent. It is a working mod and the worked example: read it to see how
parameters, groups, leases and restoration are written.

Requires DLTBRuntimeBridge 3.0.3 or newer.

### Upgrading from LuaHost 1.0.2

LuaHost is now DLTBRuntimeCrane, with its own page rather than an optional file
under
DLTBRuntimeBridge. The runtime is `DLTBRuntimeCrane.asi`; remove `LuaHost.asi`
along with `LuaHost.ini` and `LuaHost.manifest.json`, which are no longer read.
Scripts written for 1.0.2 keep working: the read-only API is unchanged and
everything new is additional.

## 1.0.2

- Adds `bridge.list(prefix)`, returning the Bridge state paths matching a
  prefix
  along with the true total match count. Scripts can discover paths instead of
  only confirming ones already known by exact spelling.
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
- Enforces a 100,000-instruction call budget and a 2 MiB interpreter memory
  cap.
- Does not expose game addresses, raw memory, file or process automation, state
  writes, leases, inventory actions, modifiers, or Survivor Sense mutations.
- Includes the Lua 5.4 MIT license required by the statically linked
  interpreter.
