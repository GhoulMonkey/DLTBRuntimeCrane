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

**[Latest release](https://github.com/GhoulMonkey/DLTBRuntimeCrane/releases/latest)**

Also on Nexus Mods, at <https://www.nexusmods.com/dyinglightthebeast/mods/973>.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.

## Requirements

DLTBRuntimeCrane does nothing on its own. It requires DLTBRuntimeBridge
3.0.3-dev.2 or newer, which provides the ASI loader and the ABI 3 runtime:

<https://www.nexusmods.com/dyinglightthebeast/mods/969>

## What is here

The source for the two binaries in the download:

| | |
|---|---|
| `asi/` | `DLTBRuntimeCrane.asi`, the scripting host (C) |
| `manager/` | `CraneManager.exe`, the script manager and settings editor (C#, WPF) |
| `asi/vendor/` | Lua 5.4, unmodified, MIT licensed |
| `asi/include/` | the Bridge's public client headers, compiled against |
| `layout/` | the installed file tree, with the two build outputs missing |

`asi/vendor/` is Lua 5.4 as published, so it can be diffed against the release
from lua.org.

## Build

### DLTBRuntimeCrane.asi

Requires [LLVM-MinGW](https://github.com/mstorsjo/llvm-mingw) with clang
targeting `x86_64-w64-mingw32`, Python 3, and bash (Git Bash is sufficient).

```sh
cd asi
CLANG=/c/llvm-mingw/bin/x86_64-w64-mingw32-clang.exe ./build-asi.sh DLTBRuntimeCrane-2.0.0.asi
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
