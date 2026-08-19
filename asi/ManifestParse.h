/*
 * Crane manifest reader -- DLTBRuntimeCrane.manifest.json.
 *
 * Split out of Crane.c so it can be exercised offline by
 * tools\test_manifest.c without a Bridge, a game, or a Lua state. It is the
 * one piece of Crane that parses user-editable input by hand, which makes
 * it the piece most worth testing rather than inspecting.
 *
 * It has no globals, does no logging and touches no Win32: text in, entries
 * or an error string out. The caller decides what to do about a failure.
 *
 * A strict reader for one fixed shape, rather than a general JSON parser:
 *
 *   {
 *     "version": 1,
 *     "scripts": [
 *       { "file": "a.lua", "enabled": true,
 *         "params": { "speed": 1.5, "loud": false, "mode": "fast" } }
 *     ]
 *   }
 *
 * The schema is small and fixed, so a reader that accepts exactly it and
 * refuses everything else is both smaller and easier to reason about than a
 * general one -- and this file is user-editable input, which is the case where
 * "accepts more than it should" turns into a bug report.
 *
 * `params` is the one nested object, and it is bounded hard: keys and values
 * are length-capped, the count is capped, values may only be a number, a
 * boolean or a string, and nothing nests inside them. Parameters were the
 * feature that expanded this parser at all; those limits are what keep the
 * expansion from being open-ended.
 *
 * Failures carry the line and what was expected. A bare "manifest invalid"
 * would be the defect here: a syntax error, a missing "file" key and a
 * duplicate entry are three different fixes.
 */
#ifndef CRANE_MANIFEST_PARSE_H
#define CRANE_MANIFEST_PARSE_H

#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define CRANE_MANIFEST_MAX_SCRIPTS 64
#define CRANE_MANIFEST_MAX_NAME 128
#define CRANE_MANIFEST_MAX_ERROR 192
#define CRANE_MANIFEST_MAX_PARAMS 16
#define CRANE_MANIFEST_MAX_KEY 48
#define CRANE_MANIFEST_MAX_TEXT 96

typedef enum manifest_param_type {
    MANIFEST_PARAM_NUMBER = 0,
    MANIFEST_PARAM_BOOL = 1,
    MANIFEST_PARAM_STRING = 2
} manifest_param_type;

typedef struct manifest_param {
    char key[CRANE_MANIFEST_MAX_KEY];
    manifest_param_type type;
    double number;
    int boolean;
    char text[CRANE_MANIFEST_MAX_TEXT];
} manifest_param;

typedef struct manifest_entry {
    int enabled;
    char file[CRANE_MANIFEST_MAX_NAME];
    manifest_param params[CRANE_MANIFEST_MAX_PARAMS];
    unsigned param_count;
} manifest_entry;

typedef struct manifest_result {
    manifest_entry entries[CRANE_MANIFEST_MAX_SCRIPTS];
    unsigned count;
    char error[CRANE_MANIFEST_MAX_ERROR];
} manifest_result;

typedef struct manifest_reader {
    const char *p;
    const char *end;
    int line;
    char error[CRANE_MANIFEST_MAX_ERROR];
} manifest_reader;

static void manifest_fail(manifest_reader *r, const char *what) {
    if (!r->error[0])
        _snprintf_s(r->error, sizeof(r->error), _TRUNCATE, "line %d: %s", r->line, what);
}

static void manifest_skip_ws(manifest_reader *r) {
    while (r->p < r->end) {
        if (*r->p == '\n') { r->line++; r->p++; }
        else if (*r->p == ' ' || *r->p == '\t' || *r->p == '\r') r->p++;
        else break;
    }
}

static int manifest_peek(manifest_reader *r, char c) {
    manifest_skip_ws(r);
    return r->p < r->end && *r->p == c;
}

static int manifest_take(manifest_reader *r, char c) {
    if (!manifest_peek(r, c)) return 0;
    r->p++;
    return 1;
}

static int manifest_expect(manifest_reader *r, char c) {
    char what[32];
    if (manifest_take(r, c)) return 1;
    _snprintf_s(what, sizeof(what), _TRUNCATE, "expected '%c'", c);
    manifest_fail(r, what);
    return 0;
}

