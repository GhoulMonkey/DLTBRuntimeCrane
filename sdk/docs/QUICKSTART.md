# Quick start

CRANE loads individually enabled `.lua` files from `ph_ft/work/bin/x64/scripts`. A script
has two globals supplied by CRANE:

- `bridge`: the only interface to game and Bridge functionality;
- `params`: values saved by Crane Manager for settings declared in the script header.

Start with a read-only script:

```lua
-- @name My First CRANE Mod
-- @description Reports when a playable session becomes available.

local was_playable = false

bridge.on("player.updated", 2, function()
  local playable = bridge.get("session.playable")
  if playable and not was_playable then
    bridge.log("My First CRANE Mod: playable session ready")
  end
  was_playable = playable == true
end)
```

Add the file through Crane Manager. New scripts arrive disabled; enable it deliberately.
Saving the file or changing a setting causes CRANE to release what the old instance owned
and reload it. Errors are contained and recorded in the shared Bridge log.

For subject-bound state, resolve the subject first:

```lua
local player = bridge.resolve("player")
if player then
  local health, err = bridge.get("player.health", player)
  if health then bridge.log("health=" .. tostring(health)) end
end
```

Treat every Bridge call that can fail as returning `nil, status`. A path existing in the
catalog does not guarantee its subject is present, its owner is free or the installed
Bridge has verified the current game build.

Read `BRIDGE-CONCEPTS.md` before writing. Prefer a lease or modifier over `bridge.set` so
CRANE can clean up when the script reloads or is disabled.

