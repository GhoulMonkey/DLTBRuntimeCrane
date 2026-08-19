-- @name My CRANE Mod
-- @description A quiet read-only starting point.

local announced = false

bridge.on("player.updated", 2, function()
  if announced then return end
  local playable = bridge.get("session.playable")
  if playable then
    announced = true
    bridge.log("My CRANE Mod: active")
  end
end)