static int manifest_string(manifest_reader *r, char *out, size_t cap) {
    size_t used = 0;
    if (!manifest_expect(r, '"')) return 0;
    while (r->p < r->end && *r->p != '"') {
        char c = *r->p++;
        if (c == '\n') { manifest_fail(r, "unterminated string"); return 0; }
        if (c == '\\') {
            if (r->p >= r->end) { manifest_fail(r, "unterminated escape"); return 0; }
            c = *r->p++;
            if (c == 'n') c = '\n';
            else if (c == 't') c = '\t';
            else if (c != '"' && c != '\\' && c != '/') {
                manifest_fail(r, "unsupported escape; use \\\" \\\\ \\/ \\n \\t");
                return 0;
            }
        }
        if (used + 1 >= cap) { manifest_fail(r, "string is too long"); return 0; }
        out[used++] = c;
    }
    if (!manifest_expect(r, '"')) return 0;
    out[used] = '\0';
    return 1;
}

static int manifest_bool(manifest_reader *r, int *out) {
    manifest_skip_ws(r);
    if ((size_t)(r->end - r->p) >= 4 && memcmp(r->p, "true", 4) == 0) {
        r->p += 4; *out = 1; return 1;
    }
    if ((size_t)(r->end - r->p) >= 5 && memcmp(r->p, "false", 5) == 0) {
        r->p += 5; *out = 0; return 1;
    }
    manifest_fail(r, "expected true or false");
    return 0;
}

/* Reads a number and hands back its value, rather than skipping past it:
   parameter values are numbers the script will act on, so they have to survive
   parsing rather than merely be stepped over. */
static int manifest_number(manifest_reader *r, double *out) {
    char buffer[64];
    size_t used = 0;
    int digits = 0;
    manifest_skip_ws(r);
    if (r->p < r->end && (*r->p == '-' || *r->p == '+')) buffer[used++] = *r->p++;
    while (r->p < r->end &&
           ((*r->p >= '0' && *r->p <= '9') || *r->p == '.' || *r->p == 'e' ||
            *r->p == 'E' || *r->p == '-' || *r->p == '+')) {
        if (*r->p >= '0' && *r->p <= '9') digits++;
        if (used + 1 >= sizeof(buffer)) { manifest_fail(r, "number is too long"); return 0; }
        buffer[used++] = *r->p++;
    }
    buffer[used] = '\0';
    if (!digits) { manifest_fail(r, "expected a number"); return 0; }
    if (out) *out = atof(buffer);
    return 1;
}

/*
 * Scripts must be plain file names inside scripts\. The manifest is written by
 * a tool and editable by hand, so a traversal or absolute path is rejected
 * rather than trusted.
 */
static int manifest_valid_name(const char *name) {
    size_t i;
    if (!name[0]) return 0;
    if (strstr(name, "..")) return 0;
    if (name[0] && name[1] == ':') return 0;
    for (i = 0; name[i]; ++i)
        if (name[i] == '/' || name[i] == '\\') return 0;
    return 1;
}

static int manifest_same_name(const char *a, const char *b) {
    size_t i;
    for (i = 0; a[i] && b[i]; ++i) {
        char ca = a[i], cb = b[i];
        if (ca >= 'A' && ca <= 'Z') ca = (char)(ca - 'A' + 'a');
        if (cb >= 'A' && cb <= 'Z') cb = (char)(cb - 'A' + 'a');
        if (ca != cb) return 0;
    }
    return a[i] == b[i];
}

/* One "params" object: flat, bounded, and three value types only. */
static int manifest_params(manifest_reader *r, manifest_entry *entry) {
    if (!manifest_expect(r, '{')) return 0;
    if (manifest_peek(r, '}')) return manifest_expect(r, '}');
    do {
        manifest_param param;
        unsigned existing;
        memset(&param, 0, sizeof(param));
        if (manifest_peek(r, '}')) break;
        if (!manifest_string(r, param.key, sizeof(param.key))) return 0;
        if (!param.key[0]) { manifest_fail(r, "a parameter has an empty name"); return 0; }
        if (!manifest_expect(r, ':')) return 0;

        manifest_skip_ws(r);
        if (r->p >= r->end) { manifest_fail(r, "expected a parameter value"); return 0; }
        if (*r->p == '"') {
            param.type = MANIFEST_PARAM_STRING;
            if (!manifest_string(r, param.text, sizeof(param.text))) return 0;
        } else if (*r->p == 't' || *r->p == 'f') {
            param.type = MANIFEST_PARAM_BOOL;
            if (!manifest_bool(r, &param.boolean)) return 0;
        } else if (*r->p == '{' || *r->p == '[') {
            manifest_fail(r, "parameter values cannot be objects or arrays");
            return 0;
        } else {
            param.type = MANIFEST_PARAM_NUMBER;
            if (!manifest_number(r, &param.number)) return 0;
        }

        for (existing = 0; existing < entry->param_count; ++existing)
            if (manifest_same_name(entry->params[existing].key, param.key)) {
                manifest_fail(r, "duplicate parameter name");
                return 0;
            }
        if (entry->param_count == CRANE_MANIFEST_MAX_PARAMS) {
            manifest_fail(r, "too many parameters for one script");
            return 0;
        }
        entry->params[entry->param_count++] = param;
    } while (manifest_take(r, ','));
    return manifest_expect(r, '}');
}

