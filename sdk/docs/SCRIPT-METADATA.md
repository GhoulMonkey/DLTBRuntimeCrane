# Script metadata and parameters

CraneLoader reads comment metadata from the first 120 lines without executing the script.
CRANE itself does not parse or enforce these declarations.

```lua
-- @name Hunger Example
-- @description Demonstrates configurable behavior.
-- @param enabled bool default=true group="General" label="Enabled"
-- @param rate number default=1 min=0.1 max=4 group="Tuning" label="Rate" desc="Multiplier applied by this script."
-- @param style enum values=gentle|strong default=gentle group="Tuning" label="Style"
-- @param message string default="Ready" group="Display" label="Message"
```

Supported parameter types are `number`, `bool`/`boolean`, `string`, and `enum`.

Options:

| Option | Applies to | Meaning |
|---|---|---|
| `default=` | all | Initial value; quote strings containing spaces |
| `min=`, `max=` | number | Bounds shown and enforced by Crane Manager |
| `values=a|b|c` | enum | Required allowed choices |
| `label=` | all | User-facing control label |
| `group=` | all | Heading in the settings pane |
| `desc=` | all | Explanation beneath the control |

Always provide a runtime fallback and validate values. Declarations assist the UI; a user
can hand-edit the manifest:

```lua
local rate = params.rate
if rate == nil then rate = 1 end
if type(rate) ~= "number" or rate < 0.1 or rate > 4 then
  bridge.log("Hunger Example: inactive; rate must be from 0.1 to 4")
  return
end
```

Parameter keys are case-insensitively unique and at most 40 characters. Keep them to ASCII
letters, digits and underscores for portable tooling.

## Declaring the paths a script leases

A lease is exclusive per path. Two enabled scripts that want the same one do not share it:
the script CRANE loads first gets it and the other is refused, which produces a script that
loads cleanly, reports no error, and does nothing. `@claims` is how a script says which
paths that could happen to, so CraneLoader can warn about it before the game is launched.

```lua
-- @claims interaction.BodyContainer.duration_scale when=body_containers
-- @claims var.AdvancedStaminaRegenerationRelative when=stamina_mul off=1
-- @claims var.DamageMulAll fallback="contributes through Damage Budget instead" needs=damage_budget.lua
```

One path per line, repeated as needed.

| Option | Meaning |
|---|---|
| `when=` | Parameter the claim depends on; the path is only taken while it is on |
| `off=` | The value of that parameter that means "does nothing" (default: `false`, or `0`) |
| `fallback=` | What the script does instead when refused; lower case, it is appended to a sentence |
| `needs=` | Filename of the script `fallback=` depends on; ignored if that script is not enabled |

CRANE never reads any of this, and neither the manifest nor the runtime changes because of
it. A missing declaration costs a warning that does not appear; a wrong one costs a warning
that appears wrongly. Neither can affect which script actually gets the lease, which is
decided by the `bridge.claim` call itself — so a script must still handle refusal at
runtime, exactly as it did before.

Declare the paths rather than expecting tooling to find them. A script that builds its
paths in a loop, or claims one only when a parameter is non-default, cannot be read by
anything short of running it — and running a script to decide whether the user wants to run
it is the thing this whole comment-header convention exists to avoid.

