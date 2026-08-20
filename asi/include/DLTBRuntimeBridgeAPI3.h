// SPDX-License-Identifier: MIT
#ifndef DLTB_RUNTIME_BRIDGE_API3_H
#define DLTB_RUNTIME_BRIDGE_API3_H

/*
 * DLTBRuntimeBridge ABI 3 -- the client contract.
 *
 * ABI 2 was an unpublished development prototype. Test clients were built, but
 * never deployed or live-tested. Every published mod predates both ABI 2 and
 * DLTBRuntimeBridge as an independently deployed component; those releases do
 * not establish ABI-2 compatibility or field evidence.
 *
 * ABI 3 retains useful prototype mechanics -- sized append-only tables, leases,
 * modifiers, reflected state, status codes and generation-tagged handles --
 * while replacing prototype vocabulary and execution rules. It is the first
 * candidate contract for independent Bridge clients, rather than a migration
 * of a published ABI.
 */

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define DLTB_API3_ABI 3U
/* Fixed char-array capacities in bytes, including the terminating NUL. */
#define DLTB_API3_PATH_MAX 128U
#define DLTB_API3_CLIENT_NAME_MAX 64U
#define DLTB_API3_CLIENT_SUMMARY_MAX 128U
#define DLTB_API3_UNITS_MAX 32U
#define DLTB_API3_TEXT_MAX 128U
#define DLTB_API3_MANIFEST_FEATURE_MAX 64U

/* ------------------------------------------------------------------------ */
/* Handles                                                                    */
/* ------------------------------------------------------------------------ */

typedef struct dltb_client { uint64_t id; } dltb_client;
typedef struct dltb_subject { uint64_t id; } dltb_subject;
typedef struct dltb_lease { uint64_t id; } dltb_lease;
typedef struct dltb_subscription { uint64_t id; } dltb_subscription;
typedef struct dltb_task { uint64_t id; } dltb_task;
typedef struct dltb_modifier { uint64_t id; } dltb_modifier;
typedef struct dltb_sense_session { uint64_t id; } dltb_sense_session;

#define DLTB_CLIENT_NONE ((dltb_client){0})
#define DLTB_SUBJECT_NONE ((dltb_subject){0})
#define DLTB_LEASE_NONE ((dltb_lease){0})
#define DLTB_SUBSCRIPTION_NONE ((dltb_subscription){0})
#define DLTB_TASK_NONE ((dltb_task){0})
#define DLTB_MODIFIER_NONE ((dltb_modifier){0})
#define DLTB_SENSE_SESSION_NONE ((dltb_sense_session){0})

/* ------------------------------------------------------------------------ */
/* Status                                                                     */
/* ------------------------------------------------------------------------ */

typedef enum dltb_status {
    DLTB_OK = 0,
    DLTB_UNSUPPORTED = 1,
    DLTB_UNVERIFIED_BUILD = 2,
    DLTB_UNKNOWN_PATH = 3,
    DLTB_UNKNOWN_CLIENT = 4,
    DLTB_TYPE_MISMATCH = 5,
    DLTB_READ_ONLY = 6,
    DLTB_NOT_OWNER = 7,
    DLTB_OWNED_BY_OTHER = 8,
    DLTB_STALE_SUBJECT = 9,
    DLTB_NO_SUBJECT = 10,
    /* Replaces ABI 2's DLTB_WRONG_THREAD. The name changed with the meaning:
       the question is which execution scope the caller is in, not which OS
       thread. The log record names the scope reached and the scopes the
       operation accepts. */
    DLTB_WRONG_SCOPE = 11,
    DLTB_OUT_OF_RANGE = 12,
    DLTB_REFUSED_UNSAFE = 13,
    DLTB_TRUNCATED = 14,
    DLTB_INVALID_ARGUMENT = 15,
    DLTB_NO_CAPACITY = 16,
    DLTB_PARTIAL = 17,
    DLTB_NOT_FOUND = 18,
    DLTB_BUSY = 19,
    /* Registration refused because this exact client name is already live.
       Distinct from capacity exhaustion: names are process-lifetime identities
       and are never reclaimed without an explicit unregister. */
    DLTB_NAME_IN_USE = 20
} dltb_status;

