-- SPDX-License-Identifier: MIT
-- @name My Configurable CRANE Mod
-- @description A configurable, reversible lease example.
-- @param enabled bool default=true group="General" label="Enabled"
-- @param scale number default=1 min=0 max=4 group="Tuning" label="Scale" desc="Example contribution written while this script is enabled."

local enabled = params.enabled
if enabled == nil then enabled = true end
local scale = params.scale
if scale == nil then scale = 1 end

if type(enabled) ~= "boolean" or type(scale) ~= "number" or scale < 0 or scale > 4 then
  bridge.log("My Configurable CRANE Mod: inactive; invalid settings")
  return
end
if not enabled then return end

-- Replace this example path only after reading its description in the installed Bridge.
-- This script requires CRANE write access because it claims and writes a lease.
local handle, claim_error = bridge.claim("interaction.Container.duration_scale")
if not handle then
  bridge.log("My Configurable CRANE Mod: claim refused: " .. tostring(claim_error))
  return
end

local ok, write_error = bridge.lease_write(handle, scale)
if not ok then
  bridge.lease_release(handle)
  bridge.log("My Configurable CRANE Mod: write refused: " .. tostring(write_error))
  return
end

bridge.log("My Configurable CRANE Mod: active at scale " .. tostring(scale))

