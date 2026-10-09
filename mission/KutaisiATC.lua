--[[
  Kutaisi ATC – MOOSE FLIGHTCONTROL with SRS speech output (text-to-speech)

  Prerequisites:
   1) SRS (SimpleRadio Standalone) installed, server running, client connected.
   2) In <DCS-Installation>\Scripts\MissionScripting.lua comment out the lines
         sanitizeModule('os')
         sanitizeModule('io')
         sanitizeModule('lfs')
      with "--".
   3) SRS path: default is "C:\Program Files\DCS-SimpleRadio-Standalone\ExternalAudio".
      Different path -> set SRS_PATH below OR create Saved Games\DCS\Config\Moose_MSRS.lua.

  If (2) is missing, the mission automatically loads the text tower (KutaisiTower.lua) as a replacement.
]]

local SRS_PATH  = nil      -- nil = default path or Moose_MSRS.lua
local SRS_PORT  = nil      -- nil = 5002 or Moose_MSRS.lua
local TWR_FREQ  = 265.0    -- MHz AM (deliberately not 263.0, so the DCS default ATC does not cut in)

-- Own voice ATC (DCS-ATC app) instead of MOOSE FLIGHTCONTROL.
-- false = MOOSE tower again.
local EXTERNAL_ATC = true

-- runway data into dcs.log (comparison with the DCS-ATC app)
pcall(function()
  for _, r in ipairs(Airbase.getByName("Kutaisi"):getRunways() or {}) do
    env.info(string.format("KutaisiATC: Runway %s course %.4f x=%.0f z=%.0f length %.0f",
      tostring(r.Name), r.course, r.position.x, r.position.z, r.length))
  end
end)

