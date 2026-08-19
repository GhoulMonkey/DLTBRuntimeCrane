#!/usr/bin/env bash
# Builds CraneManager.exe and runs its tests.
#
# Two compilers:
#
#   - The tests are built with the in-box csc from .NET Framework 4.8. They cover
#     the logic layer only -- no WPF, no window -- so they need no SDK, and
#     keeping them buildable without one means the manifest and status readers can
#     be checked on any Windows machine.
#   - The app is built with the .NET SDK, because WPF needs XAML compilation.
#     Still targeting net48, so PresentationFramework and friends come from the
#     Windows install and users need nothing.
#
# Bash rather than PowerShell, and no heredocs anywhere, matching build-asi.sh.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"

csc="$WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
[ -x "$csc" ] || csc="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
[ -x "$csc" ] || { echo "error: in-box C# compiler not found" >&2; exit 1; }

dotnet="/c/Program Files/dotnet/dotnet.exe"
[ -x "$dotnet" ] || { echo "error: .NET SDK not found at $dotnet" >&2; exit 1; }

mkdir -p build

# The manager must never write a manifest DLTBRuntimeCrane.asi would refuse, and
# must read the status file the runtime writes. Both are asserted before the app
# is built, because a window that starts and then corrupts a manifest is worse
# than one that does not build.
echo "== tests =="
"$csc" -nologo -target:exe -warn:4 -warnaserror+ -reference:System.dll \
    -out:build/CraneManagerTests.exe \
    ManifestFile.cs ScriptHeader.cs StatusFile.cs FirstRun.cs ScriptRow.cs GameLaunch.cs IniFile.cs ClientRow.cs TestManifest.cs
./build/CraneManagerTests.exe | tail -3

echo "== CraneManager.exe =="
"$dotnet" build CraneManager.csproj -c Release -v quiet -nologo

# Startup check.
#
# The unit tests cover the logic layer and structurally cannot cover this:
# dev.21 shipped an exe that died on launch with a XamlParseException, because
# MainWindow.xaml referenced an icon that existed only as a Win32 resource and
# never in the assembly resource stream. That URI resolves at runtime, so 167
# passing tests and a clean compile said nothing about whether the window could
# be built at all.
#
# The selftest switch constructs MainWindow without showing it, which runs
# InitializeComponent and therefore every resource URI, style key and converter
# reference in the XAML.
#
# Run from a directory other than the project folder. A relative pack URI can
# otherwise resolve against the working directory and pass for the wrong reason;
# the first attempt to reproduce this bug came up clean for that reason.
echo "== startup selftest =="
selftest_dir="$(mktemp -d)"
(cd "$selftest_dir" && "$root/bin/Release/CraneManager.exe" --selftest)
rmdir "$selftest_dir"

output="bin/Release/CraneManager.exe"
size="$(( $(stat -c%s "$output") / 1024 ))"
echo "Built $output (${size} KB)"
echo "SHA-256: $(sha256sum "$output" | cut -d' ' -f1)"