static inline const char *dltb_status_text(dltb_status status) {
    switch (status) {
    case DLTB_OK: return "DLTB_OK";
    case DLTB_UNSUPPORTED: return "DLTB_UNSUPPORTED";
    case DLTB_UNVERIFIED_BUILD: return "DLTB_UNVERIFIED_BUILD";
    case DLTB_UNKNOWN_PATH: return "DLTB_UNKNOWN_PATH";
    case DLTB_UNKNOWN_CLIENT: return "DLTB_UNKNOWN_CLIENT";
    case DLTB_TYPE_MISMATCH: return "DLTB_TYPE_MISMATCH";
    case DLTB_READ_ONLY: return "DLTB_READ_ONLY";
    case DLTB_NOT_OWNER: return "DLTB_NOT_OWNER";
    case DLTB_OWNED_BY_OTHER: return "DLTB_OWNED_BY_OTHER";
    case DLTB_STALE_SUBJECT: return "DLTB_STALE_SUBJECT";
    case DLTB_NO_SUBJECT: return "DLTB_NO_SUBJECT";
    case DLTB_WRONG_SCOPE: return "DLTB_WRONG_SCOPE";
    case DLTB_OUT_OF_RANGE: return "DLTB_OUT_OF_RANGE";
    case DLTB_REFUSED_UNSAFE: return "DLTB_REFUSED_UNSAFE";
    case DLTB_TRUNCATED: return "DLTB_TRUNCATED";
    case DLTB_INVALID_ARGUMENT: return "DLTB_INVALID_ARGUMENT";
    case DLTB_NO_CAPACITY: return "DLTB_NO_CAPACITY";
    case DLTB_PARTIAL: return "DLTB_PARTIAL";
    case DLTB_NOT_FOUND: return "DLTB_NOT_FOUND";
    case DLTB_BUSY: return "DLTB_BUSY";
    case DLTB_NAME_IN_USE: return "DLTB_NAME_IN_USE";
    default: return "DLTB_UNKNOWN_STATUS";
    }
}

/* ------------------------------------------------------------------------ */
/* 1. Execution scope                                                         */
/* ------------------------------------------------------------------------ */

/*
 * Where the caller is, established by the Bridge rather than proven by the
 * client.
 *
 * The Bridge wraps every entry point it owns -- thirteen of them: twelve
 * detours and the update-phase drain -- so by the time client code runs the
 * scope is a fact. `scope.current()` is discovery and a safety net, never a
 * gate a client must satisfy.
 *
 * The three are unordered. They are different places with different
 * guarantees -- UPDATE ranks no higher than DETOUR -- and an operation
 * declares the set it accepts.
 *
 * They do nest: a task scheduled into the update phase can call an inventory
 * mutation, which spawns an item, which trips the Bridge's own item detour.
 */
typedef enum dltb_scope {
    DLTB_SCOPE_NONE = 0,
    /* Concrete external/current scope, not a wildcard. A client's own worker
       thread normally has this scope. Nothing about engine state is guaranteed;
       registration, discovery and reads of Bridge-owned state live here. */
    DLTB_SCOPE_ANY = 1u << 0,
    /* The validated GameDI_PH::OnPostUpdate phase: exclusive, non-overlapping,
       no reentry. The safest place to mutate. */
    DLTB_SCOPE_UPDATE = 1u << 1,
    /* Inside a Bridge-owned detour, before or after the original. On the game
       thread with engine state coherent for the operation being intercepted,
       but the update phase is not active. This is the scope ABI 2 lacked. */
    DLTB_SCOPE_DETOUR = 1u << 2
} dltb_scope;

/* Live engine objects may be dereferenced: the game thread, either scope. */
#define DLTB_SCOPE_ENGINE (DLTB_SCOPE_UPDATE | DLTB_SCOPE_DETOUR)

/* ------------------------------------------------------------------------ */
/* Values, types and reflection                                               */
/* ------------------------------------------------------------------------ */

typedef enum dltb_type {
    DLTB_T_NONE = 0,
    DLTB_T_BOOL = 1,
    DLTB_T_I32 = 2,
    DLTB_T_F32 = 3,
    DLTB_T_ENUM = 4,
    DLTB_T_STRING = 5
} dltb_type;

typedef union dltb_numeric_value {
    uint32_t boolean;
    int32_t i32;
    float f32;
} dltb_numeric_value;

typedef struct dltb_value {
    uint32_t struct_bytes;
    dltb_type type;
    dltb_numeric_value num;
    char text[DLTB_API3_TEXT_MAX];
} dltb_value;

