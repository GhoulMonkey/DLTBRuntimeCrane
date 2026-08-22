# DLTBRuntimeCrane

A Lua scripting host for *Dying Light: The Beast*, and a client of
DLTBRuntimeBridge.

DLTBRuntimeCrane runs plain Lua 5.4 scripts that reach the game only through
the Bridge's public API. It owns no game addresses, installs no hooks, and
performs no memory writes of its own; a build check enforces that on every
compile. Scripts are enabled, ordered and configured in CraneLoader, and the
Bridge's lease system handles conflicts between scripts and restores what they
changed.

Scripts cannot change game state at all until `AllowWrites=1` is set. The
shipped default is read-only.

## CraneLoader

`CraneLoader.exe` ships alongside the ASI. Scripts are added, enabled, ordered
and configured there. It reads each script's metadata header and builds the
settings from it, so a script does not need a UI or a configuration parser of
its own.

![The CraneLoader window. Installed scripts are listed on the left under Bridge
clients and Crane scripts. The settings for the selected script fill the panel
on the right.](docs/images/craneloader.png)

Native Bridge clients appear in the same list, so one window covers the Lua
scripts CRANE runs and the compiled mods loaded through the Bridge. Those mods
keep their own `.ini` files and the manager edits them in place. The capsule at
the top right reports whether the game is running, and edits to scripts and
settings are picked up without restarting it.

Settings holds CRANE's own keys and the Bridge's, read from and written back to
the same `.ini` files both already use.

![The CraneLoader settings window. The Crane section holds the Allow scripts to
change the game switch, with the DLTBRuntimeBridge section
below.](docs/images/craneloader-settings.png)

**Allow scripts to change the game** is `AllowWrites` in
`DLTBRuntimeCrane.ini`, and the shipped default is off. With it off, scripts may
only read. Switching it off while the game is running releases every lease and
modifier the scripts hold, and the Bridge restores the values it recorded.

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
| `manager/` | `CraneLoader.exe`, the script manager and settings editor (C#, WPF) |
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

### CraneLoader.exe

Requires .NET Framework 4.8, present on Windows 10 and 11, plus .NET SDK 6 or
newer for the XAML compile, and bash.

```sh
cd manager
./build.sh
```

The output is at `manager/bin/Release/CraneLoader.exe`. The build runs 183 unit
tests and a startup selftest that constructs the main window without showing it.

Both builds also run on every push here, in
[.github/workflows/build.yml](.github/workflows/build.yml).

## Installed layout

`layout/` is the file tree as installed, relative to the game folder. Copy the
two build outputs into it:

```
ph_ft/CraneLoader.exe                              from manager/bin/Release/
ph_ft/work/bin/x64/DLTBRuntimeCrane.asi             from asi/
ph_ft/work/bin/x64/LICENSE-Lua.txt                  included
ph_ft/work/bin/x64/scripts/quick_hands.lua          included
ph_ft/work/bin/x64/DLTBRuntimeCrane.ini             included, see below
ph_ft/work/bin/x64/DLTBRuntimeCrane.manifest.json   included, see below
```

The `.ini` and the `.manifest.json` are not in the released archive.
CraneLoader writes both on first run, from `FirstRun.cs`. The copies in
`layout/` are what it writes, so the tree can be assembled by hand. The manifest
is an empty list.

`AllowWrites=0` in the `.ini` is the shipped default. Scripts may only read
until it is set to 1.

## Writing scripts

`sdk/` is the authoring kit. A CRANE script is a single `.lua` file with a
metadata header; CraneLoader reads that header to build the settings a player
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

CRANE is published under two licenses: the GPL for the host, MIT for the
interface authors build against.

`DLTBRuntimeCrane.asi` and `CraneLoader.exe` — everything under `asi/`,
`manager/` and `tools/` — are under the GNU General Public License version 3.0
only. See `LICENSE`. Distributing a modified version means distributing its
source under the same terms; selling it is permitted.

These are MIT licensed instead, being what a script or client author copies:

| | |
|---|---|
| `sdk/` | the script authoring kit — `sdk/LICENSE` |
| `asi/include/` | the Bridge's public client headers — `asi/include/LICENSE` |
| `asi/examples/`, `asi/scripts/` | the example Lua scripts, and the bundled one |

A script or client built on CRANE is yours to license as you choose.

Every source file carries an `SPDX-License-Identifier` line naming which of the
two applies, so a file stays labelled once it is copied out of the tree.

The copyright holder grants that a script CRANE loads, and a client built
against the MIT-licensed Bridge headers, are not derivative works of the host
and carry no GPL obligation.

`asi/vendor/` is Lua 5.4 under its own MIT license, at
`asi/vendor/LICENSE.txt`.

Releases up to and including 2.1.1 were published under the MIT license, and
that grant still stands for anyone who received one. Later releases are under
the GPL.

The source for any released binary is the commit tagged with that version.
Release archives name the tag in `SOURCE-CRANE.txt`.
