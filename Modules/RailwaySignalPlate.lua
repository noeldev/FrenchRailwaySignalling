-- Module:FR:RailwaySignalPlate
local p = {}

function p.generateTags(frame)
    local args = frame:getParent().args
    local category = args.category or "main"
    local signal = args.signal or ""
    local plate = args.plate or ""
    local caption = args.caption or ""

    local tags = {}
    local signalKey = "railway:signal:" .. category

    -- Function to generate a tag with optional value and option
    local function generateTag(key, value, option)
        return "*" .. frame:expandTemplate{title = "Tag", args = {key, value or "", option or ""}}
    end

    -- Function to generate a tag value
    local function generateTagValue(key, value)
        return frame:expandTemplate{title = "TagValue", args = {key, value}}
    end

    -- Tag for the signal
    table.insert(tags, generateTag(signalKey, "FR:" .. signal))

    if signal == "CARRE" then
        local platesTag = generateTag(signalKey .. ":plates", nil, generateTagValue(signalKey .. ":plates", "FR:NF"))
        if plate ~= "NF" and plate ~= "" then
            platesTag = platesTag .. ";" .. generateTagValue(signalKey .. ":plates", "FR:" .. plate)
        end
        table.insert(tags, platesTag)

        table.insert(tags, generateTag(signalKey .. ":caption", caption))
    else
        table.insert(tags, generateTag(signalKey .. ":plates", "FR:" .. plate))
    end

    return table.concat(tags, "\n")
end

return p