if EXTERNAL_ATC then
  if not (io and os and lfs) then
    -- MissionScripting.lua blocks os/io/lfs (DCS update) -> text tower as a replacement
    env.info("KutaisiATC: os/io/lfs gesperrt -> Text-Tower. DCS ATC starten (passt MissionScripting.lua an), Mission neu laden.")
    return
  end
  ATC_USE_SRS = true   -- prevents the text tower (KutaisiTower.lua)
  env.info("KutaisiATC: externes ATC (DCS-ATC-App) aktiv, FLIGHTCONTROL nicht gestartet.")

  -- Data exchange with the DCS-ATC app via %TMP%:
  --   DcsAtc-Airfields.txt   all airfields with runways (once)
  --   DcsAtc-State.txt       players (P), AI (T), weather per airfield (W), clouds/visibility (G) – every 0.5 s
  --   DcsAtc-Menu\*.txt      F10 requests "unit|text"          -> App
  --   DcsAtc-Menu\*.out      Tower texts "gid|sec|text" (0 = all)  <- App (old: "gid|text")
  --   DcsAtc-Menu\*.cmd      AI commands START/WAIT/RESUME       <- App
  DCSATC = { ai = {} }   -- ai[groupName] = "dep" (waits for clearance) | "dep-go" | "arr" | "arr-hold" (KutaisiTraffic.lua)
  local TMP = os.getenv("TMP")
  local dir = TMP .. "\\DcsAtc-Menu"
  lfs.mkdir(dir)

  local function writeFile(file, text)
    local h = io.open(file .. ".tmp", "w")
    if not h then return end
    h:write(text)
    h:close()
    os.remove(file)
    os.rename(file .. ".tmp", file)
  end
  local function clean(s) return (tostring(s or ""):gsub("[;|\r\n]", " ")) end

  local airdromes = {}
  for _, ab in ipairs(world.getAirbases() or {}) do
    if ab:getDesc().category == Airbase.Category.AIRDROME then airdromes[#airdromes + 1] = ab end
    pcall(ab.setRadioSilentMode, ab, true)   -- DCS's own ATC muted, controllers come from the app
  end
  -- AI aircraft without a player in the group: no DCS radio calls (tanker/AWACS/traffic is voiced by the app)
  timer.scheduleFunction(function()
    for side = 0, 2 do
      for _, g in ipairs(coalition.getGroups(side, Group.Category.AIRPLANE) or {}) do
        local human = false
        for _, u in ipairs(g:getUnits() or {}) do if u:getPlayerName() then human = true end end
        if not human then pcall(function() g:getController():setOption(AI.Option.Air.id.SILENCE, true) end) end
      end
    end
    return timer.getTime() + 30
  end, nil, timer.getTime() + 5)
  pcall(function()
    local l = {}
    for _, ab in ipairs(airdromes) do
      local p = ab:getPoint()
      l[#l + 1] = string.format("A;%s;%.1f;%.1f;%.1f", ab:getName(), p.x, p.z, p.y)
      for _, r in ipairs(ab:getRunways() or {}) do
        l[#l + 1] = string.format("R;%s;%.5f;%.1f;%.1f;%.0f", tostring(r.Name), r.course, r.position.x, r.position.z, r.length)
      end
    end
    writeFile(TMP .. "\\DcsAtc-Airfields.txt", table.concat(l, "\n") .. "\n")
  end)

  -- F10 menu per player group (also multiplayer/hot spawn): controllers as in BMS, same entries as the app's selection wheel
  local MENU = {
    { "Ground", "Ground: ", {
      { "Request startup", "request startup" }, { "Request taxi", "request taxi" },
      { "Taxi, departure north", "request taxi, departure north" }, { "Taxi, departure south", "request taxi, departure south" },
      { "Taxi, departure east", "request taxi, departure east" }, { "Taxi, departure west", "request taxi, departure west" },
      { "Runway vacated, taxi to parking", "runway vacated, request taxi to parking" } } },
    { "Tower", "Tower: ", {
      { "Ready for departure", "ready for departure" }, { "Ready, closed pattern (Platzrunden)", "ready for departure, closed pattern" },
      { "Initial", "initial" }, { "Overhead", "overhead" }, { "Downwind", "downwind" },
      { "Final, full stop", "final, full stop" }, { "Final, touch and go", "final, touch and go" },
      { "Final, low approach", "final, low approach" }, { "Going around", "going around" } } },
    { "Approach", "Approach: ", {
      { "Airborne (nach Start)", "airborne, climbing" }, { "Inbound for landing", "inbound for landing" }, { "Inbound, pattern work", "inbound for pattern work, touch and go" },
      { "Request ILS / straight in", "request straight in" }, { "CRP reached", "crp" } } },
    { "Notfall", "", {
      { "MAYDAY", "mayday mayday mayday, request immediate landing" }, { "PAN PAN", "pan pan, pan pan, pan pan, request priority landing" },
      { "Notfall beenden", "cancel emergency" } } },
    { "Allgemein", "", {
      { "Radio check", "radio check" }, { "Say again", "say again" }, { "QNH / weather", "request weather" },
      { "ATIS hören", "atis" } } },
  }
  local n = 0
  local function send(arg)
    local a = lfs.attributes(dir .. "\\alive")
    if not a or os.time() - a.modification > 5 then
      trigger.action.outTextForGroup(arg.gid, "DCS-ATC-App läuft nicht -> Desktop-Verknüpfung 'DCS ATC' starten, dann erneut senden.", 10)
      return
    end
    local unit = arg.unit
    if not unit then   -- group with only one player slot: the group's player
      local g = Group.getByName(arg.gname)
      for _, u in ipairs(g and g:getUnits() or {}) do
        if u:getPlayerName() then unit = u:getName() break end
      end
    end
    if not unit then return end
    n = n + 1
    local f = string.format("%s\\%010d-%04d", dir, os.time(), n)
    local h = io.open(f .. ".tmp", "w")
    if not h then return end
    h:write(clean(unit) .. "|" .. arg.text)
    h:close()
    os.rename(f .. ".tmp", f .. ".txt")
  end

  -- player slots per group from the editor: several -> own submenu per player (DCS only knows group menus)
  local slots = {}
  for _, coa in pairs(env.mission.coalition or {}) do
    for _, ctry in pairs(coa.country or {}) do
      for _, cat in ipairs({ "plane", "helicopter" }) do
        for _, g in pairs(ctry[cat] and ctry[cat].group or {}) do
          for _, u in pairs(g.units or {}) do
            if u.skill == "Client" or u.skill == "Player" then slots[g.name] = (slots[g.name] or 0) + 1 end
          end
        end
      end
    end
  end
  local built, roots = {}, {}
  local function addMenu(gid, parent, gname, unit)
    for _, m in ipairs(MENU) do
      local sub = missionCommands.addSubMenuForGroup(gid, m[1], parent)
      for _, c in ipairs(m[3]) do
        missionCommands.addCommandForGroup(gid, c[1], sub, send, { gid = gid, gname = gname, unit = unit, text = m[2] .. c[2] })
      end
    end
  end
  local function buildMenu(g, u)
    local gid, gname = g:getID(), g:getName()
    if (slots[gname] or 1) <= 1 then
      if not built[gid] then
        built[gid] = true
        addMenu(gid, missionCommands.addSubMenuForGroup(gid, "ATC"), gname, nil)
      end
      return
    end
    local key = u:getName()
    if built[key] then return end
    built[key] = true
    roots[gid] = roots[gid] or missionCommands.addSubMenuForGroup(gid, "ATC")
    local cs = (tostring(u:getCallsign() or key):gsub("^(%a+)(%d)(%d)$", "%1 %2-%3"))
    addMenu(gid, missionCommands.addSubMenuForGroup(gid, cs, roots[gid]), gname, key)
  end

  -- Weather: clouds/visibility (fog live), wind/temperature/pressure per airfield, plus range, bullseye, tanker TACAN (every 10 s)
  local wx = env.mission.weather or {}
  local cl = wx.clouds or {}
  local cover = (cl.preset and 1) or (((cl.density or 0) > 0) and 1 or 0)
  local vis = (wx.enable_fog and wx.fog and (wx.fog.thickness or 0) > 0 and wx.fog.visibility) or (wx.visibility and wx.visibility.distance) or 80000
  local G = string.format("G;%d;%.0f;%.0f", cover, cl.base or 0, vis)
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
      for _, g in pairs(ctry.plane and ctry.plane.group or {}) do scan(g.route or {}, g.name) end
    end
  end
  local W, wTime = {}, -100
  local function weather()
    local v = (wx.visibility and wx.visibility.distance) or 80000
    if not pcall(function()
      local fv = world.weather.getFogVisibilityDistance()   -- 0 = no fog (thickness alone can be > 0, e.g. ATMOS-X)
      if world.weather.getFogThickness() > 0 and fv > 0 then v = math.min(v, fv) end
    end) then v = vis end
    G = string.format("G;%d;%.0f;%.0f", cover, cl.base or 0, v)
    W = {}
    for _, ab in ipairs(airdromes) do
      local p = ab:getPoint()
      local w = atmosphere.getWind({ x = p.x, y = p.y + 10, z = p.z })
      local t, pr = atmosphere.getTemperatureAndPressure({ x = p.x, y = p.y + 2, z = p.z })
      W[#W + 1] = string.format("W;%s;%.2f;%.2f;%.1f;%.0f", ab:getName(), w.x, w.z, t - 273.15, pr)
    end
    local z = TR and TR.zones and TR.zones.range
    if z then W[#W + 1] = string.format("Z;%.0f;%.0f;%.0f", z.x, z.z, z.r) end
    local b = env.mission.coalition and env.mission.coalition.blue and env.mission.coalition.blue.bullseye
    if b then W[#W + 1] = string.format("B;%.0f;%.0f", b.x, b.y) end
    for _, k in ipairs(beacons) do W[#W + 1] = k end
  end

  -- Range Alpha: measure impacts of players' bombs/missiles (distance + clock position to the nearest target, 12 = approach direction),
  -- report gun hits and kills -> DcsAtc-Menu\*.evt
  local function evt(line)
    n = n + 1
    local f = string.format("%s\\%010d-e%04d", dir, os.time(), n)
    local h = io.open(f .. ".tmp", "w")
    if not h then return end
    h:write(line)
    h:close()
    os.rename(f .. ".tmp", f .. ".evt")
  end
  DCSATC.evt = evt   -- also for KutaisiTraffic.lua (ground crew)
  local function inRange(p)
    local z = TR and TR.zones and TR.zones.range
    return z and (p.x - z.x) ^ 2 + (p.z - z.z) ^ 2 < (z.r + 1500) ^ 2
  end
  local function nearestTarget(p)
    local best, bd = nil, 1e9
    for _, g in ipairs(coalition.getGroups(1, Group.Category.GROUND) or {}) do
      for _, u in ipairs(g:getUnits() or {}) do
        local q = u:getPoint()
        local d = math.sqrt((p.x - q.x) ^ 2 + (p.z - q.z) ^ 2)
        if d < bd then best, bd = u, d end
      end
    end
    return best, bd
  end
  local function track(w, unit, hdg)
    local last, vel, wname = nil, nil, clean(w:getTypeName())
    local t0 = timer.getTime()   -- Drop: flight time in the R event, the app checks the clearance at the time of the drop (R58)
    timer.scheduleFunction(function(_, t)
      if w:isExist() then last, vel = w:getPoint(), w:getVelocity(); return t + 0.05 end
      if not last then return end
      local ip = last
      local sp = math.sqrt(vel.x ^ 2 + vel.y ^ 2 + vel.z ^ 2)
      if sp > 1 then ip = land.getIP(last, { x = vel.x / sp, y = vel.y / sp, z = vel.z / sp }, sp * 0.1 + 30) or last end
      if not inRange(ip) then return end
      local tgt, d = nearestTarget(ip)
      if not tgt or d > 500 then evt(string.format("R;%s;%s;-1;0;;%.1f", unit, wname, t - t0)) return end
      local q = tgt:getPoint()
      local rel = (math.deg(math.atan2(ip.z - q.z, ip.x - q.x)) - hdg) % 360
      local clock = math.floor(rel / 30 + 0.5) % 12
      evt(string.format("R;%s;%s;%.0f;%d;%s;%.1f", unit, wname, d, clock == 0 and 12 or clock, clean(tgt:getTypeName()), t - t0))
    end, nil, timer.getTime() + 0.05)
  end
  world.addEventHandler({ onEvent = function(_, e)
    local u = e.initiator
    if not u or not u.getPlayerName or not pcall(u.getPlayerName, u) or not u:getPlayerName() then return end
    if e.id == world.event.S_EVENT_SHOT and e.weapon then
      local c = e.weapon:getDesc().category
      local mc = e.weapon:getDesc().missileCategory
      if c == Weapon.Category.BOMB or c == Weapon.Category.ROCKET or (c == Weapon.Category.MISSILE and mc == Weapon.MissileCategory.OTHER) then
        local pos = u:getPosition()
        track(e.weapon, clean(u:getName()), math.deg(math.atan2(pos.x.z, pos.x.x)))
      end
    elseif e.id == world.event.S_EVENT_HIT and e.target and e.target.getPoint and inRange(e.target:getPoint())
        and e.weapon and e.weapon:getDesc().category == Weapon.Category.SHELL then
      evt(string.format("H;%s;%s", clean(u:getName()), clean(e.target:getTypeName())))
    elseif e.id == world.event.S_EVENT_KILL and e.target and e.target.getPoint and inRange(e.target:getPoint()) then
      evt(string.format("K;%s;%s", clean(u:getName()), clean(e.target:getTypeName())))
    end
  end })

  local function state()
    if timer.getTime() - wTime > 10 then wTime = timer.getTime(); weather() end
    local l = { G }
    for _, w in ipairs(W) do l[#l + 1] = w end
    for side = 0, 2 do
      for _, cat in ipairs({ Group.Category.AIRPLANE, Group.Category.HELICOPTER }) do
        for _, g in ipairs(coalition.getGroups(side, cat) or {}) do
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
                local w = atmosphere.getWind(p)
                local _, pr = atmosphere.getTemperatureAndPressure(p)
                l[#l + 1] = string.format("P;%s;%d;%s;%s;%s;%.1f;%.1f;%.1f;%.1f;%.1f;%.4f;%.2f;%.2f;%.0f;%.2f;%d;%s;%d",
                  clean(u:getName()), g:getID(), clean(u:getCallsign()), clean(player), clean(u:getTypeName()),
                  p.x, p.z, p.y, p.y - land.getHeight({ x = p.x, y = p.z }), spd, hdg, w.x, w.z, pr, v.y, air,
                  tostring(u.id_ or u:getID()), u:getCoalition())   -- id_ = runtime ID like LoGetPlayerPlaneId (SRS unitId)
              else
                l[#l + 1] = string.format("T;%s;%s;%s;%.1f;%.1f;%.1f;%.4f;%.1f;%d;%s;%d;%s",
                  clean(u:getName()), clean(g:getName()), clean(u:getTypeName()), p.x, p.z, p.y, hdg, spd, air,
                  DCSATC.ai[g:getName()] or "", u:getCoalition(), clean(u:getCallsign()))
              end
            end
          end
        end
      end
    end
    writeFile(TMP .. "\\DcsAtc-State.txt", table.concat(l, "\n") .. "\n")
  end

  -- AI commands of the app (KutaisiTraffic.lua: TRF.flights[groupName] = FLIGHTGROUP)
  local function command(line)
    local c, name, a, b = line:match("^(%u+);([^;]+);?([^;]*);?([^;]*)")
    local fg = TRF and TRF.flights and TRF.flights[name]
    if not fg or not fg:IsAlive() then return end
    if c == "START" and DCSATC.ai[name] == "dep" then
      fg:StartUncontrolled()
      DCSATC.ai[name] = "dep-go"
    elseif c == "WAIT" and DCSATC.ai[name] == "arr" then
      fg:Wait(tonumber(a) or 300, tonumber(b) or 6000, 250)
      DCSATC.ai[name] = "arr-hold"
    elseif c == "RESUME" and DCSATC.ai[name] == "arr-hold" then
      fg.flaghold:Set(1)
      DCSATC.ai[name] = "arr"
    end
    env.info("KutaisiATC: KI " .. line)
  end

  local stateNext = 0
  timer.scheduleFunction(function(_, t)
    if t >= stateNext then
      stateNext = t + 0.5
      local ok, err = pcall(state)
      if not ok then env.error("KutaisiATC state: " .. tostring(err)) end
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
          local dur = tonumber(sec) or math.max(20, #text / 8)   -- Display duration from the app (AI radio short), otherwise reading time: at least 20 s, longer for long texts
          if gid == 0 then trigger.action.outText(text, dur) else trigger.action.outTextForGroup(gid, text, dur) end
        end
      end
      os.remove(dir .. "\\" .. f)
    end
    return t + 0.25
  end, nil, timer.getTime() + 1)
  return
end


ATC_USE_SRS = (os ~= nil and io ~= nil and lfs ~= nil)

if not ATC_USE_SRS then
  env.info("KutaisiATC: os/io/lfs gesperrt -> kein SRS möglich, Text-Tower wird verwendet.")
  return
end

-- Speech output without a terminal window: MOOSE otherwise starts on every announcement via os.execute
-- a console that steals focus from DCS. Instead write the command to a queue;
-- Start-DCS-Funk.ps1 (Saved Games\DCS\Scripts) runs it invisibly.
-- If the starter is not running (no sign of life < 5 s), normal MOOSE behavior.
local QDIR = os.getenv("TMP") .. "\\MSRS-Queue"
local origExec = MSRS._ExecCommand
function MSRS:_ExecCommand(command)
  local hb = lfs.attributes(QDIR .. "\\alive")
  if not hb or os.time() - hb.modification > 5 or command:find("CommandNotFound") then
    return origExec(self, command)
  end
  local f = QDIR .. "\\" .. MSRS.uuid()
  local h = io.open(f .. ".tmp", "w")
  h:write(command)
  h:close()
  os.rename(f .. ".tmp", f .. ".txt")
  return 0
end

FC_KUTAISI =FLIGHTCONTROL:New("Kutaisi", TWR_FREQ, radio.modulation.AM, SRS_PATH, SRS_PORT)

-- Voices
FC_KUTAISI:SetSRSTower("male", "en-US", nil, 1.0, "Kutaisi Tower")
FC_KUTAISI:SetSRSPilot("male", "en-GB", nil, 1.0)

-- Traffic rules
FC_KUTAISI:SetLimitLanding(2, 0)          -- max. 2 flights with landing clearance, no parallel takeoffs
FC_KUTAISI:SetLandingInterval(90)         -- 90 s spacing between landing clearances
FC_KUTAISI:SetLimitTaxi(2, false, 0)      -- AI: max. 2 flights taxi to the runway
FC_KUTAISI:SetSpeedLimitTaxi(30)          -- Taxi speed max. 30 kts, otherwise announcement

-- Parking guard: soldier in front of the aircraft, disappears only with taxi clearance
FC_KUTAISI:SetParkingGuard("FC_PARKING_GUARD")

-- Holding: standard pattern parallel to the active runway, marked on the F10 map
FC_KUTAISI:SetMarkHoldingPattern(true)

-- Couple ATIS (information letter, runway)
if KutaisiATIS then FC_KUTAISI:SetATIS(KutaisiATIS) end

-- Only transmit when players are at the airfield (saves SRS calls)
FC_KUTAISI:SetTransmitOnlyWithPlayers(true)

FC_KUTAISI:Start()

trigger.action.outText(string.format(
  "Kutaisi ATC aktiv (SRS): Tower %.3f AM, ATIS 131.500 AM.\nF10 -> ATC-Menü der Mission. SRS-Client muss verbunden sein.", TWR_FREQ), 20)
