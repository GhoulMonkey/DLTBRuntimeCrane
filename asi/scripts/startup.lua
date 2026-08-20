-- SPDX-License-Identifier: MIT
-- @name Startup
-- @description Crane's default script: quiet, read-only, safe to leave enabled.
--
-- Crane's automatically reloaded development script.
--
-- Save this file while the game is running. Crane releases everything this
-- script owns -- subscriptions, leases, modifiers -- then loads the new version
-- in the next Bridge update phase.
--
-- The @name and @description lines above are read by the manifest manager. It
-- parses them as text and never runs the script to find out what it is.

bridge.log("startup.lua loaded; session is " .. tostring(bridge.get("session.state")))

-- Discovery: bridge.list(prefix) returns matching path names and the true
-- total. Filter by prefix. An unfiltered "var." listing reads live state for
-- all 3111 PlayerVariables in one update phase, so keep it out of startup and
-- run it from the pipe console when it is actually the question.
--
--   local names, total = bridge.list("var.Hunger")
--   for i = 1, #names do bridge.log(names[i]) end
--   bridge.log("matched " .. total)

-- Subject-bound state (2.0.0). Paths like hunger.*, player.* and flashlight.*
-- need a subject; bridge.resolve returns one. Through 1.0.2 these all failed
-- with DLTB_STALE_SUBJECT.
--
--   local player = bridge.resolve("player")
--   bridge.log("hunger=" .. tostring(bridge.get("hunger.points", player)))

-- Keep the installed default quiet. `hunger.updated` fires about once per
-- frame, so logging it directly floods the console and can obscure real
-- diagnostics. Store the last value and log only on change:
--
--   local last
--   bridge.on("flashlight.updated", 2, function(e)
--       if e.data.energy ~= last then
--           last = e.data.energy
--           bridge.log("flashlight energy=" .. tostring(last))
--       end
--   end)

-- Writing (2.0.0) needs AllowWrites=1 in DLTBRuntimeCrane.ini. Until then every write
-- returns nil, "CRANE_WRITES_DISABLED". Prefer a lease over bridge.set for
-- anything you want undone: the Bridge restores the original value when the
-- lease is released, which includes when this script reloads.
--
--   local h, err = bridge.claim("var.DamageTotalMul(?)")
--   if h then
--       bridge.lease_write(h, 2.0)      -- released automatically on reload
--   else
--       bridge.log("claim refused: " .. err)
--   end