enum {
    DLTB_ACCESS_READ = 1u << 0,
    DLTB_ACCESS_SET = 1u << 1,
    DLTB_ACCESS_HOLD = 1u << 2,
    DLTB_ACCESS_LIST = 1u << 3
};

/*
 * How much a path's behaviour is trusted. It is a recorded property: every
 * capability carries its mechanism tier and the evidence it was proven with,
 * and the API documentation holds the per-path table.
 */
typedef enum dltb_tier {
    DLTB_TIER_STABLE = 1,
    DLTB_TIER_PROVISIONAL = 2,
    DLTB_TIER_EXPERIMENTAL = 3
} dltb_tier;

typedef struct dltb_path_info {
    uint32_t struct_bytes;
    char path[DLTB_API3_PATH_MAX];
    dltb_type type;
    uint32_t access;
    dltb_tier tier;
    /* Which scopes this path's read and write accept. A client can ask before
       calling instead of discovering by refusal. */
    uint32_t scopes;
    char units[DLTB_API3_UNITS_MAX];
    /*
     * Whether some client currently holds this path, and which one.
     *
     * A platform where several mods run at once has to let one of them ask
     * "is anybody already driving this?" before it claims and finds out by
     * being refused. `owner` is the holder's registered name, empty when the
     * path is unheld or when the holder's name cannot be recovered -- so
     * `owned` is the flag to test, not a non-empty name.
     */
    uint32_t owned;
    char owner[DLTB_API3_CLIENT_NAME_MAX];
} dltb_path_info;

typedef enum dltb_subject_source {
    /* Resolved from current runtime state whenever requested. */
    DLTB_SUBJECT_SOURCE_LIVE = 1,
    /* Becomes resolvable only after a Bridge hook observes an instance. */
    DLTB_SUBJECT_SOURCE_OBSERVED = 2
} dltb_subject_source;

typedef struct dltb_subject_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_PATH_MAX];
    dltb_subject_source source;
    dltb_tier tier;
    uint32_t resolve_scopes;
} dltb_subject_info;

/* ------------------------------------------------------------------------ */
/* Events                                                                     */
/* ------------------------------------------------------------------------ */

typedef enum dltb_phase {
    DLTB_PHASE_BEFORE = 1,
    DLTB_PHASE_AFTER = 2
} dltb_phase;

#define DLTB_API3_EVENT_PAYLOAD_MAX 8U

/*
 * Semantic hunger bands, carried by `hunger.updated` as its `hunger_band`
 * field.
 *
 * The engine's own hunger state is reported separately, by `hunger.state`:
 * an engine enum whose boundaries are the game's business and which does not
 * move when a mod changes the thresholds. A band is derived from the
 * four live threshold variables, so a mod that raises
 * `var.HungerStateHungryThreshold` moves the band with it -- which is what a
 * client reacting to "the player is getting hungry" actually means.
 *
 * The field falls back to the engine state when the thresholds cannot be read
 * or do not describe a descending ladder.
 */
typedef enum dltb_hunger_band {
    DLTB_HUNGER_FULL = 0,
    DLTB_HUNGER_HUNGRY = 1,
    DLTB_HUNGER_VERY_HUNGRY = 2,
    DLTB_HUNGER_FAMISHED = 3,
    DLTB_HUNGER_EMPTY = 4
} dltb_hunger_band;

typedef struct dltb_named_value {
    const char *name;
    dltb_value value;
} dltb_named_value;

typedef struct dltb_event {
    uint32_t struct_bytes;
    const char *name;
    dltb_phase phase;
    dltb_subject subject;
    /* Set by the Bridge. Bridge-originated events carry NONE and an empty
       name. A client event carries the registered publisher handle and a copy
       of its name; caller-supplied values in these fields are ignored. */
    dltb_client publisher;
    char publisher_name[DLTB_API3_CLIENT_NAME_MAX];
    uint32_t payload_count;
    dltb_named_value payload[DLTB_API3_EVENT_PAYLOAD_MAX];
    /*
     * A before-event's decision. Setting this to non-zero asks the Bridge to
     * suppress the intercepted operation.
     *
     * This is what replaces ABI 2's bespoke interception verbs: "when the
     * player next uses a consumable, substitute this one" and "suppress this
     * survivor-sense scan" stop being Bridge-defined operations and become a
     * decision a client makes inside a before-event, which is what a script
     * extender is for. Ignored on an after-event.
     */
    uint32_t suppress;
} dltb_event;

