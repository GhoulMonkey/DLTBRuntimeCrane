-- @name Observe Item Use
-- @description Logs completed native item uses without changing them.

bridge.on("item.use.completed", 2, function(event)
  bridge.log("item " .. tostring(event.data.item) .. " count " ..
             tostring(event.data.count_before) .. " -> " ..
             tostring(event.data.count_after))
end)

