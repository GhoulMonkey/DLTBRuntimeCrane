# Bridge concepts for CRANE scripts

## Public state paths

CRANE scripts name public state such as `session.playable`, `hunger.points` or
`interaction.Container.duration_scale`. A path names a semantic Bridge capability, which
the Bridge resolves to whatever the current game build requires. Scripts must not carry
RVAs, offsets, raw pointers or native layouts.

## Subjects

Global paths need no subject. Player-, hunger- and flashlight-owned paths do. Resolve the
owner and pass its integer subject id as the final argument:

```lua
local hunger = bridge.resolve("hunger")
local points, err = bridge.get("hunger.points", hunger)
```

Subjects can become stale across load, death or replacement. Resolve them during the script
load or event where they are needed and handle `DLTB_STALE_SUBJECT`.

## Read, set, lease or modifier

- `bridge.get` observes.
- `bridge.set` performs an unmanaged write. CRANE cannot infer when you want it undone.
- A **lease** is exclusive. One owner records a baseline, writes it, and restoration occurs
  on release. CRANE releases script-owned leases on reload, disable, removal and shutdown.
- A **modifier** is composable. Multiple mods may contribute when the path defines a
  composition rule. Releasing one removes only its contribution.

Prefer a lease for an exclusive reversible setting. Prefer a modifier where multiple mods
should combine. Use `set` only when the operation is intentionally unmanaged.

## Events and phases

`bridge.on(name, phase, callback)` uses phase `1` for BEFORE and `2` for AFTER. A callback
receives `{ name, scope, suppressed, data }`. Returning `true` from a BEFORE callback asks
the Bridge to suppress the underlying event. Suppression changes gameplay and must never be
used merely to observe.

Some events fire every update. Log only transitions or rate-limit them. The instruction
budget applies to each entry into Lua; a runaway callback is stopped, but noisy correct code
can still bury useful diagnostics.

## Writes switch

With `AllowWrites=0`, set, claim, lease writes, and modifier acquisition/writes are refused
with `CRANE_WRITES_DISABLED`. Turning writes off releases script-owned leases and modifiers.
The switch governs game-state mutation. It places no other restriction on a script beyond
the Lua environment described below, so run only scripts you trust.

## Runtime limits

CRANE exposes Lua 5.4 base, table, string, math and UTF-8 libraries. It does not expose
`os`, `io`, `debug` or `package`. The interpreter has a 2 MiB allocator limit and each Lua
entry has a 100,000-instruction budget. Scripts share the interpreter, so avoid large
per-event allocations and global monkey-patching.

