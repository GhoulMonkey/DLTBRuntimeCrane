#!/usr/bin/env bash
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

echo "== manifest parser tests =="
"$clang" -O1 -std=c11 -Wall -Wextra -Werror -o build/test_manifest.exe tools/test_manifest.c
./build/test_manifest.exe | tail -2

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
