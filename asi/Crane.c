/*
 * Crane -- optional Lua 5.4 developer client for DLTBRuntimeBridge ABI 3.
 *
 * This ASI owns no game address, hook or game-memory access.
 * Lua can touch the game only through the same typed, scoped ABI available to
 * native clients.  Commands and watched scripts run in the Bridge update
 * phase; event callbacks run in their declared Bridge delivery scope.
 *
 * 2.0.0 turns the single-script console into a small mod platform:
 *   - DLTBRuntimeCrane.manifest.json declares an ordered, individually enableable list
 *     of scripts instead of one startup.lua;
 *   - every subscription, lease and modifier is owned by the script that
 *     created it, and is released when that script reloads or is disabled;
 *   - writes exist, and are refused unless DLTBRuntimeCrane.ini opts in.
 *
 * Design notes for the parts that are not obvious:
 *
 * Handles are integers rather than userdata with a __gc finaliser.  A finaliser
 * runs at a time and in a scope the garbage collector chooses, and releasing a
 * Bridge lease from an arbitrary scope is the unverifiable mutation this client
 * is built to avoid.  Explicit release plus owner-driven cleanup keeps every
 * Bridge lifetime edge on a known thread in a known scope.
 *
 * The new operations are probed with DLTB_API3_DOMAIN_HAS at call time rather
 * than demanded in manifest.requires.  Writes are off by default, so a Bridge
 * without leases must still load a read-only Crane rather than refusing it.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"
#include "DLTBRuntimeBridgeClientKit.h"
#include "ManifestParse.h"

#define CRANE_VERSION "2.0.0"
#define CRANE_CLIENT_VERSION 20000
#define CRANE_MEMORY_LIMIT (2u * 1024u * 1024u)
#define CRANE_INSTRUCTION_BUDGET 100000
#define CRANE_MAX_COMMAND 4096
#define CRANE_MAX_SUBSCRIPTIONS 96
#define CRANE_MAX_SCRIPTS 64
#define CRANE_MAX_LEASES 64
/* Long enough for the longest state path the catalog exposes, with room. */
#define CRANE_MAX_PATH 128
#define CRANE_MAX_MODIFIERS 64
#define CRANE_MAX_NAME 128
#define CRANE_MAX_MANIFEST (256u * 1024u)
/*
 * Enumeration ceiling, in whole dltb_path_info records.
 *
 * Chosen to exceed the largest single family the Bridge currently publishes
 * (var.*, 3111 members) so an unfiltered listing of it is complete rather than
 * quietly clipped. The buffer is malloc'd for the duration of the call, not
 * taken from the Lua allocator, so it does not compete with the 2 MiB script
 * budget. A listing that still overflows returns the true total alongside the
 * names and logs the shortfall.
 */
#define CRANE_MAX_LIST 4096u

/* Owner id for anything claimed from the named pipe rather than by a script.
   Console-owned handles survive a script reload; they are released only at
   shutdown. */
#define CRANE_OWNER_CONSOLE (-1)

/*
 * read_manifest copies parsed entries straight into g_scripts, so the parser's
 * ceilings must not exceed this file's. Checked here rather than trusted:
 * raising one of the four constants alone would be a buffer overflow reachable
 * from an edited manifest, and it would compile silently.
 */
_Static_assert(CRANE_MANIFEST_MAX_SCRIPTS <= CRANE_MAX_SCRIPTS,
               "manifest script ceiling exceeds g_scripts capacity");
_Static_assert(CRANE_MANIFEST_MAX_NAME <= CRANE_MAX_NAME,
               "manifest name ceiling exceeds lua_host_script.file capacity");

DLTBCK_EMBED_BUILD_VERSION(CRANE_VERSION);

typedef struct lua_host_alloc { size_t used, limit; } lua_host_alloc;

typedef struct lua_host_subscription {
    int active;
    int owner;
    int function_ref;
    dltb_phase phase;
    dltb_subscription subscription;
} lua_host_subscription;

typedef struct lua_host_lease {
    int active;
    int owner;
    dltb_type type;
    dltb_lease lease;
    /*
     * The path, kept so a claim can be checked against the ones already held.
     *
     * The Bridge enforces exclusivity per CLIENT, and every script here runs
     * under one client handle, so from the Bridge's side two scripts claiming
     * one path look like the same owner claiming twice and both succeed. Per
     * SCRIPT exclusivity is Crane's job, and this is what makes it possible.
     */
    char path[CRANE_MAX_PATH];
} lua_host_lease;

typedef struct lua_host_modifier {
    int active;
    int owner;
    dltb_type type;
    dltb_modifier modifier;
} lua_host_modifier;

/*
 * What happened to a script on the last reload.
 *
 * Written out for CraneManager, which otherwise has no way to know: the manager
 * writes the manifest and this host reads it, and until now nothing flowed back.
 * Without it a script shows as ticked in the manager whether it ran or failed
 * on line 47.
 */
typedef enum crane_script_state {
    CRANE_STATE_DISABLED = 0,   /* listed, but switched off */
    CRANE_STATE_LOADED = 1,     /* ran to completion */
    CRANE_STATE_MISSING = 2,    /* listed but not in scripts\ */
    CRANE_STATE_FAILED = 3      /* syntax error, runtime error, budget stop */
} crane_script_state;

typedef struct lua_host_script {
    int enabled;
    char file[CRANE_MAX_NAME];
    crane_script_state state;
    char error[256];
    FILETIME write_time;
    manifest_param params[CRANE_MANIFEST_MAX_PARAMS];
    unsigned param_count;
} lua_host_script;

/* g_scripts mirrors the parser's parameter array verbatim, so the two must not
   drift apart -- same reasoning as the ceilings above, and it has to sit here
   rather than with them because it names a type declared in this file. */
_Static_assert(sizeof(((lua_host_script *)0)->params) ==
               sizeof(((manifest_entry *)0)->params),
               "script parameter storage does not match the parser's");

static HMODULE g_self;
static HANDLE g_stop_event;
static HANDLE g_worker;
static HANDLE g_pipe_worker;
static CRITICAL_SECTION g_lock;
static const dltb_api *g_api;
static dltb_client g_client;
static lua_State *g_lua;
static lua_host_alloc g_alloc = {0, CRANE_MEMORY_LIMIT};
static lua_host_subscription g_subscriptions[CRANE_MAX_SUBSCRIPTIONS];
static lua_host_lease g_leases[CRANE_MAX_LEASES];
static lua_host_modifier g_modifiers[CRANE_MAX_MODIFIERS];
static lua_host_script g_scripts[CRANE_MAX_SCRIPTS];
static unsigned g_script_count;
static int g_current_owner = CRANE_OWNER_CONSOLE;
static int g_allow_writes;
static int g_writes_state_announced;   /* so an unchanged state is not re-announced */
static wchar_t g_module_dir[MAX_PATH];
static wchar_t g_manifest_path[MAX_PATH];
static wchar_t g_status_path[MAX_PATH];
static wchar_t g_ini_path[MAX_PATH];
static wchar_t g_legacy_script[MAX_PATH];
static FILETIME g_manifest_write;
static int g_manifest_present;
static FILETIME g_ini_write;
static int g_ini_present;
static volatile LONG g_task_pending;
static volatile LONG g_reload_requested;
static volatile LONG g_settings_dirty;
static char g_command[CRANE_MAX_COMMAND];
static volatile LONG g_command_ready;

static void host_log(dltb_log_class kind, const char *format, ...) {
    char text[512];
    va_list args;
    if (!g_api || !g_client.id || !g_api->log) return;
    va_start(args, format);
    _vsnprintf_s(text, sizeof(text), _TRUNCATE, format, args);
    va_end(args);
    g_api->log->write(g_client, kind, text);
}

