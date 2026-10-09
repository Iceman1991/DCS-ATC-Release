--[[
  DCS-ATC – mission part (mod). Runs in every mission on the host or in single player.

  Loaded via one line in <DCS>\Scripts\MissionScripting.lua (the installer adds it) before DCS blocks os/io/lfs:
  only this script keeps file access, the mission itself stays blocked.

  Data exchange with DcsAtc.exe via %TMP%:
    DcsAtc-Airfields.txt   all airfields with runways (once)
    DcsAtc-State.txt       players (P), AI (T), weather (G/W), bullseye (B), tanker TACAN (K), range zone (Z) – every 0.5 s
    DcsAtc-Menu\*.txt      F10 requests "unit|text"                 -> App
    DcsAtc-Menu\*.evt      events (C;unit;crew|back|fuel|stopped, X;shot|guns|hit|kill|eject|pitbull|maddog|trashed;… AI radio, R/H/K range)  -> app
    DcsAtc-Menu\*.out      Controller texts "gid|sec|text" (0 = all)   <- App (old: "gid|text")
    DcsAtc-Menu\*.cmd      Commands START/WAIT/RESUME, WHEEL;unit;text (VR radio wheel)   <- App

  Plus: AI traffic at the airfields near the players, ground crew with fuel/ammo trucks and fire brigade,
  tanker and AWACS (if the mission has none), cleanup of wrecks.
]]
if DCSATC then return end
local io, os, lfs = io, os, lfs
if not (io and os and lfs) then return end
DCSATC = { ai = {} }   -- ai[groupName] = "dep|dep-go|arr|arr-hold:airfield"

-- DCS radio of the AI muted (R308/R312/R313), already from loading: DCS-ATC speaks for AWACS, tanker and AI flights.
-- Options as numbers like in the mission editor (me_action_db: SILENCE = 7, PROHIBIT_WP_PASS_REPORT = 19), not via AI.Option.Air.id.
-- Groups with a player stay unmuted (wingman replies). Birth handler before mission start -> also the starting lineup immediately
-- (otherwise "Overlord, on station" / "passing waypoint 1" came before the first pass); plus every 0.5 s in the first 10 s, then every 60 s.
local SILENCE, NO_WP_REPORT = 7, 19
local function silenceGroup(g) pcall(function()
  local c = g:getController()
  c:setOption(SILENCE, true)
  c:setOption(NO_WP_REPORT, true)
end) end
local function silenceAi(g)
  for _, u in ipairs(g:getUnits() or {}) do if u:getPlayerName() then return false end end
  silenceGroup(g)
  return true
end
local function silenceAll()
  local n = 0
  for side = 0, 2 do
    for _, cat in ipairs({ Group.Category.AIRPLANE, Group.Category.HELICOPTER }) do
      for _, g in ipairs(coalition.getGroups(side, cat) or {}) do if silenceAi(g) then n = n + 1 end end
    end
  end
  return n
end
world.addEventHandler({ onEvent = function(_, e)
  if e.id ~= world.event.S_EVENT_BIRTH or not e.initiator or not e.initiator.getGroup then return end
  pcall(function()
    local g = e.initiator:getGroup()
    if g and (g:getCategory() == Group.Category.AIRPLANE or g:getCategory() == Group.Category.HELICOPTER) then silenceAi(g) end
  end)
end })
pcall(silenceAll)
local silencedLogged
timer.scheduleFunction(function(_, t)
  local ok, n = pcall(silenceAll)
  if ok and not silencedLogged and t > 5 then silencedLogged = true env.info("DCS-ATC: Funkstille für " .. n .. " KI-Fluggruppen") end
  return t + (t < 10 and 0.5 or 60)
end, nil, timer.getTime() + 0.1)

