// SPDX-License-Identifier: MIT
#ifndef DLTB_RUNTIME_BRIDGE_CLIENT_KIT_H
#define DLTB_RUNTIME_BRIDGE_CLIENT_KIT_H

/*
 * Client-side conveniences for ABI-3 Bridge clients. Header-only, address-free.
 *
 * This exists because the third client was about to copy the same ~200 lines
 * from the second: the lease-parameter layer, the session-playable gate, the
 * Bridge-owned logging wrappers, the LogLevel INI reader and the debounced INI
 * reload. Per-client copies of shared machinery are how the vendored-header
 * drift in an earlier client happened (620 diff lines, four API versions
 * behind, nothing warning) -- so the machinery lives beside the API header it
 * depends on, versioned with it, and a client includes rather than copies.
 *
 * What belongs here: patterns every client repeats, expressed only through the
 * public ABI. What does not: anything with a game address, which is the
 * Bridge's side of the line, and anything one client wants, which is that
 * client's own code.
 *
 * Everything is `static inline`, so unused helpers cost nothing and compile
 * clean under -Wall -Wextra -Werror.
 *
 * Current ABI-3 clients share this machinery rather than carrying independent
 * lifecycle implementations that can drift from the public scope contract.
 *
 * Logging discipline is baked in: INFO is for meaningful state transitions,
 * and a client waiting on a prerequisite is not an error state, so the session
 * gate says "waiting" at DEBUG, once, and nothing is claimed or read before
 * the session is playable.
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdarg.h>
#include <stdlib.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "DLTBRuntimeBridgeAPI3.h"

/* Keep build identity machine-readable without putting release bookkeeping in
   the player console. Package gates scan this exported metadata to reject a
   stale binary while the Bridge owns the timeless startup presentation. */
#define DLTBCK_EMBED_BUILD_VERSION(version_literal) \
    __declspec(dllexport) const char DLTBClientBuildVersion[] = version_literal

/* ------------------------------------------------------------------------- */
/* Context                                                                    */
/* ------------------------------------------------------------------------- */

typedef struct dltbck_context {
    const dltb_api *api;
    dltb_client client;
} dltbck_context;

/* ------------------------------------------------------------------------- */
/* Engine-scoped client shutdown                                              */
/* ------------------------------------------------------------------------- */

typedef struct dltbck_unregister_request {
    const dltb_api *api;
    dltb_client client;
} dltbck_unregister_request;

static __inline void dltbck_unregister_on_update(dltb_scope scope,
                                                  void *opaque) {
    dltbck_unregister_request *request =
        (dltbck_unregister_request *)opaque;
    (void)scope;
    request->api->client->unregister_client(request->client);
    HeapFree(GetProcessHeap(), 0, request);
}

/*
 * Request unregister without lying about completion.
 *
 * unregister_client restores engine-facing holdings and therefore accepts
 * UPDATE/DETOUR only. Most clients notice shutdown or startup failure on their
 * own worker. From there this helper queues one copied request and returns the
 * schedule result; DLTB_OK means requested, not already completed. If already
 * in engine scope it completes synchronously.
 *
 * The request owns no caller context and frees itself after delivery. If the
 * process is already tearing down and UPDATE never arrives, normal Bridge
 * process cleanup remains the final safety net; calling an engine restore from
 * the worker would be the unsafe alternative, not a stronger cleanup.
 */
static __inline dltb_status dltbck_request_unregister(
    const dltbck_context *ctx) {
    dltbck_unregister_request *request;
    dltb_task task = DLTB_TASK_NONE;
    dltb_scope current;
    dltb_status status;
    if (!ctx || !ctx->api || !ctx->client.id || !ctx->api->client ||
        !ctx->api->client->unregister_client || !ctx->api->scope ||
        !ctx->api->scope->current || !ctx->api->scope->schedule)
        return DLTB_INVALID_ARGUMENT;
    current = ctx->api->scope->current();
    if (current & DLTB_SCOPE_ENGINE)
        return ctx->api->client->unregister_client(ctx->client);
    request = (dltbck_unregister_request *)HeapAlloc(
        GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(*request));
    if (!request) return DLTB_NO_CAPACITY;
    request->api = ctx->api;
    request->client = ctx->client;
    status = ctx->api->scope->schedule(
        ctx->client, dltbck_unregister_on_update, request, &task);
    if (status != DLTB_OK) HeapFree(GetProcessHeap(), 0, request);
    return status;
}