static void *host_alloc(void *ud, void *ptr, size_t old_size, size_t new_size) {
    lua_host_alloc *state = (lua_host_alloc *)ud;
    if (new_size == 0) {
        if (old_size <= state->used) state->used -= old_size;
        free(ptr);
        return NULL;
    }
    if (new_size > old_size && new_size - old_size > state->limit - state->used)
        return NULL;
    ptr = realloc(ptr, new_size);
    if (!ptr) return NULL;
    if (new_size >= old_size) state->used += new_size - old_size;
    else state->used -= old_size - new_size;
    return ptr;
}

static void instruction_limit(lua_State *L, lua_Debug *ar) {
    (void)ar;
    /*
     * %d, not %u. Lua's own formatter accepts %d, %s, %f, %p, %c, %U and %%, and
     * nothing else; %u made it raise "invalid option '%u' to 'lua_pushfstring'"
     * instead of saying what happened. Containment worked and reported itself as
     * a formatting bug, which is the kind of error that gets read as the mod
     * being broken.
     */
    luaL_error(L, "instruction budget exceeded (%d); callback stopped",
               (int)CRANE_INSTRUCTION_BUDGET);
}

/* `detail`, when given, receives the Lua error text. The log has always carried
   it; the status file needs it too, and re-deriving it from the log would mean
   parsing our own prose. */
static int protected_call_detail(int nargs, int nresults, const char *where,
                                 char *detail, size_t detail_bytes) {
    int status;
    lua_sethook(g_lua, instruction_limit, LUA_MASKCOUNT, CRANE_INSTRUCTION_BUDGET);
    status = lua_pcall(g_lua, nargs, nresults, 0);
    lua_sethook(g_lua, NULL, 0, 0);
    if (status != LUA_OK) {
        const char *message = lua_tostring(g_lua, -1);
        if (!message) message = "unknown error";
        host_log(DLTB_LOG_CLASS_ERROR, "%s: %s", where, message);
        if (detail && detail_bytes) strncpy_s(detail, detail_bytes, message, _TRUNCATE);
        lua_pop(g_lua, 1);
        return 0;
    }
    return 1;
}

static int protected_call(int nargs, int nresults, const char *where) {
    return protected_call_detail(nargs, nresults, where, NULL, 0);
}

static void push_status_error(dltb_status status) {
    lua_pushnil(g_lua);
    lua_pushstring(g_lua, dltb_status_text(status));
}

/* Name of the script that owns `owner`, for log lines. Console-owned work says
   so rather than pretending to be a script. */
static const char *owner_name(int owner) {
    if (owner == CRANE_OWNER_CONSOLE) return "<console>";
    if (owner < 0 || (unsigned)owner >= g_script_count) return "<unknown>";
    return g_scripts[owner].file;
}

/* ------------------------------------------------------------------ */
/* Settings                                                            */
/* ------------------------------------------------------------------ */

/*
 * DLTBRuntimeCrane.ini is new in 2.0.0 -- 1.0.2 shipped no INI at all. Absent file
 * means every default, which is the read-only 1.0.2 behaviour.
 */
/* Returns 1 when the write permission changed, which the caller must act on --
   see apply_settings_change. */
static int read_settings(void) {
    UINT allow = GetPrivateProfileIntW(L"Crane", L"AllowWrites", 0, g_ini_path);
    UINT level = GetPrivateProfileIntW(L"Crane", L"LogLevel", 0, g_ini_path);
    int previous = g_allow_writes;
    g_allow_writes = (allow != 0);
    if (level >= 1 && level <= 3 && g_api && g_api->log &&
        DLTB_API3_DOMAIN_HAS(g_api->log, dltb_ns_log, set_level))
        g_api->log->set_level(g_client, (dltb_log_level)level);
    else if (level != 0)
        host_log(DLTB_LOG_CLASS_WARN,
                 "LogLevel=%u is out of range (1-3); keeping the Bridge's level",
                 level);
    /*
     * Announced when the permission changes, rather than on every read.
     *
     * A raised bound has to announce itself, so a switch left on from a test
     * session cannot masquerade as normal behaviour later. But this runs on
     * every INI reload: repeating an unchanged state at INFO on each one made
     * the line easy to ignore, through startup and again at every session
     * transition. The first evaluation still announces, since that is a change
     * from "nothing said yet".
     */
    if (g_allow_writes != previous || !g_writes_state_announced) {
        g_writes_state_announced = 1;
        if (g_allow_writes)
            host_log(DLTB_LOG_CLASS_INFO,
                     "script writes are ENABLED by DLTBRuntimeCrane.ini AllowWrites=1; "
                     "scripts may change game state");
        else
            host_log(DLTB_LOG_CLASS_DEBUG,
                     "script writes are disabled (DLTBRuntimeCrane.ini AllowWrites=0)");
    }
    return g_allow_writes != previous;
}

static int require_writes(lua_State *L) {
    if (g_allow_writes) return 1;
    lua_pushnil(L);
    lua_pushliteral(L, "CRANE_WRITES_DISABLED");
    return 0;
}

/* ------------------------------------------------------------------ */
/* Manifest                                                            */
/* ------------------------------------------------------------------ */


/*
 * Reads the manifest, or falls back to the 1.0.2 layout when there is none so
 * an upgrade does not silently stop running an existing startup.lua.
 */
static void read_manifest(void) {
    HANDLE file;
    DWORD size, read = 0;
    char *text;

    memset(g_scripts, 0, sizeof(g_scripts));
    g_script_count = 0;

    file = CreateFileW(g_manifest_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                       NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        WIN32_FILE_ATTRIBUTE_DATA legacy;
        g_manifest_present = 0;
        if (GetFileAttributesExW(g_legacy_script, GetFileExInfoStandard, &legacy)) {
            g_scripts[0].enabled = 1;
            strncpy_s(g_scripts[0].file, sizeof(g_scripts[0].file), "startup.lua", _TRUNCATE);
            g_script_count = 1;
            host_log(DLTB_LOG_CLASS_DEBUG,
                     "no DLTBRuntimeCrane.manifest.json; running scripts\\startup.lua");
        } else {
            host_log(DLTB_LOG_CLASS_DEBUG,
                     "no DLTBRuntimeCrane.manifest.json and no scripts\\startup.lua; nothing to run");
        }
        return;
    }
    g_manifest_present = 1;
    size = GetFileSize(file, NULL);
    if (size == INVALID_FILE_SIZE || size > CRANE_MAX_MANIFEST) {
        CloseHandle(file);
        host_log(DLTB_LOG_CLASS_ERROR,
                 "DLTBRuntimeCrane.manifest.json is unreadable or larger than %u bytes",
                 CRANE_MAX_MANIFEST);
        return;
    }
    text = (char *)malloc((size_t)size + 1);
    if (!text) { CloseHandle(file); host_log(DLTB_LOG_CLASS_ERROR, "out of memory reading the manifest"); return; }
    if (!ReadFile(file, text, size, &read, NULL) || read != size) {
        CloseHandle(file); free(text);
        host_log(DLTB_LOG_CLASS_ERROR, "DLTBRuntimeCrane.manifest.json could not be read");
        return;
    }
    CloseHandle(file);
    text[read] = '\0';
    {
        /* Static rather than automatic. With parameters a manifest_result is
           well over a hundred kilobytes, which is more stack than a worker
           thread should be asked for. Safe as one instance: read_manifest only
           runs inside the update phase, under g_lock. */
        static manifest_result parsed;
        if (manifest_parse(text, read, &parsed)) {
            unsigned i;
            for (i = 0; i < parsed.count; ++i) {
                g_scripts[i].enabled = parsed.entries[i].enabled;
                strncpy_s(g_scripts[i].file, sizeof(g_scripts[i].file),
                          parsed.entries[i].file, _TRUNCATE);
                memcpy(g_scripts[i].params, parsed.entries[i].params,
                       sizeof(g_scripts[i].params));
                g_scripts[i].param_count = parsed.entries[i].param_count;
            }
            g_script_count = parsed.count;
            host_log(DLTB_LOG_CLASS_DEBUG, "manifest lists %u script(s)", g_script_count);
        } else {
            /* Name the line and the reason: a bare "manifest invalid" leaves
               three different fixes indistinguishable. */
            host_log(DLTB_LOG_CLASS_ERROR, "DLTBRuntimeCrane.manifest.json: %s", parsed.error);
            host_log(DLTB_LOG_CLASS_ERROR,
                     "no scripts were loaded; fix the manifest and save it to retry");
            g_script_count = 0;
        }
    }
    free(text);
}

