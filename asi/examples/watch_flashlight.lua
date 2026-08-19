local last_energy = nil

bridge.on("flashlight.updated", 2, function(event)
    local energy = event.data.energy
    if energy ~= last_energy then
        last_energy = energy
        bridge.log("flashlight energy=" .. tostring(energy))
    end
end)
