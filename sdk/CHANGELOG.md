# Changelog

## Unreleased

- Adds the `@claims` metadata tag, with `when=`, `off=`, `fallback=` and `needs=`.
  CraneLoader uses it to warn about an exclusive-lease clash between two enabled
  scripts before the game is launched. CRANE does not read it, and a script must
  still handle a refused `bridge.claim` at runtime.
- Metadata is now scanned over the first 120 lines rather than 60, matching
  CraneLoader.
- `validate.ps1` checks `@claims` options, errors on a `when=` naming an
  undeclared parameter, and warns when a script leases without declaring.

## 2.0.0

First public release, alongside CRANE 2.0.0 and DLTBRuntimeBridge 3.0.3.

- First CRANE Lua-only authoring kit.
- Documents all 16 global `bridge` calls and the injected `params` table.
- Adds Lua Language Server annotations, two templates and four focused examples.
- Adds strict metadata/package validation and Vortex archive generation.
- Adds a generated-format public Bridge catalog for stable paths, subjects and events.