/* ------------------------------------------------------------------ */
/* Ownership                                                           */
/* ------------------------------------------------------------------ */

/*
 * Release everything a script owns. This is what makes hot reload safe once
 * scripts can write: a lease the old copy claimed is restored by the Bridge
 * before the new copy runs, rather than being inherited by code that never
 * asked for it.
 */
static void release_owned(int owner) {
    unsigned i;
    unsigned leases = 0, modifiers = 0, subscriptions = 0;
    for (i = 0; i < CRANE_MAX_SUBSCRIPTIONS; ++i)
        if (g_subscriptions[i].active && g_subscriptions[i].owner == owner) {
            g_api->events->unsubscribe(g_client, g_subscriptions[i].subscription);
            luaL_unref(g_lua, LUA_REGISTRYINDEX, g_subscriptions[i].function_ref);
            memset(&g_subscriptions[i], 0, sizeof(g_subscriptions[i]));
            subscriptions++;
        }
    for (i = 0; i < CRANE_MAX_LEASES; ++i)
        if (g_leases[i].active && g_leases[i].owner == owner) {
            g_api->lease->release(g_leases[i].lease);
            memset(&g_leases[i], 0, sizeof(g_leases[i]));
            leases++;
        }
    for (i = 0; i < CRANE_MAX_MODIFIERS; ++i)
        if (g_modifiers[i].active && g_modifiers[i].owner == owner) {
            g_api->modifiers->release(g_modifiers[i].modifier);
            memset(&g_modifiers[i], 0, sizeof(g_modifiers[i]));
            modifiers++;
        }
    if (leases || modifiers || subscriptions)
        host_log(DLTB_LOG_CLASS_DEBUG,
                 "released %s: %u subscription(s), %u lease(s), %u modifier(s)",
                 owner_name(owner), subscriptions, leases, modifiers);
}

static void release_all_scripts(void) {
    unsigned i;
    for (i = 0; i < g_script_count; ++i) release_owned((int)i);
}

/* ------------------------------------------------------------------ */
/* Read surface                                                        */
/* ------------------------------------------------------------------ */

static dltb_subject subject_argument(lua_State *L, int index) {
    dltb_subject subject = DLTB_SUBJECT_NONE;
    if (!lua_isnoneornil(L, index))
        subject.id = (uint64_t)luaL_checkinteger(L, index);
    return subject;
}

static int push_value(lua_State *L, const dltb_value *value) {
    switch (value->type) {
    case DLTB_T_BOOL: lua_pushboolean(L, value->num.boolean); break;
    case DLTB_T_I32: lua_pushinteger(L, value->num.i32); break;
    case DLTB_T_F32: lua_pushnumber(L, value->num.f32); break;
    case DLTB_T_ENUM: lua_pushinteger(L, value->num.i32); break;
    case DLTB_T_STRING: lua_pushstring(L, value->text); break;
    default: lua_pushnil(L); lua_pushliteral(L, "unsupported value type"); return 2;
    }
    return 1;
}

/*
 * Coerce a Lua argument into the type the path actually declares, so a script
 * writing 1 to a float path does the obvious thing instead of being refused.
 */
static int value_from_lua(lua_State *L, int index, dltb_type type, dltb_value *out) {
    memset(out, 0, sizeof(*out));
    out->struct_bytes = sizeof(*out);
    out->type = type;
    switch (type) {
    case DLTB_T_BOOL:
        out->num.boolean = lua_toboolean(L, index) ? 1 : 0;
        return 1;
    case DLTB_T_I32:
    case DLTB_T_ENUM:
        if (!lua_isnumber(L, index)) return 0;
        out->num.i32 = (int32_t)lua_tointeger(L, index);
        return 1;
    case DLTB_T_F32:
        if (!lua_isnumber(L, index)) return 0;
        out->num.f32 = (float)lua_tonumber(L, index);
        return 1;
    case DLTB_T_STRING:
        if (!lua_isstring(L, index)) return 0;
        strncpy_s(out->text, sizeof(out->text), lua_tostring(L, index), _TRUNCATE);
        return 1;
    default:
        return 0;
    }
}

static int path_type(const char *path, dltb_type *out) {
    dltb_path_info info;
    memset(&info, 0, sizeof(info));
    info.struct_bytes = sizeof(info);
    if (g_api->state->describe(g_client, path, &info) != DLTB_OK) return 0;
    *out = info.type;
    return 1;
}

static int lua_bridge_scope(lua_State *L) {
    lua_pushinteger(L, g_api ? (lua_Integer)g_api->scope->current() : 0);
    return 1;
}

static int lua_bridge_log(lua_State *L) {
    const char *text = luaL_checkstring(L, 1);
    host_log(DLTB_LOG_CLASS_INFO, "lua: %s", text);
    return 0;
}

/*
 * bridge.resolve(path) -> subject id
 *
 * New in 2.0.0, and the reason subject-bound state stops being invisible.
 * Through 1.0.2 every hunger.*, player.* and flashlight.* path returned
 * DLTB_STALE_SUBJECT because the host passed DLTB_SUBJECT_NONE and bound no
 * resolve; those paths are also where most of the interesting writes live.
 */
static int lua_bridge_resolve(lua_State *L) {
    const char *path = luaL_checkstring(L, 1);
    dltb_subject subject = DLTB_SUBJECT_NONE;
    dltb_status status;
    if (!DLTB_API3_DOMAIN_HAS(g_api->state, dltb_ns_state, resolve)) {
        lua_pushnil(L);
        lua_pushliteral(L, "DLTB_UNSUPPORTED");
        return 2;
    }
    status = g_api->state->resolve(g_client, path, &subject);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_pushinteger(L, (lua_Integer)subject.id);
    return 1;
}

static int lua_bridge_get(lua_State *L) {
    const char *path = luaL_checkstring(L, 1);
    dltb_subject subject = subject_argument(L, 2);
    dltb_value value;
    dltb_status status;
    memset(&value, 0, sizeof(value)); value.struct_bytes = sizeof(value);
    status = g_api->state->read(g_client, path, subject, &value);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    return push_value(L, &value);
}

static int lua_bridge_describe(lua_State *L) {
    const char *path = luaL_checkstring(L, 1);
    dltb_path_info info;
    dltb_status status;
    memset(&info, 0, sizeof(info)); info.struct_bytes = sizeof(info);
    status = g_api->state->describe(g_client, path, &info);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_createtable(L, 0, 4);
    lua_pushinteger(L, info.type); lua_setfield(L, -2, "type");
    lua_pushinteger(L, info.access); lua_setfield(L, -2, "access");
    lua_pushinteger(L, info.scopes); lua_setfield(L, -2, "scopes");
    lua_pushinteger(L, info.tier); lua_setfield(L, -2, "tier");
    return 1;
}