/* ------------------------------------------------------------------------- */
/* Logging through the Bridge                                                 */
/* ------------------------------------------------------------------------- */

static __inline void dltbck_say(const dltbck_context *ctx,
                                dltb_log_class line_class,
                                const char *message) {
    if (ctx && ctx->api && ctx->api->log && ctx->api->log->write)
        ctx->api->log->write(ctx->client, line_class, message);
}

static __inline void dltbck_sayf(const dltbck_context *ctx,
                                 dltb_log_class line_class,
                                 const char *format, ...) {
    char message[512];
    va_list args;
    va_start(args, format);
    _vsnprintf_s(message, sizeof(message), _TRUNCATE, format, args);
    va_end(args);
    dltbck_say(ctx, line_class, message);
}

/* Report the client's one successful startup transition. The client supplies
   only what became available; the Bridge owns `loaded;`, INFO classification,
   attribution and console presentation for the whole platform. */
static __inline dltb_status dltbck_report_loaded(
    const dltbck_context *ctx, const char *summary) {
    dltb_status status;
    if (!ctx || !ctx->api || !ctx->api->client || !ctx->client.id ||
        !DLTB_API3_DOMAIN_HAS(ctx->api->client, dltb_ns_client,
                             report_loaded))
        return DLTB_UNSUPPORTED;
    status = ctx->api->client->report_loaded(ctx->client, summary);
    if (status != DLTB_OK)
        dltbck_sayf(ctx, DLTB_LOG_CLASS_ERROR,
                    "startup completion report failed: %s",
                    dltb_status_text(status));
    return status;
}

/*
 * LogLevel from a client's own INI, name or number.
 *
 * The ABI-3 names and the legacy spellings every 1.x-era INI shipped with are
 * both accepted, so nobody's existing file changes meaning. The level reaches
 * the Bridge only through set_level -- a client that never calls it follows the
 * Bridge's level, which is not the same as honouring its own INI.
 */
static __inline int dltbck_read_log_level(const wchar_t *ini_path,
                                          const wchar_t *section,
                                          const wchar_t *key) {
    wchar_t text[32];
    wchar_t *end;
    long value;
    if (!GetPrivateProfileStringW(section, key, L"", text, _countof(text),
                                  ini_path) ||
        !text[0])
        return (int)DLTB_LOG_INFO;
    if (_wcsicmp(text, L"Off") == 0) return (int)DLTB_LOG_OFF;
    if (_wcsicmp(text, L"Info") == 0 || _wcsicmp(text, L"Minimal") == 0)
        return (int)DLTB_LOG_INFO;
    if (_wcsicmp(text, L"Debug") == 0 || _wcsicmp(text, L"Normal") == 0)
        return (int)DLTB_LOG_DEBUG;
    if (_wcsicmp(text, L"Trace") == 0 || _wcsicmp(text, L"Verbose") == 0)
        return (int)DLTB_LOG_TRACE;
    value = wcstol(text, &end, 10);
    while (*end == L' ' || *end == L'\t') ++end;
    if (*end != L'\0' || value < (long)DLTB_LOG_OFF ||
        value > (long)DLTB_LOG_TRACE)
        return (int)DLTB_LOG_INFO;
    return (int)value;
}

static __inline void dltbck_apply_log_level(const dltbck_context *ctx,
                                            int level) {
    if (ctx && ctx->api && ctx->api->log && ctx->api->log->set_level)
        ctx->api->log->set_level(ctx->client, (dltb_log_level)level);
}

/* ------------------------------------------------------------------------- */
/* The session gate                                                           */
/* ------------------------------------------------------------------------- */

/*
 * Everything waits for `session.playable`, quietly.
 *
 * Hunger and player ticks arrive during loading, before engine state resolves.
 * A client acting on them reports failures whose entire cause is that the game
 * is not ready yet -- the startup that reads as a mod failing three times and
 * mysteriously recovering. Gate on this at the top of every handler; before
 * playable, claim nothing, read nothing, say nothing above DEBUG.
 *
 * `on_leave` (optional) runs when a playable session ends, which is where a
 * client releases its leases so the next session starts from claims rather
 * than stale holds.
 */
typedef struct dltbck_session_gate {
    int was_playable; /* -1 until first observation */
} dltbck_session_gate;

