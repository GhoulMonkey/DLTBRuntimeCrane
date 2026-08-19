-- @name Hunger Drain Contribution
-- @description Demonstrates a composable modifier contribution.
-- @param multiplier number default=1 min=0 max=4 group="Tuning" label="Drain contribution"
-- Requires CRANE write access.

local multiplier = params.multiplier
if multiplier == nil then multiplier = 1 end
if type(multiplier) ~= "number" or multiplier < 0 or multiplier > 4 then
  bridge.log("Hunger Drain Contribution: invalid multiplier")
  return
end

local handle, acquire_error = bridge.modifier_acquire("hunger.drain_multiplier")
if not handle then
  bridge.log("Hunger Drain Contribution: acquire refused: " .. tostring(acquire_error))
  return
end
local ok, write_error = bridge.modifier_write(handle, multiplier)
if not ok then
  bridge.modifier_release(handle)
  bridge.log("Hunger Drain Contribution: write refused: " .. tostring(write_error))
end