/*
 * bridge.list(prefix) -> names, total
 *
 * This is for discovery. Without it a script can only confirm a path it
 * already knows the exact spelling of, which leaves the largest thing the
 * Bridge publishes -- 3111 reflected PlayerVariables -- invisible from inside
 * the inspection tool built to look at it.
 *
 * `names` is a plain array of path strings; per-path type/access/scope/tier
 * stay behind bridge.describe so a broad listing does not build thousands of
 * Lua tables. `total` is the Bridge's true match count, which is larger than
 * #names when the ceiling clipped the listing.
 */
static int lua_bridge_list(lua_State *L) {
    const char *prefix = luaL_optstring(L, 1, "");
    dltb_path_info *buffer;
    uint32_t total = 0;
    uint32_t written;
    uint32_t i;
    dltb_status status;
    if (!DLTB_API3_DOMAIN_HAS(g_api->state, dltb_ns_state, enumerate)) {
        lua_pushnil(L);
        lua_pushliteral(L, "DLTB_UNSUPPORTED");
        return 2;
    }
    buffer = (dltb_path_info *)calloc(CRANE_MAX_LIST, sizeof(*buffer));
    if (!buffer) {
        lua_pushnil(L);
        lua_pushliteral(L, "DLTB_NO_CAPACITY");
        return 2;
    }
    status = g_api->state->enumerate(g_client, prefix, buffer,
                                     (uint32_t)sizeof(*buffer),
                                     CRANE_MAX_LIST, &total);
    /* TRUNCATED is a short buffer, not a failed call: the records that did
       land are valid and the count is the real one. */
    if (status != DLTB_OK && status != DLTB_TRUNCATED) {
        free(buffer);
        push_status_error(status);
        return 2;
    }
    written = (total < CRANE_MAX_LIST) ? total : CRANE_MAX_LIST;
    lua_createtable(L, (int)written, 0);
    for (i = 0; i < written; ++i) {
        lua_pushstring(L, buffer[i].path);
        lua_rawseti(L, -2, (lua_Integer)i + 1);
    }
    free(buffer);
    if (total > written)
        host_log(DLTB_LOG_CLASS_WARN,
                 "list(\"%s\") returned %u of %u matches; narrow the prefix",
                 prefix, written, total);
    lua_pushinteger(L, (lua_Integer)total);
    return 2;
}

/* ------------------------------------------------------------------ */
/* Write surface                                                       */
/* ------------------------------------------------------------------ */

static int lua_bridge_set(lua_State *L) {
    const char *path = luaL_checkstring(L, 1);
    dltb_subject subject = subject_argument(L, 3);
    dltb_value value;
    dltb_type type;
    dltb_status status;
    if (!require_writes(L)) return 2;
    if (!path_type(path, &type)) {
        lua_pushnil(L);
        lua_pushliteral(L, "DLTB_UNKNOWN_PATH");
        return 2;
    }
    if (!value_from_lua(L, 2, type, &value)) {
        lua_pushnil(L);
        lua_pushliteral(L, "CRANE_WRONG_VALUE_TYPE");
        return 2;
    }
    status = g_api->state->set(g_client, path, subject, &value);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_pushboolean(L, 1);
    return 1;
}

static int lua_bridge_claim(lua_State *L) {
    const char *path = luaL_checkstring(L, 1);
    dltb_subject subject = subject_argument(L, 2);
    dltb_value baseline;
    dltb_type type;
    dltb_status status;
    unsigned slot;
    if (!require_writes(L)) return 2;
    if (!DLTB_API3_DOMAIN_HAS(g_api->lease, dltb_ns_lease, claim)) {
        lua_pushnil(L); lua_pushliteral(L, "DLTB_UNSUPPORTED"); return 2;
    }
    if (!path_type(path, &type)) {
        lua_pushnil(L); lua_pushliteral(L, "DLTB_UNKNOWN_PATH"); return 2;
    }
    /*
     * Exclusivity between scripts, enforced here because the Bridge cannot.
     *
     * Leases are exclusive per path, and the manager's Move up/Move down exists
     * to decide who wins when two scripts want the same one. That promise was
     * never kept: the Bridge keys ownership on the client handle, all scripts
     * share one, and so the second claimant was granted the path and quietly
     * overwrote the first. The live gate on 2026-08-18 caught it, with both
     * scripts reporting success and the LOWER one winning.
     *
     * Checked before the Bridge call so a refusal costs nothing and leaves no
     * lease to unwind. The refusal names both scripts, because "refused" without
     * saying who holds it sends the user to the wrong file.
     */
    for (slot = 0; slot < CRANE_MAX_LEASES; ++slot) {
        if (!g_leases[slot].active) continue;
        if (g_leases[slot].owner == g_current_owner) continue;
        if (strcmp(g_leases[slot].path, path) != 0) continue;
        host_log(DLTB_LOG_CLASS_INFO,
                 "%s refused %s: already held by %s, which is higher in the list",
                 owner_name(g_current_owner), path,
                 owner_name(g_leases[slot].owner));
        lua_pushnil(L); lua_pushliteral(L, "CRANE_PATH_OWNED"); return 2;
    }

    for (slot = 0; slot < CRANE_MAX_LEASES && g_leases[slot].active; ++slot) {}
    if (slot == CRANE_MAX_LEASES) {
        lua_pushnil(L); lua_pushliteral(L, "CRANE_LEASE_CAPACITY"); return 2;
    }
    memset(&baseline, 0, sizeof(baseline)); baseline.struct_bytes = sizeof(baseline);
    status = g_api->lease->claim(g_client, path, subject, &g_leases[slot].lease, &baseline);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    g_leases[slot].active = 1;
    g_leases[slot].owner = g_current_owner;
    g_leases[slot].type = type;
    _snprintf_s(g_leases[slot].path, sizeof(g_leases[slot].path), _TRUNCATE,
                "%s", path);
    host_log(DLTB_LOG_CLASS_DEBUG, "%s claimed a lease on %s",
             owner_name(g_current_owner), path);
    lua_pushinteger(L, (lua_Integer)slot + 1);
    return 1;
}

/*
 * Handles are validated against the caller's ownership on every use, so one
 * script cannot write through another's lease by guessing an integer.
 */
static lua_host_lease *lease_argument(lua_State *L, int index) {
    lua_Integer handle = luaL_checkinteger(L, index);
    lua_host_lease *entry;
    if (handle < 1 || handle > CRANE_MAX_LEASES) return NULL;
    entry = &g_leases[handle - 1];
    if (!entry->active) return NULL;
    if (entry->owner != g_current_owner) return NULL;
    return entry;
}

static int lua_bridge_lease_write(lua_State *L) {
    lua_host_lease *entry;
    dltb_value value;
    dltb_status status;
    if (!require_writes(L)) return 2;
    entry = lease_argument(L, 1);
    if (!entry) { lua_pushnil(L); lua_pushliteral(L, "CRANE_BAD_LEASE"); return 2; }
    if (!value_from_lua(L, 2, entry->type, &value)) {
        lua_pushnil(L); lua_pushliteral(L, "CRANE_WRONG_VALUE_TYPE"); return 2;
    }
    status = g_api->lease->write(entry->lease, &value);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_pushboolean(L, 1);
    return 1;
}

static int lua_bridge_lease_baseline(lua_State *L) {
    lua_host_lease *entry = lease_argument(L, 1);
    dltb_value baseline;
    dltb_status status;
    if (!entry) { lua_pushnil(L); lua_pushliteral(L, "CRANE_BAD_LEASE"); return 2; }
    if (!DLTB_API3_DOMAIN_HAS(g_api->lease, dltb_ns_lease, baseline)) {
        lua_pushnil(L); lua_pushliteral(L, "DLTB_UNSUPPORTED"); return 2;
    }
    memset(&baseline, 0, sizeof(baseline)); baseline.struct_bytes = sizeof(baseline);
    status = g_api->lease->baseline(entry->lease, &baseline);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    return push_value(L, &baseline);
}