#define DLTBCK_SESSION_GATE_INIT {-1}

static __inline int dltbck_session_playable(const dltbck_context *ctx,
                                            dltbck_session_gate *gate,
                                            void (*on_leave)(void)) {
    dltb_value value;
    int playable;
    memset(&value, 0, sizeof(value));
    value.struct_bytes = (uint32_t)sizeof(value);
    if (ctx->api->state->read(ctx->client, "session.playable",
                              DLTB_SUBJECT_NONE, &value) != DLTB_OK ||
        value.type != DLTB_T_BOOL)
        return 0;
    playable = value.num.boolean != 0;
    if (playable != gate->was_playable) {
        dltbck_say(ctx, DLTB_LOG_CLASS_DEBUG,
                   playable ? "session playable; engaging"
                            : "waiting: session not yet playable; all claims "
                              "deferred");
        if (!playable && gate->was_playable == 1 && on_leave) on_leave();
        gate->was_playable = playable;
    }
    return playable;
}

/* ------------------------------------------------------------------------- */
/* Subject handles that survive a stale one                                   */
/* ------------------------------------------------------------------------- */

/*
 * Resolve a subject once, and re-resolve it when the Bridge rejects it.
 *
 * A subject handle carries a generation and stays valid for as long as the
 * thing it names does. Across a state transition it can stop matching, and a
 * client that resolved once and cached the result has no reason to ask again,
 * so every later call fails against the same dead handle.
 *
 * `dltbck_subject_hold` resolves on first use and after any invalidation.
 * `dltbck_subject_failed` invalidates the handle when a status means the handle
 * itself was the problem, so the next tick re-resolves. Callers pass every
 * failed status through it and need no opinion about which ones mean stale.
 */
typedef struct dltbck_subject_ref {
    dltb_subject subject;
    int resolved;
} dltbck_subject_ref;

#define DLTBCK_SUBJECT_REF_INIT {{0}, 0}

static __inline int dltbck_subject_hold(const dltbck_context *ctx,
                                        dltbck_subject_ref *ref,
                                        const char *path) {
    if (ref->resolved) return 1;
    if (!ctx || !ctx->api || !ctx->api->state || !ctx->api->state->resolve)
        return 0;
    if (ctx->api->state->resolve(ctx->client, path, &ref->subject) != DLTB_OK)
        return 0;
    ref->resolved = 1;
    return 1;
}

static __inline void dltbck_subject_invalidate(dltbck_subject_ref *ref) {
    ref->subject = DLTB_SUBJECT_NONE;
    ref->resolved = 0;
}

/*
 * Returns 1 if the handle was dropped, so a caller can log the re-resolution
 * rather than silently papering over a transition it might want to know about.
 */
static __inline int dltbck_subject_failed(dltbck_subject_ref *ref,
                                          dltb_status status) {
    /* DLTB_NO_SUBJECT belongs here: a handle can decode cleanly and still
       resolve to a pointer that is gone, which is re-resolvable too. */
    if (status != DLTB_STALE_SUBJECT && status != DLTB_NO_SUBJECT &&
        status != DLTB_INVALID_ARGUMENT && status != DLTB_NOT_OWNER)
        return 0;
    dltbck_subject_invalidate(ref);
    return 1;
}

/* ------------------------------------------------------------------------- */
/* Edge-triggered condition reporting                                         */
/* ------------------------------------------------------------------------- */

/*
 * Report a condition once when it starts, again when it changes, again while it
 * persists, and once when it clears.
 *
 * Three clients each wrote their own `if (already_said) return;` latch with no
 * path back, so a feature that failed once reported once and then failed
 * silently for the rest of the process. A caller in a tick loop needs all four
 * of the transitions above; a bare latch gives only the first.
 *
 * `reason` is any nonzero client-defined code; 0 means healthy. A client with
 * one failure mode passes 1.
 */
typedef struct dltbck_reporter {
    int reason;              /* 0 = healthy */
    ULONGLONG repeat_at;     /* 0 = say the next failure immediately */
} dltbck_reporter;

#define DLTBCK_REPORTER_INIT {0, 0}

/* Long enough that a persistent outage is a line a minute rather than two a
   second; short enough that somebody reading the tail of a log still sees it. */
#define DLTBCK_REPORT_REPEAT_MS 60000

