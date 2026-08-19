# Script metadata and parameters

Crane Manager reads comment metadata from the first 60 lines without executing the script.
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

