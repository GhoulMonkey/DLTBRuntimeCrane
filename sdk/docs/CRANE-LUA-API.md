# CRANE Lua API 2.0

This is the normative reference for Lua scripts hosted by CRANE 2.0. CRANE supplies the
global `bridge` table and one script-specific global `params` table. Scripts do not load a
module and must not use the native DLTBRuntimeBridge C ABI.

Unless stated otherwise, a failed Bridge operation returns `nil, "STATUS"`. Status strings
include Bridge statuses such as `DLTB_UNKNOWN_PATH`, `DLTB_STALE_SUBJECT`,
`DLTB_WRONG_SCOPE`, `DLTB_BUSY` and `DLTB_UNSUPPORTED`, plus CRANE statuses such as
`CRANE_WRITES_DISABLED`, `CRANE_WRONG_VALUE_TYPE`, `CRANE_BAD_LEASE`,
`CRANE_BAD_MODIFIER`, `CRANE_LEASE_CAPACITY` and `CRANE_MODIFIER_CAPACITY`.

Bridge values become Lua booleans, integers, numbers or strings. Lease, modifier, subject
and subscription handles are opaque Lua integers. Do not calculate, persist or share them.

## Globals

### `params`

```lua
params: table<string, boolean|integer|number|string>
```

CRANE creates this table immediately before running each enabled script. Values come from
that script's manifest entry. A declaration missing from the manifest is absent, so every
script must provide and validate its own fallback.

### `bridge.scope()`

Returns the current numeric Bridge execution scope. Scripts normally load and reload in the
validated update phase; event callbacks receive the scope in which the event was delivered.
Prefer operation results over branching on raw scope numbers.

### `bridge.log(text)`

Writes one INFO message through CRANE into the shared Bridge log and console. It returns no
value. Do not log every delivery of a per-update event.

From CRANE 2.4.0, a repeated announcement at load time is dropped: while a script's body is
running, a line identical to one that script logged on its previous load is not written again.
Reloading re-runs every listed script, which is how load order stays a function of the manifest,
so without this, ticking one script in the manager made every other script restate itself.

Only the body is affected. Lines logged from event callbacks or update work always go through,
however often they repeat, because a repeat there is a real observation. If a load-time line
should appear every time, put a value or a count in it that changes.

## Discovery and observation

### `bridge.resolve(subject_path)`

```lua
local subject, err = bridge.resolve("hunger")
```

Resolves a subject path such as `player`, `hunger` or `player.flashlight`. Returns an opaque
subject integer, or `nil, status`. A subject can become stale when a level or owned object is
replaced; resolve again rather than retaining it across arbitrary session transitions.

### `bridge.get(path [, subject])`

```lua
local value, err = bridge.get("session.playable")
local points, err = bridge.get("hunger.points", hunger)
```

Reads one typed public state path. Global paths omit the subject; subject-bound paths require
the subject returned by `bridge.resolve`.

### `bridge.describe(path)`

Returns `{ type, access, scopes, tier }`, with numeric values matching the Bridge's public
enums, or `nil, status`. Use it to inspect exact capabilities at runtime. The table
describes the path. Read the path's current value with `bridge.get`.

### `bridge.list([prefix])`

Returns `names, total`, where `names` is an array of matching state paths and `total` is the
true match count. CRANE returns at most 4096 names and logs a warning if clipped. On failure
it returns `nil, status`.

Enumeration describes live public state. A broad reflected-family query can perform
thousands of reads in one update; always supply the narrowest useful prefix.

## Events

### `bridge.on(name, phase, callback)`

```lua
local subscription, err = bridge.on("item.use.completed", 2, function(event)
  bridge.log(tostring(event.data.item))
end)
```

Subscribes the current script to an event. Phase `1` is BEFORE and `2` is AFTER. On success
it returns an opaque subscription id. CRANE owns and automatically removes the subscription
when the script reloads, is disabled, is removed or CRANE shuts down.

The callback receives:

```lua
{
  name = "event.name",
  scope = 2,
  suppressed = false,
  data = { field = value }
}
```

Returning `true` from a BEFORE callback asks the Bridge to suppress the event. A return from
an AFTER callback has no suppressive effect. Callback and payload data are valid only for
that invocation; copy scalar values if they are needed later.

If subscription capacity is exhausted, CRANE raises a Lua error. Other subscription failures
return `nil, status`.

## Unmanaged state write

### `bridge.set(path, value [, subject])`

Writes one settable state path and returns `true`, or `nil, status`. Write access must be
enabled. This operation has no ownership handle and no automatic restoration. Prefer a lease
for an exclusive change that should be undone.

## Exclusive leases

