# DLTBRuntimeCrane

A Lua scripting host for *Dying Light: The Beast*, and a client of
DLTBRuntimeBridge.

DLTBRuntimeCrane runs plain Lua 5.4 scripts that reach the game only through
the Bridge's public API. It owns no game addresses, installs no hooks, and
performs no memory writes of its own; a build gate enforces that on every
compile. Scripts are enabled, ordered and configured in CraneManager, and the
Bridge's lease system handles conflicts between scripts and restores what they
changed.

Scripts cannot change game state at all until `AllowWrites=1` is set. The
shipped default is read-only.

## What is here

This repository is the source for the two binaries in the download:

| | |
|---|---|
| `asi/` | `DLTBRuntimeCrane.asi`, the scripting host (C) |
| `manager/` | `CraneManager.exe`, the script manager and settings editor (C#, WPF) |
| `asi/vendor/` | Lua 5.4, unmodified, MIT licensed |
| `asi/include/` | the Bridge's public client headers, compiled against |
| `layout/` | the installed file tree, with the two build outputs missing |

`BUILD.txt` has the build instructions. `asi/vendor/` is Lua 5.4 as published,
so it can be diffed against the release from lua.org.

## Requirements

DLTBRuntimeCrane does nothing on its own. It requires DLTBRuntimeBridge
3.0.3-dev.2 or newer, which provides the ASI loader and the ABI 3 runtime:

<https://www.nexusmods.com/dyinglightthebeast/mods/969>

## Downloads

The published mod is on Nexus Mods:

<https://www.nexusmods.com/dyinglightthebeast/mods/973>

Each release here carries the same archive, built from the tagged source by
GitHub Actions:

<https://github.com/GhoulMonkey/DLTBRuntimeCrane/releases/latest>

The two are not byte-identical, because the linker records the build path. The
Nexus file is the published one; this repository exists so the code can be read
and the binaries rebuilt by anyone who wants to.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.