/*
 * Report a failure. Says on entry, on any change of reason, and every
 * DLTBCK_REPORT_REPEAT_MS while the same reason persists. Returns 1 if it
 * spoke, so a caller can attach a one-off detailed trace to the same moment.
 */
static __inline int dltbck_report_failure(const dltbck_context *ctx,
                                          dltbck_reporter *reporter,
                                          int reason,
                                          dltb_log_class line_class,
                                          const char *message) {
    ULONGLONG now = GetTickCount64();
    if (reason == 0) reason = 1;
    if (reason == reporter->reason && now < reporter->repeat_at) {
        return 0;
    }
    dltbck_say(ctx, line_class, message);
    reporter->reason = reason;
    reporter->repeat_at = now + DLTBCK_REPORT_REPEAT_MS;
    return 1;
}

/*
 * Report that the condition cleared. Says only if it had been failing, so the
 * healthy path costs nothing and never speaks. Returns 1 if it spoke.
 */
static __inline int dltbck_report_recovered(const dltbck_context *ctx,
                                            dltbck_reporter *reporter,
                                            dltb_log_class line_class,
                                            const char *message) {
    if (reporter->reason == 0) return 0;
    dltbck_say(ctx, line_class, message);
    reporter->reason = 0;
    reporter->repeat_at = 0;
    return 1;
}

/*
 * Forget what was said, so the next failure reports immediately even if it is
 * the same reason.
 *
 * This is the call the three hand-rolled latches were all missing. Use it where
 * the player has just done something that entitles them to an answer -- a
 * feature toggle, a config reload, a new session -- because "I pressed the
 * button and nothing was logged" is indistinguishable from a dead client.
 */
static __inline void dltbck_report_forget(dltbck_reporter *reporter) {
    reporter->reason = 0;
    reporter->repeat_at = 0;
}

/* ------------------------------------------------------------------------- */
/* Event payloads                                                             */
/* ------------------------------------------------------------------------- */

/* Payload fields are matched by name: the catalog owns the names
   and the raiser supplies only values, so an index would bind the client to
   the order of a table it does not control. */
static __inline int dltbck_payload_f32(const dltb_event *event,
                                       const char *name, float *out) {
    uint32_t index;
    for (index = 0; index < event->payload_count; ++index) {
        const dltb_named_value *field = &event->payload[index];
        if (!field->name || strcmp(field->name, name) != 0) continue;
        if (field->value.type == DLTB_T_F32) {
            *out = field->value.num.f32;
            return 1;
        }
        if (field->value.type == DLTB_T_I32 ||
            field->value.type == DLTB_T_ENUM) {
            *out = (float)field->value.num.i32;
            return 1;
        }
        return 0;
    }
    return 0;
}

static __inline int dltbck_payload_i32(const dltb_event *event,
                                       const char *name, int32_t *out) {
    uint32_t index;
    for (index = 0; index < event->payload_count; ++index) {
        const dltb_named_value *field = &event->payload[index];
        if (!field->name || strcmp(field->name, name) != 0) continue;
        if (field->value.type == DLTB_T_I32 ||
            field->value.type == DLTB_T_ENUM) {
            *out = field->value.num.i32;
            return 1;
        }
        if (field->value.type == DLTB_T_F32) {
            *out = (int32_t)field->value.num.f32;
            return 1;
        }
        return 0;
    }
    return 0;
}

/* Generic postcondition for any catalogued event carrying count_before and
   count_after.  In particular, item.use.completed describes the native
   controller lifecycle: completion with an unchanged count is not proof that
   the item's effect occurred, and the callback may precede the enclosing
   player activity's return to calm.  Keeping that distinction here prevents
   every native-use client from rediscovering it as a game-mode or safe-zone
   bug.  A guarded recovery retains the attempt and revalidates on a later
   engine update; it must not replay synchronously from the callback. */
typedef enum dltbck_item_count_change {
    DLTBCK_ITEM_COUNT_UNKNOWN = 0,
    DLTBCK_ITEM_COUNT_UNCHANGED = 1,
    DLTBCK_ITEM_COUNT_DECREASED = 2,
    DLTBCK_ITEM_COUNT_INCREASED = 3
} dltbck_item_count_change;