typedef struct dltb_event_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_PATH_MAX];
    dltb_phase phase;
    dltb_tier tier;
    /* The scope this event is delivered in. A before-event raised inside a
       detour is delivered in DLTB_SCOPE_DETOUR, so a handler knows what it may
       do without guessing. */
    dltb_scope delivery_scope;
    uint32_t payload_count;
} dltb_event_info;

/*
 * Callbacks carry the scope they are invoked in, so a helper shared between an
 * event handler and a scheduled task behaves correctly without inspecting
 * global state.
 */
typedef void (*dltb_event_fn)(dltb_event *event, dltb_scope scope,
                              void *context);

/* Subscription options. A one-shot subscription is retired as it is delivered,
   which is what a client waiting for the next occurrence of something actually
   wants and what it would otherwise have to implement with a flag and an
   unsubscribe from inside its own callback. */
enum { DLTB_SUBSCRIBE_ONCE = 1u << 0 };
typedef void (*dltb_task_fn)(dltb_scope scope, void *context);

/* ------------------------------------------------------------------------ */
/* Items                                                                      */
/* ------------------------------------------------------------------------ */

typedef enum dltb_item_category {
    DLTB_ITEM_UNKNOWN = 0,
    DLTB_ITEM_CONSUMABLE = 1,
    DLTB_ITEM_RESOURCE = 2
} dltb_item_category;

enum {
    DLTB_ITEM_FLAG_DIRECT_USE = 1u << 0,
    DLTB_ITEM_FLAG_NATIVE_USE = 1u << 1,
    DLTB_ITEM_FLAG_TRANSFERABLE = 1u << 2
};

typedef struct dltb_item_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_TEXT_MAX];
    int32_t count;
    dltb_item_category category;
    uint32_t flags;
    char effect_path[DLTB_API3_PATH_MAX];
    float effect_amount;
} dltb_item_info;

/* How an item's effect is applied. */
typedef enum dltb_use_mode {
    /* Apply the effect directly. No animation, no interruption. */
    DLTB_USE_DIRECT = 1,
    /* Drive the game's own use action, with its animation and its ability to
       be interrupted. */
    DLTB_USE_NATIVE = 2
} dltb_use_mode;

enum {
    DLTB_ACTION_ITEM_REMOVED = 1u << 0,
    DLTB_ACTION_VALUE_WRITTEN = 1u << 1,
    DLTB_ACTION_WRITE_VERIFIED = 1u << 2,
    DLTB_ACTION_ITEM_ROLLED_BACK = 1u << 3,
    DLTB_ACTION_ROLLBACK_FAILED = 1u << 4,
    DLTB_ACTION_CLAMPED = 1u << 5,
    DLTB_ACTION_VALUE_ROLLED_BACK = 1u << 6
};

/* Public vocabulary for item.use.{engaged,started,completed,cancelled}::reason. */
typedef enum dltb_use_reason {
    DLTB_USE_REASON_NONE = 0,
    DLTB_USE_REASON_INTERRUPTED = 1,
    DLTB_USE_REASON_TIMEOUT = 2
} dltb_use_reason;

/* Public bit layout for player.activity.requested::flags. The activity IDs
   themselves remain observational engine IDs, not a frozen semantic enum. */
enum {
    DLTB_ACTIVITY_REQUEST_PRESERVE = 1u << 0,
    DLTB_ACTIVITY_REQUEST_FORCE = 1u << 8
};

typedef struct dltb_action_outcome {
    uint32_t struct_bytes;
    char item[DLTB_API3_TEXT_MAX];
    int32_t requested_count;
    int32_t count_before;
    int32_t count_after;
    float value_before;
    float value_after;
    uint32_t flags;
} dltb_action_outcome;

/* ------------------------------------------------------------------------ */
/* Build identity and registration                                            */
/* ------------------------------------------------------------------------ */

typedef struct dltb_build_info {
    uint32_t struct_bytes;
    uint32_t verified;
    char bridge_version[32];
    char game_version[32];
} dltb_build_info;

typedef struct dltb_manifest {
    uint32_t struct_bytes;
    const char *name;
    uint32_t client_version;
    uint32_t min_abi;
    const char *const *requires;
    const char *const *optional;
} dltb_manifest;