local function start()
  local A = DCSATC
  local TMP = os.getenv("TMP") or os.getenv("TEMP")
  local dir = TMP .. "\\DcsAtc-Menu"
  lfs.mkdir(dir)
  local function log(s) env.info("DCS-ATC: " .. s) end
  local function clean(s) return (tostring(s or ""):gsub("[;|\r\n]", " ")) end
  local function d2(a, b) return (a.x - b.x) ^ 2 + (a.z - b.z) ^ 2 end
  local NM = 1852

  -- Settings: default < DcsAtcOptions.lua (Saved Games\DCS\Scripts\DcsAtc) < DCSATC_OPTIONS from the mission < F10 menu
  local OPT = { traffic = 2, tanker = true, awacs = true, crew = false }   -- traffic: 0 off, 1 little, 2 normal, 3 lots
  pcall(function() local f = loadfile(lfs.writedir() .. "Scripts\\DcsAtc\\DcsAtcOptions.lua") if f then for k, v in pairs(f() or {}) do OPT[k] = v end end end)
  for k, v in pairs(type(DCSATC_OPTIONS) == "table" and DCSATC_OPTIONS or {}) do OPT[k] = v end
  if not DCSATC_SPAWNTEST then OPT.traffic, OPT.tanker, OPT.awacs, OPT.arrivals = 0, false, false, nil end   -- 0.9.4: mod no longer spawns aircraft (not even via old option files); spawn code now only in missiontest
  A.opt = OPT
  -- Language of the menus (DcsAtcLang.txt is written by the installer: "de" or "en")
  local LANG = "de"
  pcall(function() local h = io.open(lfs.writedir() .. "Scripts\\DcsAtc\\DcsAtcLang.txt", "r") if h then local s = h:read("*a") or "" h:close() if s:match("^%s*(.-)%s*$") == "en" then LANG = "en" end end end)
  local function L(de, en) return LANG == "en" and en or de end
  -- Modules picked in the installer or settings window (DcsAtcModules.txt: "atc,awacs,…"); missing file = all on
  local MODS
  pcall(function() local h = io.open(lfs.writedir() .. "Scripts\\DcsAtc\\DcsAtcModules.txt", "r") if h then MODS = {} for w in (h:read("*a") or ""):lower():gmatch("%a+") do MODS[w] = true end h:close() end end)
  local function on(m) return not MODS or MODS[m] == true end
  if not on("crew") then OPT.crew = false end
  local function textOn(t)   -- like Program.TextOn (radio wheel): prefix = module, unprefixed = ATC; radio check, say again, debrief always
    local p = t:match("^(%a+):")
    if p == "Range" then return on("range") elseif p == "AWACS" then return on("awacs") and (not t:find("tanker") or on("tanker"))
    elseif p == "Tanker" then return on("tanker") elseif p == "Carrier" then return on("carrier") end
    return t == "radio check" or t == "say again" or t == "debrief" or on("atc")
  end
  local onOpt = {}   -- [name] = function() – apply immediately

  local function writeFile(file, text)
    local h = io.open(file .. ".tmp", "w")
    if not h then return end
    h:write(text)
    h:close()
    os.remove(file)
    os.rename(file .. ".tmp", file)
  end
  local n = 0
  local function drop(text, ext)   -- File for the app: DcsAtc-Menu\<time>-<n>.<ext>
    n = n + 1
    local f = string.format("%s\\%010d-%04d", dir, os.time(), n)
    local h = io.open(f .. ".tmp", "w")
    if not h then return end
    h:write(text)
    h:close()
    os.rename(f .. ".tmp", f .. ext)
  end
  local function evt(line) drop(line, ".evt") end

  -- Airfields ---------------------------------------------------------------------------------------------
  local fields = {}
  for _, ab in ipairs(world.getAirbases() or {}) do
    if ab:getDesc().category == Airbase.Category.AIRDROME then
      local spots = {}
      for _, s in ipairs(ab:getParking() or {}) do
        local p = s.vTerminalPos
        spots[#spots + 1] = { x = p.x, y = p.y, z = p.z, n = s.Term_Index, t = s.Term_Type }   -- Number/type for Ground (assign parking position)
      end
      fields[#fields + 1] = { ab = ab, name = ab:getName(), id = ab:getID(), p = ab:getPoint(), spots = spots }
    end
  end
  -- DCS's own ATC muted (also carrier: Marshal/Air Boss/LSO of DCS): controllers come from DCS-ATC
  for _, ab in ipairs(world.getAirbases() or {}) do
    if ab:getDesc().category == Airbase.Category.AIRDROME or ab:getDesc().category == Airbase.Category.SHIP then pcall(ab.setRadioSilentMode, ab, true) end
  end
  local silence = silenceGroup
  local function nearestField(p, maxd)
    local best, bd
    for _, f in ipairs(fields) do
      local d = d2(f.p, p)
      if d < maxd ^ 2 and (not bd or d < bd) then best, bd = f, d end
    end
    return best
  end
  pcall(function()
    local l = { "M;" .. tostring(env.mission.theatre) }
    for _, f in ipairs(fields) do
      l[#l + 1] = string.format("A;%s;%.1f;%.1f;%.1f;%d;%d", f.name, f.p.x, f.p.z, f.p.y, f.id, f.ab:getCoalition())
      for _, r in ipairs(f.ab:getRunways() or {}) do
        l[#l + 1] = string.format("R;%s;%.5f;%.1f;%.1f;%.0f", tostring(r.Name), r.course, r.position.x, r.position.z, r.length)
      end
      for _, s in ipairs(f.spots) do l[#l + 1] = string.format("P;%.1f;%.1f;%d;%d", s.x, s.z, s.n or 0, s.t or 0) end
    end
    writeFile(TMP .. "\\DcsAtc-Airfields.txt", table.concat(l, "\n") .. "\n")
  end)

  -- Player slots from the editor: parking spots (taboo for AI) and sides with players ------------------------
  local slots, blocked, playerSides = {}, {}, {}
  for sideName, coa in pairs(env.mission.coalition or {}) do
    for _, ctry in pairs(coa.country or {}) do
      for _, cat in ipairs({ "plane", "helicopter" }) do
        for _, g in pairs(ctry[cat] and ctry[cat].group or {}) do
          local wp = g.route and g.route.points and g.route.points[1]
          for _, u in pairs(g.units or {}) do
            if u.skill == "Client" or u.skill == "Player" then
              slots[g.name] = (slots[g.name] or 0) + 1
              playerSides[sideName == "red" and 1 or 2] = true
              if wp and wp.airdromeId and u.parking then
                blocked[wp.airdromeId] = blocked[wp.airdromeId] or {}
                blocked[wp.airdromeId][tonumber(u.parking) or -1] = true
              end
            end
          end
        end
      end
    end
  end
  if not next(playerSides) then playerSides[2] = true end

  local function countryOf(side)
    local name = side == 1 and "red" or "blue"
    local c = env.mission.coalition and env.mission.coalition[name] and env.mission.coalition[name].country
    if c and c[1] and c[1].id then return c[1].id end
    return side == 1 and country.id.RUSSIA or country.id.USA
  end

  -- F10 menu per player group (same entries and names as the app's radio wheel, Wheel.cs; parity test in the app's selftest) --------------
  -- Entry: { label, text } = command; { label, nil, { entries } } = group; { label, text, nil, "immediate"|"priority" } = emergency with type submenu;
  -- ifr = true: airfield list instead of a fixed command (R8). F10 allows 9 entries per level (R390): longer lists go into subgroups.
  -- Deliberately not here: "Select airfield" and "Settings" of the wheel (own windows of the app; F10 has "Settings" below ATC).
  local HAS = {}   -- range zone / carrier in the mission (set below): the groups only appear then, like in the wheel
  local MENU = {
    { "Ground", nil, {
      { "Request startup", "Ground: request startup" }, { "IFR clearance", "Ground: request IFR clearance", ifr = true },
      { "Request taxi", "Ground: request taxi" }, { "Vacated, taxi to parking", "Ground: runway vacated, request taxi to parking" },
      { "Progressive taxi", "Ground: request progressive taxi" }, { "Hot brakes", "Ground: hot brakes" } } },   -- R251/R297
    { "Tower", nil, {
      { "Ready for departure", "Tower: ready for departure" }, { "Ready, closed pattern", "Tower: ready for departure, closed pattern" },
      { "Initial", "Tower: initial" }, { "Overhead", "Tower: overhead" }, { "Base, gear down", "Tower: base, gear down" },
      { "Final, gear down, full stop", "Tower: final, gear down, full stop" }, { "Base, touch and go", "Tower: base, gear down, touch and go" },
      { "Going around", "Tower: going around" },
      { "Closed / SFO", nil, {   -- R202/R204/R253
        { "Request closed", "Tower: request closed traffic" }, { "Request SFO", "Tower: request S F O" }, { "High key", "Tower: high key" },
        { "Low key, gear down", "Tower: low key, gear down" }, { "Ready, practice approach", "Tower: ready for departure, practice approach" } } } } },
    { "Approach", nil, {
      { L("Airborne (nach Start)", "Airborne (after takeoff)"), "Approach: airborne, climbing" }, { "Inbound for landing", "Approach: inbound for landing" },
      { "Inbound, pattern work", "Approach: inbound for pattern work, touch and go" }, { "Request ILS / straight in", "Approach: request straight in" },
      { "Flight following", "Approach: request flight following" }, { L("Abmelden (cancel approach)", "Cancel approach"), "Approach: cancel approach" },
      { L("Verkehr", "Traffic"), nil, { { "Traffic in sight", "Approach: traffic in sight" }, { "Negative contact", "Approach: negative contact" } } },
      { "C R P", "Approach: C R P" },
      { L("Mehr", "More"), nil, {
        { L("Fahrt melden", "Report airspeed"), "Approach: report airspeed" }, { "Say again", "Approach: say again" }, { "Request higher", "Approach: request higher" } } } } },
    { "Range", nil, {
      { "Check in", "Range: checking in" }, { "IP inbound", "Range: IP inbound" }, { "In hot", "Range: in hot" }, { "Off safe", "Range: off safe" },
      { "Check out", "Range: checking out" }, { "Check out, hung ordnance", "Range: checking out, hung ordnance" } }, need = "range" },
    { "AWACS / Tanker", nil, {
      { "AWACS", nil, {
        { "Check in", "AWACS: checking in" }, { "Picture", "AWACS: request picture" }, { "Bogey dope", "AWACS: bogey dope" }, { "Sort", "AWACS: request sort" },
        { L("Nächster Tanker", "Nearest tanker"), "AWACS: vector to tanker" }, { L("Vektor nächster Platz", "Vector nearest airfield"), "AWACS: vector to nearest airfield" },
        { "Check out", "AWACS: checking out" },
        { L("Gefecht", "Combat"), nil, {   -- LD17
          { "Committing", "AWACS: committing" }, { "Fox three", "AWACS: fox three" }, { "Splash", "AWACS: splash one" },
          { "Defending", "AWACS: missile, defending" }, { "Request support", "AWACS: request support" }, { "Unable", "AWACS: unable" },
          { "Winchester", "AWACS: winchester" }, { "Bingo, RTB", "AWACS: bingo, RTB" }, { "Say again", "AWACS: say again" } } } } },
      { "Tanker", nil, {
        { "Request rejoin", "Tanker: request rejoin" }, { "Visual", "Tanker: visual" }, { "Observation", "Tanker: observation" },
        { "Pre-contact", "Tanker: pre contact" }, { "Refuel complete", "Tanker: refuel complete" } } } } },
    { "Carrier", nil, {
      { "Marshal check in", "Carrier: Marshal, checking in" }, { "See you at", "Carrier: see you at angels" },
      { "Initial", "Carrier: initial" }, { "Commencing", "Carrier: commencing" }, { "Platform", "Carrier: platform" },
      { "Ball", "Carrier: ball" }, { "Clara", "Carrier: Clara" }, { "Pigeons", "Carrier: pigeons" } }, need = "carrier" },
    { L("Notfall", "Emergency"), nil, {
      { "MAYDAY", "mayday mayday mayday", nil, "immediate" }, { "PAN PAN", "pan pan, pan pan, pan pan", nil, "priority" },
      { L("Spritmangel", "Minimum fuel"), "minimum fuel" },   -- R297 like the radio wheel (Wheel.cs): R245 minimum fuel, R251 hung ordnance, R296 Flameout
      { "Hung ordnance", "Approach: hung ordnance" }, { "Flameout, high key", "Tower: flameout, high key" },
      { L("Notfall beenden", "Cancel emergency"), "cancel emergency" } } },
    { L("Allgemein", "General"), nil, {
      { "Radio check", "radio check" }, { "Say again", "say again" }, { "QNH / weather", "request weather" },
      { L("ATIS hören", "Listen to ATIS"), "atis" }, { "Request zone transit", "Approach: request zone transit" }, { "Debriefing", "debrief" } } },
  }
  local function prune(items)   -- modules: drop entries of deselected modules, then empty groups
    for j = #items, 1, -1 do
      local c = items[j]
      if c[3] then prune(c[3]) end
      if (c[3] and #c[3] == 0) or (not c[3] and not textOn(c[2])) then table.remove(items, j) end
    end
  end
  prune(MENU)
  -- R297: like Wheel.Kinds; fuel only as MAYDAY ("mayday mayday mayday fuel, emergency fuel", R245), never as PAN
  local KINDS = { { L("Triebwerksausfall", "Engine failure"), "engine failure" }, { L("Treibstoff", "Fuel"), "emergency fuel" },
    { L("Hydraulik", "Hydraulic failure"), "hydraulic failure" }, { L("Gefechtsschaden", "Battle damage"), "battle damage" },
    { L("Medizinisch", "Medical"), "medical emergency" }, { L("Vogelschlag", "Bird strike"), "bird strike" } }
  local function send(arg)
    local a = lfs.attributes(dir .. "\\alive")
    if not a or os.time() - a.modification > 5 then
      trigger.action.outTextForGroup(arg.gid, L("DCS-ATC läuft nicht – Startmenü -> DCS-ATC starten, dann erneut senden.", "DCS-ATC is not running – Start menu -> Start DCS-ATC, then send again."), 10)
      return
    end
    local unit = arg.unit
    if not unit then   -- Group with only one player slot
      local g = Group.getByName(arg.gname)
      for _, u in ipairs(g and g:getUnits() or {}) do
        if u:getPlayerName() then unit = u:getName() break end
      end
    end
    if unit then drop(clean(unit) .. "|" .. arg.text, ".txt") end
  end
  local function onoff(b) return b and L("an", "on") or L("aus", "off") end
  local function showOpt(gid)
    trigger.action.outTextForGroup(gid, string.format(L("DCS-ATC: Bodenpersonal %s", "DCS-ATC: ground crew %s"),
      onoff(OPT.crew)), 10)
  end
  local function setOpt(a)
    if a.v == nil then OPT[a.k] = not OPT[a.k] else OPT[a.k] = a.v end
    if onOpt[a.k] then pcall(onOpt[a.k]) end
    log("Einstellung " .. a.k .. " = " .. tostring(OPT[a.k]))
    showOpt(a.gid)
  end
  local function optMenu(gid, parent)
    local m = missionCommands.addSubMenuForGroup(gid, L("Einstellungen", "Settings"), parent)
    for _, x in ipairs({ { "crew", L("Bodenpersonal an/aus", "Ground crew on/off") } }) do
      missionCommands.addCommandForGroup(gid, x[2], m, setOpt, { gid = gid, k = x[1] })
    end
    missionCommands.addCommandForGroup(gid, L("Anzeigen", "Show"), m, showOpt, gid)
  end
  local built, roots = {}, {}
  local function addItems(gid, parent, items, gname, unit, u)
    for _, c in ipairs(items) do
      local a = { gid = gid, gname = gname, unit = unit, text = c[2] }
      if c.need and not HAS[c.need] then   -- Range / Carrier only if the mission has one
      elseif c[3] then addItems(gid, missionCommands.addSubMenuForGroup(gid, c[1], parent), c[3], gname, unit, u)
      elseif c[4] then   -- Emergency: submenu with the type (like the radio wheel), position/altitude/heading added by the app
        local k = missionCommands.addSubMenuForGroup(gid, c[1], parent)
        for _, x in ipairs(KINDS) do
          local fuel = x[2] == "emergency fuel"
          if not fuel or c[4] == "immediate" then
            missionCommands.addCommandForGroup(gid, x[1], k, send, { gid = gid, gname = gname, unit = unit, text = c[2] .. (fuel and " fuel" or "") .. ", " .. x[2] .. ", request " .. c[4] .. " landing" })
          end
        end
      elseif c.ifr then   -- R8: en-route clearance with destination: own/neutral airfields by distance from spawn, first without destination (app: nearest in departure direction)
        local k, p, side, l = missionCommands.addSubMenuForGroup(gid, c[1], parent), u:getPoint(), u:getCoalition(), {}
        for _, f in ipairs(fields) do
          local fc = f.ab:getCoalition()
          if (fc == side or fc == 0) and d2(f.p, p) > 5000 ^ 2 then l[#l + 1] = { f = f, d = d2(f.p, p) } end
        end
        table.sort(l, function(x, y) return x.d < y.d end)
        missionCommands.addCommandForGroup(gid, L("Nächster in Abflugrichtung", "Next in departure direction"), k, send, a)
        for i = 1, math.min(8, #l) do
          missionCommands.addCommandForGroup(gid, l[i].f.name, k, send, { gid = gid, gname = gname, unit = unit, text = c[2] .. " to " .. l[i].f.name })
        end
      else
        missionCommands.addCommandForGroup(gid, c[1], parent, send, a)
      end
    end
  end
  local function addMenu(gid, parent, gname, unit, u)
    addItems(gid, parent, MENU, gname, unit, u)
    if not unit and on("crew") then optMenu(gid, parent) end
  end
  local function buildMenu(g, u)
    local gid, gname = g:getID(), g:getName()
    if (slots[gname] or 1) <= 1 then
      if not built[gid] then
        built[gid] = true
        addMenu(gid, missionCommands.addSubMenuForGroup(gid, "ATC"), gname, nil, u)
      end
      return
    end
    local key = u:getName()   -- several players in a group: one submenu per player
    if built[key] then return end
    built[key] = true
    if not roots[gid] then roots[gid] = missionCommands.addSubMenuForGroup(gid, "ATC"); if on("crew") then optMenu(gid, roots[gid]) end end
    local cs = (tostring(u:getCallsign() or key):gsub("^(%a+)(%d)(%d)$", "%1 %2-%3"))
    addMenu(gid, missionCommands.addSubMenuForGroup(gid, cs, roots[gid]), gname, key, u)
  end

  -- Weather, Bullseye, tanker TACAN -------------------------------------------------------------------
  local wx = env.mission.weather or {}
  local cl = wx.clouds or {}
  local cover = (cl.preset and 1) or (((cl.density or 0) > 0) and 1 or 0)
  -- R367: ceiling only with BKN/OVC in the lowest layer; presets 1-5 are FEW/SCT (METAR in Config/Effects/clouds.lua), SCT/BKN counts as ceiling; legacy clouds from density 5/10
  local fewSct = { Preset1 = true, Preset2 = true, Preset3 = true, Preset4 = true, Preset5 = true }
  local ceil = cl.preset and (fewSct[cl.preset] and 0 or 1) or (((cl.density or 0) >= 5) and 1 or 0)
  local beacons = {}
  local function scan(t, gname)
    for _, v in pairs(t) do
      if type(v) == "table" then
        if v.id == "ActivateBeacon" and v.params and v.params.channel then
          beacons[#beacons + 1] = string.format("K;%s;%d%s", clean(gname), v.params.channel, v.params.modeChannel or "X")
        else scan(v, gname) end
      end
    end
  end
  for _, coa in pairs(env.mission.coalition or {}) do
    for _, ctry in pairs(coa.country or {}) do
      for _, g in pairs(ctry.plane and ctry.plane.group or {}) do
        scan(g.route or {}, g.name)
        if g.task == "AWACS" and g.frequency then beacons[#beacons + 1] = string.format("F;%s;%.3f", clean(g.name), g.frequency) end   -- AWACS radio (MHz)
      end
    end
  end
  -- Carrier (M5): radio, TACAN, ICLS from the mission editor; position comes as a C line with the state
  local boats, boatRoute, cvUnits, cvBound, gfreq, cvNoTurn = {}, {}, {}, {}, {}, {}   -- cvNoTurn: CVTURN rejected because of land ahead (C line, Marshal then states the real BRC); cvBound: AI group with landing waypoint on a carrier (flag "cv"); gfreq: group -> radio MHz
  local function isCarrier(t) return t:find("CVN") or t:find("Stennis") or t:find("Forrestal") or t:find("KUZNECOW") or t:find("CV_1143") or t:find("LHA_Tarawa") end
  local function findTask(t, id)
    for _, v in pairs(t) do
      if type(v) == "table" then
        if v.id == id and v.params then return v.params end
        local r = findTask(v, id)
        if r then return r end
      end
    end
  end
  for _, coa in pairs(env.mission.coalition or {}) do
    for _, ctry in pairs(coa.country or {}) do
      for _, g in pairs(ctry.ship and ctry.ship.group or {}) do
        local u1 = g.units and g.units[1]
        if u1 and isCarrier(u1.type or "") then
          local tb, ic = findTask(g.route or {}, "ActivateBeacon"), findTask(g.route or {}, "ActivateICLS")
          boatRoute[g.name] = g.route and g.route.points   -- for CVRESUME after the recovery
          for _, u in ipairs(g.units) do if u.unitId then cvUnits[u.unitId] = true end end
          boats[g.name] = string.format("%.3f;%s;%s", (u1.frequency or 0) / 1e6,
            tb and tb.channel and (tb.channel .. (tb.modeChannel or "X")) or "", ic and ic.channel or "")
        end
      end
    end
  end
  local gtask = {}   -- group -> editor task (R376: only CAP/sweep/escort/intercept flights check in and commit)
  HAS.carrier = next(boats) ~= nil   -- R390: F10 shows Carrier only then
  for _, coa in pairs(env.mission.coalition or {}) do
    for _, ctry in pairs(coa.country or {}) do
      for _, cat in ipairs({ "plane", "helicopter" }) do
        for _, g in pairs(ctry[cat] and ctry[cat].group or {}) do
          for _, p in ipairs(g.route and g.route.points or {}) do
            if p.type == "Land" and (cvUnits[p.linkUnit] or cvUnits[p.helipadId]) then cvBound[g.name] = true end
          end
          gfreq[g.name] = tonumber(g.frequency) or 0
          gtask[g.name] = g.task or ""
        end
      end
    end
  end

  -- AI radio (KI-FUNK-PLAN): shots, hits, kills, ejections from/at AI aircraft -> "X;..." to the app ----------------
  local WC, MC, GT = Weapon and Weapon.Category or {}, Weapon and Weapon.MissileCategory or {}, Weapon and Weapon.GuidanceType or {}
  local function enum(t, v) for k, x in pairs(t) do if x == v then return k:lower() end end return "" end   -- RADAR_ACTIVE -> "radar_active"
  local function air(o)
    local ok, r = pcall(function() return o:getCategory() == Object.Category.UNIT and (o:getDesc().category or 9) <= 1 end)   -- Aircraft/helicopter
    return ok and r
  end
  local function isAi(o) return air(o) and not o:getPlayerName() end
  local function uname(o) local ok, n = pcall(function() return o:getName() end) return ok and clean(n) or "" end
  local function gname(o) local ok, n = pcall(function() return o:getGroup():getName() end) return ok and clean(n) or "" end
  local ammo, tracked, fmax, lastHit, ejected = {}, {}, {}, {}, {}   -- lastHit: target -> last X;hit (R382); ejected: unit -> pilot out (R361); fmax: unit -> internal tank kg (LK15); ammo: unit -> "fox3/fox1/fox2/Kanone/Luft-Boden"; tracked: air-to-air missiles (Pitbull, Trashed)
  local function ammoOf(u)
    local n = { 0, 0, 0, 0, 0 }
    for _, a in ipairs(u:getAmmo() or {}) do
      local d, c = a.desc or {}, a.count or 0
      local k = d.category == WC.SHELL and 4 or (d.category == WC.MISSILE and d.missileCategory == MC.AAM)
                and (d.guidance == GT.RADAR_ACTIVE and 1 or d.guidance == GT.RADAR_SEMI_ACTIVE and 2 or 3) or 5
      n[k] = n[k] + c
    end
    return table.concat(n, "/")
  end
  world.addEventHandler({ onEvent = function(_, e) pcall(function()
    local E, u, t = world.event, e.initiator, e.target
    if e.id == E.S_EVENT_SHOT and e.weapon then
      local w = e.weapon
      local d = w:getDesc() or {}
      t = w.getTarget and w:getTarget()
      if not (isAi(u) or isAi(t)) then return end
      ammo[uname(u)] = nil
      local p = u:getPoint()
      local active = d.category == WC.MISSILE and d.missileCategory == MC.AAM and d.guidance == GT.RADAR_ACTIVE
      evt(string.format("X;shot;%s;%s;%d;%s;%s;%s;%s;%s;%s;%.0f;%.0f", uname(u), gname(u), u:getCoalition(), clean(w:getTypeName()),
        enum(WC, d.category), enum(MC, d.missileCategory), enum(GT, d.guidance), uname(t), gname(t), p.x, p.z))
      if active and not t then evt("X;maddog;" .. uname(u)) end
      if d.category == WC.MISSILE and d.missileCategory == MC.AAM and t and #tracked < 40 then
        tracked[#tracked + 1] = { w = w, id = w.id_, t = t, unit = uname(u), active = active, at = timer.getTime() }
      end
    elseif e.id == E.S_EVENT_SHOOTING_START then
      if isAi(u) then evt(string.format("X;guns;%s;%s;%d;%s;%s", uname(u), gname(u), u:getCoalition(), uname(t), air(t) and "air" or "ground")) end
    elseif e.id == E.S_EVENT_SHOOTING_END then
      ammo[uname(u)] = nil
    elseif e.id == E.S_EVENT_HIT then
      local wid = e.weapon and e.weapon.id_
      for _, m in ipairs(tracked) do if wid and m.id == wid then m.hit = true end end
      if not (isAi(u) or isAi(t)) then return end
      local life = 1
      pcall(function() life = t:getLife() / math.max(1, t:getLife0()) end)
      local lh, now = lastHit[uname(t)], timer.getTime()   -- R382: gun bursts give dozens of hits per second -> one event per target and second unless the damage jumps
      if lh and now - lh.at < 1 and math.abs(lh.life - life) <= 0.05 then return end
      lastHit[uname(t)] = { at = now, life = life }
      evt(string.format("X;hit;%s;%s;%s;%s;%s;%.2f", uname(u), gname(u), uname(t), gname(t), clean(e.weapon and e.weapon:getTypeName() or ""), life))
    elseif e.id == E.S_EVENT_KILL then
      if not (isAi(u) or isAi(t)) then return end
      local ty = ""
      pcall(function() ty = clean(t:getTypeName()) end)
      evt(string.format("X;kill;%s;%s;%s;%s;%s;%d", uname(u), gname(u), uname(t), gname(t), ty, air(t) and 1 or 0))
    elseif e.id == E.S_EVENT_EJECTION then
      if not air(u) then return end   -- also players (N45): the app finds the player via the unit
      local p = u:getPoint()
      ejected[uname(u)] = true
      local ok, st = pcall(land.getSurfaceType, { x = p.x, y = p.z })
      evt(string.format("X;eject;%s;%s;%.0f;%.0f;%d", uname(u), gname(u), p.x, p.z, ok and (st == 2 or st == 3) and 1 or 0))   -- R361: 1 = over water (SurfaceType 2/3)
    elseif e.id == E.S_EVENT_PILOT_DEAD and ejected[uname(u)] then
      ejected[uname(u)] = nil
      evt("X;pilotdead;" .. uname(u))   -- R361: no SAR call for a dead pilot
    end
  end) end })
  -- every 0.5 s: active missile closer than 10 NM to the target -> Pitbull; missile gone without a hit, target alive -> Trashed
  local function track(m, now)   -- true = done
    if m.w:isExist() then
      if m.active and not m.pit and m.t:isExist() then
        local a, b = m.w:getPoint(), m.t:getPoint()
        if (a.x - b.x) ^ 2 + (a.y - b.y) ^ 2 + (a.z - b.z) ^ 2 < (10 * NM) ^ 2 then
          m.pit = true
          if now - m.at > 3 then evt("X;pitbull;" .. m.unit .. ";" .. uname(m.t)) end   -- shot down within 10 NM: no own Pitbull call; target: its "defending" (R379)
        end
      end
      return now - m.at > 120
    end
    m.gone = m.gone or now
    if now - m.gone < 2 then return false end   -- hits/kills come shortly after disappearing
    if not m.hit and m.t:isExist() then evt("X;trashed;" .. m.unit .. ";" .. uname(m.t)) end
    return true
  end
  local function trackWeapons()
    for i = #tracked, 1, -1 do
      local ok, done = pcall(track, tracked[i], timer.getTime())
      if done or not ok then table.remove(tracked, i) end
    end
  end
  local cvPts, cvNear = {}, false   -- R27: carrier of this state; player under 2 NM in the air -> state every 0.25 s (LSO)
  local function carriers(l)
    cvPts = {}
    for side = 1, 2 do
      for _, g in ipairs(coalition.getGroups(side, Group.Category.SHIP) or {}) do
        for _, u in ipairs(g:getUnits() or {}) do
          if u:isExist() and isCarrier(u:getTypeName()) then
            local p, pos, v = u:getPoint(), u:getPosition(), u:getVelocity()
            cvPts[#cvPts + 1] = p
            local hdg = math.atan2(pos.x.z, pos.x.x)
            if hdg < 0 then hdg = hdg + 2 * math.pi end
            local w = atmosphere.getWind({ x = p.x, y = p.y + 20, z = p.z })   -- Wind at the deck (carrier into the wind)
            local _, pa = atmosphere.getTemperatureAndPressure({ x = p.x, y = 0, z = p.z })   -- Air pressure at sea level (altimeter)
            l[#l + 1] = string.format("C;%s;%s;%s;%.1f;%.1f;%.4f;%.1f;%d;%s;%.1f;%.1f;%.0f;%d", clean(u:getName()), clean(g:getName()), clean(u:getTypeName()),
              p.x, p.z, hdg, math.sqrt(v.x * v.x + v.z * v.z), side, boats[g:getName()] or "0;;", w.x, w.z, pa, cvNoTurn[g:getName()] and 1 or 0)
          end
        end
      end
    end
  end
  -- Carrier into the wind (M5): CVTURN;group;course rad;kt -> 50 NM straight; CVRESUME;group -> mission route from the next waypoint
  local function carrierCmd(line)
    local dv = line:match("^DIVERT;([^;]+)")   -- AI after three waveoffs (M6): to the nearest own/neutral airfield
    if dv then
      local g = Group.getByName(dv)
      local u = g and g:getUnit(1)
      if not u then return end
      local p, best, bd = u:getPoint(), nil, math.huge
      for _, f in ipairs(fields) do
        local c = f.ab:getCoalition()
        if (c == 0 or c == u:getCoalition()) and d2(f.p, p) < bd then best, bd = f, d2(f.p, p) end
      end
      if not best then return end
      cvBound[dv] = nil
      g:getController():setTask({ id = "Mission", params = { route = { points = {
        { x = p.x, y = p.z, alt = p.y, type = "Turning Point", action = "Turning Point", speed = 150 },
        { x = best.p.x, y = best.p.z, alt = best.p.y, type = "Land", action = "Landing", airdromeId = best.id, speed = 120 } } } } })
      return best
    end
    local name, h, kt = line:match("^CVTURN;([^;]+);([%d%.%-]+);([%d%.]+)")
    local g = Group.getByName(name or line:match("^CVRESUME;([^;]+)") or "")
    if not g or not g:getUnit(1) then return end
    cvNoTurn[g:getName()] = nil
    local p, pts = g:getUnit(1):getPoint(), nil
    if name then
      h, kt = tonumber(h), tonumber(kt) * 0.514444
      local len = 50   -- Leg in NM; land ahead (check every 5 NM): ends 2 NM before the last water point, land already at 5 NM -> do not turn
      for d = 5, 50, 5 do
        local ok, st = pcall(land.getSurfaceType, { x = p.x + math.cos(h) * d * NM, y = p.z + math.sin(h) * d * NM })
        if ok and st ~= 2 and st ~= 3 then   -- SurfaceType: 2 SHALLOW_WATER, 3 WATER, otherwise land/road/runway
          len = d - 7
          break
        end
      end
      if len < 1 then cvNoTurn[name] = true log("CVTURN " .. name .. ": Land voraus, nicht gedreht") return end
      pts = { { x = p.x, y = p.z, type = "Turning Point", action = "Turning Point", speed = kt },
              { x = p.x + math.cos(h) * len * NM, y = p.z + math.sin(h) * len * NM, type = "Turning Point", action = "Turning Point", speed = kt } }
    else
      local r, best, bd = boatRoute[g:getName()] or {}, nil, math.huge
      for i, w in ipairs(r) do local d = (w.x - p.x) ^ 2 + (w.y - p.z) ^ 2 if d < bd then best, bd = i, d end end
      if not best or best >= #r then return end   -- Route finished: stays in the wind
      pts = { { x = p.x, y = p.z, type = "Turning Point", action = "Turning Point", speed = r[best + 1].speed or 10 } }
      for i = best + 1, #r do pts[#pts + 1] = { x = r[i].x, y = r[i].y, type = "Turning Point", action = "Turning Point", speed = r[i].speed or 10 } end
    end
    g:getController():setTask({ id = "Mission", params = { route = { points = pts } } })
  end

  -- Range (A17, from KutaisiATC.lua): first trigger zone "Range…" of the mission (circle or quad: farthest corner point) -> Z line; without a zone no range controller.
  -- Impacts of players' bombs/missiles/air-to-ground guided weapons in it: distance and clock position (12 = approach direction) to the nearest ground, ship or static target of the opposing side in the zone,
  -- flight time (the app checks the clearance for the drop, R58); gun hits and kills in the zone -> R/H/K events. Only measure, spawn nothing.
  local RZ
  for _, z in ipairs(env.mission.triggers and env.mission.triggers.zones or {}) do
    if not RZ and tostring(z.name or ""):lower():find("^range") then
      local r = z.radius or 0
      for _, v in ipairs(z.verticies or {}) do r = math.max(r, math.sqrt((v.x - z.x) ^ 2 + (v.y - z.y) ^ 2)) end
      RZ = { x = z.x, z = z.y, r = r }
    end
  end
  HAS.range = RZ ~= nil   -- R390: F10 shows Range only then
  if RZ then
    local function inRange(p) return (p.x - RZ.x) ^ 2 + (p.z - RZ.z) ^ 2 < (RZ.r + 1500) ^ 2 end
    local function nearestTarget(p, side)   -- R369: ground groups, ships and static objects of the opposing side
      local best, bd = nil, 1e9
      local function scan(units)
        for _, u in ipairs(units) do
          local q = u:getPoint()
          local d = math.sqrt((p.x - q.x) ^ 2 + (p.z - q.z) ^ 2)
          if d < bd and inRange(q) then best, bd = u, d end
        end
      end
      for _, cat in ipairs({ Group.Category.GROUND, Group.Category.SHIP }) do
        for _, g in ipairs(coalition.getGroups(3 - side, cat) or {}) do scan(g:getUnits() or {}) end
      end
      scan(coalition.getStaticObjects and coalition.getStaticObjects(3 - side) or {})
      return best, bd
    end
    local function track(w, unit, hdg, side)
      local last, vel, wname, t0 = nil, nil, clean(w:getTypeName()), timer.getTime()
      timer.scheduleFunction(function(_, t)
        if w:isExist() then last, vel = w:getPoint(), w:getVelocity(); return t + 0.05 end
        if not last then return end
        local ip = last
        local sp = math.sqrt(vel.x ^ 2 + vel.y ^ 2 + vel.z ^ 2)
        if sp > 1 then ip = land.getIP(last, { x = vel.x / sp, y = vel.y / sp, z = vel.z / sp }, sp * 0.1 + 30) or last end
        if not inRange(ip) then return end
        local tgt, d = nearestTarget(ip, side)
        if not tgt or d > 500 then evt(string.format("R;%s;%s;-1;0;;%.1f", unit, wname, t - t0)) return end
        local q = tgt:getPoint()
        local clock = math.floor(((math.deg(math.atan2(ip.z - q.z, ip.x - q.x)) - hdg) % 360) / 30 + 0.5) % 12
        evt(string.format("R;%s;%s;%.0f;%d;%s;%.1f", unit, wname, d, clock == 0 and 12 or clock, clean(tgt:getTypeName()), t - t0))
      end, nil, timer.getTime() + 0.05)
    end
    world.addEventHandler({ onEvent = function(_, e) pcall(function()
      local u = e.initiator
      if not (u and u.getPlayerName and u:getPlayerName()) then return end
      local E = world.event
      if e.id == E.S_EVENT_SHOT and e.weapon then
        local d = e.weapon:getDesc() or {}
        if d.category == Weapon.Category.BOMB or d.category == Weapon.Category.ROCKET or (d.category == Weapon.Category.MISSILE and d.missileCategory == Weapon.MissileCategory.OTHER) then
          local pos = u:getPosition()
          track(e.weapon, clean(u:getName()), math.deg(math.atan2(pos.x.z, pos.x.x)), u:getCoalition())
        end
      elseif e.id == E.S_EVENT_HIT and e.target and e.target.getPoint and inRange(e.target:getPoint()) and e.weapon and (e.weapon:getDesc() or {}).category == Weapon.Category.SHELL then
        evt(string.format("H;%s;%s", clean(u:getName()), clean(e.target:getTypeName())))
      elseif e.id == E.S_EVENT_KILL and e.target and e.target.getPoint and inRange(e.target:getPoint()) then
        local tc = 0   -- R369: coalition of the destroyed object (the app scores only the opposing side)
        pcall(function() tc = e.target:getCoalition() end)
        evt(string.format("K;%s;%s;%d", clean(u:getName()), clean(e.target:getTypeName()), tc))
      end
    end) end })
  end

  local G, W, wTime = "G;0;0;80000", {}, -100
  local function weather()
    local v = (wx.visibility and wx.visibility.distance) or 80000
    pcall(function()
      if world.weather.getFogThickness() > 0 then v = math.min(v, world.weather.getFogVisibilityDistance()) end
    end)
    G = string.format("G;%d;%.0f;%.0f;%d", cover, cl.base or 0, v, ceil)
    W = {}
    for _, f in ipairs(fields) do
      local w = atmosphere.getWind({ x = f.p.x, y = f.p.y + 10, z = f.p.z })
      local t, pr = atmosphere.getTemperatureAndPressure({ x = f.p.x, y = f.p.y + 2, z = f.p.z })
      W[#W + 1] = string.format("W;%s;%.2f;%.2f;%.1f;%.0f;%d", f.name, w.x, w.z, t - 273.15, pr, f.ab:getCoalition())   -- Side: conquest (N1)
    end
    for n, side in pairs({ [1] = "red", [2] = "blue" }) do   -- Bullseye per side
      local b = env.mission.coalition and env.mission.coalition[side] and env.mission.coalition[side].bullseye
      if b then W[#W + 1] = string.format("B;%.0f;%.0f;%d", b.x, b.y, n) end
    end
    for _, k in ipairs(beacons) do W[#W + 1] = k end
    if RZ then W[#W + 1] = string.format("Z;%.0f;%.0f;%.0f", RZ.x, RZ.z, RZ.r) end   -- Range (A17)
  end

  -- Create groups ------------------------------------------------------------------------------------
  local seq = 0
  local function offset(p, brg, dist) return { x = p.x + math.cos(brg) * dist, z = p.z + math.sin(brg) * dist } end
  local function addPlane(side, data)
    seq = seq + 1
    data.name = data.name .. " #" .. seq
    for i, u in ipairs(data.units) do u.name = data.name .. "-" .. i end
    local ok, g = pcall(coalition.addGroup, countryOf(side), Group.Category.AIRPLANE, data)
    if not ok or not g then log("Spawn fehlgeschlagen: " .. data.name .. " " .. tostring(g)) return nil end
    silence(g)   -- AI radio is spoken by DCS-ATC; R304: also the tankers (basket/boom continue via the DCS radio menu, MENU)
    return g
  end
  local function cmd(g, id, params) pcall(function() g:getController():setCommand({ id = id, params = params or {} }) end) end

  -- AI traffic --------------------------------------------------------------------------------------------
  -- At the (max. 3) own/neutral airfields closest to players (≤ 60 NM): departures stand cold and
  -- "uncontrolled" on the apron until the app starts them (START, otherwise by themselves after 10 min). Fighter jets exercise 15–25 min
  -- and land again, transports fly to the nearest airfield. Arrivals come from 50 NM, the app can send them to the
  -- holding (WAIT/RESUME). After parking they disappear after 5 min.
  local TYPES = {
    [2] = { fighter = { { "F-16C_50", 3249 }, { "FA-18C_hornet", 4900 }, { "A-10C_2", 5029 }, { "F-15ESE", 10000 } },
            transport = { { "C-130", 20830 }, { "C-17A", 132405 } } },
    [1] = { fighter = { { "Su-27", 5590 }, { "MiG-29A", 3376 }, { "Su-25T", 3790 } },
            transport = { { "Il-76MD", 80000 }, { "An-26B", 5500 } } },
  }
  local CS = { "Enfield", "Springfield", "Uzi", "Colt", "Dodge", "Ford", "Chevy", "Pontiac" }
  local function callsign(side)
    if side == 1 then return math.random(100, 999) end
    local i, f = math.random(#CS), math.random(2, 9)
    return { [1] = i, [2] = f, [3] = 1, name = CS[i] .. f .. "1" }
  end
  local flights = {}   -- [groupName] = { field = f, kind = "dep"|"arr" }
  A.flights = flights
  local function setFlag(name, state) A.ai[name] = state .. ":" .. flights[name].field.name end
  local function stateOf(name) return (A.ai[name] or ""):match("^([%a-]+)") end

  local function planeAt(p, r)
    for side = 1, 2 do
      for _, g in ipairs(coalition.getGroups(side, Group.Category.AIRPLANE) or {}) do
        for _, u in ipairs(g:getUnits() or {}) do if d2(u:getPoint(), p) < r ^ 2 then return true end end
      end
    end
  end
  local function freeSpot(f, big)
    local list = {}
    for _, s in ipairs(f.ab:getParking(true) or {}) do
      local t = s.Term_Type
      local fits = big and (t == 104) or (t == 68 or t == 72 or t == 104)
      if fits and not (blocked[f.id] and blocked[f.id][s.Term_Index]) and not planeAt(s.vTerminalPos, 35) then list[#list + 1] = s end
    end
    return #list > 0 and list[math.random(#list)] or nil
  end

  local function spawnDep(f, side, kind)
    local t = TYPES[side][kind][math.random(#TYPES[side][kind])]
    local s = freeSpot(f, kind == "transport")
    if not s then return end
    local p = s.vTerminalPos
    local points = { { type = "TakeOffParking", action = "From Parking Area", airdromeId = f.id, x = p.x, y = p.z, alt = f.p.y, alt_type = "BARO", speed = 0 } }
    if kind == "fighter" then
      local o, alt = offset(f.p, math.random() * 2 * math.pi, 40 * NM), math.random(4500, 7500)
      points[2] = { type = "Turning Point", action = "Turning Point", x = o.x, y = o.z, alt = alt, alt_type = "BARO", speed = 220,
        task = { id = "ComboTask", params = { tasks = { { number = 1, id = "ControlledTask", enabled = true, auto = false, params = {
          task = { id = "Orbit", params = { pattern = "Circle", altitude = alt, speed = 200 } },
          stopCondition = { duration = math.random(900, 1500) } } } } } } }
      points[3] = { type = "Land", action = "Landing", airdromeId = f.id, x = f.p.x, y = f.p.z, alt = f.p.y, alt_type = "BARO", speed = 150 }
    else
      local dest, dd
      for _, o in ipairs(fields) do
        local d = d2(o.p, f.p)
        if o ~= f and d > (25 * NM) ^ 2 and (o.ab:getCoalition() == side or o.ab:getCoalition() == 0) and (not dd or d < dd) then dest, dd = o, d end
      end
      if not dest then return end
      points[2] = { type = "Land", action = "Landing", airdromeId = dest.id, x = dest.p.x, y = dest.p.z, alt = dest.p.y, alt_type = "BARO", speed = 120 }
    end
    local g = addPlane(side, { name = "ATC " .. f.name .. " DEP", task = kind == "fighter" and "CAP" or "Transport", uncontrolled = true,
      route = { points = points },
      units = { { type = t[1], skill = "High", x = p.x, y = p.z, alt = f.p.y, alt_type = "BARO", heading = 0, speed = 0,
                  parking = s.Term_Index, payload = { fuel = t[2], flare = 30, chaff = 30, gun = 100, pylons = {} }, callsign = callsign(side) } } })
    if not g then return end
    local name = g:getName()
    flights[name] = { field = f, kind = "dep" }
    setFlag(name, "dep")
    cmd(g, "SetInvisible", { value = true })   -- Opponents ignore the exercise traffic
    timer.scheduleFunction(function()          -- App not running: start by themselves after 10 min
      if stateOf(name) == "dep" and Group.getByName(name) then cmd(Group.getByName(name), "Start"); setFlag(name, "dep-go") end
    end, nil, timer.getTime() + 600)
    log("Abflug " .. name .. " (" .. t[1] .. ")")
  end

  local function spawnArr(f, side, kind, at, atAlt)
    local t = TYPES[side][kind][math.random(#TYPES[side][kind])]
    local brg = math.random() * 2 * math.pi
    local s, alt = at or offset(f.p, brg, 50 * NM), atAlt or math.random(4000, 6500)
    local g = addPlane(side, { name = "ATC " .. f.name .. " ARR", task = kind == "fighter" and "CAP" or "Transport",
      route = { points = {
        { type = "Turning Point", action = "Turning Point", x = s.x, y = s.z, alt = alt, alt_type = "BARO", speed = 220 },
        -- Speed applies to the leg there (TAS, ~290 kt; 80 m/s was crawling at altitude), approach slows the AI itself
        { type = "Land", action = "Landing", airdromeId = f.id, x = f.p.x, y = f.p.z, alt = f.p.y, alt_type = "BARO", speed = 150 } } },
      units = { { type = t[1], skill = "High", x = s.x, y = s.z, alt = alt, alt_type = "BARO", heading = math.atan2(f.p.z - s.z, f.p.x - s.x),
                  speed = 220, payload = { fuel = t[2] * 0.5, flare = 30, chaff = 30, gun = 100, pylons = {} }, callsign = callsign(side) } } })
    if not g then return end
    flights[g:getName()] = { field = f, kind = "arr" }
    setFlag(g:getName(), "arr")
    cmd(g, "SetInvisible", { value = true })
    log("Anflug " .. g:getName() .. " (" .. t[1] .. ")")
  end

  -- Arrivals at mission start (option arrivals = { ["Senaki-Kolkhi"] = 5 }): one after another from one direction,
  -- 10, 14, 18 ... NM, staggered in altitude -> land one after another ahead of the player
  for fname, n in pairs(type(OPT.arrivals) == "table" and OPT.arrivals or {}) do
    for _, f in ipairs(fields) do
      if f.name == fname then
        local side, brg = f.ab:getCoalition() == 1 and 1 or 2, math.random() * 2 * math.pi
        for i = 1, n do spawnArr(f, side, "fighter", offset(f.p, brg, (6 + 4 * i) * NM), 900 + 300 * i) end
      end
    end
  end

  -- Terrain around airfields (minimum altitudes of radar vectoring): grid 1 NM, ±30 NM, per cell highest of 3×3 points.
  -- Airfields near players first and every mission anew, the rest once per map (file missing); one after another, 4 lines per 0.1 s.
  -- File %TMP%\DcsAtc-Terrain-<map>-<Id>.txt
  local terrainDone, terrainQ, terrainBusy = {}, {}, false
  local function terrainFile(f) return TMP .. "\\DcsAtc-Terrain-" .. tostring(env.mission.theatre) .. "-" .. f.id .. ".txt" end
  local function terrainNext()
    local f = table.remove(terrainQ, 1)
    terrainBusy = f ~= nil
    if not f then return end
    local n, cell = 61, NM
    local x0, z0 = f.p.x - 30 * NM, f.p.z - 30 * NM
    local rows, i = {}, 0
    timer.scheduleFunction(function()
      for _ = 1, 4 do
        if i >= n then
          writeFile(terrainFile(f), string.format("%s;%.1f;%.1f;%.1f;%d\n", f.name, x0, z0, cell, n) .. table.concat(rows, "\n") .. "\n")
          terrainNext()
          return nil
        end
        local r = {}
        for j = 0, n - 1 do
          local m = 0
          for a = 0, 2 do for b = 0, 2 do
            local h = land.getHeight({ x = x0 + (i + a / 2) * cell, y = z0 + (j + b / 2) * cell })
            if h > m then m = h end
          end end
          r[#r + 1] = string.format("%.0f", m)
        end
        rows[#rows + 1] = table.concat(r, ",")
        i = i + 1
      end
      return timer.getTime() + 0.1
    end, nil, timer.getTime() + 0.1)
  end
  local function terrainFor(f, first)
    if terrainDone[f.id] then return end
    terrainDone[f.id] = true
    table.insert(terrainQ, first and 1 or #terrainQ + 1, f)
    if not terrainBusy then terrainNext() end
  end
  local function allTerrain()
    for _, f in ipairs(fields) do
      local fh = io.open(terrainFile(f), "r")
      if fh then fh:close() else terrainFor(f) end
    end
  end

  -- Coarse grid over the whole map (V15: terrain warning en route): airfields ±60 NM, cell 2 NM, once per map
  local function mapTerrain()
    local file = TMP .. "\\DcsAtc-Terrain-" .. tostring(env.mission.theatre) .. "-map.txt"
    local fh = io.open(file, "r")
    if fh then fh:close() return end
    if #fields == 0 then return end
    local x0, z0, x1, z1 = math.huge, math.huge, -math.huge, -math.huge
    for _, f in ipairs(fields) do
      x0, z0 = math.min(x0, f.p.x), math.min(z0, f.p.z)
      x1, z1 = math.max(x1, f.p.x), math.max(z1, f.p.z)
    end
    local cell = 2 * NM
    x0, z0 = x0 - 60 * NM, z0 - 60 * NM
    local n = math.ceil(math.max(x1 - x0, z1 - z0) / cell + 60 * NM / cell)
    local rows, i = {}, 0
    timer.scheduleFunction(function()
      for _ = 1, 2 do
        if i >= n then
          writeFile(file, string.format("Map;%.1f;%.1f;%.1f;%d\n", x0, z0, cell, n) .. table.concat(rows, "\n") .. "\n")
          return nil
        end
        local r = {}
        for j = 0, n - 1 do
          local m = 0
          for a = 0, 2 do for b = 0, 2 do
            local h = land.getHeight({ x = x0 + (i + a / 2) * cell, y = z0 + (j + b / 2) * cell })
            if h > m then m = h end
          end end
          r[#r + 1] = string.format("%.0f", m)
        end
        rows[#rows + 1] = table.concat(r, ",")
        i = i + 1
      end
      return timer.getTime() + 0.1
    end, nil, timer.getTime() + 1)
  end
  timer.scheduleFunction(function() pcall(mapTerrain) pcall(allTerrain) return nil end, nil, timer.getTime() + 15)

  local nextSpawn = {}
  local function players()
    local l = {}
    for side = 1, 2 do
      for _, cat in ipairs({ Group.Category.AIRPLANE, Group.Category.HELICOPTER }) do
        for _, g in ipairs(coalition.getGroups(side, cat) or {}) do
          for _, u in ipairs(g:getUnits() or {}) do
            if u:getPlayerName() then l[#l + 1] = { p = u:getPoint(), side = side } end
          end
        end
      end
    end
    return l
  end
  timer.scheduleFunction(function()
    local now = timer.getTime()
    local total, per = 0, {}
    for name, fl in pairs(flights) do
      if Group.getByName(name) then total = total + 1; per[fl.field] = (per[fl.field] or 0) + 1
      else flights[name] = nil; A.ai[name] = nil end
    end
    local near = {}
    for _, pl in ipairs(players()) do
      for _, f in ipairs(fields) do
        local c, d = f.ab:getCoalition(), d2(f.p, pl.p)
        if (c == pl.side or c == 0) and d < (60 * NM) ^ 2 and (not near[f] or d < near[f].d) then near[f] = { d = d, side = pl.side } end
      end
    end
    local list = {}
    for f in pairs(near) do terrainFor(f, true) end
    for f, x in pairs(near) do list[#list + 1] = { f = f, d = x.d, side = x.side } end
    table.sort(list, function(a, b) return a.d < b.d end)
    local lv = ({ [0] = { 0, 0, 1 }, { 3, 1, 2 }, { 6, 2, 1 }, { 10, 3, 0.6 } })[OPT.traffic] or { 6, 2, 1 }   -- total, per airfield, distance factor
    for i = 1, math.min(3, #list) do
      local f, side = list[i].f, list[i].side
      nextSpawn[f] = nextSpawn[f] or now + 30
      if total < lv[1] and (per[f] or 0) < lv[2] and now >= nextSpawn[f] then
        nextSpawn[f] = now + math.random(300, 600) * lv[3]
        local kind = math.random() < 0.3 and "transport" or "fighter"
        if math.random() < 0.5 then spawnDep(f, side, kind) else spawnArr(f, side, kind) end
        total = total + 1
      end
    end
    return now + 20
  end, nil, timer.getTime() + 10)

  onOpt.traffic = function()   -- off: existing AI flights gone immediately
    if OPT.traffic ~= 0 then return end
    for name in pairs(flights) do local g = Group.getByName(name) if g then g:destroy() end flights[name] = nil; A.ai[name] = nil end
  end

  -- Air refueling of the player: real contact/separation (instead of distance to the tanker) -> "F;Unit;start|stop;Tank 0–1;internal tank kg" (kg = fuelMassMax, from it the amount delivered in lb)
  world.addEventHandler({ onEvent = function(_, e)
    local u = e.initiator
    if e.id ~= world.event.S_EVENT_REFUELING and e.id ~= world.event.S_EVENT_REFUELING_STOP then return end
    if not u or not u.getPlayerName or not u:getPlayerName() then return end
    evt(string.format("F;%s;%s;%.3f;%.0f", clean(u:getName()), e.id == world.event.S_EVENT_REFUELING and "start" or "stop", u:getFuel() or 0, (u:getDesc() or {}).fuelMassMax or 0))
  end })

  -- R27: LSO grade from DCS on the player's trap -> "W;Unit;Wire;grade" (Wire from "WIRE# 3"), instead of the estimate from the touchdown point
  world.addEventHandler({ onEvent = function(_, e)
    local u = e.initiator
    if e.id ~= world.event.S_EVENT_LANDING_QUALITY_MARK or not u or not u.getPlayerName or not u:getPlayerName() then return end
    local w = tostring(e.comment or ""):match("WIRE#%s*(%d)")
    if w then evt(string.format("W;%s;%s;%s", clean(u:getName()), w, clean(e.comment))) end
  end })

  -- Player spawns (dynamic slots) beside/on parked AI: AI gone
  world.addEventHandler({ onEvent = function(_, e)
    if e.id ~= world.event.S_EVENT_BIRTH or not e.initiator or not e.initiator.getPlayerName or not e.initiator:getPlayerName() then return end
    local p = e.initiator:getPoint()
    for name in pairs(flights) do
      local g = Group.getByName(name)
      for _, u in ipairs(g and g:getUnits() or {}) do
        if not u:inAir() and d2(u:getPoint(), p) < 40 ^ 2 then g:destroy(); flights[name] = nil; A.ai[name] = nil break end
      end
    end
  end })

  -- Parked AI gone after 5 min; damaged or 10 min stuck AI on the ground immediately (blocks taxiways). Player: "C;Unit;stopped" (parked)
  world.addEventHandler({ onEvent = function(_, e)
    if e.id ~= world.event.S_EVENT_ENGINE_SHUTDOWN or not e.initiator or not e.initiator.getGroup then return end
    if e.initiator.getPlayerName and e.initiator:getPlayerName() then evt("C;" .. clean(e.initiator:getName()) .. ";stopped") return end
    local g = e.initiator:getGroup()
    local name = g and g:getName()
    if name and flights[name] then
      timer.scheduleFunction(function() local x = Group.getByName(name) if x then x:destroy() end end, nil, timer.getTime() + 300)
    end
  end })
  local stuck = {}
  timer.scheduleFunction(function()
    for name in pairs(flights) do
      local g = Group.getByName(name)
      local u = g and g:getUnit(1)
      if u and not u:inAir() then
        local damaged = u:getLife() < u:getLife0()
        local p, s = u:getPoint(), stuck[name]
        if s and d2(p, s) < 25 then s.n = s.n + 1 else stuck[name] = { x = p.x, z = p.z, n = 0 } end
        if damaged or (stateOf(name) ~= "dep" and stuck[name].n >= 10) then
          log(name .. (damaged and " beschädigt" or " sitzt fest") .. " – entfernt")
          g:destroy(); flights[name] = nil; A.ai[name] = nil; stuck[name] = nil
        end
      end
    end
    return timer.getTime() + 60
  end, nil, timer.getTime() + 60)

  -- Clear away wrecks/debris at airfields after 60 s
  local junk = { [world.event.S_EVENT_CRASH] = true, [world.event.S_EVENT_DEAD] = true, [world.event.S_EVENT_UNIT_LOST] = true }
  world.addEventHandler({ onEvent = function(_, e)
    if not junk[e.id] or not e.initiator or not e.initiator.getPoint then return end
    local ok, p = pcall(e.initiator.getPoint, e.initiator)
    if not ok or not p or not nearestField(p, 5000) then return end
    timer.scheduleFunction(function()
      pcall(world.removeJunk, { id = world.VolumeType.SPHERE, params = { point = p, radius = 150 } })
    end, nil, timer.getTime() + 60)
  end })

  -- Tanker and AWACS (only if the mission has none for the player side) ---------------------------------
  local function hasTask(side, task)
    local c = env.mission.coalition and env.mission.coalition[side == 1 and "red" or "blue"]
    for _, ctry in pairs(c and c.country or {}) do
      for _, g in pairs(ctry.plane and ctry.plane.group or {}) do if g.task == task then return true end end
    end
  end
  local TRACKS = {   -- Racetrack pattern (x/z), otherwise over the centroid of the own airfields
    Caucasus = { [2] = { tanker = { -250000, 560000, -300000, 590000 }, awacs = { -280000, 520000, -330000, 545000 } },
                 [1] = { tanker = { -60000, 330000, -30000, 380000 }, awacs = { -50000, 270000, -20000, 320000 } } },
  }
  local function track(side, what)
    local t = TRACKS[env.mission.theatre] and TRACKS[env.mission.theatre][side]
    if t then local v = t[what] return { x = v[1], z = v[2] }, { x = v[3], z = v[4] } end
    local sx, sz, k = 0, 0, 0
    for _, f in ipairs(fields) do
      if f.ab:getCoalition() == side or f.ab:getCoalition() == 0 then sx, sz, k = sx + f.p.x, sz + f.p.z, k + 1 end
    end
    if k == 0 then return nil end
    local c = { x = sx / k, z = sz / k + (what == "awacs" and -40000 or 0) }
    return { x = c.x - 25000, z = c.z }, { x = c.x + 25000, z = c.z }
  end
  local function tacan(ch) return (ch < 64 and 962 or 1151) + ch - (ch < 64 and 1 or 64) end   -- MHz, mode X
  local ORBIT = {
    { side = 2, task = "Refueling", what = "tanker", type = "KC135MPRS", cs = 1, csName = "Texaco", ch = 51, ident = "TEX", ft = 22000, kt = 270, track = "Alpha" },
    { side = 2, task = "Refueling", what = "tanker", type = "KC-135", cs = 2, csName = "Arco", ch = 52, ident = "ARC", ft = 24000, kt = 280, dz = 15000, track = "Bravo" },
    { side = 2, task = "AWACS", what = "awacs", type = "E-3A", cs = 1, csName = "Overlord", ft = 30000, kt = 300 },
    { side = 1, task = "Refueling", what = "tanker", type = "IL-78M", ch = 51, ident = "TKR", ft = 20000, kt = 270, track = "Alpha" },
    { side = 1, task = "AWACS", what = "awacs", type = "A-50", ft = 30000, kt = 300 },
  }
  local function spawnOrbit(o)
    local p1, p2 = track(o.side, o.what)
    if not p1 then return end
    local dz = o.dz or 0
    p1, p2 = { x = p1.x, z = p1.z + dz }, { x = p2.x, z = p2.z + dz }
    local alt, spd = o.ft * 0.3048, o.kt * (1 + 0.02 * o.ft / 1000) * 0.5144   -- kt = IAS; DCS wants TAS (≈ +2 %/1000 ft)
    local g = addPlane(o.side, { name = "ATC " .. (o.csName or o.type), task = o.task,
      frequency = o.what == "tanker" and 255.5 or 251.5, modulation = 0, communication = true,   -- as DCS-ATC (tanker 255.5, AWACS 251.5; not on an airfield frequency)
      route = { points = { { type = "Turning Point", action = "Turning Point", x = p1.x, y = p1.z, alt = alt, alt_type = "BARO", speed = spd,
        task = { id = "ComboTask", params = { tasks = {
          { number = 1, id = o.what == "tanker" and "Tanker" or "AWACS", enabled = true, auto = false, params = {} },
          { number = 2, id = "Orbit", enabled = true, auto = false, params = { pattern = "Race-Track", point = { x = p1.x, y = p1.z },
            point2 = { x = p2.x, y = p2.z }, altitude = alt, speed = spd } } } } } } } },
      units = { { type = o.type, skill = "Excellent", x = p1.x, y = p1.z, alt = alt, alt_type = "BARO", speed = spd,
                  heading = math.atan2(p2.z - p1.z, p2.x - p1.x), payload = { fuel = 90000, flare = 0, chaff = 0, gun = 100, pylons = {} },
                  callsign = o.side == 2 and { [1] = o.cs, [2] = 1, [3] = 1, name = o.csName .. "11" } or math.random(100, 999) } } })
    if not g then return end
    cmd(g, "SetUnlimitedFuel", { value = true })
    cmd(g, "SetInvisible", { value = true })
    cmd(g, "SetImmortal", { value = true })
    if o.ch then
      local u = g:getUnit(1)
      cmd(g, "ActivateBeacon", { type = 4, system = 4, name = o.ident, callsign = o.ident, frequency = tacan(o.ch) * 1000000,
                                 AA = true, channel = o.ch, modeChannel = "X", bearing = true, unitId = u and u:getID() })
      beacons[#beacons + 1] = string.format("K;%s;%dX;%s", clean(g:getName()), o.ch, o.track or "")   -- Track name for the radio
    end
    if o.what == "awacs" then beacons[#beacons + 1] = string.format("F;%s;251.500", clean(g:getName())) end
    log(o.task .. " " .. g:getName() .. " (" .. o.type .. ")")
    return g:getName()
  end
  local orbiting = {}
  local function orbitTick()   -- create, recreate after loss, remove when switched off
    for i, o in ipairs(ORBIT) do
      local g = orbiting[i] and Group.getByName(orbiting[i])
      if not OPT[o.what] then
        if g then g:destroy() end
        orbiting[i] = nil
      elseif playerSides[o.side] and not hasTask(o.side, o.task) and not g then
        orbiting[i] = spawnOrbit(o)
      end
    end
  end
  onOpt.tanker, onOpt.awacs = orbitTick, orbitTick
  timer.scheduleFunction(function() orbitTick() return timer.getTime() + 300 end, nil, timer.getTime() + 5)

  -- Ground crew ------------------------------------------------------------------------------------------
  -- When an aircraft stands at a parking spot for 10 s: crew chief front left, fire watch front right. After a landing
  -- fuel and ammunition trucks drive up from the next free parking spot (4 min, scenery only – refueling/arming continues via F8).
  -- When it starts rolling: soldiers step back, trucks drive away, then all disappear. Crash/crash landing: fire brigade.
  local GC = {
    [2] = { soldier = "Soldier M4", fuel = "M978 HEMTT Tanker", ammo = "M 818", fire = "HEMTT TFFT" },
    [1] = { soldier = "Infantry AK", fuel = "ATZ-10", ammo = "KAMAZ Truck", fire = "Ural ATsP-6" },
  }
  local TURN = 240
  local crew, landed, still, gcN = {}, {}, {}, 0

  local function rel(pos, f, r) return { x = pos.p.x + pos.x.x * f + pos.z.x * r, z = pos.p.z + pos.x.z * f + pos.z.z * r } end
  local function spawnGround(ctry, units, route)
    gcN = gcN + 1
    local name = "ATC GC " .. gcN
    local us = {}
    for i, u in ipairs(units) do
      us[i] = { name = name .. "-" .. i, type = u.type, x = u.x, y = u.z, heading = u.hdg or 0, skill = "Average", playerCanDrive = false }
    end
    local ok, g = pcall(coalition.addGroup, ctry, Group.Category.GROUND, { name = name, task = "Ground Nothing", units = us,
      route = route and { points = route } or nil })
    if ok and g then pcall(function() g:getController():setOption(AI.Option.Ground.id.ROE, AI.Option.Ground.val.ROE.WEAPON_HOLD) end) end
    return name
  end
  local function wp(p, speed) return { x = p.x, y = p.z, type = "Turning Point", action = "Off Road", speed = speed or 6 } end
  local function drive(name, from, to, speed)
    local g = Group.getByName(name)
    if g then g:getController():setTask({ id = "Mission", params = { route = { points = { wp(from, speed), wp(to, speed) } } } }) end
  end
  local function kill(names) for _, nm in ipairs(names) do local g = Group.getByName(nm) if g then g:destroy() end end end
  local function clear(c) kill(c.soldiers); for _, t in ipairs(c.trucks) do kill({ t.name }) end end

  -- next free parking spot 80–500 m away (start point of the trucks), otherwise 150 m behind the aircraft
  local function depot(f, pos)
    local best, bd
    for _, s in ipairs(f and f.spots or {}) do
      local d = d2(s, pos.p)
      if d > 80 ^ 2 and d < 500 ^ 2 and (not bd or d < bd) and not planeAt(s, 30) then best, bd = s, d end
    end
    return best and { x = best.x, z = best.z } or rel(pos, -150, 0)
  end
  local function taxiingNear(p, r)
    for side = 1, 2 do
      for _, g in ipairs(coalition.getGroups(side, Group.Category.AIRPLANE) or {}) do
        for _, u in ipairs(g:getUnits() or {}) do
          local v = u:getVelocity()
          if not u:inAir() and v.x ^ 2 + v.z ^ 2 > 4 and d2(u:getPoint(), p) < r ^ 2 then return true end
        end
      end
    end
  end
  local function fireTruck(p, ctry, side)
    if not OPT.crew then return end
    local pos = { p = p, x = { x = 1, z = 0 }, z = { x = 0, z = 1 } }
    local from = depot(nearestField(p, 3000), pos)
    local nm = spawnGround(ctry, { { type = (GC[side] or GC[2]).fire, x = from.x, z = from.z } }, { wp(from, 15), wp({ x = p.x + 25, z = p.z }, 15) })
    timer.scheduleFunction(function() kill({ nm }) end, nil, timer.getTime() + 300)
  end

  world.addEventHandler({ onEvent = function(_, e)
    local u = e.initiator
    if not u or not u.getPoint or not u.getCoalition then return end
    local ok, p = pcall(u.getPoint, u)
    if not ok or not nearestField(p, 3000) then return end
    if e.id == world.event.S_EVENT_LAND and u.getName then
      landed[u:getName()] = true
      if u:getLife() < u:getLife0() * 0.8 then fireTruck(p, u:getCountry(), u:getCoalition()) end
    elseif e.id == world.event.S_EVENT_CRASH and u.getCountry then
      fireTruck(p, u:getCountry(), u:getCoalition())
    end
  end })

  local function tickCrew()
    local now = timer.getTime()
    if not OPT.crew then
      for name, c in pairs(crew) do clear(c); crew[name] = nil end
      return now + 2
    end
    local seen = {}
    for side = 1, 2 do
      for _, g in ipairs(coalition.getGroups(side, Group.Category.AIRPLANE) or {}) do
        for _, u in ipairs(g:getUnits() or {}) do
          local name, pos = u:getName(), u:getPosition()
          local v = u:getVelocity()
          local spd = math.sqrt(v.x ^ 2 + v.z ^ 2)
          local c = crew[name]
          seen[name] = true
          local f = not u:inAir() and nearestField(pos.p, 3000)
          if f then
            local types = GC[side] or GC[2]
            if not c and spd < 0.5 then
              local atSpot = false
              for _, s in ipairs(f.spots) do if d2(s, pos.p) < 30 ^ 2 then atSpot = true break end end
              still[name] = atSpot and (still[name] or now) or nil
              if atSpot and now - still[name] >= 10 then   -- 10 s standstill: parked, not just briefly stopped
                still[name] = nil
                local b = u:getDesc().box or { max = { x = 8, z = 5 }, min = { x = -8 } }
                local cc, fg = rel(pos, b.max.x + 5, -b.max.z * 0.7), rel(pos, b.max.x * 0.4, b.max.z + 5)
                local function face(q) return math.atan2(pos.p.z - q.z, pos.p.x - q.x) end
                c = { soldiers = { spawnGround(u:getCountry(), { { type = types.soldier, x = cc.x, z = cc.z, hdg = face(cc) },
                                                                 { type = types.soldier, x = fg.x, z = fg.z, hdg = face(fg) } }) },
                      trucks = {}, box = b, ctry = u:getCountry(), types = types, field = f, player = u:getPlayerName() ~= nil }
                if landed[name] then c.trucksAt = now + 20 end
                if c.player then evt("C;" .. clean(name) .. (landed[name] and ";back" or ";crew")) end
                landed[name] = nil
                crew[name] = c
              end
            elseif c and not c.leaving and spd > 1.5 then
              c.leaving = now   -- starts rolling
              local s = Group.getByName(c.soldiers[1])
              local q = s and s:getUnit(1) and s:getUnit(1):getPoint()
              if q then drive(c.soldiers[1], q, { x = q.x + (q.x - pos.p.x), z = q.z + (q.z - pos.p.z) }, 3) end
              for _, t in ipairs(c.trucks) do drive(t.name, t.at, t.home, 8) end
            elseif c and not c.leaving and c.trucksAt and now >= c.trucksAt and #c.trucks == 0 then
              if taxiingNear(pos.p, 200) then
                c.trucksAt = now + 10   -- drive only when nobody is taxiing here
              else
                local b, home = c.box, depot(c.field, pos)
                for _, t in ipairs({ { c.types.fuel, rel(pos, b.min.x * 0.3, b.max.z + 9) }, { c.types.ammo, rel(pos, b.min.x * 0.5, -b.max.z - 10) } }) do
                  local nm = spawnGround(c.ctry, { { type = t[1], x = home.x, z = home.z } }, { wp(home), wp(t[2]) })
                  c.trucks[#c.trucks + 1] = { name = nm, at = t[2], home = home }
                  home = { x = home.x + 12, z = home.z }
                end
                c.doneAt = now + TURN
                if c.player then evt("C;" .. clean(name) .. ";fuel") end
              end
            elseif c and not c.leaving and c.doneAt and now >= c.doneAt then
              c.doneAt = nil
              for _, t in ipairs(c.trucks) do drive(t.name, t.at, t.home, 8) end
              c.trucksGone = now   -- Trucks drive away silently (A37: nothing refueled, so no "done" message)
            end
          end
          if c and c.leaving and now - c.leaving > 40 then clear(c); crew[name] = nil
          elseif c and c.trucksGone and now - c.trucksGone > 90 then
            for _, t in ipairs(c.trucks) do kill({ t.name }) end
            c.trucks, c.trucksGone, c.trucksAt = {}, nil, nil
          end
        end
      end
    end
    for name, c in pairs(crew) do if not seen[name] then clear(c); crew[name] = nil end end
    return now + 2
  end
  timer.scheduleFunction(function() local ok, e = pcall(tickCrew) if not ok then log("Bodenpersonal: " .. tostring(e)) end return timer.getTime() + 2 end,
    nil, timer.getTime() + 5)

  -- Status to the app, commands/texts from the app ------------------------------------------------------------
  -- Radar picture (V9): what own AWACS/EWR/ships have detected (DCS computes terrain and radar horizon).
  -- "D;side" = side queried, "D;side;unit;1|0" = detected, type known yes/no
  local D, dTime = {}, -100
  local function detected()
    D = {}
    for side = 1, 2 do
      D[#D + 1] = "D;" .. side
      local seen, gci = {}, false
      for _, cat in ipairs({ Group.Category.AIRPLANE, Group.Category.GROUND, Group.Category.SHIP }) do
        for _, g in ipairs(coalition.getGroups(side, cat) or {}) do
          local u = g:getUnit(1)
          if u and u:isExist() and (cat == Group.Category.SHIP or u:hasAttribute("AWACS") or u:hasAttribute("EWR")) then
            if cat == Group.Category.GROUND then gci = true end   -- Ground radar: GCI without AWACS (V19)
            for _, t in ipairs(g:getController():getDetectedTargets(Controller.Detection.RADAR, Controller.Detection.DLINK) or {}) do
              local o = t.object
              if o and o:isExist() and o:getCategory() == Object.Category.UNIT and o:inAir() then
                local n = clean(o:getName())
                if not seen[n] or t.type then seen[n] = t.type and "1" or "0" end
              end
            end
          end
        end
      end
      for n, k in pairs(seen) do D[#D + 1] = "D;" .. side .. ";" .. n .. ";" .. k end
      if gci then D[#D + 1] = "R;" .. side end
    end
  end

  -- KF6 (LK14): hostile radars (aircraft, SAMs on the ground and on ships) tracking an AI flyer (unit:getRadar() -> on, tracked object).
  -- Newly locked: "X;spike;target;emitter type;air|sam;x;z" (x/z = emitter), no radar on the target anymore: "X;naked;target". Only on change; AWACS/EWR (search radar) not.
  local spiked = {}   -- Target -> { Emitter = true }
  local function spikes()
    local now = {}
    for side = 1, 2 do
      for _, cat in ipairs({ Group.Category.AIRPLANE, Group.Category.GROUND, Group.Category.SHIP }) do
        for _, g in ipairs(coalition.getGroups(side, cat) or {}) do
          for _, e in ipairs(g:getUnits() or {}) do
            local ok, on, o = pcall(e.getRadar, e)
            if ok and on and o and isAi(o) and o:getCoalition() ~= side and not (e:hasAttribute("AWACS") or e:hasAttribute("EWR")) then
              local t, en = uname(o), uname(e)
              now[t] = now[t] or {}
              now[t][en] = true
              if not (spiked[t] and spiked[t][en]) then
                local p = e:getPoint()
                evt(string.format("X;spike;%s;%s;%s;%.0f;%.0f", t, clean(e:getTypeName()), cat == Group.Category.AIRPLANE and "air" or "sam", p.x, p.z))
              end
            end
          end
        end
      end
    end
    for t in pairs(spiked) do if not now[t] then evt("X;naked;" .. t) end end
    spiked = now
  end

  local sunAt   -- defined below (state() needs it for the Y line)
  local function state()
    if timer.getTime() - wTime > 10 then wTime = timer.getTime(); weather() end
    if timer.getTime() - dTime >= 2 then dTime = timer.getTime(); pcall(detected); pcall(spikes) end
    pcall(trackWeapons)
    local l = { G }
    for _, w in ipairs(W) do l[#l + 1] = w end
    for _, d in ipairs(D) do l[#l + 1] = d end
    cvNear = false
    pcall(carriers, l)
    if timer.getAbsTime then   -- Mission clock (Expected Charlie) and sun elevation at the first carrier, otherwise at the first airfield (case selection at night); without sun only the clock
      local pt = fields[1] and fields[1].p
      for _, s in ipairs(l) do
        local x, z = s:match("^C;[^;]*;[^;]*;[^;]*;([%-%d%.]+);([%-%d%.]+)")
        if x then pt = { x = tonumber(x), y = 0, z = tonumber(z) } break end
      end
      local ok, sun = pcall(sunAt, pt)
      l[#l + 1] = string.format("Y;%.0f", timer.getAbsTime()) .. (ok and string.format(";%.1f", sun) or "")
    end
    l[#l + 1] = string.format("S;%.1f", timer.getTime())   -- Mission run time: if it jumps back, it is a new mission (ATIS identifiers anew)
    for side = 0, 2 do
      for _, cat in ipairs({ Group.Category.AIRPLANE, Group.Category.HELICOPTER }) do
        for _, g in ipairs(coalition.getGroups(side, cat) or {}) do
          local kind = cat == Group.Category.HELICOPTER and "h" or "a"   -- N50: helicopter/aircraft
          for _, u in ipairs(g:getUnits() or {}) do
            if u:isExist() and u:isActive() then
              local p, v, pos = u:getPoint(), u:getVelocity(), u:getPosition()
              local hdg = math.atan2(pos.x.z, pos.x.x)
              if hdg < 0 then hdg = hdg + 2 * math.pi end
              local spd = math.sqrt(v.x * v.x + v.z * v.z)
              local air = u:inAir() and 1 or 0
              local player = u:getPlayerName()
              if player then
                buildMenu(g, u)
                for _, c in ipairs(cvPts) do if air == 1 and d2(p, c) < (2 * NM) ^ 2 then cvNear = true end end
                local w = atmosphere.getWind(p)
                local _, pr = atmosphere.getTemperatureAndPressure(p)
                local gok, gear = pcall(u.getDrawArgumentValue, u, 0)   -- Nose gear 0 = up, 1 = down (LSO: no "call the ball" before the break)
                l[#l + 1] = string.format("P;%s;%d;%s;%s;%s;%.1f;%.1f;%.1f;%.1f;%.1f;%.4f;%.2f;%.2f;%.0f;%.2f;%d;%s;%d;%.2f;%.2f;%s",
                  clean(u:getName()), g:getID(), clean(u:getCallsign()), clean(player), clean(u:getTypeName()),
                  p.x, p.z, p.y, p.y - land.getHeight({ x = p.x, y = p.z }), spd, hdg, w.x, w.z, pr, v.y, air,
                  tostring(u.id_ or u:getID()), u:getCoalition(), u.getFuel and u:getFuel() or 1, gok and tonumber(gear) or -1, kind)   -- id_ = runtime ID as in SRS; tank 0–1; gear (-1 unknown); h/a
              else
                local un = clean(u:getName())
                if not ammo[un] then local ok, a = pcall(ammoOf, u) ammo[un] = ok and a or "" end
                if not fmax[un] then local ok, d = pcall(u.getDesc, u) fmax[un] = ok and d and d.fuelMassMax or 0 end   -- LK15: internal tank kg (ops check in 1000 lb)
                l[#l + 1] = string.format("T;%s;%s;%s;%.1f;%.1f;%.1f;%.4f;%.1f;%d;%s;%d;%s;%.2f;%d;%.3f;%s;%s;%.0f;%s",
                  un, clean(g:getName()), clean(u:getTypeName()), p.x, p.z, p.y, hdg, spd, air,
                  clean(A.ai[g:getName()] or (cvBound[g:getName()] and "cv") or ""), u:getCoalition(), clean(u:getCallsign()), u.getFuel and u:getFuel() or 1, g:getID(),   -- Tank 0–1: fuel shortage -> priority; group ID: AI flight member of the player
                  gfreq[g:getName()] or gfreq[(g:getName():gsub("%s*#%d+$", ""))] or 0, ammo[un], kind, fmax[un],   -- Flight frequency (editor), ammunition fox3/fox1/fox2/gun/air-to-ground (AI radio), h/a, internal tank kg
                  clean(gtask[g:getName()] or gtask[(g:getName():gsub("%s*#%d+$", ""))] or ""))   -- editor task (CAP, SEAD, ...)
              end
            end
          end
        end
      end
    end
    writeFile(TMP .. "\\DcsAtc-State.txt", table.concat(l, "\n") .. "\n")
  end

  -- DCS radio menu for the player (the app then presses the keys): position of tanker or airfield in the DCS list,
  -- sorted like RadioCommandDialogsPanel (Config/Common/Tanker.lua, ATC.lua) by distance -> "M;Unit;tanker|atc|dep;number;count", otherwise "M;Unit;type;0;reason".
  -- Airfield (lighting, atc = "Inbound" in the air, dep = "Request startup" on the ground): only sun below 6° or poor visibility (flag w of the app),
  -- the ATC is audible for 15 s for this (it switches on the lighting with the answer), then silent again.
  -- Easy Communication off: list the same (just grayed), but DCS does not tune by itself -> without a tuned airfield frequency (flag u, SRS) nothing gets through.
  local CUM = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 }
  local TZ = { Caucasus = 4, PersianGulf = 4, Nevada = -8, Normandy = 0, TheChannel = 2, Syria = 3, MarianaIslands = 10,
               Falklands = -3, SinaiMap = 2, Kola = 3, Afghanistan = 4.5, Iraq = 3, GermanyCW = 1 }   -- Mission clock = local time of the map, UTC+h (as MOOSE)
  function sunAt(p)   -- Sun elevation in degrees at point p at mission time
    local lat, lon = coord.LOtoLL(p)
    local t, d = timer.getAbsTime(), env.mission.date or {}
    local b = 2 * math.pi / 365 * (CUM[d.Month or 6] + (d.Day or 21) + math.floor(t / 86400) - 81)
    local eot = 9.87 * math.sin(2 * b) - 7.53 * math.cos(b) - 1.5 * math.sin(b)   -- Equation of time in min
    local h = math.rad(15 * ((t % 86400) / 3600 - (TZ[env.mission.theatre] or lon / 15) + lon / 15 + eot / 60 - 12))
    local decl, la = math.rad(23.44) * math.sin(b), math.rad(lat)
    return math.deg(math.asin(math.sin(la) * math.sin(decl) + math.cos(la) * math.cos(decl) * math.cos(h)))
  end
  local easy = true
  pcall(function()
    local fo = env.mission.forcedOptions
    if fo and fo.easyCommunication ~= nil then easy = fo.easyCommunication return end
    local h = io.open(lfs.writedir() .. "Config\\options.lua", "r")
    if h then easy = h:read("*a"):match('%["easyCommunication"%]%s*=%s*(%a+)') ~= "false" h:close() end
  end)
  -- Open DCS-ATC dialog (hot start/runway, answer to our request) shows its entries on opening instead of F5 ATC: the keys would hit "Abort ..."
  local dlg = {}
  world.addEventHandler({ onEvent = function(_, e)
    local u, bp, E = e.initiator, world.BirthPlace or {}, world.event
    if not (u and u.getPlayerName and u:getPlayerName()) then return end
    if e.id == E.S_EVENT_BIRTH then dlg[u:getName()] = not u:inAir() and e.subPlace ~= bp.wsBirthPlace_Park and e.subPlace ~= bp.wsBirthPlace_Heliport_Cold or nil
    elseif e.id == E.S_EVENT_TAKEOFF or e.id == E.S_EVENT_RUNWAY_TAKEOFF or e.id == E.S_EVENT_ENGINE_SHUTDOWN and not u:inAir() then dlg[u:getName()] = nil end   -- Engine off in the air: DCS approach dialog stays open
  end })
  local function menuIndex(line)
    local unit, kind, target, fl = line:match("^MENU;([^;]+);(%a+);([^;]+);?(.*)$")
    local u = unit and Unit.getByName(unit)
    if not u then return end
    local me, side, list, want = u:getPoint(), u:getCoalition(), {}, nil
    local function d(o) local p = o:getPoint() return (p.x - me.x) ^ 2 + (p.z - me.z) ^ 2 end
    local function no(why) evt(string.format("M;%s;%s;0;%s", clean(unit), kind, clean(why))) end
    local f
    if dlg[unit] then return no("DCS-ATC-Dialog offen (Heißstart oder schon gemeldet)") end   -- also tanker: F5 showed the dialog entries
    if kind ~= "tanker" then
      for _, x in ipairs(fields) do if tostring(x.id) == target then f = x end end
      if not f then return no("Platz " .. target .. " nicht in der Mission") end
      local sun = sunAt(f.p)
      if sun >= 6 and not fl:find("w") then return no(string.format("Tag (Sonne %.0f Grad, gute Sicht)", sun)) end
      if not easy and fl:find("u") then return no("Easy Communication aus und Platzfrequenz nicht gerastet") end
      for _, s in ipairs({ coalition.side.NEUTRAL, side }) do
        for _, a in ipairs(coalition.getServiceProviders(s, coalition.service.ATC) or {}) do list[#list + 1] = a end
      end
      local function key(a) local at = a:getDesc().attributes or {} return (at.Airfields or at["Arresting Gear"]) and d(a) or d(a) + 1e10 end
      table.sort(list, function(l, r) return key(l) < key(r) end)
      want = function(a) return a:getID() == f.id end
    else
      list = coalition.getServiceProviders(side, coalition.service.TANKER) or {}
      table.sort(list, function(l, r) return d(l) < d(r) end)
      want = function(t) local g = t.getGroup and t:getGroup() return g and g:getName() == target end
    end
    for i, o in ipairs(list) do
      if want(o) then
        if i > 10 then return no(string.format("Nr. %d von %d, DCS listet nur 10", i, #list)) end
        if f then
          if not fl:find("s") then   -- s: airfield stays silent (no audible DCS answer)
            pcall(f.ab.setRadioSilentMode, f.ab, false)
            timer.scheduleFunction(function() pcall(f.ab.setRadioSilentMode, f.ab, true) end, nil, timer.getTime() + 15)
          end
          dlg[unit] = true   -- the answer opens the DCS dialog (until takeoff or shutdown)
        elseif not fl:find("s") then   -- R304 tanker: s = stays silent (our controller speaks), without s audible for 15 s like the airfields
          local g = o:getGroup()
          pcall(function() g:getController():setOption(SILENCE, false) end)
          timer.scheduleFunction(function() silence(g) end, nil, timer.getTime() + 15)
        end
        return evt(string.format("M;%s;%s;%d;%d", clean(unit), kind, i, #list))
      end
    end
    no((f and "Platz " or "Tanker ") .. target .. " nicht in der DCS-Liste (Koalition)")
  end

  local function command(line)
    if line:match("^MENU;") then pcall(menuIndex, line) return end
    local wu, wt = line:match("^WHEEL;([^;]+);(.*)$")   -- VR: radio wheel as text to the player, empty = wheel closed
    if wu then
      local u = Unit.getByName(wu)
      if u then trigger.action.outTextForUnit(u:getID(), wt, wt == "" and 1 or 600, true) end   -- clearView: replaces the previous wheel text
      return
    end
    if line:match("^CV") then carrierCmd(line) return end
    local dv = line:match("^DIVERT;([^;]+)")
    if dv then   -- N40: approach to captured airfield -> new airfield, there anew in the sequence
      local best = carrierCmd(line)
      if best and flights[dv] then flights[dv].field = best; setFlag(dv, "arr") end
      return
    end
    local og, ox, oz = line:match("^ORBIT;([^;]+);([-%d.]+);([-%d.]+)$")
    if og then   -- LD9: AWACS evades a threat – 10 min circle at the new point at its altitude, then on as planned
      local g = Group.getByName(og)
      local u = g and g:getUnit(1)
      if u then
        g:getController():pushTask({ id = "ControlledTask", params = { stopCondition = { duration = 600 },
          task = { id = "Orbit", params = { pattern = "Circle", point = { x = tonumber(ox), y = tonumber(oz) }, altitude = u:getPoint().y, speed = 180 } } } })
      end
      return
    end
    local rg, rf = line:match("^RTB;([^;]+);(.+)$")
    if rg then   -- LD7: AI fighter checks out with the AWACS -> approach under DCS-ATC (flag arr, lands at the named airfield)
      local g = Group.getByName(rg)
      for _, f in ipairs(fields) do
        if g and f.name == rf and not flights[rg] then
          flights[rg] = { field = f, kind = "arr" }; setFlag(rg, "arr")
          g:getController():setTask({ id = "Mission", params = { route = { points = {
            { type = "Land", action = "Landing", airdromeId = f.id, x = f.p.x, y = f.p.z, alt = f.p.y, alt_type = "BARO", speed = 150 } } } } })
        end
      end
      return
    end
    -- LD1: assignment by AWACS (app, Abm) -> the AI fighter really attacks the group (AttackGroup laid over the mission task); DISENGAGE
    -- (skip it/reset) takes it away again, only while the target is alive (if it is shot down, DCS has already ended the task itself: no second pop)
    local eg, et = line:match("^ENGAGE;([^;]+);(.+)$")
    local dg = line:match("^DISENGAGE;(.+)$")
    if eg or dg then
      A.engaged = A.engaged or {}
      local function alive(n) local x = n and Group.getByName(n) return x and (not x.isExist or x:isExist()) and x end
      local g = Group.getByName(eg or dg)
      if g then pcall(function()
        if alive(A.engaged[eg or dg]) then g:getController():popTask() end
        A.engaged[eg or dg] = nil
        local t = alive(et)
        if t then g:getController():pushTask({ id = "AttackGroup", params = { groupId = t:getID() } }); A.engaged[eg] = et end
      end) end
      log("Befehl " .. line)
      return
    end
    local c, name, a, b = line:match("^(%u+);([^;]+);?([^;]*);?([^;]*)")
    local g = name and flights[name] and Group.getByName(name)
    if not g then return end
    local st = stateOf(name)
    if c == "START" and st == "dep" then
      cmd(g, "Start")
      setFlag(name, "dep-go")
    elseif c == "WAIT" and st == "arr" then
      local u = g:getUnit(1)
      local p = u and u:getPoint()
      if not p then return end
      A.holdPt = A.holdPt or {}
      A.holdPt[name] = { x = p.x, z = p.z }
      g:getController():pushTask({ id = "Orbit", params = { pattern = "Circle", point = { x = p.x, y = p.z },
                                                           altitude = (tonumber(b) or 6000) * 0.3048, speed = 118 } })   -- 230 kt (Tower.HoldKt)
      setFlag(name, "arr-hold")
      timer.scheduleFunction(function()   -- continue after the announced time at the latest
        if stateOf(name) == "arr-hold" and Group.getByName(name) then command("RESUME;" .. name) end
      end, nil, timer.getTime() + (tonumber(a) or 300))
    elseif c == "STACK" and st == "arr-hold" and A.holdPt and A.holdPt[name] then   -- Holding: new altitude at the same point
      local hp = A.holdPt[name]
      g:getController():popTask()
      g:getController():pushTask({ id = "Orbit", params = { pattern = "Circle", point = { x = hp.x, y = hp.z },
                                                           altitude = (tonumber(a) or 6000) * 0.3048, speed = 118 } })   -- 230 kt (Tower.HoldKt)
    elseif c == "RESUME" and st == "arr-hold" then   -- continue to the landing point: DCS does approach and runway itself (own waypoints before it -> DCS circles again)
      g:getController():popTask()
      setFlag(name, "arr")
    end
    log("Befehl " .. line)
  end

  local stateNext = 0
  timer.scheduleFunction(function(_, t)
    if t >= stateNext then
      stateNext = t + (cvNear and 0.25 or 0.5)
      local ok, err = pcall(state)
      if not ok then env.error("DCS-ATC state: " .. tostring(err)) end
    end
    local files = {}
    for f in lfs.dir(dir) do
      local ext = f:sub(-4)
      if ext == ".out" or ext == ".cmd" then files[#files + 1] = f end
    end
    table.sort(files)
    for _, f in ipairs(files) do
      local h = io.open(dir .. "\\" .. f, "r")
      if h then
        local s = h:read("*a")
        h:close()
        if f:sub(-4) == ".cmd" then
          pcall(command, s)
        else
          local gid, sec, text = s:match("^(%d+)|(%d+)|(.*)$")   -- "gid|sek|text"; old: "gid|text" (without display duration)
          if not gid then gid, text = s:match("^(%d+)|(.*)$") end
          gid = tonumber(gid) or 0
          text = text or s
          local dur = tonumber(sec) or math.max(20, #text / 8)   -- Display duration from the app (AI radio short), otherwise reading time: at least 20 s, long texts longer
          local side = tonumber(f:match("%-c(%d)%.out$"))   -- "{ticks}-0-c{side}.out": to all of that coalition only (R356)
          if gid ~= 0 then trigger.action.outTextForGroup(gid, text, dur)
          elseif side then trigger.action.outTextForCoalition(side, text, dur)
          else trigger.action.outText(text, dur) end
        end
      end
      os.remove(dir .. "\\" .. f)
    end
    return t + 0.25
  end, nil, timer.getTime() + 1)

  log("bereit (" .. #fields .. " Plätze)")
end

timer.scheduleFunction(function()
  local ok, err = pcall(start)
  if not ok then env.error("DCS-ATC: " .. tostring(err)) end
end, nil, timer.getTime() + 1)