/* Returns 1 on success. On failure `out->error` says which line and what,
   and `out->count` is 0 -- a broken manifest runs nothing rather than
   running the prefix it managed to parse. */
static int manifest_parse(const char *text, size_t length, manifest_result *out) {
    manifest_reader reader;
    unsigned count = 0;

    memset(out, 0, sizeof(*out));
    memset(&reader, 0, sizeof(reader));
    reader.p = text;
    reader.end = text + length;
    reader.line = 1;

    if (!manifest_expect(&reader, '{')) goto failed;
    do {
        char key[64];
        if (manifest_peek(&reader, '}')) break;
        if (!manifest_string(&reader, key, sizeof(key))) goto failed;
        if (!manifest_expect(&reader, ':')) goto failed;
        if (strcmp(key, "version") == 0) {
            if (!manifest_number(&reader, NULL)) goto failed;
        } else if (strcmp(key, "scripts") == 0) {
            if (!manifest_expect(&reader, '[')) goto failed;
            if (!manifest_peek(&reader, ']')) do {
                manifest_entry entry;
                int have_file = 0;
                unsigned existing;
                memset(&entry, 0, sizeof(entry));
                entry.enabled = 1;
                if (!manifest_expect(&reader, '{')) goto failed;
                do {
                    char field[64];
                    if (manifest_peek(&reader, '}')) break;
                    if (!manifest_string(&reader, field, sizeof(field))) goto failed;
                    if (!manifest_expect(&reader, ':')) goto failed;
                    if (strcmp(field, "file") == 0) {
                        if (!manifest_string(&reader, entry.file, sizeof(entry.file))) goto failed;
                        have_file = 1;
                    } else if (strcmp(field, "enabled") == 0) {
                        if (!manifest_bool(&reader, &entry.enabled)) goto failed;
                    } else if (strcmp(field, "params") == 0) {
                        if (!manifest_params(&reader, &entry)) goto failed;
                    } else {
                        manifest_fail(&reader, "unknown key in a scripts entry; expected file, enabled or params");
                        goto failed;
                    }
                } while (manifest_take(&reader, ','));
                if (!manifest_expect(&reader, '}')) goto failed;
                if (!have_file) { manifest_fail(&reader, "a scripts entry has no \"file\""); goto failed; }
                if (!manifest_valid_name(entry.file)) {
                    manifest_fail(&reader, "\"file\" must be a plain name inside scripts\\");
                    goto failed;
                }
                if (count == CRANE_MANIFEST_MAX_SCRIPTS) {
                    manifest_fail(&reader, "too many scripts");
                    goto failed;
                }
                for (existing = 0; existing < count; ++existing)
                    if (manifest_same_name(out->entries[existing].file, entry.file)) {
                        manifest_fail(&reader, "duplicate \"file\" entry");
                        goto failed;
                    }
                out->entries[count++] = entry;
            } while (manifest_take(&reader, ','));
            if (!manifest_expect(&reader, ']')) goto failed;
        } else {
            manifest_fail(&reader, "unknown top-level key; expected version or scripts");
            goto failed;
        }
    } while (manifest_take(&reader, ','));
    if (!manifest_expect(&reader, '}')) goto failed;

    /* Trailing content is a mistake worth naming: it usually means a second
       object was pasted after the first. */
    manifest_skip_ws(&reader);
    if (reader.p != reader.end) {
        manifest_fail(&reader, "unexpected content after the closing '}'");
        goto failed;
    }

    out->count = count;
    return 1;

failed:
    memset(out->entries, 0, sizeof(out->entries));
    out->count = 0;
    strncpy_s(out->error, sizeof(out->error), reader.error, _TRUNCATE);
    return 0;
}

#endif /* CRANE_MANIFEST_PARSE_H */