/*
 * A manifest feature is one exact public capability, not a product-version
 * proxy. Qualified names are unambiguous and are the preferred spelling:
 *
 *   operation:inventory.transfer   state:hunger.points
 *   modifier:hunger.drain_rate     event:hunger.updated
 *   item:Craft_Battery              subject:player.flashlight
 *
 * Existing unqualified exact names remain accepted when they resolve to
 * exactly one catalog. Each requires/optional array is NUL-terminated and may
 * contain at most DLTB_API3_MANIFEST_FEATURE_MAX entries.
 */
typedef enum dltb_feature_kind {
    DLTB_FEATURE_UNKNOWN = 0,
    DLTB_FEATURE_OPERATION = 1,
    DLTB_FEATURE_STATE = 2,
    DLTB_FEATURE_MODIFIER = 3,
    DLTB_FEATURE_EVENT = 4,
    DLTB_FEATURE_ITEM = 5,
    DLTB_FEATURE_SUBJECT = 6
} dltb_feature_kind;

typedef struct dltb_feature_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_PATH_MAX];
    uint32_t required;
    uint32_t available;
    dltb_feature_kind kind;
    dltb_status status;
    uint32_t scopes;
    dltb_tier tier;
} dltb_feature_info;

typedef struct dltb_operation_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_PATH_MAX];
    uint32_t scopes;
    dltb_tier tier;
} dltb_operation_info;

typedef struct dltb_modifier_info {
    uint32_t struct_bytes;
    char path[DLTB_API3_PATH_MAX];
    dltb_type type;
    dltb_tier tier;
    uint32_t acquire_scopes;
    uint32_t write_scopes;
    uint32_t read_scopes;
    uint32_t release_scopes;
    char units[DLTB_API3_UNITS_MAX];
    float minimum;
    float maximum;
    float neutral;
    uint32_t active_count;
} dltb_modifier_info;

/* ------------------------------------------------------------------------ */
/* 2. Platform services                                                       */
/* ------------------------------------------------------------------------ */

typedef struct dltb_ns_client {
    uint32_t struct_bytes;
    dltb_status (*register_client)(const dltb_manifest *manifest,
                                   dltb_client *client_out);
    dltb_status (*unregister_client)(dltb_client client);
    /* Inspect the immutable admission result captured at registration. */
    dltb_status (*requirement)(dltb_client client, const char *name,
                               dltb_feature_info *info_out);
    dltb_status (*enumerate_requirements)(
        dltb_client client, dltb_feature_info *buffer,
        uint32_t element_bytes, uint32_t capacity, uint32_t *count_out);
    /*
     * Report completion of client startup once. The client supplies only the
     * concise capability summary; the Bridge owns attribution, severity and
     * the complete user-facing `loaded; ...` record.
     */
    dltb_status (*report_loaded)(dltb_client client, const char *summary);
} dltb_ns_client;

typedef struct dltb_ns_state {
    uint32_t struct_bytes;
    dltb_status (*describe)(dltb_client client, const char *path,
                            dltb_path_info *info_out);
    dltb_status (*read)(dltb_client client, const char *path,
                        dltb_subject subject, dltb_value *value_out);
    dltb_status (*set)(dltb_client client, const char *path,
                       dltb_subject subject, const dltb_value *value);
    dltb_status (*enumerate)(dltb_client client, const char *prefix,
                             dltb_path_info *buffer, uint32_t element_bytes,
                             uint32_t capacity, uint32_t *count_out);
    /*
     * Obtain a subject by name -- see sec.3. `player` and `hunger` are
     * well-known names rather than privileged types, and a subject reached by
     * relationship uses a path: "player.flashlight".
     */
    dltb_status (*resolve)(dltb_client client, const char *path,
                           dltb_subject *subject_out);
    /* Static subject vocabulary; availability remains a live resolve result. */
    dltb_status (*describe_subject)(dltb_client client, const char *name,
                                    dltb_subject_info *info_out);
    dltb_status (*enumerate_subjects)(
        dltb_client client, const char *prefix, dltb_subject_info *buffer,
        uint32_t element_bytes, uint32_t capacity, uint32_t *count_out);
} dltb_ns_state;

