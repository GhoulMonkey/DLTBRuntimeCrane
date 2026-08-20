// SPDX-License-Identifier: MIT
/* Syntax-only checker for the CRANE Script SDK.
 * Links the exact Lua parser sources used by CRANE, loads a chunk, never runs it.
 */
#include <stdio.h>
#include "lua.h"
#include "lauxlib.h"

int main(int argc, char **argv) {
    lua_State *state;
    int status;
    if (argc != 2) {
        fputs("usage: crane-lua-check <script.lua>\n", stderr);
        return 2;
    }
    state = luaL_newstate();
    if (!state) {
        fputs("crane-lua-check: could not create Lua state\n", stderr);
        return 2;
    }
    status = luaL_loadfile(state, argv[1]);
    if (status != LUA_OK) {
        const char *message = lua_tostring(state, -1);
        fprintf(stderr, "%s\n", message ? message : "Lua syntax check failed");
        lua_close(state);
        return 1;
    }
    lua_close(state);
    return 0;
}

