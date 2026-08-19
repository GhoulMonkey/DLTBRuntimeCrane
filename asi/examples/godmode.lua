-- @name God Mode (example)
-- @description Leases damage multipliers so damage taken drops to nothing. Released on reload or disable.
-- @param incoming number default=-0.9 min=-1.0 max=0.0 label="Incoming damage" group="Damage" desc="Added to the incoming multiplier. -1.0 is immunity; -0.9 leaves a tenth getting through."
-- @param outgoing number default=9.0  min=0.0  max=20.0 label="Outgoing damage" group="Damage" desc="Added to the outgoing multiplier. 0.0 is vanilla."
-- @param announce bool   default=true label="Log what it claimed" group="Diagnostics" desc="Writes each lease to the shared console, so you can see the claim and the release."

local INCOMING = params.incoming or -0.9
local OUTGOING = params.outgoing or 9.0
local ANNOUNCE = params.announce
if ANNOUNCE == nil then ANNOUNCE = true end

local held = {}

local function hold(path, value)
  local handle, err = bridge.claim(path)
  if not handle then
    bridge.log("godmode: could not claim " .. path .. ": " .. tostring(err))
    return
  end
  local baseline = bridge.lease_baseline(handle)
  local ok, werr = bridge.lease_write(handle, value)
  if not ok then
    bridge.log("godmode: claimed " .. path .. " but the write failed: " .. tostring(werr))
    bridge.lease_release(handle)
    return
  end
  held[#held + 1] = path
  if ANNOUNCE then
    bridge.log(string.format("godmode: %s %s -> %s", path, tostring(baseline), tostring(value)))
  end
end

hold("var.IncomingDamageMul", INCOMING)
hold("var.DamageMulAll", OUTGOING)

if #held == 0 then
  bridge.log("godmode: nothing was applied. Is 'Allow scripts to change the game' ticked?")
elseif ANNOUNCE then
  bridge.log("godmode: active on " .. #held ..
             " path(s). Untick it in the manager, or re-save this file, to release them.")
end