typedef struct dltb_ns_scope {
    uint32_t struct_bytes;
    /* Exactly one concrete bit: ANY, UPDATE, or DETOUR. */
    dltb_scope (*current)(void);
    /* Which scopes a path or operation accepts, answerable before calling. */
    dltb_status (*required)(dltb_client client, const char *path,
                            uint32_t *scopes_out);
    /*
     * Run a callback in the next update phase. The explicit deferral.
     *
     * A scope mismatch is refused with DLTB_WRONG_SCOPE and never silently
     * queued: returning DLTB_OK for something that has not happened yet is the
     * "success while lying" failure this project has already paid for twice.
     * A client that wants deferral asks for it here.
     */
    dltb_status (*schedule)(dltb_client client, dltb_task_fn callback,
                            void *context, dltb_task *task_out);
    dltb_status (*cancel)(dltb_client client, dltb_task task);
    /* Non-state operation discovery. State paths remain discoverable through
       state->describe/enumerate; modifier paths through modifiers below. */
    dltb_status (*describe)(dltb_client client, const char *operation,
                            dltb_operation_info *info_out);
    dltb_status (*enumerate)(dltb_client client, const char *prefix,
                             dltb_operation_info *buffer,
                             uint32_t element_bytes, uint32_t capacity,
                             uint32_t *count_out);
} dltb_ns_scope;

/*
 * How much a client emits. A threshold, and the only thing configuration sets.
 *
 * The scale is the conventional logging hierarchy read as increasing detail,
 * because everyone already knows what these mean. TRACE rather than VERBOSE at
 * the top: verbose says only "a lot", trace says what kind -- the execution
 * path, call by call.
 */
typedef enum dltb_log_level {
    DLTB_LOG_OFF = 0,
    /* Normal lifecycle and significant operational events. What a player
       should be able to observe while the mod is deployed. */
    DLTB_LOG_INFO = 1,
    /* Diagnostic state, decisions, bindings, requests, state changes. */
    DLTB_LOG_DEBUG = 2,
    /* Individual calls, values, hook entry and exit, polling. */
    DLTB_LOG_TRACE = 3
} dltb_log_level;

/*
 * What kind of statement a given line is. Fixed by whoever wrote the line,
 * never by configuration -- LogLevel=3 does not turn every line into a TRACE.
 *
 * Emission follows from the two together:
 *
 *   INFO   emitted at level >= 1
 *   DEBUG  emitted at level >= 2
 *   TRACE  emitted at level >= 3
 *   WARN   emitted at any nonzero level
 *   ERROR  emitted at any nonzero level
 *
 * WARN and ERROR are severities rather than verbosities, so a threshold cannot
 * hide them. A level that can suppress the one line somebody needs will, and a
 * verbosity threshold must not be able to hide a failure.
 */
typedef enum dltb_log_class {
    DLTB_LOG_CLASS_INFO = 1,
    DLTB_LOG_CLASS_DEBUG = 2,
    DLTB_LOG_CLASS_TRACE = 3,
    /* Unexpected condition; the operation can continue. */
    DLTB_LOG_CLASS_WARN = 4,
    /* The operation failed, or a capability is unavailable. */
    DLTB_LOG_CLASS_ERROR = 5
} dltb_log_class;

/*
 * The Bridge owns logging; clients do not open files.
 *
 * Every reference platform does this -- SMAPI's IMonitor, libretro's log
 * interface, BG3SE's Ext.Log, Quake II's gi.Com_Print -- and none has mods
 * writing their own. One merged stream is also the only place an interaction
 * bug is visible: one client claiming a parameter another then writes cannot be
 * seen in per-client files. The CMTrace `component` field carries the client
 * name, so any CMTrace viewer filters per client without a second file.
 *
 * INFO is the user-facing surface. Write sentences a player can act on there,
 * put diagnostic state at DEBUG, and call-by-call detail at TRACE.
 */
typedef struct dltb_ns_log {
    uint32_t struct_bytes;
    /*
     * `line_class` is what this line is, rather than how much you want
     * emitted. It also decides the record's CMTrace severity, so WARN and
     * ERROR keep the viewer's yellow and red highlighting while DEBUG and
     * TRACE stay informational -- that field has no room for the verbosity
     * distinction, which is why the class is written into the message text
     * as well.
     */
    void (*write)(dltb_client client, dltb_log_class line_class,
                  const char *message);
    /* The client's effective level, so a message that would be discarded need
       not be built. */
    dltb_log_level (*level)(dltb_client client);
    /*
     * Declare this client's level, governing its records in both the console
     * and the log file.
     *
     * You own your INI; the Bridge owns the log. This is how the two meet, and
     * it is one number for both sinks -- a player who turns a mod down should
     * not have to discover that the console has a separate knob.
     *
     * Call it again whenever the setting changes; it is not sticky across
     * re-registration. A client that never calls it follows the Bridge's own
     * level.
     *
     * INFO is what a normal player needs to observe while the mod is deployed.
     * Something that changes what the mod is doing, or explains why it did not
     * do what was asked, belongs there. Diagnostic decisions belong at DEBUG;
     * call-by-call maintainer detail belongs at TRACE.
     */
    dltb_status (*set_level)(dltb_client client, dltb_log_level level);
} dltb_ns_log;