static __inline dltbck_item_count_change
dltbck_item_count_change_from_event(const dltb_event *event,
                                    int32_t *before_out,
                                    int32_t *after_out) {
    int32_t before;
    int32_t after;
    if (!event || !dltbck_payload_i32(event, "count_before", &before) ||
        !dltbck_payload_i32(event, "count_after", &after))
        return DLTBCK_ITEM_COUNT_UNKNOWN;
    if (before_out) *before_out = before;
    if (after_out) *after_out = after;
    if (after < before) return DLTBCK_ITEM_COUNT_DECREASED;
    if (after > before) return DLTBCK_ITEM_COUNT_INCREASED;
    return DLTBCK_ITEM_COUNT_UNCHANGED;
}

/* ------------------------------------------------------------------------- */
/* Debounced INI reload                                                       */
/* ------------------------------------------------------------------------- */

/*
 * Watches the file's own mtime with a settle window, so a half-saved file is
 * never read. Poll from a handler or a worker loop; when it returns 1, reload
 * and reapply -- including set_level, which is not sticky.
 */
typedef struct dltbck_ini_watch {
    FILETIME write_time;
    FILETIME pending_write_time;
    int write_time_valid;
    int pending_valid;
    ULONGLONG pending_since;
    ULONGLONG next_check;
} dltbck_ini_watch;

static __inline int dltbck_ini_watch_time(const wchar_t *ini_path,
                                          FILETIME *write_time) {
    WIN32_FILE_ATTRIBUTE_DATA attributes;
    if (!GetFileAttributesExW(ini_path, GetFileExInfoStandard, &attributes))
        return 0;
    *write_time = attributes.ftLastWriteTime;
    return 1;
}

static __inline void dltbck_ini_watch_init(dltbck_ini_watch *watch,
                                           const wchar_t *ini_path) {
    memset(watch, 0, sizeof(*watch));
    watch->write_time_valid =
        dltbck_ini_watch_time(ini_path, &watch->write_time);
    watch->next_check = GetTickCount64() + 500;
}

static __inline int dltbck_ini_changed(dltbck_ini_watch *watch,
                                       const wchar_t *ini_path) {
    FILETIME current;
    ULONGLONG now = GetTickCount64();
    if (now < watch->next_check) return 0;
    watch->next_check = now + 250;
    if (!dltbck_ini_watch_time(ini_path, &current)) return 0;
    if (!watch->write_time_valid) {
        watch->write_time = current;
        watch->write_time_valid = 1;
        return 0;
    }
    if (CompareFileTime(&current, &watch->write_time) == 0) {
        watch->pending_valid = 0;
        return 0;
    }
    if (!watch->pending_valid ||
        CompareFileTime(&current, &watch->pending_write_time) != 0) {
        watch->pending_write_time = current;
        watch->pending_valid = 1;
        watch->pending_since = now;
        return 0;
    }
    if (now - watch->pending_since < 500) return 0;
    watch->write_time = current;
    watch->pending_valid = 0;
    return 1;
}

/* ------------------------------------------------------------------------- */
/* Parameters as leases                                                       */
/* ------------------------------------------------------------------------- */

/*
 * One held lease per parameter, claim-once/write-many.
 *
 * The baseline is captured by the claim -- it is the game's own value, and it
 * is what the Bridge restores. Session-aware clients release on_leave. A
 * client that carries a lease across subject replacement must use
 * dltbck_refresh_baseline after DLTB_STALE_SUBJECT, recompute, then write.
 *
 * `refused` is the feature-disable latch: after a refusal the parameter stops
 * being retried, so one refused path cannot spam the log sixty times a second.
 * Clear it on config reload -- the user's "try again".
 */
typedef struct dltbck_param {
    const char *path;
    dltb_lease lease;
    int held;
    int has_written;
    float written;
    float baseline;
    int baseline_valid;
    int refused;
} dltbck_param;

#define DLTBCK_PARAM(name) {name, {0}, 0, 0, 0.0f, 0.0f, 0, 0}

static __inline int dltbck_claim(const dltbck_context *ctx,
                                 dltbck_param *param) {
    dltb_value baseline;
    dltb_status status;
    if (param->held) return 1;
    if (param->refused) return 0;
    memset(&baseline, 0, sizeof(baseline));
    baseline.struct_bytes = (uint32_t)sizeof(baseline);
    status = ctx->api->lease->claim(ctx->client, param->path,
                                    DLTB_SUBJECT_NONE, &param->lease,
                                    &baseline);
    if (status != DLTB_OK) {
        dltbck_sayf(ctx, DLTB_LOG_CLASS_WARN, "claim refused: %s status=%d",
                    param->path, (int)status);
        return 0;
    }
    param->held = 1;
    param->has_written = 0;
    param->baseline_valid = baseline.type == DLTB_T_F32;
    param->baseline = param->baseline_valid ? baseline.num.f32 : 0.0f;
    return 1;
}

