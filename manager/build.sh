#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"

csc="$WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
[ -x "$csc" ] || csc="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
[ -x "$csc" ] || { echo "error: in-box C# compiler not found" >&2; exit 1; }

dotnet="/c/Program Files/dotnet/dotnet.exe"
[ -x "$dotnet" ] || { echo "error: .NET SDK not found at $dotnet" >&2; exit 1; }

mkdir -p build

echo "== tests =="
"$csc" -nologo -target:exe -warn:4 -warnaserror+ -reference:System.dll \
    -out:build/CraneManagerTests.exe \
    ManifestFile.cs ScriptHeader.cs StatusFile.cs FirstRun.cs ScriptRow.cs GameLaunch.cs IniFile.cs ClientRow.cs TestManifest.cs
./build/CraneManagerTests.exe | tail -3

echo "== CraneManager.exe =="
"$dotnet" build CraneManager.csproj -c Release -v quiet -nologo

echo "== startup selftest =="
selftest_dir="$(mktemp -d)"
(cd "$selftest_dir" && "$root/bin/Release/CraneManager.exe" --selftest)
rmdir "$selftest_dir"

output="bin/Release/CraneManager.exe"
size="$(( $(stat -c%s "$output") / 1024 ))"
echo "Built $output (${size} KB)"
echo "SHA-256: $(sha256sum "$output" | cut -d' ' -f1)"