static int lua_bridge_lease_release(lua_State *L) {
    lua_host_lease *entry = lease_argument(L, 1);
    dltb_status status;
    if (!entry) { lua_pushnil(L); lua_pushliteral(L, "CRANE_BAD_LEASE"); return 2; }
    status = g_api->lease->release(entry->lease);
    memset(entry, 0, sizeof(*entry));
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_pushboolean(L, 1);
    return 1;
}

static int lua_bridge_modifier_acquire(lua_State *L) {
    const char *path = luaL_checkstring(L, 1);
    dltb_subject subject = subject_argument(L, 2);
    dltb_type type;
    dltb_status status;
    unsigned slot;
    if (!require_writes(L)) return 2;
    if (!DLTB_API3_DOMAIN_HAS(g_api->modifiers, dltb_ns_modifiers, acquire)) {
        lua_pushnil(L); lua_pushliteral(L, "DLTB_UNSUPPORTED"); return 2;
    }
    if (!path_type(path, &type)) {
        lua_pushnil(L); lua_pushliteral(L, "DLTB_UNKNOWN_PATH"); return 2;
    }
    for (slot = 0; slot < CRANE_MAX_MODIFIERS && g_modifiers[slot].active; ++slot) {}
    if (slot == CRANE_MAX_MODIFIERS) {
        lua_pushnil(L); lua_pushliteral(L, "CRANE_MODIFIER_CAPACITY"); return 2;
    }
    status = g_api->modifiers->acquire(g_client, path, subject, &g_modifiers[slot].modifier);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    g_modifiers[slot].active = 1;
    g_modifiers[slot].owner = g_current_owner;
    g_modifiers[slot].type = type;
    host_log(DLTB_LOG_CLASS_DEBUG, "%s acquired a modifier on %s",
             owner_name(g_current_owner), path);
    lua_pushinteger(L, (lua_Integer)slot + 1);
    return 1;
}

static lua_host_modifier *modifier_argument(lua_State *L, int index) {
    lua_Integer handle = luaL_checkinteger(L, index);
    lua_host_modifier *entry;
    if (handle < 1 || handle > CRANE_MAX_MODIFIERS) return NULL;
    entry = &g_modifiers[handle - 1];
    if (!entry->active) return NULL;
    if (entry->owner != g_current_owner) return NULL;
    return entry;
}

static int lua_bridge_modifier_write(lua_State *L) {
    lua_host_modifier *entry;
    dltb_value value;
    dltb_status status;
    if (!require_writes(L)) return 2;
    entry = modifier_argument(L, 1);
    if (!entry) { lua_pushnil(L); lua_pushliteral(L, "CRANE_BAD_MODIFIER"); return 2; }
    if (!value_from_lua(L, 2, entry->type, &value)) {
        lua_pushnil(L); lua_pushliteral(L, "CRANE_WRONG_VALUE_TYPE"); return 2;
    }
    status = g_api->modifiers->write(entry->modifier, &value);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_pushboolean(L, 1);
    return 1;
}

/* Returns this modifier's own contribution and the combined effective value,
   which is the only way a script can see what the other contributions did. */
static int lua_bridge_modifier_read(lua_State *L) {
    lua_host_modifier *entry = modifier_argument(L, 1);
    dltb_value contribution, effective;
    dltb_status status;
    int pushed;
    if (!entry) { lua_pushnil(L); lua_pushliteral(L, "CRANE_BAD_MODIFIER"); return 2; }
    memset(&contribution, 0, sizeof(contribution)); contribution.struct_bytes = sizeof(contribution);
    memset(&effective, 0, sizeof(effective)); effective.struct_bytes = sizeof(effective);
    status = g_api->modifiers->read(entry->modifier, &contribution, &effective);
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    pushed = push_value(L, &contribution);
    if (pushed != 1) return pushed;
    return 1 + push_value(L, &effective);
}

static int lua_bridge_modifier_release(lua_State *L) {
    lua_host_modifier *entry = modifier_argument(L, 1);
    dltb_status status;
    if (!entry) { lua_pushnil(L); lua_pushliteral(L, "CRANE_BAD_MODIFIER"); return 2; }
    status = g_api->modifiers->release(entry->modifier);
    memset(entry, 0, sizeof(*entry));
    if (status != DLTB_OK) { push_status_error(status); return 2; }
    lua_pushboolean(L, 1);
    return 1;
}

/* ------------------------------------------------------------------ */
/* Events                                                              */
/* ------------------------------------------------------------------ */

static void push_event(const dltb_event *event, dltb_scope scope) {
    uint32_t i;
    lua_createtable(g_lua, 0, 5);
    lua_pushstring(g_lua, event->name); lua_setfield(g_lua, -2, "name");
    lua_pushinteger(g_lua, scope); lua_setfield(g_lua, -2, "scope");
    lua_pushboolean(g_lua, event->suppress); lua_setfield(g_lua, -2, "suppressed");
    lua_createtable(g_lua, 0, (int)event->payload_count);
    for (i = 0; i < event->payload_count; ++i) {
        const dltb_named_value *field = &event->payload[i];
        if (field->value.type == DLTB_T_BOOL) lua_pushboolean(g_lua, field->value.num.boolean);
        else if (field->value.type == DLTB_T_I32) lua_pushinteger(g_lua, field->value.num.i32);
        else if (field->value.type == DLTB_T_F32) lua_pushnumber(g_lua, field->value.num.f32);
        else if (field->value.type == DLTB_T_ENUM) lua_pushinteger(g_lua, field->value.num.i32);
        else if (field->value.type == DLTB_T_STRING) lua_pushstring(g_lua, field->value.text);
        else lua_pushnil(g_lua);
        lua_setfield(g_lua, -2, field->name);
    }
    lua_setfield(g_lua, -2, "data");
}

/*
 * A callback runs as its owning script, so a lease claimed from inside an
 * event handler belongs to that script and is released with it.
 */
static void on_event(dltb_event *event, dltb_scope scope, void *context) {
    lua_host_subscription *entry = (lua_host_subscription *)context;
    int previous_owner;
    if (!entry || !entry->active || !g_lua) return;
    EnterCriticalSection(&g_lock);
    previous_owner = g_current_owner;
    g_current_owner = entry->owner;
    lua_rawgeti(g_lua, LUA_REGISTRYINDEX, entry->function_ref);
    push_event(event, scope);
    if (protected_call(1, 1, "Lua event callback")) {
        if (lua_isboolean(g_lua, -1) && lua_toboolean(g_lua, -1) && entry->phase == DLTB_PHASE_BEFORE)
            event->suppress = 1;
        lua_pop(g_lua, 1);
    }
    g_current_owner = previous_owner;
    LeaveCriticalSection(&g_lock);
}

static int lua_bridge_on(lua_State *L) {
    const char *name = luaL_checkstring(L, 1);
    dltb_phase phase = (dltb_phase)luaL_checkinteger(L, 2);
    unsigned i;
    dltb_status status;
    luaL_checktype(L, 3, LUA_TFUNCTION);
    for (i = 0; i < CRANE_MAX_SUBSCRIPTIONS && g_subscriptions[i].active; ++i) {}
    if (i == CRANE_MAX_SUBSCRIPTIONS) return luaL_error(L, "subscription capacity exhausted");
    lua_pushvalue(L, 3);
    g_subscriptions[i].function_ref = luaL_ref(L, LUA_REGISTRYINDEX);
    g_subscriptions[i].phase = phase;
    g_subscriptions[i].owner = g_current_owner;
    status = g_api->events->subscribe(g_client, name, phase, 0, 0, on_event,
                                      &g_subscriptions[i], &g_subscriptions[i].subscription);
    if (status != DLTB_OK) {
        luaL_unref(L, LUA_REGISTRYINDEX, g_subscriptions[i].function_ref);
        memset(&g_subscriptions[i], 0, sizeof(g_subscriptions[i]));
        push_status_error(status); return 2;
    }
    g_subscriptions[i].active = 1;
    lua_pushinteger(L, (lua_Integer)g_subscriptions[i].subscription.id);
    return 1;
}

