-- @name God Mode (example)
-- @description Leases damage multipliers so damage taken drops to nothing. Released on reload or disable.
-- @param incoming number default=-0.9 min=-1.0 max=0.0 label="Incoming damage" group="Damage" desc="Added to the incoming multiplier. -1.0 is immunity; -0.9 leaves a tenth getting through."
-- @param outgoing number default=9.0  min=0.0  max=20.0 label="Outgoing damage" group="Damage" desc="Added to the outgoing multiplier. 0.0 is vanilla."
-- @param announce bool   default=true label="Log what it claimed" group="Diagnostics" desc="Writes each lease to the shared console, so you can see the claim and the release."
--
-- An unmistakable demonstration of Crane 2.0.0. Demonstrates:
--
--   1. a Lua script can change the game;
--   2. what it changed is a *lease*, so the Bridge restores the original value;
--   3. reloading or disabling the script is the off switch -- there is no
--      "unapply" code below, because there does not need to be any;
--   4. the two numbers above are editable in Crane Manager, and changing one
--      re-applies it within a second, with no text editor involved.
--
-- Requires AllowWrites=1, which is the toggle in the manager's header bar.
-- Without it every call here is refused with CRANE_WRITES_DISABLED and the
-- script simply says so.
--
-- USE A SAVE YOU DO NOT MIND LOSING.
--
-- ---------------------------------------------------------------------------
-- What is actually known about these two variables
--
-- `DamageMulAll` and `IncomingDamageMul` are both members of the
-- `DamageTotalMul("?")` family: PlayerVariable floats whose value slot is a
-- literal "?" in damagedefinitions.scr, meaning the engine reads them live.
-- Both are baseline 0.0 and additive -- 0.25 means +25%, -1.0 means -100%.
-- Do not write 1.0 here expecting "no change"; that is +100%.
--
-- `DamageMulAll` was proven live-read in game on 2026-08-12 with CatalogProbe
-- 0.1.0-dev.14. `IncomingDamageMul` is a confirmed FloatPlayerVariable in the
-- published catalog and sits in the same block with the same declared shape,
-- but its live consumer has not been independently proven. So this script is
-- partly an experiment: if outgoing damage changes and incoming does not, that
-- asymmetry is the finding, and it is worth writing down.
--
-- Whether -1.0 clamps to full immunity or goes further (healing when hit) is
-- also unknown. The default is -0.9; confirm the direction before trying -1.0.
-- ---------------------------------------------------------------------------

-- Always state a fallback. Crane does not read the @param declarations above
-- -- only the manager does -- so a parameter missing from the manifest arrives
-- as nil, and `or` turns that into the same default the declaration advertises.
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
  -- The baseline is what the Bridge will put back on release. Log it, so the
  -- restoration can be checked against a recorded number rather than a memory.
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

-- There is no cleanup function here, no unload hook, no bookkeeping of what to
-- restore. Crane releases every lease this script owns when it reloads, is
-- disabled, or the game exits, and the Bridge writes the baseline back. A
-- bridge.set in place of the claim would be permanent instead, and undoing it
-- would be this script's problem.
