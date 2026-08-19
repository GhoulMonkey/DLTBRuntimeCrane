# CRANE

A Lua scripting host for *Dying Light: The Beast*, and a client of
DLTBRuntimeBridge.

CRANE runs plain Lua 5.4 scripts that reach the game only through the Bridge's
public API. It owns no game addresses, installs no hooks, and performs no memory
writes of its own; a build gate enforces that on every compile. Scripts are
enabled, ordered and configured in CraneManager, and the Bridge's lease system
handles conflicts between scripts and restores what they changed.

Scripts cannot change game state at all until `AllowWrites=1` is set. The shipped
default is read-only.

## What is here

This repository is the source for the two binaries in the CRANE download:

| | |
|---|---|
| `asi/` | `DLTBRuntimeCrane.asi`, the scripting host (C) |
| `manager/` | `CraneManager.exe`, the script manager and settings editor (C#, WPF) |
| `asi/vendor/` | Lua 5.4, unmodified, MIT licensed |
| `asi/include/` | the Bridge's public client headers, which Crane compiles against |
| `layout/` | the installed file tree, with the two build outputs missing |

`BUILD.txt` has the build instructions.

Comments are stripped from `asi/` and `manager/`. `asi/vendor/` is Lua 5.4 as
published, so it can be diffed against the release from lua.org.

## Requirements

CRANE does nothing on its own. It requires DLTBRuntimeBridge 3.0.3-dev.2 or
newer, which provides the ASI loader and the ABI 3 runtime:

<https://www.nexusmods.com/dyinglightthebeast/mods/969>

## Downloads

The built mod is on Nexus Mods:

<https://www.nexusmods.com/dyinglightthebeast/mods/973>

This repository exists so the code can be read and the binaries rebuilt by
anyone who wants to.