static void install_bridge_table(void) {
    lua_createtable(g_lua, 0, 16);
    lua_pushcfunction(g_lua, lua_bridge_scope); lua_setfield(g_lua, -2, "scope");
    lua_pushcfunction(g_lua, lua_bridge_log); lua_setfield(g_lua, -2, "log");
    lua_pushcfunction(g_lua, lua_bridge_get); lua_setfield(g_lua, -2, "get");
    lua_pushcfunction(g_lua, lua_bridge_describe); lua_setfield(g_lua, -2, "describe");
    lua_pushcfunction(g_lua, lua_bridge_list); lua_setfield(g_lua, -2, "list");
    lua_pushcfunction(g_lua, lua_bridge_resolve); lua_setfield(g_lua, -2, "resolve");
    lua_pushcfunction(g_lua, lua_bridge_on); lua_setfield(g_lua, -2, "on");
    lua_pushcfunction(g_lua, lua_bridge_set); lua_setfield(g_lua, -2, "set");
    lua_pushcfunction(g_lua, lua_bridge_claim); lua_setfield(g_lua, -2, "claim");
    lua_pushcfunction(g_lua, lua_bridge_lease_write); lua_setfield(g_lua, -2, "lease_write");
    lua_pushcfunction(g_lua, lua_bridge_lease_baseline); lua_setfield(g_lua, -2, "lease_baseline");
    lua_pushcfunction(g_lua, lua_bridge_lease_release); lua_setfield(g_lua, -2, "lease_release");
    lua_pushcfunction(g_lua, lua_bridge_modifier_acquire); lua_setfield(g_lua, -2, "modifier_acquire");
    lua_pushcfunction(g_lua, lua_bridge_modifier_write); lua_setfield(g_lua, -2, "modifier_write");
    lua_pushcfunction(g_lua, lua_bridge_modifier_read); lua_setfield(g_lua, -2, "modifier_read");
    lua_pushcfunction(g_lua, lua_bridge_modifier_release); lua_setfield(g_lua, -2, "modifier_release");
    lua_setglobal(g_lua, "bridge");
}

/* ------------------------------------------------------------------ */
/* Loading                                                             */
/* ------------------------------------------------------------------ */

static void script_full_path(const char *file, wchar_t *out, size_t capacity) {
    wchar_t wide[CRANE_MAX_NAME];
    MultiByteToWideChar(CP_UTF8, 0, file, -1, wide, CRANE_MAX_NAME);
    _snwprintf_s(out, capacity, _TRUNCATE, L"%sscripts\\%s", g_module_dir, wide);
}

/*
 * Publishes one script's parameters as the global `params` before it runs.
 *
 * Values come from the manifest, never from the script. A script declares its
 * knobs in header comments which only the manager reads; Crane neither parses
 * nor enforces those declarations, so a parameter absent from the manifest is
 * simply absent here. Scripts therefore state their own fallback:
 *
 *     local speed = params.speed or 1.0
 *
 * which also means a hand-edited manifest that drops a key degrades to the
 * script's default rather than to nil arithmetic.
 *
 * Set fresh per script: two scripts must never see each other's values, and a
 * script with no parameters gets an empty table rather than leftovers.
 */
static void push_params(unsigned index) {
    unsigned i;
    lua_createtable(g_lua, 0, (int)g_scripts[index].param_count);
    for (i = 0; i < g_scripts[index].param_count; ++i) {
        const manifest_param *param = &g_scripts[index].params[i];
        switch (param->type) {
        case MANIFEST_PARAM_BOOL:   lua_pushboolean(g_lua, param->boolean); break;
        case MANIFEST_PARAM_STRING: lua_pushstring(g_lua, param->text); break;
        case MANIFEST_PARAM_NUMBER:
        default:                    lua_pushnumber(g_lua, param->number); break;
        }
        lua_setfield(g_lua, -2, param->key);
    }
    lua_setglobal(g_lua, "params");
}

static void load_one_script(unsigned index) {
    wchar_t wide_path[MAX_PATH];
    char path[MAX_PATH];
    WIN32_FILE_ATTRIBUTE_DATA attributes;

    script_full_path(g_scripts[index].file, wide_path, MAX_PATH);
    if (GetFileAttributesExW(wide_path, GetFileExInfoStandard, &attributes))
        g_scripts[index].write_time = attributes.ftLastWriteTime;
    else {
        host_log(DLTB_LOG_CLASS_ERROR, "%s is listed in the manifest but not present in scripts\\",
                 g_scripts[index].file);
        g_scripts[index].state = CRANE_STATE_MISSING;
        strncpy_s(g_scripts[index].error, sizeof(g_scripts[index].error),
                  "not found in scripts\\", _TRUNCATE);
        return;
    }
    if (!WideCharToMultiByte(CP_UTF8, 0, wide_path, -1, path, sizeof(path), NULL, NULL)) {
        host_log(DLTB_LOG_CLASS_ERROR, "cannot encode the path of %s", g_scripts[index].file);
        g_scripts[index].state = CRANE_STATE_FAILED;
        strncpy_s(g_scripts[index].error, sizeof(g_scripts[index].error),
                  "path could not be encoded", _TRUNCATE);
        return;
    }
    if (luaL_loadfile(g_lua, path) != LUA_OK) {
        const char *message = lua_tostring(g_lua, -1);
        if (!message) message = "could not be loaded";
        host_log(DLTB_LOG_CLASS_ERROR, "%s: %s", g_scripts[index].file, message);
        g_scripts[index].state = CRANE_STATE_FAILED;
        strncpy_s(g_scripts[index].error, sizeof(g_scripts[index].error), message, _TRUNCATE);
        lua_pop(g_lua, 1);
        return;
    }
    g_current_owner = (int)index;
    push_params(index);
    if (protected_call_detail(0, 0, g_scripts[index].file,
                              g_scripts[index].error, sizeof(g_scripts[index].error))) {
        g_scripts[index].state = CRANE_STATE_LOADED;
        g_scripts[index].error[0] = '\0';
        /* One line per enabled script, at DEBUG. When this host ran a single
           startup.lua, one line for the host was the right granularity; as a
           mod loader each enabled script is effectively a mod, and knowing
           which ones are live is what you want when one misbehaves. */
        if (g_scripts[index].param_count)
            host_log(DLTB_LOG_CLASS_DEBUG, "running %s (%u parameter(s))",
                     g_scripts[index].file, g_scripts[index].param_count);
        else
            host_log(DLTB_LOG_CLASS_DEBUG, "running %s", g_scripts[index].file);
    } else {
        g_scripts[index].state = CRANE_STATE_FAILED;
    }
    g_current_owner = CRANE_OWNER_CONSOLE;
}

static void json_escape(const char *in, char *out, size_t out_bytes) {
    size_t used = 0;
    if (!out_bytes) return;
    for (; in && *in; ++in) {
        const char *escaped = NULL;
        char buffer[8];
        unsigned char c = (unsigned char)*in;
        switch (c) {
        case '"':  escaped = "\\\""; break;
        case '\\': escaped = "\\\\"; break;
        case '\n': escaped = "\\n"; break;
        case '\r': escaped = "\\r"; break;
        case '\t': escaped = "\\t"; break;
        default:
            if (c < 0x20) {
                /* Lua messages are ordinary text, but a control character would
                   produce invalid JSON and the manager would refuse the file. */
                _snprintf_s(buffer, sizeof(buffer), _TRUNCATE, "\\u%04x", c);
                escaped = buffer;
            }
            break;
        }
        if (escaped) {
            size_t length = strlen(escaped);
            if (used + length >= out_bytes) break;
            memcpy(out + used, escaped, length);
            used += length;
        } else {
            if (used + 1 >= out_bytes) break;
            out[used++] = (char)c;
        }
    }
    out[used] = '\0';
}

