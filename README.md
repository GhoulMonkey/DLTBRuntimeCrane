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

## Downloads

You can download the latest release from here: <https://github.com/GhoulMonkey/DLTBRuntimeCrane/releases/latest>

Also on Nexus Mods, at <https://www.nexusmods.com/dyinglightthebeast/mods/973>.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.

## Requirements

DLTBRuntimeCrane requires DLTBRuntimeBridge, which provides the ASI loader and the ABI 3 runtime:

<https://www.nexusmods.com/dyinglightthebeast/mods/969>

## What is here

The source for the two binaries in the download, and the authoring kit:

| | |
|---|---|
| `asi/` | `DLTBRuntimeCrane.asi`, the scripting host (C) |
| `manager/` | `CraneManager.exe`, the script manager and settings editor (C#, WPF) |
| `asi/vendor/` | Lua 5.4, unmodified, MIT licensed |
| `asi/include/` | the Bridge's public client headers, compiled against |
| `layout/` | the installed file tree, with the two build outputs missing |
| `sdk/` | the script author's kit: Lua API reference, templates, examples, validator |

`asi/vendor/` is Lua 5.4 as published, so it can be diffed against the release
from lua.org.

## Build

### DLTBRuntimeCrane.asi

Requires [LLVM-MinGW](https://github.com/mstorsjo/llvm-mingw) with clang
targeting `x86_64-w64-mingw32`, Python 3, and bash (Git Bash is sufficient).

```sh
cd asi
CLANG=/c/llvm-mingw/bin/x86_64-w64-mingw32-clang.exe ./build-asi.sh DLTBRuntimeCrane-2.1.1.asi
```

Omit `CLANG=` if the compiler is on `PATH`. The build runs a gate asserting the
source reaches the game only through the Bridge ABI, then 95 manifest parser
checks, then compiles with `-Wall -Wextra -Werror`.

### CraneManager.exe

Requires .NET Framework 4.8, present on Windows 10 and 11, plus .NET SDK 6 or
newer for the XAML compile, and bash.

```sh
cd manager
./build.sh
```

The output is at `manager/bin/Release/CraneManager.exe`. The build runs 183 unit
tests and a startup selftest that constructs the main window without showing it.

Both builds also run on every push here, in
[.github/workflows/build.yml](.github/workflows/build.yml).

## Installed layout

`layout/` is the file tree as installed, relative to the game folder. Copy the
two build outputs into it:

```
ph_ft/CraneManager.exe                              from manager/bin/Release/
ph_ft/work/bin/x64/DLTBRuntimeCrane.asi             from asi/
ph_ft/work/bin/x64/LICENSE-Lua.txt                  included
ph_ft/work/bin/x64/scripts/quick_hands.lua          included
ph_ft/work/bin/x64/DLTBRuntimeCrane.ini             included, see below
ph_ft/work/bin/x64/DLTBRuntimeCrane.manifest.json   included, see below
```

The `.ini` and the `.manifest.json` are not in the released archive.
CraneManager writes both on first run, from `FirstRun.cs`. The copies in
`layout/` are what it writes, so the tree can be assembled by hand. The manifest
is an empty list.

`AllowWrites=0` in the `.ini` is the shipped default. Scripts may only read
until it is set to 1.

## Writing scripts

`sdk/` is the authoring kit. A CRANE script is a single `.lua` file with a
metadata header; CraneManager reads that header to build the settings a player
sees, and the script reaches the game through the global `bridge` table.

Start at [sdk/docs/QUICKSTART.md](sdk/docs/QUICKSTART.md), then
[sdk/docs/CRANE-LUA-API.md](sdk/docs/CRANE-LUA-API.md), which is the reference
for all 16 calls.

| | |
|---|---|
| `sdk/docs/` | quickstart, Lua API reference, Bridge concepts, metadata format, packaging, troubleshooting |
| `sdk/templates/` | a minimal script and a configurable one, to copy |
| `sdk/examples/` | one technique each: read state, observe an event, take an exclusive lease, stack a modifier |
| `sdk/library/` | annotations, so a Lua language server completes `bridge` and `params` |
| `sdk/schemas/` | the state paths and events this Bridge exposes, and the metadata schema |
| `sdk/tools/` | `validate.ps1` and `package.ps1` |

Validate a script before installing it:

```powershell
cd sdk
./tools/validate.ps1 ../MyMod.lua
```

The syntax check uses `sdk/tools/crane-lua-check.exe`, which the packaged SDK
ships prebuilt. Build it here with `sdk/tools-source/build-checker.sh`, using the
same LLVM-MinGW as the ASI, or install Lua 5.4 and `validate.ps1` falls back to
`luac -p`. Metadata and parameter validation run without either.

**The SDK is now published and included in the release.** It is still under development.
The catalog under `sdk/schemas/` is an authoring aid; the installed Bridge is
authoritative, and `bridge.describe` and `bridge.list` answer at runtime. Expect
paths to be added to it as development continues.

## License

Two licenses, split along one line: the program is GPL, and the interface you
build against is MIT.

`DLTBRuntimeCrane.asi` and `CraneManager.exe` — everything under `asi/`,
`manager/` and `tools/` — are licensed under the GNU General Public License
version 3.0 only. Fork it, repair it, improve it, sell it if you like; if you
distribute a modified version, its recipients get the same source and the same
rights. See `LICENSE`.

What a script or client author copies from is MIT licensed, so a mod built on
CRANE carries no obligation back:

| | |
|---|---|
| `sdk/` | the script authoring kit — `sdk/LICENSE` |
| `asi/include/` | the Bridge's public client headers — `asi/include/LICENSE` |
| `asi/examples/`, `asi/scripts/` | the example Lua scripts, and the bundled one |

Every source file carries an `SPDX-License-Identifier` line naming which of the
two applies, so a header or an example stays labelled after somebody copies it
out of the tree.

The copyright holder grants that a script CRANE loads, and a client built
against the MIT-licensed Bridge headers, are not derivative works of the host
and carry no GPL obligation. The GPL is here to keep forks of the host open,
and it stops there.

`asi/vendor/` is Lua 5.4 under its own MIT license, kept at
`asi/vendor/LICENSE.txt`.

Releases up to and including 2.1.1 were published under the MIT license. That
grant still stands for anyone who received one of those versions. Later
releases are under the GPL.

The source corresponding to any released binary is the commit tagged with that
version in this repository. Release archives name the tag in
`SOURCE-CRANE.txt`.