/*
 * Exclusive, reversible ownership of one path. The Bridge restores the original
 * value on release or on client unregister.
 */
typedef struct dltb_ns_lease {
    uint32_t struct_bytes;
    dltb_status (*claim)(dltb_client client, const char *path,
                         dltb_subject subject, dltb_lease *lease_out,
                         dltb_value *baseline_out);
    dltb_status (*write)(dltb_lease lease, const dltb_value *value);
    dltb_status (*release)(dltb_lease lease);
    /* Return the baseline for the currently bound engine subject. A lease
       write returns DLTB_STALE_SUBJECT after subject replacement until this
       operation refreshes the baseline. Relative clients must query it
       immediately before computing each write. */
    dltb_status (*baseline)(dltb_lease lease, dltb_value *baseline_out);
} dltb_ns_lease;

/*
 * Composable contributions. Unlike a lease, a modifier does not own the target
 * exclusively: the Bridge combines every active contribution using the
 * operation documented for the path.
 */
typedef struct dltb_ns_modifiers {
    uint32_t struct_bytes;
    dltb_status (*acquire)(dltb_client client, const char *path,
                           dltb_subject subject, dltb_modifier *modifier_out);
    dltb_status (*write)(dltb_modifier modifier,
                         const dltb_value *contribution);
    dltb_status (*read)(dltb_modifier modifier, dltb_value *contribution_out,
                        dltb_value *effective_out);
    dltb_status (*release)(dltb_modifier modifier);
    dltb_status (*describe)(dltb_client client, const char *path,
                            dltb_modifier_info *info_out);
    dltb_status (*enumerate)(dltb_client client, const char *prefix,
                             dltb_modifier_info *buffer,
                             uint32_t element_bytes, uint32_t capacity,
                             uint32_t *count_out);
} dltb_ns_modifiers;

typedef struct dltb_ns_events {
    uint32_t struct_bytes;
    dltb_status (*subscribe)(dltb_client client, const char *pattern,
                             dltb_phase phase, int32_t priority,
                             uint32_t options, dltb_event_fn callback,
                             void *context,
                             dltb_subscription *subscription_out);
    dltb_status (*unsubscribe)(dltb_client client,
                               dltb_subscription subscription);
    dltb_status (*publish)(dltb_client client, const dltb_event *event);
    dltb_status (*describe)(dltb_client client, const char *name,
                            dltb_event_info *info_out);
    dltb_status (*enumerate)(dltb_client client, const char *prefix,
                             dltb_event_info *buffer, uint32_t element_bytes,
                             uint32_t capacity, uint32_t *count_out);
} dltb_ns_events;

/* ------------------------------------------------------------------------ */
/* 3. Game concepts                                                           */
/* ------------------------------------------------------------------------ */

typedef struct dltb_ns_inventory {
    uint32_t struct_bytes;
    dltb_status (*count)(dltb_client client, dltb_subject subject,
                         const char *item, int32_t *count_out);
    dltb_status (*give)(dltb_client client, dltb_subject subject,
                        const char *item, int32_t count, int32_t *granted_out);
    dltb_status (*take)(dltb_client client, dltb_subject subject,
                        const char *item, int32_t count, int32_t *taken_out);
    dltb_status (*describe)(dltb_client client, dltb_subject subject,
                            const char *item, dltb_item_info *info_out);
    dltb_status (*enumerate)(dltb_client client, dltb_subject subject,
                             dltb_item_info *buffer, uint32_t element_bytes,
                             uint32_t capacity, uint32_t *count_out);
    /*
     * Apply an item's effect. Replaces ABI 2's `use.consume_direct` and
     * `use.consume_immersive`, which were the same operation in two spellings.
     */
    dltb_status (*use)(dltb_client client, dltb_subject subject,
                       const char *item, dltb_use_mode mode,
                       dltb_action_outcome *outcome);
    /*
     * Consume items to raise a named resource on a subject, clamped at that
     * resource's maximum, rolling the items back if the write fails.
     *
     * This is what ABI 2 expressed twice: `use.consume_resource` for a player
     * and `flashlight.replace_resource` for a torch. The general form is the
     * game's own shape -- it models vehicle fuel the same way, as items poured
     * into a capped tank -- so a refuelling mod and Auto-Battery are the same
     * call with different subjects, and neither needs a Bridge update.
     */
    dltb_status (*transfer)(dltb_client client, dltb_subject subject,
                            const char *item, const char *resource_path,
                            float amount, dltb_action_outcome *outcome);
} dltb_ns_inventory;

