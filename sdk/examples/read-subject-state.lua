-- @name Read Hunger State
-- @description Logs hunger only when the observed value changes.

local last
bridge.on("hunger.updated", 2, function()
  local hunger, resolve_error = bridge.resolve("hunger")
  if not hunger then return end
  local points, read_error = bridge.get("hunger.points", hunger)
  if points ~= nil and points ~= last then
    last = points
    bridge.log("hunger points=" .. tostring(points))
  end
end)