### `bridge.claim(path [, subject])`

Claims an exclusively holdable path. Returns a script-owned lease handle, or `nil, status`.
Write access must be enabled. The Bridge records the baseline when the claim succeeds.

Not every writable path can be leased. A path carries independent access flags:

| | |
|---|---|
| `read` | `bridge.get` works |
| `set` | `bridge.set` works |
| `hold` | `bridge.claim` and `bridge.lease_write` work |
| `modifier` | `bridge.modifier_acquire` works |

`set` and `hold` are unrelated. Most writable paths in `schemas/bridge-catalog.json`
are set-only and cannot be claimed; `flashlight.energy` and `hunger.points` are
both like this. Claiming one returns `nil` with a refusal status. A script that
assumes otherwise installs, validates and then does nothing at runtime.

Among the static paths, only `hunger.drain_multiplier` is leasable. Most leasable
targets live in the dynamic families, such as
`interaction.<Class>.duration_scale`, which `examples/exclusive-lease.lua` uses.

**`var.*` supports both.** A lease takes a variable exclusively; a modifier contributes a
delta that the Bridge sums over the variable's vanilla value, so several scripts can raise one
variable at once without one of them losing. Prefer the modifier unless your script genuinely
has to be the only thing setting the value — and note the two exclude each other per variable:
claiming one that has live contributions is refused, and so is contributing to a leased one.
Check with `bridge.describe(path)` before writing a claim, unless the catalog
already lists the path with `hold`.

Use `bridge.set` for a set-only path. Nothing restores its value when the script
reloads or is disabled.

### When a path supports both `hold` and `modifier`

`hunger.drain_multiplier` supports both, and they are not interchangeable:

| | |
|---|---|
| `bridge.claim` | Exclusive. One script owns the path; every other claim is refused, naming the holder. Use it where your script has to be the only thing setting the value. |
| `bridge.modifier_acquire` | Composable. Several scripts contribute at once and the Bridge combines them under the path's composition rule. |

Prefer the modifier for a value other mods may also want to influence. Claim the
path where a single owner is required, and handle the refusal.

`examples/exclusive-lease.lua` and `examples/composable-modifier.lua` show one
each. The modifier example uses `hunger.drain_multiplier`; the lease example uses
`interaction.Container.duration_scale`, which is `hold`-only.

### Passing the wrong subject

A path's `subject` in the catalog is exact. `""` means the path takes no subject,
and passing one is an error rather than being ignored: `bridge.get` answers
`DLTB_STALE_SUBJECT` and `bridge.modifier_acquire` answers
`DLTB_INVALID_ARGUMENT`. `hunger.drain_multiplier` takes no subject despite being
a hunger path; `hunger.points` takes the `hunger` subject.

### `bridge.lease_write(handle, value)`

Writes through the current script's lease. Returns `true`, or `nil, status`. The value must
match the path type. A handle owned by another script, released handle or invalid integer
returns `CRANE_BAD_LEASE`.

### `bridge.lease_baseline(handle)`

Returns the value recorded when the lease was claimed, or `nil, status`. This is an
observation of Bridge-owned lease state and does not require writes to remain enabled.

### `bridge.lease_release(handle)`

Releases the lease and asks the Bridge to restore its recorded baseline. Returns `true`, or
`nil, status`. CRANE performs the same release automatically for every lease owned by a
script when that script reloads, is disabled, is removed, or CRANE shuts down.

## Composable modifiers

### `bridge.modifier_acquire(path [, subject])`

Acquires one script-owned contribution to a modifier path. Returns a modifier handle, or
`nil, status`. Write access must be enabled. Only paths explicitly published as modifiers
support this operation.

### `bridge.modifier_write(handle, value)`

Sets this handle's contribution and returns `true`, or `nil, status`. It does not replace
other clients' contributions. An invalid or foreign handle returns `CRANE_BAD_MODIFIER`.

### `bridge.modifier_read(handle)`

Returns `contribution, effective`: this script's own contribution and the combined effective
value after the Bridge's composition rule. On failure it returns `nil, status`.

### `bridge.modifier_release(handle)`

Removes this contribution and returns `true`, or `nil, status`. CRANE automatically releases
script-owned modifiers at the same reload/disable/removal/shutdown boundaries as leases.

## Lua environment and containment

CRANE opens Lua 5.4 base, table, string, math and UTF-8 libraries. `os`, `io`, `debug` and
`package` are unavailable. Each entry into Lua has a 100,000-instruction budget and the
shared interpreter has a 2 MiB allocation ceiling. CRANE invokes scripts with protected
calls; a Lua error is recorded and contained rather than unwinding into the game.

