-- SPDX-License-Identifier: MIT
-- @name Reversible Interaction Scale
-- @description Demonstrates one exclusive setting restored on reload or disable.
-- @param scale number default=0.5 min=0 max=2 group="Tuning" label="Container hold scale"
-- @claims interaction.Container.duration_scale
-- Requires CRANE write access.

local scale = params.scale
if scale == nil then scale = 0.5 end
if type(scale) ~= "number" or scale < 0 or scale > 2 then
  bridge.log("Reversible Interaction Scale: invalid scale")
  return
end

local handle, claim_error = bridge.claim("interaction.Container.duration_scale")
if not handle then
  bridge.log("Reversible Interaction Scale: claim refused: " .. tostring(claim_error))
  return
end
local ok, write_error = bridge.lease_write(handle, scale)
if not ok then
  bridge.lease_release(handle)
  bridge.log("Reversible Interaction Scale: write refused: " .. tostring(write_error))
end
