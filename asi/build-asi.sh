#!/usr/bin/env bash
# Builds DLTBRuntimeCrane.asi with LLVM-MinGW (clang targeting x86_64-w64-mingw32).
#
# Set CLANG to the compiler if it is not on PATH, e.g.
#   CLANG=/c/llvm-mingw/bin/x86_64-w64-mingw32-clang.exe ./build-asi.sh
#
# Gates, in the order they are cheapest to fail:
#   1. pure-client   -- Crane reaches the game only through ABI 3
#   2. manifest tests -- the one hand-written parser of user-editable input
#   3. AllowWrites=0  -- the shipped default cannot invert unnoticed
#   4. the build itself, -Werror
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"
bridge="include"
clang="${CLANG:-x86_64-w64-mingw32-clang}"
output="${1:-DLTBRuntimeCrane-2.0.0.asi}"

command -v "$clang" >/dev/null 2>&1 || [ -x "$clang" ] || {
    echo "error: LLVM-MinGW clang not found: $clang (set CLANG)" >&2; exit 1; }
mkdir -p build

echo "== pure-client gate =="
python tools/validate_pure_client.py Crane.c

# Intermediates live beside the sources, never in %TEMP%: compiling into the
# temp directory and executing the result from the same process tree is a
# pattern some endpoint-protection products treat as a dropper.
echo "== manifest parser tests =="
"$clang" -O1 -std=c11 -Wall -Wextra -Werror -o build/test_manifest.exe tools/test_manifest.c
./build/test_manifest.exe | tail -2

# There is no shipped INI -- CraneManager generates it on first run -- so the
# AllowWrites=0 assertion lives in the manager's test suite, against
# FirstRun.IniTemplate, which is the text that actually reaches a user's disk.
echo "== AllowWrites default: asserted in manager/TestManifest.cs =="

echo "== DLTBRuntimeCrane.asi =="
lua_sources=()
for f in vendor/src/*.c; do
    case "$(basename "$f")" in
        lua.c|luac.c|lcorolib.c|ldblib.c|liolib.c|linit.c|loadlib.c|loslib.c) continue ;;
    esac
    lua_sources+=("$f")
done

"$clang" -O2 -std=c11 -Wall -Wextra -Werror -shared \
    -I "$bridge" -I vendor/src \
    -o "$output" Crane.c "${lua_sources[@]}" -lkernel32

size="$(( $(stat -c%s "$output") / 1024 ))"
echo "Built $output (${size} KB)"
echo "SHA-256: $(sha256sum "$output" | cut -d' ' -f1)"
