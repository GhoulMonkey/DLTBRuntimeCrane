// SPDX-License-Identifier: GPL-3.0-only
/*
 * Offline tests for the Crane manifest reader.
 *
 * The manifest is the one place Crane parses user-editable input by hand, so
 * it is tested rather than inspected. No Bridge, no game, no Lua state: the
 * parser carries no globals and no Win32, so this runs on any build machine,
 * and it is wired into build.ps1 ahead of the ASI link.
 *
 * Cases are grouped by what a failure would cost. The accept cases protect
 * ordinary use; the reject cases protect the two properties that matter --
 * that a broken manifest runs nothing at all rather than a half-parsed prefix,
 * and that a "file" entry can never escape scripts\.
 */
#include <stdio.h>
#include <string.h>

#include "../ManifestParse.h"

static int g_failures;
static int g_checks;

static void check(int condition, const char *what) {
    g_checks++;
    if (!condition) {
        g_failures++;
        printf("  FAIL: %s\n", what);
    }
}

static void accepts(const char *label, const char *text, unsigned expected) {
    manifest_result out;
    int ok = manifest_parse(text, strlen(text), &out);
    printf("accept %s\n", label);
    check(ok, "should parse");
    if (!ok) { printf("    error was: %s\n", out.error); return; }
    check(out.count == expected, "entry count");
    if (out.count != expected)
        printf("    expected %u, got %u\n", expected, out.count);
}

static void rejects(const char *label, const char *text) {
    manifest_result out;
    int ok = manifest_parse(text, strlen(text), &out);
    printf("reject %s\n", label);
    check(!ok, "should be refused");
    if (ok) return;
    check(out.error[0] != '\0', "carries a reason");
    /* A refusal must run nothing at all, not the prefix it managed to read. */
    check(out.count == 0, "loads no scripts on failure");
    printf("    -> %s\n", out.error);
}

int main(void) {
    manifest_result out;

    accepts("minimal", "{\"scripts\":[]}", 0);
    accepts("one script", "{\"version\":1,\"scripts\":[{\"file\":\"a.lua\",\"enabled\":true}]}", 1);
    accepts("enabled defaults to true", "{\"scripts\":[{\"file\":\"a.lua\"}]}", 1);
    accepts("ordered pair", "{\"scripts\":[{\"file\":\"a.lua\"},{\"file\":\"b.lua\",\"enabled\":false}]}", 2);
    accepts("whitespace and newlines",
            "{\n  \"version\" : 1 ,\n  \"scripts\" : [\n    { \"file\" : \"a.lua\" }\n  ]\n}\n", 1);
    accepts("empty object", "{}", 0);

    rejects("truncated", "{\"scripts\":[{\"file\":\"a.lua\"");
    rejects("trailing comma in array", "{\"scripts\":[{\"file\":\"a.lua\"},]}");
    rejects("unknown top-level key", "{\"scrpits\":[]}");
    rejects("unknown entry key", "{\"scripts\":[{\"file\":\"a.lua\",\"order\":2}]}");
    rejects("entry without file", "{\"scripts\":[{\"enabled\":true}]}");
    rejects("enabled is not a bool", "{\"scripts\":[{\"file\":\"a.lua\",\"enabled\":1}]}");
    rejects("duplicate file", "{\"scripts\":[{\"file\":\"a.lua\"},{\"file\":\"A.LUA\"}]}");
    rejects("content after the close", "{\"scripts\":[]} {\"scripts\":[]}");
    rejects("unterminated string", "{\"scripts\":[{\"file\":\"a.lua}]}");
    rejects("version is not a number", "{\"version\":\"one\",\"scripts\":[]}");

    /* Path containment. These are the cases where accepting too much would let
       an edited manifest reach outside scripts\. */
    rejects("parent traversal", "{\"scripts\":[{\"file\":\"..\\\\evil.lua\"}]}");
    rejects("subdirectory", "{\"scripts\":[{\"file\":\"sub/evil.lua\"}]}");
    rejects("backslash path", "{\"scripts\":[{\"file\":\"sub\\\\evil.lua\"}]}");
    rejects("absolute path", "{\"scripts\":[{\"file\":\"C:\\\\evil.lua\"}]}");
    rejects("empty name", "{\"scripts\":[{\"file\":\"\"}]}");

    /* ---- parameters ----------------------------------------------------
       The one nested object in the schema, and the reason this parser grew at
       all. Bounds are asserted here rather than trusted, since every one of
       them is reachable from a hand-edited file. */
    printf("\nparameters\n");
    accepts("empty params", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{}}]}", 1);
    accepts("three value types",
            "{\"scripts\":[{\"file\":\"a.lua\",\"params\":"
            "{\"speed\":1.5,\"loud\":false,\"mode\":\"fast\"}}]}", 1);
    accepts("negative and exponent",
            "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":-0.9,\"b\":1e3}}]}", 1);

    rejects("param object value", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":{}}}]}");
    rejects("param array value", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":[1]}}]}");
    rejects("duplicate param", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":1,\"A\":2}}]}");
    rejects("empty param name", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"\":1}}]}");
    rejects("param missing value", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":}}]}");
    rejects("param not an object", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":5}]}");

    /* Values must survive parsing: a number the script acts on is useless if
       the reader only stepped past it. */
    printf("values survive\n");
    {
        manifest_result parsed;
        const char *text =
            "{\"scripts\":[{\"file\":\"a.lua\",\"params\":"
            "{\"speed\":-0.9,\"loud\":true,\"mode\":\"fast\"}}]}";
        check(manifest_parse(text, strlen(text), &parsed), "parses");
        check(parsed.entries[0].param_count == 3, "three parameters kept");
        check(parsed.entries[0].params[0].type == MANIFEST_PARAM_NUMBER, "first is a number");
        check(parsed.entries[0].params[0].number < -0.89 &&
              parsed.entries[0].params[0].number > -0.91, "number value is -0.9");
        check(parsed.entries[0].params[1].type == MANIFEST_PARAM_BOOL, "second is a bool");
        check(parsed.entries[0].params[1].boolean == 1, "bool value is true");
        check(parsed.entries[0].params[2].type == MANIFEST_PARAM_STRING, "third is a string");
        check(strcmp(parsed.entries[0].params[2].text, "fast") == 0, "string value is \"fast\"");
        check(strcmp(parsed.entries[0].params[0].key, "speed") == 0, "key is preserved");
    }

    /* The ceiling is reachable from an edited file, so it must refuse rather
       than overflow. */
    printf("parameter ceiling\n");
    {
        char big[4096];
        int n = 0, i;
        n += sprintf_s(big + n, sizeof(big) - (size_t)n, "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{");
        for (i = 0; i < CRANE_MANIFEST_MAX_PARAMS + 1; i++)
            n += sprintf_s(big + n, sizeof(big) - (size_t)n, "%s\"k%d\":%d", i ? "," : "", i, i);
        n += sprintf_s(big + n, sizeof(big) - (size_t)n, "}}]}");
        rejects("one parameter over the ceiling", big);
    }

    /* A failure must not leave stale entries visible to the caller. */
    printf("state after failure\n");
    manifest_parse("{\"scripts\":[{\"file\":\"a.lua\"},{\"file\":\"a.lua\"}]}",
                   47, &out);
    check(out.count == 0, "duplicate rejection clears every entry");
    check(out.entries[0].file[0] == '\0', "first entry is cleared too");

    printf("\n%d checks, %d failure(s)\n", g_checks, g_failures);
    return g_failures ? 1 : 0;
}