static const char *state_name(crane_script_state state) {
    switch (state) {
    case CRANE_STATE_LOADED:  return "loaded";
    case CRANE_STATE_MISSING: return "missing";
    case CRANE_STATE_FAILED:  return "failed";
    case CRANE_STATE_DISABLED:
    default:                  return "disabled";
    }
}

/*
 * Writes DLTBRuntimeCrane.status.json after every reload.
 *
 * This is the only channel from the runtime back to CraneManager. The manager
 * writes the manifest and this host reads it; without this file the manager can
 * show that a script is ticked but never that it failed.
 *
 * A file rather than the named pipe: the manager already watches this
 * directory, a file survives the manager not being open, and it needs no
 * connection handshake for something written a few times a session.
 *
 * Written whole and small, with no partial-write protection beyond that -- a
 * reader that catches a half-written file parses nothing and waits for the next
 * change notification, the same thing it does for a malformed manifest.
 */
static void write_status(void) {
    HANDLE file;
    DWORD written = 0;
    char body[16384];
    char escaped_file[CRANE_MAX_NAME * 2];
    char escaped_error[sizeof(((lua_host_script *)0)->error) * 2];
    int length = 0;
    unsigned i;

    length += _snprintf_s(body + length, sizeof(body) - (size_t)length, _TRUNCATE,
                          "{\n  \"version\": 1,\n  \"writes\": %s,\n  \"scripts\": [",
                          g_allow_writes ? "true" : "false");

    for (i = 0; i < g_script_count && length > 0; ++i) {
        json_escape(g_scripts[i].file, escaped_file, sizeof(escaped_file));
        json_escape(g_scripts[i].error, escaped_error, sizeof(escaped_error));
        length += _snprintf_s(body + length, sizeof(body) - (size_t)length, _TRUNCATE,
                              "%s\n    { \"file\": \"%s\", \"state\": \"%s\", \"error\": \"%s\" }",
                              i ? "," : "", escaped_file,
                              state_name(g_scripts[i].state), escaped_error);
    }
    if (length > 0)
        length += _snprintf_s(body + length, sizeof(body) - (size_t)length, _TRUNCATE,
                              "%s]\n}\n", g_script_count ? "\n  " : "");
    if (length <= 0) {
        host_log(DLTB_LOG_CLASS_WARN, "status file not written: too many scripts to format");
        return;
    }

    file = CreateFileW(g_status_path, GENERIC_WRITE, FILE_SHARE_READ, NULL,
                       CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        /* The host works perfectly well with no manager watching, so this is
           neither fatal nor worth an ERROR. Say it once at DEBUG so a puzzled
           manager has an explanation in the log. */
        host_log(DLTB_LOG_CLASS_DEBUG, "could not write DLTBRuntimeCrane.status.json");
        return;
    }
    WriteFile(file, body, (DWORD)length, &written, NULL);
    CloseHandle(file);
}

/*
 * Reload is release-and-reclaim for every script rather than a per-file diff.
 *
 * Manifest order decides who wins a contested lease, so reloading one script
 * in place could leave a path held by whoever happened to claim it first
 * historically rather than by whoever the current order says should hold it.
 * Rebuilding the whole set keeps "what is loaded" a function of the manifest
 * alone.
 */
static void reload_scripts(void) {
    unsigned i;
    release_all_scripts();
    read_manifest();
    for (i = 0; i < g_script_count; ++i) {
        if (!g_scripts[i].enabled) {
            /* At DEBUG: a script that is present but switched off is a state
               the player chose and may have forgotten choosing. */
            host_log(DLTB_LOG_CLASS_DEBUG, "skipping %s (disabled)", g_scripts[i].file);
            g_scripts[i].state = CRANE_STATE_DISABLED;
            g_scripts[i].error[0] = '\0';
            continue;
        }
        load_one_script(i);
    }
    write_status();
}

static void run_console_command(void) {
    char command[CRANE_MAX_COMMAND];
    if (InterlockedCompareExchange(&g_command_ready, 0, 1) != 1) return;
    strncpy_s(command, sizeof(command), g_command, _TRUNCATE);
    if (luaL_loadstring(g_lua, command) != LUA_OK) {
        host_log(DLTB_LOG_CLASS_ERROR, "Lua command: %s", lua_tostring(g_lua, -1)); lua_pop(g_lua, 1); return;
    }
    if (protected_call(0, LUA_MULTRET, "Lua command")) {
        int count = lua_gettop(g_lua);
        if (count) host_log(DLTB_LOG_CLASS_INFO, "Lua command returned %s", luaL_tolstring(g_lua, 1, NULL));
        lua_settop(g_lua, 0);
    }
}

/*
 * Turning writes off releases everything the scripts own.
 *
 * Gating only *new* writes would leave a script's lease still applied while the
 * INI says writes are disabled -- the config claiming one thing and the game
 * showing another. Releasing restores every baseline, so switching the flag
 * off returns the game to vanilla for anything a script was holding.
 *
 * Turning it on reloads too: a script that was refused a claim at load has no
 * other way to retry.
 *
 * Either way this is reload_scripts(), which already releases and reclaims.
 */
static void on_update(dltb_scope scope, void *context) {
    (void)scope; (void)context;
    InterlockedExchange(&g_task_pending, 0);
    EnterCriticalSection(&g_lock);
    if (InterlockedCompareExchange(&g_settings_dirty, 0, 1) == 1) {
        if (read_settings()) {
            host_log(DLTB_LOG_CLASS_INFO,
                     g_allow_writes
                         ? "writes enabled; reloading scripts so they can claim"
                         : "writes disabled; releasing everything scripts held");
            InterlockedExchange(&g_reload_requested, 1);
        }
    }
    if (InterlockedCompareExchange(&g_reload_requested, 0, 1) == 1) reload_scripts();
    run_console_command();
    LeaveCriticalSection(&g_lock);
}

static void queue_update(void) {
    dltb_task task;
    if (InterlockedCompareExchange(&g_task_pending, 1, 0) == 0) {
        dltb_status status = g_api->scope->schedule(g_client, on_update, NULL, &task);
        if (status != DLTB_OK) {
            InterlockedExchange(&g_task_pending, 0);
            host_log(DLTB_LOG_CLASS_WARN,
                     "Lua work is waiting for the game update path");
            host_log(DLTB_LOG_CLASS_DEBUG,
                     "Lua work schedule returned %s (%d)",
                     dltb_status_text(status), (int)status);
        }
    }
}

