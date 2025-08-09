-- Module:FR:RailwaySignalState
local p = {}

-- Signal shapes mapped to signal widths
local signalWidths = {
    A = "60",
    C = "60",
    F = "120",
    H = "120",
    R = "120",
    K = "46"
}

-- Signal types mapped to plate and target types
local signalTypes = {
    CARRE = { plate="NF", targets = { "C1", "F1", "H1" }},
    CV = { plate="NF", targets = { "A2", "C2", "F2", "H2", "K2" }},
    S = { plate="F", targets = { "A1", "A3", "K1" }},
    D = { plate="D", targets = { "R6" }},
    A = { plate="A", targets = { "R1" }}
}

-- Information by state
local stateInfo = {
    C = {
        fullname = "Carré",
        targets = {
            main = { "C1", "F1", "H1" }
        }
    },
    CV = {
        fullname = "Carré violet",
        targets = {
            main = { "A2", "C2", "F2", "H2", "K2" }
        }
    },
    D = {
        fullname = "Disque",
        targets = {
            distant = {"R6"}
        }
    },
    S = {
        fullname = "Sémaphore",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" }
        }
    },
    ["(S)"] = {
        fullname = "Feu rouge clignotant",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" }
        }
    },
    A = {
        fullname = "Avertissement",
        targets = {
            main = { "A1", "C1", "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(A)"] = {
        fullname = "Feu jaune clignotant",
        targets = {
            main = { "A1", "C1", "F1", "H1" },
            distant = { "R6" }
        }
    },
    R = {
        fullname = "Ralentissement 30",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["R+A"] = {
        fullname = "Ralentissement 30 et Avertissement",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["R+(A)"] = {
        fullname = "Ralentissement 30 et Feu jaune clignotant",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(R)"] = {
        fullname = "Ralentissement 60",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(R)+A"] = {
        fullname = "Ralentissement 60 et Avertissement",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(R)+(A)"] = {
        fullname = "Ralentissement 60 et Feu jaune clignotant",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    RR = {
        fullname = "Rappel 30",
        targets = {
            main = { "H1" }
        }
    },
    ["RR+A"] = {
        fullname = "Rappel 30 et Avertissement",
        targets = {
            main = { "H1" }
        }
    },
    ["RR+(A)"] = {
        fullname = "Rappel 30 et Feu jaune clignotant",
        targets = {
            main = { "H1" }
        }
    },
    ["(RR)"] = {
        fullname = "Rappel 60",
        targets = {
            main = { "H1" }
        }
    },
    ["(RR)+A"] = {
        fullname = "Rappel 60 et Avertissement",
        targets = {
            main = { "H1" }
        }
    },
    ["(RR)+(A)"] = {
        fullname = "Rappel 60 et Feu jaune clignotant",
        targets = {
            main = { "H1" }
        }
    },
    M = {
        fullname = "Feu blanc",
        targets = {
            main = { "A2", "C2", "F2", "H2", "K2" }
        }
    },
    ["(M)"] = {
        fullname = "Feu blanc clignotant",
        targets = {
            main = { "A2", "C2", "F2", "H2", "K2" }
        }
    },
    VL = {
        fullname = "Feu vert",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" },
            distant = { "R1" }
        }
    },
    ["(VL)"] = {
        fullname = "Feu vert clignotant",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" },
            distant = { "R1" }
        }
    }
}

-- Function to get the signal type based on target
local function getSignalType(target)
    for signalType, signalData in pairs(signalTypes) do
        for _, t in ipairs(signalData.targets) do
            if t == target then
                return signalType
            end
        end
    end
    return nil  -- Return nil if target type not found
end

-- Main function to generate content based on parameters
function p.render(frame)
    local args = frame:getParent().args
    local state = args.state
    if not state then
        return "State not specified!"
    end
    local category = args.category or "main"
    local info = stateInfo[state]
    if not info then
        return "Unknown state!"
    end

	-- Function to generate a tag with optional value and option
	local function generateTag(key, value, option)
        return "*" .. frame:expandTemplate{title = "Tag", args = {key, value or "", option or ""}}
	end

	-- Function to generate a tag value
	local function generateTagValue(key, value)
        return frame:expandTemplate{title = "TagValue", args = {key, value}}
	end

    -- Determine the image extensions based on the state
    -- MediaWiki does not support flashing lights in SVG; use animated GIFs instead.
    local ext = "svg"
    if string.find(state, "%b()") then
        ext = "gif"
    end

    -- Build the page
	local result = {}

    -- Transclude the subpage describing the state
    local subpageName = "Template:FR:RailwaySignalStateDescription/" .. info.fullname
	local titleObj = mw.title.new(subpageName)

    if titleObj and titleObj.exists then
        -- If the subpage exists, include it
        table.insert(result, frame:expandTemplate{title = subpageName})
    else
        -- If the subpage does not exist, add a message
        table.insert(result, "The subpage for '''" .. info.fullname .. "''' does not exist.\n")
    end

    -- Insert examples
    local targets = info.targets[category]

    if targets then
		local signalKey = "railway:signal:" .. category
    	local stateValue = generateTagValue(signalKey .. ":states", "FR:" .. state)
        local prefix = (state == "CV") and "Cv" or state -- Fix for "Cv" file names 

        for _, target in ipairs(targets) do
            local shape = string.match(target, "^.")
            local fileName = prefix .. " Cible " .. target .. "." .. ext
            local signalType = getSignalType(target)
            local plateValue = generateTagValue(signalKey .. ":plates", "FR:" .. signalTypes[signalType].plate)
        	local signalWidth = signalWidths[shape]
			local tagArgs = {
    			{signalKey, "FR:" .. signalType},
    			{signalKey .. ":plates", plateValue .. ";*"},
    			{signalKey .. ":form", "light"},
    			{signalKey .. ":shape", "FR:" .. shape},
    			{signalKey .. ":type", "FR:" .. target},
    			{signalKey .. ":states", "", stateValue .. ";*"}
			}

            table.insert(result, "=== Target type \"" .. target .. "\" ===\n\n")
            table.insert(result, "[[File:" .. fileName .. "|frameless|" .. signalWidth .. "px]]\n")

			for _, tagArg in ipairs(tagArgs) do
				-- table.insert(result, generateTag(table.unpack(tagArg)) .. "\n")
    			table.insert(result, generateTag(tagArg[1], tagArg[2], tagArg[3]) .. "\n")
    		end
		end
    else
        table.insert(result, "No targets available for the category \"" .. category .. "\".\n\n")
    end

    return table.concat(result)
end

return p
