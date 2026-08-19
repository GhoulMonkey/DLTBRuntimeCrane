-- @name Quick Hands
-- @description Shortens hold time for six separately configurable loot interaction classes.
-- @param duration_percent number default=25 min=0 max=800 label="Hold time" group="Speed" desc="Percentage of the vanilla hold time, applied to every class ticked below. 100 is vanilla; lower is faster."
-- @param body_containers bool default=true group="What to speed up" label="Bodies" desc="Searching a corpse."
-- @param containers bool default=true group="What to speed up" label="Containers" desc="Crates, lockers and the ordinary lootable world."
-- @param collectables bool default=true group="What to speed up" label="Collectable objects" desc="Single pick-ups taken straight into the inventory."
-- @param complex_containers bool default=true group="What to speed up" label="Complex containers" desc="Multi-slot containers such as vehicle storage."
-- @param quest_inventory bool default=true group="What to speed up" label="Quest inventory items" desc="Quest items taken from the world."
-- @param quest_lootables bool default=true group="What to speed up" label="Quest-lootable items" desc="Quest containers and bodies. Leave on unless a quest times you deliberately."
-- Quick Hands 3.1.0-dev.3 for Crane 2.0.0-dev.15.
-- Requires AllowWrites=1 in DLTBRuntimeCrane.ini.

-- Crane publishes manifest values but deliberately does not parse or enforce
-- the declarations above. Every parameter therefore has a matching fallback,
-- and hand-edited out-of-range/type-invalid values fail closed.
local duration_percent = params.duration_percent
if duration_percent == nil then duration_percent = 25 end
if type(duration_percent) ~= "number" or duration_percent < 0 or duration_percent > 800 then
  bridge.log("Quick Hands: inactive; Hold time (%) must be a number from 0 to 800")
  return
end
local DURATION_SCALE = duration_percent / 100

local function bool_param(key, fallback, label)
  local value = params[key]
  if value == nil then return fallback end
  if type(value) ~= "boolean" then
    bridge.log("Quick Hands: inactive; " .. label .. " must be true or false")
    return nil, false
  end
  return value, true
end

-- This is the complete loot policy carried by the native 3.0.0-dev.11 client.
-- Keep it explicit: broad interaction-prefix discovery would also speed up seats,
-- portals, resting places, switches, refuelling, and repairs.
local declarations = {
  { "BodyContainer", "body_containers", "Bodies" },
  { "Container", "containers", "Containers" },
  { "CollectableObject", "collectables", "Collectable objects" },
  { "ComplexContainer", "complex_containers", "Complex containers" },
  { "QuestInventoryItemDI", "quest_inventory", "Quest inventory items" },
  { "QuestLootableItem", "quest_lootables", "Quest-lootable items" }
}

local LOOT_CLASSES = {}
for _, declaration in ipairs(declarations) do
  local enabled, valid = bool_param(declaration[2], true, declaration[3])
  if valid == false then return end
  if enabled then LOOT_CLASSES[#LOOT_CLASSES + 1] = declaration[1] end
end

if #LOOT_CLASSES == 0 then
  bridge.log("Quick Hands: inactive; no loot classes are enabled")
  return
end

local held = {}

local function release_all()
  for i = #held, 1, -1 do
    bridge.lease_release(held[i])
  end
  held = {}
end

for _, class_name in ipairs(LOOT_CLASSES) do
  local path = "interaction." .. class_name .. ".duration_scale"
  local handle, claim_error = bridge.claim(path)
  if not handle then
    release_all()
    bridge.log("Quick Hands: inactive; could not claim " .. path .. ": " ..
               tostring(claim_error))
    return
  end

  held[#held + 1] = handle
  local ok, write_error = bridge.lease_write(handle, DURATION_SCALE)
  if not ok then
    release_all()
    bridge.log("Quick Hands: inactive; could not write " .. path .. ": " ..
               tostring(write_error))
    return
  end
end

bridge.log("Quick Hands: active; " .. #LOOT_CLASSES .. " loot class(es) at " ..
           string.format("%.0f%%", DURATION_SCALE * 100) .. " hold time")