/*
 * Survivor sense -- the game's own name, and its own concept.
 *
 * ABI 2 called this `markers`, which was Auto-Markers' word. The game's
 * `Markers()` construct is an AI tactical-points thing carrying
 * `PointWorldSpan` and is unrelated; what this actually drives is described in
 * ai/survivor_sense_presets.scr, with detection range, visibility distance,
 * per-preset colour and night variants. An author reading `survivor_sense` can
 * find it in the game's files.
 *
 * Interception is not here. Suppressing or redirecting a scan is a decision
 * made inside a before-event (see dltb_event::suppress).
 */
typedef struct dltb_ns_survivor_sense {
    uint32_t struct_bytes;
    dltb_status (*publish)(dltb_client client, dltb_subject subject,
                           float radius, uint32_t lifetime_ms);
    dltb_status (*begin)(dltb_client client, dltb_subject subject,
                         float radius, uint32_t lifetime_ms,
                         dltb_sense_session *session_out);
    dltb_status (*refresh)(dltb_client client, dltb_sense_session session);
    dltb_status (*clear)(dltb_client client, dltb_sense_session session);
} dltb_ns_survivor_sense;

/* Facade bounds, chosen to cover the shipped presets and established clients
   while refusing values that can only be unit or arithmetic mistakes. */
#define DLTB_SURVIVOR_SENSE_RADIUS_MIN 0.1f
#define DLTB_SURVIVOR_SENSE_RADIUS_MAX 500.0f
#define DLTB_SURVIVOR_SENSE_LIFETIME_MIN_MS 1U
#define DLTB_SURVIVOR_SENSE_LIFETIME_MAX_MS 60000U

/* ------------------------------------------------------------------------ */
/* Reserved                                                                   */
/* ------------------------------------------------------------------------ */

/*
 * Reserved, and empty. A client-supplied address or symbol can be neither
 * verified nor reversed by the Bridge, which is the one guarantee the platform
 * makes, so the generic call hatch these would become is ruled out.
 */
typedef struct dltb_ns_hooks {
    uint32_t struct_bytes;
    void *reserved[4];
} dltb_ns_hooks;

typedef struct dltb_ns_x {
    uint32_t struct_bytes;
    void *reserved[8];
} dltb_ns_x;

/* ------------------------------------------------------------------------ */
/* Root                                                                       */
/* ------------------------------------------------------------------------ */

typedef struct dltb_api {
    uint32_t struct_bytes;
    uint32_t abi;
    const dltb_build_info *build;
    const dltb_ns_client *client;
    const dltb_ns_state *state;
    const dltb_ns_scope *scope;
    const dltb_ns_log *log;
    const dltb_ns_lease *lease;
    const dltb_ns_modifiers *modifiers;
    const dltb_ns_events *events;
    const dltb_ns_inventory *inventory;
    const dltb_ns_survivor_sense *survivor_sense;
    const dltb_ns_hooks *hooks;
    const dltb_ns_x *x;
} dltb_api;

typedef const dltb_api *(*dltb_get_api3_fn)(uint32_t abi);

/*
 * Availability is per member, always. The root and every domain table are
 * append-only and sized, so a client built against an older minor build checks
 * what it needs and downgrades a feature rather than failing to load.
 */
#define DLTB_API3_ROOT_HAS(api_pointer, member) \
    ((api_pointer) && (api_pointer)->struct_bytes >= \
        (uint32_t)(offsetof(dltb_api, member) + \
                   sizeof((api_pointer)->member)) && \
     (api_pointer)->member)

#define DLTB_API3_DOMAIN_HAS(domain_pointer, domain_type, member) \
    ((domain_pointer) && (domain_pointer)->struct_bytes >= \
        (uint32_t)(offsetof(domain_type, member) + \
                   sizeof((domain_pointer)->member)) && \
     (domain_pointer)->member)

#ifdef __cplusplus
}
#endif

#endif
