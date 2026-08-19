#!/usr/bin/env bash
# Builds sdk/tools/crane-lua-check.exe, the Lua 5.4 syntax checker that
# tools/validate.ps1 uses.
#
# The packaged SDK ships this already built. This is a source tree, so here you
# build it, or install Lua 5.4 and let validate.ps1 fall back to luac -p, which
# checks the same grammar.
#
# Set CLANG if LLVM-MinGW is not on PATH, e.g.
#   CLANG=/c/llvm-mingw/bin/x86_64-w64-mingw32-clang.exe ./build-checker.sh
set -euo pipefail

sdk="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lua="$(cd "$sdk/../asi/vendor" && pwd)"
clang="${CLANG:-x86_64-w64-mingw32-clang}"

command -v "$clang" >/dev/null 2>&1 || [ -x "$clang" ] || {
    echo "error: LLVM-MinGW clang not found: $clang (set CLANG)" >&2; exit 1; }

# The interpreter, compiler and the libraries a syntax check never reaches.
# Parsing needs the core only, and leaving the rest out keeps the checker from
# carrying an io and os surface it has no use for.
sources=()
for file in "$lua"/src/*.c; do
    case "$(basename "$file")" in
        lua.c|luac.c|lcorolib.c|ldblib.c|liolib.c|linit.c|loadlib.c|loslib.c) continue ;;
    esac
    sources+=("$file")
done

mkdir -p "$sdk/tools" "$sdk/LICENSES"
"$clang" -O2 -std=c11 -Wall -Wextra -Werror \
    -I "$lua/src" \
    -o "$sdk/tools/crane-lua-check.exe" \
    "$sdk/tools-source/crane-lua-check.c" "${sources[@]}"
cp "$lua/LICENSE.txt" "$sdk/LICENSES/Lua-5.4.txt"
echo "Built $sdk/tools/crane-lua-check.exe"
