#ifndef DLTB_RUNTIME_BRIDGE_API3_H
#define DLTB_RUNTIME_BRIDGE_API3_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define DLTB_API3_ABI 3U

#define DLTB_API3_PATH_MAX 128U
#define DLTB_API3_CLIENT_NAME_MAX 64U
#define DLTB_API3_CLIENT_SUMMARY_MAX 128U
#define DLTB_API3_UNITS_MAX 32U
#define DLTB_API3_TEXT_MAX 128U
#define DLTB_API3_MANIFEST_FEATURE_MAX 64U

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

    DLTB_WRONG_SCOPE = 11,
    DLTB_OUT_OF_RANGE = 12,
    DLTB_REFUSED_UNSAFE = 13,
    DLTB_TRUNCATED = 14,
    DLTB_INVALID_ARGUMENT = 15,
    DLTB_NO_CAPACITY = 16,
    DLTB_PARTIAL = 17,
    DLTB_NOT_FOUND = 18,
    DLTB_BUSY = 19,

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

typedef enum dltb_scope {
    DLTB_SCOPE_NONE = 0,

    DLTB_SCOPE_ANY = 1u << 0,

    DLTB_SCOPE_UPDATE = 1u << 1,

    DLTB_SCOPE_DETOUR = 1u << 2
} dltb_scope;

#define DLTB_SCOPE_ENGINE (DLTB_SCOPE_UPDATE | DLTB_SCOPE_DETOUR)

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

    uint32_t scopes;
    char units[DLTB_API3_UNITS_MAX];

    uint32_t owned;
    char owner[DLTB_API3_CLIENT_NAME_MAX];
} dltb_path_info;

typedef enum dltb_subject_source {
    DLTB_SUBJECT_SOURCE_LIVE = 1,

    DLTB_SUBJECT_SOURCE_OBSERVED = 2
} dltb_subject_source;

typedef struct dltb_subject_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_PATH_MAX];
    dltb_subject_source source;
    dltb_tier tier;
    uint32_t resolve_scopes;
} dltb_subject_info;

typedef enum dltb_phase {
    DLTB_PHASE_BEFORE = 1,
    DLTB_PHASE_AFTER = 2
} dltb_phase;

#define DLTB_API3_EVENT_PAYLOAD_MAX 8U

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

    dltb_client publisher;
    char publisher_name[DLTB_API3_CLIENT_NAME_MAX];
    uint32_t payload_count;
    dltb_named_value payload[DLTB_API3_EVENT_PAYLOAD_MAX];

    uint32_t suppress;
} dltb_event;

typedef struct dltb_event_info {
    uint32_t struct_bytes;
    char name[DLTB_API3_PATH_MAX];
    dltb_phase phase;
    dltb_tier tier;

    dltb_scope delivery_scope;
    uint32_t payload_count;
} dltb_event_info;

typedef void (*dltb_event_fn)(dltb_event *event, dltb_scope scope,
                              void *context);

enum { DLTB_SUBSCRIBE_ONCE = 1u << 0 };
typedef void (*dltb_task_fn)(dltb_scope scope, void *context);

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

typedef enum dltb_use_mode {
    DLTB_USE_DIRECT = 1,

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

typedef enum dltb_use_reason {
    DLTB_USE_REASON_NONE = 0,
    DLTB_USE_REASON_INTERRUPTED = 1,
    DLTB_USE_REASON_TIMEOUT = 2
} dltb_use_reason;

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

typedef struct dltb_ns_client {
    uint32_t struct_bytes;
    dltb_status (*register_client)(const dltb_manifest *manifest,
                                   dltb_client *client_out);
    dltb_status (*unregister_client)(dltb_client client);

    dltb_status (*requirement)(dltb_client client, const char *name,
                               dltb_feature_info *info_out);
    dltb_status (*enumerate_requirements)(
        dltb_client client, dltb_feature_info *buffer,
        uint32_t element_bytes, uint32_t capacity, uint32_t *count_out);

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

    dltb_status (*resolve)(dltb_client client, const char *path,
                           dltb_subject *subject_out);

    dltb_status (*describe_subject)(dltb_client client, const char *name,
                                    dltb_subject_info *info_out);
    dltb_status (*enumerate_subjects)(
        dltb_client client, const char *prefix, dltb_subject_info *buffer,
        uint32_t element_bytes, uint32_t capacity, uint32_t *count_out);
} dltb_ns_state;

typedef struct dltb_ns_scope {
    uint32_t struct_bytes;

    dltb_scope (*current)(void);

    dltb_status (*required)(dltb_client client, const char *path,
                            uint32_t *scopes_out);

    dltb_status (*schedule)(dltb_client client, dltb_task_fn callback,
                            void *context, dltb_task *task_out);
    dltb_status (*cancel)(dltb_client client, dltb_task task);

    dltb_status (*describe)(dltb_client client, const char *operation,
                            dltb_operation_info *info_out);
    dltb_status (*enumerate)(dltb_client client, const char *prefix,
                             dltb_operation_info *buffer,
                             uint32_t element_bytes, uint32_t capacity,
                             uint32_t *count_out);
} dltb_ns_scope;

typedef enum dltb_log_level {
    DLTB_LOG_OFF = 0,

    DLTB_LOG_INFO = 1,

    DLTB_LOG_DEBUG = 2,

    DLTB_LOG_TRACE = 3
} dltb_log_level;

typedef enum dltb_log_class {
    DLTB_LOG_CLASS_INFO = 1,
    DLTB_LOG_CLASS_DEBUG = 2,
    DLTB_LOG_CLASS_TRACE = 3,

    DLTB_LOG_CLASS_WARN = 4,

    DLTB_LOG_CLASS_ERROR = 5
} dltb_log_class;

typedef struct dltb_ns_log {
    uint32_t struct_bytes;

    void (*write)(dltb_client client, dltb_log_class line_class,
                  const char *message);

    dltb_log_level (*level)(dltb_client client);

    dltb_status (*set_level)(dltb_client client, dltb_log_level level);
} dltb_ns_log;

typedef struct dltb_ns_lease {
    uint32_t struct_bytes;
    dltb_status (*claim)(dltb_client client, const char *path,
                         dltb_subject subject, dltb_lease *lease_out,
                         dltb_value *baseline_out);
    dltb_status (*write)(dltb_lease lease, const dltb_value *value);
    dltb_status (*release)(dltb_lease lease);

    dltb_status (*baseline)(dltb_lease lease, dltb_value *baseline_out);
} dltb_ns_lease;

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

    dltb_status (*use)(dltb_client client, dltb_subject subject,
                       const char *item, dltb_use_mode mode,
                       dltb_action_outcome *outcome);

    dltb_status (*transfer)(dltb_client client, dltb_subject subject,
                            const char *item, const char *resource_path,
                            float amount, dltb_action_outcome *outcome);
} dltb_ns_inventory;

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

#define DLTB_SURVIVOR_SENSE_RADIUS_MIN 0.1f
#define DLTB_SURVIVOR_SENSE_RADIUS_MAX 500.0f
#define DLTB_SURVIVOR_SENSE_LIFETIME_MIN_MS 1U
#define DLTB_SURVIVOR_SENSE_LIFETIME_MAX_MS 60000U

typedef struct dltb_ns_hooks {
    uint32_t struct_bytes;
    void *reserved[4];
} dltb_ns_hooks;

typedef struct dltb_ns_x {
    uint32_t struct_bytes;
    void *reserved[8];
} dltb_ns_x;

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