/* True when the manifest or any listed script changed on disk. */
static int sources_changed(void) {
    WIN32_FILE_ATTRIBUTE_DATA attributes;
    unsigned i;
    int manifest_now = GetFileAttributesExW(g_manifest_path, GetFileExInfoStandard, &attributes);
    if (manifest_now) {
        if (!g_manifest_present || CompareFileTime(&attributes.ftLastWriteTime, &g_manifest_write) != 0) {
            g_manifest_write = attributes.ftLastWriteTime;
            return 1;
        }
    } else if (g_manifest_present) {
        return 1;
    }
    /* The INI is watched alongside the manifest so AllowWrites and LogLevel can
       be changed without restarting the game. Everything else about this host
       hot-reloads; requiring a restart to flip one switch would put back the
       slow loop it exists to remove. */
    if (GetFileAttributesExW(g_ini_path, GetFileExInfoStandard, &attributes)) {
        if (!g_ini_present || CompareFileTime(&attributes.ftLastWriteTime, &g_ini_write) != 0) {
            g_ini_write = attributes.ftLastWriteTime;
            g_ini_present = 1;
            return 1;
        }
    } else if (g_ini_present) {
        g_ini_present = 0;
        return 1;
    }
    for (i = 0; i < g_script_count; ++i) {
        wchar_t path[MAX_PATH];
        if (!g_scripts[i].enabled) continue;
        script_full_path(g_scripts[i].file, path, MAX_PATH);
        if (GetFileAttributesExW(path, GetFileExInfoStandard, &attributes) &&
            CompareFileTime(&attributes.ftLastWriteTime, &g_scripts[i].write_time) != 0)
            return 1;
    }
    return 0;
}

static int connect_to_bridge(void) {
    static const char *const required[] = {
        "operation:client.unregister_client",
        "operation:client.report_loaded",
        "operation:log.write",
        "operation:scope.current",
        "operation:scope.schedule",
        "operation:state.describe",
        "operation:state.enumerate",
        "operation:state.read",
        "operation:events.subscribe",
        "operation:events.unsubscribe",
        "state:session.playable",
        NULL
    };
    HMODULE bridge = GetModuleHandleW(L"DLTBRuntimeBridge.asi");
    dltb_get_api3_fn get_api;
    dltb_manifest manifest;
    if (!bridge) return 0;
    get_api = (dltb_get_api3_fn)(void *)GetProcAddress(bridge, "DLTBBridgeGetAPI3");
    if (!get_api || !(g_api = get_api(DLTB_API3_ABI)) || !g_api->build ||
        !g_api->build->verified)
        return 0;
    memset(&manifest, 0, sizeof(manifest)); manifest.struct_bytes = sizeof(manifest);
    manifest.name = "DLTBRuntimeCrane"; manifest.client_version = CRANE_CLIENT_VERSION; manifest.min_abi = DLTB_API3_ABI;
    manifest.requires = required;
    return g_api->client->register_client(&manifest, &g_client) == DLTB_OK;
}

static DWORD WINAPI pipe_thread(LPVOID unused) {
    (void)unused;
    while (WaitForSingleObject(g_stop_event, 0) == WAIT_TIMEOUT) {
        DWORD read = 0;
        HANDLE pipe = CreateNamedPipeW(L"\\\\.\\pipe\\DLTBRuntimeCrane", PIPE_ACCESS_INBOUND,
                                       PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
                                       1, 0, CRANE_MAX_COMMAND, 0, NULL);
        if (pipe == INVALID_HANDLE_VALUE) return 0;
        if (ConnectNamedPipe(pipe, NULL) || GetLastError() == ERROR_PIPE_CONNECTED) {
            if (ReadFile(pipe, g_command, CRANE_MAX_COMMAND - 1, &read, NULL) && read) {
                g_command[read] = '\0';
                if (InterlockedCompareExchange(&g_command_ready, 1, 0) == 0) queue_update();
                else host_log(DLTB_LOG_CLASS_WARN, "Lua command dropped: another command is waiting for the update phase");
            }
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
    return 0;
}

static DWORD WINAPI worker_thread(LPVOID unused) {
    (void)unused;
    while (WaitForSingleObject(g_stop_event, 500) == WAIT_TIMEOUT) {
        if (connect_to_bridge()) break;
    }
    if (!g_api) return 0;
    g_lua = lua_newstate(host_alloc, &g_alloc);
    if (!g_lua) { host_log(DLTB_LOG_CLASS_ERROR, "cannot create Lua state (memory cap %u bytes)", CRANE_MEMORY_LIMIT); return 0; }
    /* A game experiment surface rather than a general Windows automation host:
       os, io, debug and package are left out. */
    luaL_requiref(g_lua, "_G", luaopen_base, 1); lua_pop(g_lua, 1);
    luaL_requiref(g_lua, LUA_TABLIBNAME, luaopen_table, 1); lua_pop(g_lua, 1);
    luaL_requiref(g_lua, LUA_STRLIBNAME, luaopen_string, 1); lua_pop(g_lua, 1);
    luaL_requiref(g_lua, LUA_MATHLIBNAME, luaopen_math, 1); lua_pop(g_lua, 1);
    luaL_requiref(g_lua, LUA_UTF8LIBNAME, luaopen_utf8, 1); lua_pop(g_lua, 1);
    install_bridge_table();
    {
        WIN32_FILE_ATTRIBUTE_DATA ini_attributes;
        if (GetFileAttributesExW(g_ini_path, GetFileExInfoStandard, &ini_attributes)) {
            g_ini_write = ini_attributes.ftLastWriteTime;
            g_ini_present = 1;
        }
    }
    (void)read_settings();
    InterlockedExchange(&g_reload_requested, 1);
    queue_update();
    {
        dltbck_context loaded_context = {g_api, g_client};
        (void)dltbck_report_loaded(&loaded_context,
                                   "Lua scripting host active");
    }
    host_log(DLTB_LOG_CLASS_DEBUG,
             "commands: DLTBRuntimeCrane.manifest.json, scripts\\*.lua or \\\\.\\pipe\\DLTBRuntimeCrane");
    g_pipe_worker = CreateThread(NULL, 0, pipe_thread, NULL, 0, NULL);
    if (g_pipe_worker) CloseHandle(g_pipe_worker);
    while (WaitForSingleObject(g_stop_event, 250) == WAIT_TIMEOUT) {
        if (sources_changed()) {
            /* Settings are re-read in the update phase rather than here:
               log->set_level and any consequent release must happen on the
               Bridge's thread, not the watcher's. */
            InterlockedExchange(&g_settings_dirty, 1);
            InterlockedExchange(&g_reload_requested, 1);
            queue_update();
        }
    }
    EnterCriticalSection(&g_lock);
    release_all_scripts();
    release_owned(CRANE_OWNER_CONSOLE);
    LeaveCriticalSection(&g_lock);
    if (g_client.id) {
        dltbck_context shutdown_context = {g_api, g_client};
        if (dltbck_request_unregister(&shutdown_context) != DLTB_OK)
            host_log(DLTB_LOG_CLASS_WARN,
                     "Crane could not unregister cleanly before shutdown");
    }
    g_client = DLTB_CLIENT_NONE;
    lua_close(g_lua); g_lua = NULL;
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = instance; DisableThreadLibraryCalls(instance);
        InitializeCriticalSection(&g_lock);
        GetModuleFileNameW(g_self, g_module_dir, MAX_PATH);
        {
            wchar_t *slash = wcsrchr(g_module_dir, L'\\');
            if (slash) slash[1] = L'\0';
        }
        _snwprintf_s(g_manifest_path, MAX_PATH, _TRUNCATE, L"%sDLTBRuntimeCrane.manifest.json", g_module_dir);
        _snwprintf_s(g_status_path, MAX_PATH, _TRUNCATE, L"%sDLTBRuntimeCrane.status.json", g_module_dir);
        _snwprintf_s(g_ini_path, MAX_PATH, _TRUNCATE, L"%sDLTBRuntimeCrane.ini", g_module_dir);
        _snwprintf_s(g_legacy_script, MAX_PATH, _TRUNCATE, L"%sscripts\\startup.lua", g_module_dir);
        g_stop_event = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (g_stop_event) {
            g_worker = CreateThread(NULL, 0, worker_thread, NULL, 0, NULL);
            if (g_worker) CloseHandle(g_worker);
        }
    } else if (reason == DLL_PROCESS_DETACH && g_stop_event) SetEvent(g_stop_event);
    return TRUE;
}
