-- /!\ Work in progress.
local p = {}

-- Signal names
local signalNames = {
    CARRE = {name = "Carré" },
    S = {name = "Sémaphore" },
    D = {name = "Disque" },
    CV = {name = "Carré violet" },
}

-- Signal shapes mapped to signal types and widths
local signalShapes = {
    A = {signal = "S", width = "60"},
    C = {signal = "CARRE", width = "60"},
    F = {signal = "CARRE", width = "120"},
    H = {signal = "CARRE", width = "120"},
    R = {signal = "D", width = "120"},
    K = {signal = "CV", width = "46"}
}

-- Information by state
local stateInfo = {
    C = {
        fullname = "Carré",
        description = "Two steady red lights, either arranged vertically or horizontally.",
        information = "The signal is equipped with both a clearing light ('''œilleton''') and a '''Nf''' (''Non franchissable'') plate. " ..
        			  "The clearing light is always turned on except when the Carré (C) state is displayed.",
        action = "Absolute Stop.",
        targets = {
            main = { "C1", "F1", "H1" }
        }
    },
    CV = {
        fullname = "Carré violet",
        description = "One steady purple light.",
        action = "Absolute stop.",
        targets = {
            main = { "C2", "F2", "H2" },
            shunting = { "A2", "K2" }
        }
    },
    S = {
        fullname = "Sémaphore",
        description = "One steady red light.",
        action = "Commands the train driver to stop before the signal. However:\n" ..
                 "* During shunting operations, upon the order of the shunting supervisor, " ..
                 "a closed Sémaphore can be passed without stopping.\n" ..
                 "* In automatic block signalling (BAL or BAPR), the driver of maintenance trains " ..
                 "is permitted to pass closed Sémaphores without stopping. " ..
                 "They proceed at a speed not exceeding 15 km/h ('''Marche à Vue''').",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" }
        }
    },
    ["(S)"] = {
        fullname = "Feu rouge clignotant.",
        description = "One flashing red light.",
        action = "The driver may proceed at sight without stopping ('''Marche à vue'''), " ..
                 "but must not exceed 15 km/h when crossing this signal.\n\nThis state is used:\n" ..
                 "* on gradients to avoid a complete stop and difficult restart for a heavy train; or\n" ..
                 "* in place of an announcement ('''Avertissement''') signal for a very short block; or\n"..
                 "* on an occupied track, e.g., for positioning a locomotive at the head (''mise en tête'').",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" }
        }
    },
    D = {
        fullname = "Disque",
        description = "Both a yellow light and a red light, horizontally aligned.",
        action = "TBD",
        targets = {
            distant = {"R6"}
        }
    },
    A = {
        fullname = "Avertissement",
        description = "One steady yellow light.",
        action = "TBD",
        targets = {
            main = { "A1", "C1", "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(A)"] = {
        fullname = "Feu jaune clignotant",
        description = "One flashing yellow light.",
        action = "Ralentir. TBD",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1" },
            distant = { "R6" }
        }
    },
    R = {
        fullname = "Ralentissement 30",
        description = "Two steady horizontal yellow lights.",
        action = "Ralentir.",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["R+A"] = {
        fullname = "Ralentissement 30 et avertissement",
        description = "Two steady horizontal yellow lights and one additional steady yellow light.",
        action = "TBD",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["R+(A)"] = {
        fullname = "Ralentissement 30 et feu jaune clignotant",
        description = "Two steady horizontal yellow lights and one additional flashing yellow light.",
        action = "TBD",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(R)"] = {
        fullname = "Ralentissement 60",
        description = "Two flashing horizontal yellow lights.",
        action = "TBD",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(R)+A"] = {
        fullname = "Ralentissement 60 et avertissement",
        description = "Two flashing horizontal yellow lights and one additional steady yellow light.",
        action = "TBD",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    ["(R)+(A)"] = {
        fullname = "Ralentissement 60 et feu jaune clignotant",
        description = "Two flashing horizontal yellow lights and one additional flashing yellow light.",
        action = "TBD",
        targets = {
            main = { "F1", "H1" },
            distant = { "R6" }
        }
    },
    RR = {
        fullname = "Rappel 30",
        description = "Two steady vertical yellow lights.",
        action = "TBD",
        targets = {
            main = { "H1" }
        }
    },
    ["RR+A"] = {
        fullname = "Rappel 30 et avertissement",
        description = "Two steady vertical yellow lights and one additional steady yellow light.",
        action = "TBD",
        targets = {
            main = { "H1" }
        }
    },
    ["RR+(A)"] = {
        fullname = "Rappel 30 et feu jaune clignotant",
        description = "Two steady vertical yellow lights and one additional flashing yellow light.",
        action = "TBD",
        targets = {
            main = { "H1" }
        }
    },
    ["(RR)"] = {
        fullname = "Rappel 60",
        description = "Two flashing vertical yellow lights.",
        action = "TBD",
        targets = {
            main = { "H1" }
        }
    },
    ["(RR)+A"] = {
        fullname = "Rappel 60 et avertissement",
        description = "Two flashing vertical yellow lights and one additional steady yellow light.",
        action = "TBD",
        targets = {
            main = { "H1" }
        }
    },
    ["(RR)+(A)"] = {
        fullname = "Rappel 60 et feu jaune clignotant",
        description = "Two flashing vertical yellow lights and one additional flashing yellow light.",
        action = "TBD",
        targets = {
            main = {  "H1" }
        }
    },
    M = {
        fullname = "Feu blanc",
        description = "One steady white light.",
        action = "Shunting allowed.",
        targets = {
            main = { "C2", "F2", "H2" },
            shunting = { "A2", "K2" }
        }
    },
    ["(M)"] = {
        fullname = "Feu blanc clignotant",
        description = "One flashing white light.",
        action = "Shunting allowed on a short distance.",
        targets = {
            main = { "C2", "F2", "H2" },
            shunting = { "A2", "K2" }
        }
    },
    VL = {
        fullname = "Voie Libre",
        description = "One steady green light.",
        action = "The track ahead is clear. The driver may continue or resume normal speed if there was a previous speed restriction.",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" },
            distant = { "R1" }
        }
    },
    ["(VL)"] = {
        fullname = "Feu vert clignotant",
        description = "One flashing green light.",
        action = "La voie est libre, continuer. Reduce speed to 160.",
        targets = {
            main = { "A1", "A3", "C1", "F1", "H1", "K1" },
            distant = { "R1" }
        }
    }
}

-- Main function to generate content based on parameters
function p.render(frame)
    local args = frame:getParent().args
    local state = args.state or ""
    local category = args.category or "main"
    local info = stateInfo[state]
    if not info then
        return "Unknown state!"
    end

    -- Determine the image extensions based on the state
    -- MediaWiki does not support flashing lights in SVG; use animated GIFs instead.
    local ext = "svg"
    if string.find(state, "%b()") then
        ext = "gif"
    end

    -- Build the page
	local result = {}
    table.insert(result, "== State ==\n")
    table.insert(result, "'''" .. info.fullname .. "''' ['''" .. state .. "''']\n")
    if info.information then
        table.insert(result, "\n" .. info.information .. "\n\n")
    else
        table.insert(result, "\n")
    end
    table.insert(result, "== Description ==\n\n" .. info.description .. "\n\n")
    table.insert(result, "== Action ==\n\n" .. info.action .. "\n\n")
    table.insert(result, "== Examples ==\n\nRendering on different types of signals.\n\n")

    local targets = info.targets[category]

    if targets then
		local signalKey = "railway:signal:" .. category
    	local stateValue = frame:expandTemplate{title = "TagValue", args = {signalKey .. ":states", "FR:" .. state }}
	    local currentSection = ""

        for _, target in ipairs(targets) do
            local shape = string.match(target, "^.")
        	local shapeInfo = signalShapes[shape]
        	local signalInfo = signalNames[shapeInfo.signal]
			local tagArgs = {
    			{signalKey, "FR:" .. shapeInfo.signal},
    			{signalKey .. ":plates", ""},
    			{signalKey .. ":form", "light"},
    			{signalKey .. ":shape", "FR:" .. shape},
    			{signalKey .. ":type", "FR:" .. target},
    			{signalKey .. ":states", "", stateValue .. ";*"}
			}

	        if currentSection ~= signalInfo.name then
                table.insert(result, "=== " .. signalInfo.name .. " ===\n\n")
        	    currentSection = signalInfo.name
        	end

            table.insert(result, "==== Target type \"" .. target .. "\" ====\n\n")
            table.insert(result, "[[File:" .. state .. " Cible " .. target .. "." .. ext .. "|frameless|" .. shapeInfo.width .. "px]]\n")

			for _, tagArg in ipairs(tagArgs) do
    			table.insert(result, "*" .. frame:expandTemplate{title = "Tag", args = tagArg} .. "\n")
			end
		end
    else
        table.insert(result, "No targets available for the category \"" .. category .. "\".\n\n")
    end

    return table.concat(result)
end

return p