static __inline dltb_status dltbck_refresh_baseline(
    const dltbck_context *ctx, dltbck_param *param) {
    dltb_value baseline;
    dltb_status status;
    if (!ctx || !ctx->api || !param || !param->held ||
        !DLTB_API3_DOMAIN_HAS(ctx->api->lease, dltb_ns_lease, baseline))
        return DLTB_UNSUPPORTED;
    memset(&baseline, 0, sizeof(baseline));
    baseline.struct_bytes = (uint32_t)sizeof(baseline);
    status = ctx->api->lease->baseline(param->lease, &baseline);
    if (status != DLTB_OK) return status;
    param->baseline_valid = baseline.type == DLTB_T_F32;
    param->baseline = param->baseline_valid ? baseline.num.f32 : 0.0f;
    param->has_written = 0;
    return DLTB_OK;
}

static __inline int dltbck_write_value(const dltbck_context *ctx,
                                       dltbck_param *param,
                                       const dltb_value *value) {
    dltb_status status;
    if (param->refused) return 0;
    if (!param->held && !dltbck_claim(ctx, param)) return 0;
    status = ctx->api->lease->write(param->lease, value);
    if (status != DLTB_OK) {
        dltbck_sayf(ctx, DLTB_LOG_CLASS_WARN, "write refused: %s status=%d",
                    param->path, (int)status);
        return 0;
    }
    return 1;
}

/* Write an f32 unless it has barely moved; `step` is in the value's own units
   and zero writes every time. */
static __inline int dltbck_write(const dltbck_context *ctx,
                                 dltbck_param *param, float value,
                                 float step) {
    dltb_value wanted;
    if (param->refused) return 0;
    if (param->held && param->has_written && step > 0.0f) {
        float delta = value - param->written;
        if (delta < 0.0f) delta = -delta;
        if (delta < step) return 1;
    }
    memset(&wanted, 0, sizeof(wanted));
    wanted.struct_bytes = (uint32_t)sizeof(wanted);
    wanted.type = DLTB_T_F32;
    wanted.num.f32 = value;
    if (!dltbck_write_value(ctx, param, &wanted)) return 0;
    param->written = value;
    param->has_written = 1;
    return 1;
}

static __inline int dltbck_write_bool(const dltbck_context *ctx,
                                      dltbck_param *param, int value) {
    dltb_value wanted;
    memset(&wanted, 0, sizeof(wanted));
    wanted.struct_bytes = (uint32_t)sizeof(wanted);
    wanted.type = DLTB_T_BOOL;
    wanted.num.boolean = value ? 1u : 0u;
    return dltbck_write_value(ctx, param, &wanted);
}

static __inline void dltbck_release(const dltbck_context *ctx,
                                    dltbck_param *param) {
    if (!param->held) return;
    ctx->api->lease->release(param->lease);
    param->held = 0;
    param->has_written = 0;
    param->baseline_valid = 0;
}

static __inline void dltbck_refuse(const dltbck_context *ctx,
                                   dltbck_param *param) {
    dltbck_release(ctx, param);
    param->refused = 1;
}

/*
 * All-or-none claims, for features whose parameters only mean something
 * together. A feature holding three of its four parameters is not a degraded
 * version of that feature; it is a different and unintended one. Parameters
 * that are independent of one another -- per-class damage, say -- do not go
 * through a group.
 */
static __inline int dltbck_claim_group(const dltbck_context *ctx,
                                       dltbck_param **group, size_t count) {
    size_t index;
    for (index = 0; index < count; ++index) {
        if (dltbck_claim(ctx, group[index])) continue;
        while (index-- > 0) dltbck_release(ctx, group[index]);
        return 0;
    }
    return 1;
}

static __inline void dltbck_release_group(const dltbck_context *ctx,
                                          dltbck_param **group, size_t count) {
    size_t index;
    for (index = 0; index < count; ++index) dltbck_release(ctx, group[index]);
}

#endif /* DLTB_RUNTIME_BRIDGE_CLIENT_KIT_H */
