--[[
  Kutaisi Tower  –  text-based air traffic control (without SRS)
  Requires MOOSE (loaded beforehand) for runway data.

  F10 -> "Kutaisi Tower": Startup, Taxi, Ready for departure, Report approach, Final, ATIS text.
  The tower keeps a takeoff list and a landing list, checks whether the runway is free
  and issues clearances automatically as soon as it fits. Unauthorized takeoffs/landings
  are logged (with a wink).
]]

if ATC_USE_SRS then
  env.info("KutaisiTower: SRS-FLIGHTCONTROL aktiv, Text-Tower nicht geladen.")
  return
end

TWR = {}
TWR.airbaseName = "Kutaisi"
TWR.towerFreq   = "134.000 / 263.000"
TWR.atisFreq    = "131.500"
TWR.awacs       = "Overlord 251.0"
TWR.nav = "TACAN 44X (KTS)  |  VOR KTS 113.60  |  ILS siehe Kneeboard/F10-Karte"
TWR.flights = {}        -- [unitName] = flight
TWR.depQueue = {}       -- unitNames
TWR.arrQueue = {}       -- unitNames
TWR.menus = {}          -- [groupId] = menu root path
TWR.log = {}

local AB = AIRBASE:FindByName(TWR.airbaseName)
local DCSAB = Airbase.getByName(TWR.airbaseName)
local abPos = DCSAB:getPoint()
local abElev = land.getHeight({ x = abPos.x, y = abPos.z })
local magvar = UTILS.GetMagneticDeclination() or 6

---------------------------------------------------------------------------
-- Helpers
---------------------------------------------------------------------------
local function dist2d(a, b)
  local dx, dz = a.x - b.x, a.z - b.z
  return math.sqrt(dx * dx + dz * dz)
end

local function callsignOf(unit)
  local cs = unit.getCallsign and unit:getCallsign() or nil
  if cs and cs ~= "" then
    local name, a, b = cs:match("^(%a+)(%d)(%d)$")
    if name then return name .. " " .. a .. "-" .. b end
    return cs
  end
  return unit:getPlayerName() or unit:getName()
end

local function say(f, text, t)
  trigger.action.outTextForGroup(f.groupId, "KUTAISI TOWER: " .. f.callsign .. ", " .. text, t or 15)
  table.insert(TWR.log, string.format("%05d %s: %s", math.floor(timer.getTime()), f.callsign, text))
end

local function pilot(f, text)
  trigger.action.outTextForGroup(f.groupId, f.callsign .. ": " .. text, 6)
end

local function removeFrom(list, name)
  for i = #list, 1, -1 do
    if list[i] == name then table.remove(list, i) end
  end
end

local function indexOf(list, name)
  for i, n in ipairs(list) do if n == name then return i end end
  return nil
end

local function getUnit(f)
  local u = Unit.getByName(f.unitName)
  if u and u:isExist() then return u end
  return nil
end

-- Weather
function TWR.weather()
  local p = { x = abPos.x, y = abElev + 10, z = abPos.z }
  local w = atmosphere.getWind(p)
  local spd = math.sqrt(w.x * w.x + w.z * w.z)
  local to = math.deg(math.atan2(w.z, w.x))
  local from = (to + 180 - magvar) % 360
  local kts = spd * 1.94384
  local tK, pPa = atmosphere.getTemperatureAndPressure({ x = abPos.x, y = abElev, z = abPos.z })
  local qfe = pPa / 100
  local qnh = qfe + abElev / 8.3
  local windTxt
  if kts < 2 then
    windTxt = "wind calm"
  else
    windTxt = string.format("wind %03d at %d", math.floor(from / 10 + 0.5) * 10 % 360, math.floor(kts + 0.5))
  end
  return {
    windTxt = windTxt,
    qnh = math.floor(qnh + 0.5),
    qnhInHg = qnh * 0.02953,
    tempC = math.floor(tK - 273.15 + 0.5),
  }
end

function TWR.activeRunway()
  local rw = AB:GetRunwayIntoWind()
  local name = AB:GetRunwayName(rw)
  return rw, name
end

-- All aircraft around the airfield
local function aircraftNear(radius)
  local out = {}
  local vol = { id = world.VolumeType.SPHERE, params = { point = abPos, radius = radius } }
  world.searchObjects(Object.Category.UNIT, vol, function(obj)
    local ok, desc = pcall(obj.getDesc, obj)
    if ok and desc and (desc.category == Unit.Category.AIRPLANE or desc.category == Unit.Category.HELICOPTER) then
      table.insert(out, obj)
    end
    return true
  end)
  return out
end

local function onRunway(unit)
  local p = unit:getPoint()
  for _, rw in pairs(AB:GetRunways() or {}) do
    if rw.zone and rw.zone:IsVec2InZone({ x = p.x, y = p.z }) then return true end
  end
  return false
end

-- Runway occupied? (ignore = unitName currently asking)
function TWR.runwayStatus(ignore)
  for _, u in ipairs(aircraftNear(5000)) do
    if u:getName() ~= ignore and onRunway(u) then
      return false, "traffic on the runway"
    end
  end
  -- Takeoff clearance given, but not yet airborne
  for name, f in pairs(TWR.flights) do
    if name ~= ignore and f.state == "cleared_takeoff" then
      return false, "departing traffic"
    end
  end
  return true
end

-- Approaching traffic on short final?
function TWR.trafficOnShortFinal(ignore, rangeM)
  local rw = TWR.activeRunway()
  local thr = rw.position:GetVec3()
  for _, u in ipairs(aircraftNear(15000)) do
    if u:getName() ~= ignore and u:inAir() then
      local p = u:getPoint()
      local agl = p.y - land.getHeight({ x = p.x, y = p.z })
      if dist2d(p, thr) < (rangeM or 8000) and agl < 500 then
        local f = TWR.flights[u:getName()]
        if not f or f.state == "cleared_land" or f.state == "final" then
          return true
        end
      end
    end
  end
  return false
end

---------------------------------------------------------------------------
-- Radio requests
---------------------------------------------------------------------------
function TWR.requestStartup(f)
  pilot(f, "Kutaisi Ground, request startup.")
  local _, rwName = TWR.activeRunway()
  local wx = TWR.weather()
  f.state = "startup"
  say(f, string.format("startup approved. Runway %s in use, %s, QNH %d (%.2f). ATIS on %s.",
    rwName, wx.windTxt, wx.qnh, wx.qnhInHg, TWR.atisFreq))
end

function TWR.requestTaxi(f)
  local u = getUnit(f)
  if u and u:inAir() then say(f, "you are airborne, say intentions.") return end
  pilot(f, "Kutaisi Ground, request taxi.")
  local _, rwName = TWR.activeRunway()
  local wx = TWR.weather()
  f.state = "taxi"
  local extra = ""
  if TWR.trafficOnShortFinal(f.unitName, 15000) then extra = " Caution, inbound traffic." end
  say(f, string.format("taxi to holding point runway %s, hold short. QNH %d.%s", rwName, wx.qnh, extra))
end

function TWR.readyDeparture(f)
  local u = getUnit(f)
  if not u then return end
  if u:inAir() then say(f, "you are airborne, say intentions.") return end
  pilot(f, "Kutaisi Tower, holding short, ready for departure.")
  if not indexOf(TWR.depQueue, f.unitName) then table.insert(TWR.depQueue, f.unitName) end
  f.state = "holding"
  local pos = indexOf(TWR.depQueue, f.unitName)
  if pos > 1 then
    local _, rwName = TWR.activeRunway()
    say(f, string.format("hold short runway %s, number %d for departure.", rwName, pos))
  else
    TWR.tryClearTakeoff(f, true)
  end
end

function TWR.tryClearTakeoff(f, verbose)
  local _, rwName = TWR.activeRunway()
  local free, why = TWR.runwayStatus(f.unitName)
  if free and TWR.trafficOnShortFinal(f.unitName, 6000) then
    free, why = false, "traffic on short final"
  end
  if free then
    local wx = TWR.weather()
    f.state = "cleared_takeoff"
    f.clearedAt = timer.getTime()
    say(f, string.format("runway %s, %s, cleared for takeoff.", rwName, wx.windTxt), 20)
    return true
  elseif verbose then
    say(f, string.format("hold short runway %s, %s. Expect clearance shortly.", rwName, why))
  end
  return false
end

function TWR.inbound(f)
  local u = getUnit(f)
  if not u then return end
  if not u:inAir() then say(f, "you are on the ground. Request taxi first.") return end
  local d = dist2d(u:getPoint(), abPos) / 1852
  pilot(f, string.format("Kutaisi Tower, %d miles out, inbound for landing.", math.floor(d + 0.5)))
  if not indexOf(TWR.arrQueue, f.unitName) then table.insert(TWR.arrQueue, f.unitName) end
  f.state = "inbound"
  local _, rwName = TWR.activeRunway()
  local wx = TWR.weather()
  local pos = indexOf(TWR.arrQueue, f.unitName)
  local seq = pos == 1 and "number 1" or string.format("number %d, follow traffic ahead", pos)
  say(f, string.format("runway %s, %s, QNH %d (%.2f). You are %s. Report 5 mile final.",
    rwName, wx.windTxt, wx.qnh, wx.qnhInHg, seq), 20)
end

function TWR.final(f)
  local u = getUnit(f)
  if not u or not u:inAir() then return end
  pilot(f, "Kutaisi Tower, 5 mile final.")
  if not indexOf(TWR.arrQueue, f.unitName) then table.insert(TWR.arrQueue, f.unitName) end
  f.state = "final"
  if not TWR.tryClearLanding(f, true) then
    -- is automatically checked again
  end
end

function TWR.tryClearLanding(f, verbose)
  local _, rwName = TWR.activeRunway()
  local pos = indexOf(TWR.arrQueue, f.unitName) or 1
  -- only the first of the landing list that is on final gets clearance
  for i = 1, pos - 1 do
    local other = TWR.flights[TWR.arrQueue[i]]
    if other and (other.state == "final" or other.state == "cleared_land") then
      if verbose then say(f, string.format("continue approach, number %d, traffic ahead on final.", pos)) end
      return false
    end
  end
  local free, why = TWR.runwayStatus(f.unitName)
  if free then
    local wx = TWR.weather()
    f.state = "cleared_land"
    say(f, string.format("runway %s, %s, cleared to land.", rwName, wx.windTxt), 20)
    return true
  elseif verbose then
    say(f, string.format("continue approach, %s. Expect late landing clearance.", why))
  end
  return false
end

function TWR.atisText(f)
  local _, rwName = TWR.activeRunway()
  local wx = TWR.weather()
  trigger.action.outTextForGroup(f.groupId, string.format(
    "KUTAISI INFORMATION (Text)\nRunway in use: %s\n%s\nQNH %d hPa / %.2f inHg\nTemperature %d°C\n" ..
    "Tower %s  |  ATIS %s (Sprache)\n%s",
    rwName, wx.windTxt, wx.qnh, wx.qnhInHg, wx.tempC, TWR.towerFreq, TWR.atisFreq, TWR.nav), 25)
end

function TWR.frequencies(f)
  trigger.action.outTextForGroup(f.groupId,
    "FREQUENZEN\n" ..
    "Kutaisi Tower: " .. TWR.towerFreq .. "\n" ..
    "Kutaisi ATIS:  " .. TWR.atisFreq .. " AM (Sprachansage)\n" ..
    "AWACS:         " .. TWR.awacs .. "\n" ..
    "Tanker Shell:  260.0, TACAN 51X (Boom)\n" ..
    "Tanker Texaco: 261.0, TACAN 52X (Korb)\n" ..
    TWR.nav, 25)
end

function TWR.cancel(f)
  removeFrom(TWR.depQueue, f.unitName)
  removeFrom(TWR.arrQueue, f.unitName)
  f.state = "none"
  say(f, "roger, clearances cancelled.")
end

---------------------------------------------------------------------------
-- Menus per player group
---------------------------------------------------------------------------
local function buildMenu(f)
  local gid = f.groupId
  if TWR.menus[gid] then missionCommands.removeItemForGroup(gid, TWR.menus[gid]) end
  local root = missionCommands.addSubMenuForGroup(gid, "Kutaisi Tower")
  TWR.menus[gid] = root
  local function cmd(label, fn)
    missionCommands.addCommandForGroup(gid, label, root, function()
      local fl = TWR.flights[f.unitName]
      if fl then fn(fl) end
    end)
  end
  cmd("1 Anlassen anfragen", TWR.requestStartup)
  cmd("2 Rollen anfragen", TWR.requestTaxi)
  cmd("3 Startbereit (Rollhalt)", TWR.readyDeparture)
  cmd("4 Anflug melden", TWR.inbound)
  cmd("5 Endanflug 5 NM", TWR.final)
  cmd("Wetter / ATIS (Text)", TWR.atisText)
  cmd("Frequenzen & Navigation", TWR.frequencies)
  cmd("Freigaben stornieren", TWR.cancel)
end

local function registerPlayer(unit)
  local grp = unit:getGroup()
  if not grp then return end
  local f = {
    unitName = unit:getName(),
    groupId = grp:getID(),
    callsign = callsignOf(unit),
    state = "none",
  }
  TWR.flights[f.unitName] = f
  buildMenu(f)
end

local function unregister(name)
  local f = TWR.flights[name]
  if not f then return end
  removeFrom(TWR.depQueue, name)
  removeFrom(TWR.arrQueue, name)
  TWR.flights[name] = nil
end

---------------------------------------------------------------------------
-- Events
---------------------------------------------------------------------------
local function isKutaisi(place)
  return place and place.getName and place:getName() == TWR.airbaseName
end

TWR.eh = {}
function TWR.eh:onEvent(e)
  local u = e.initiator
  if not u or not u.getPlayerName then return end
  local ok, pname = pcall(u.getPlayerName, u)
  if not ok or not pname then
    if e.id == world.event.S_EVENT_PLAYER_LEAVE_UNIT and u.getName then unregister(u:getName()) end
    return
  end
  local name = u:getName()

  if e.id == world.event.S_EVENT_BIRTH then
    registerPlayer(u)
    timer.scheduleFunction(function()
      local f = TWR.flights[name]
      if f then
        trigger.action.outTextForGroup(f.groupId,
          "Kutaisi Tower " .. TWR.towerFreq .. " – F10 -> Kutaisi Tower. ATIS (Sprache) auf " .. TWR.atisFreq .. " AM.", 12)
      end
    end, nil, timer.getTime() + 5)
    return
  end

  local f = TWR.flights[name]
  if not f then return end

  if e.id == world.event.S_EVENT_TAKEOFF and isKutaisi(e.place) then
    if f.state ~= "cleared_takeoff" then
      say(f, "you departed without takeoff clearance. That one goes in the log...")
    else
      say(f, "airborne. Contact " .. TWR.awacs .. ". Good hunting.")
    end
    removeFrom(TWR.depQueue, name)
    f.state = "airborne"
  elseif e.id == world.event.S_EVENT_LAND and isKutaisi(e.place) then
    if f.state ~= "cleared_land" then
      say(f, "landed without clearance. The controller is not amused. Vacate the runway.")
    else
      say(f, "welcome back. Vacate runway when able, taxi to parking.")
    end
    removeFrom(TWR.arrQueue, name)
    f.state = "landed"
  elseif e.id == world.event.S_EVENT_CRASH or e.id == world.event.S_EVENT_EJECTION
      or e.id == world.event.S_EVENT_PILOT_DEAD or e.id == world.event.S_EVENT_PLAYER_LEAVE_UNIT then
    unregister(name)
  end
end
world.addEventHandler(TWR.eh)

---------------------------------------------------------------------------
-- Controller: work through lists every 4 s
---------------------------------------------------------------------------
function TWR.tick()
  -- clean up dead entries
  for name, _ in pairs(TWR.flights) do
    if not Unit.getByName(name) then unregister(name) end
  end

  -- Takeoff clearance pending too long (player is not taxiing) -> release the runway again
  for _, f in pairs(TWR.flights) do
    if f.state == "cleared_takeoff" and f.clearedAt and timer.getTime() - f.clearedAt > 240 then
      local u = getUnit(f)
      if u and not onRunway(u) then
        f.state = "holding"
        say(f, "takeoff clearance cancelled, hold short and report ready.")
      end
    end
  end

  -- Landings have priority
  for i, name in ipairs(TWR.arrQueue) do
    local f = TWR.flights[name]
    if f and f.state == "final" then
      local u = getUnit(f)
      if u then
        local rw = TWR.activeRunway()
        local thr = rw.position:GetVec3()
        local p = u:getPoint()
        local agl = p.y - land.getHeight({ x = p.x, y = p.z })
        if not TWR.tryClearLanding(f, false) and dist2d(p, thr) < 1500 and agl < 150 then
          say(f, "GO AROUND, I say again, GO AROUND! Runway not clear. Report final again.", 15)
          f.state = "inbound"
        end
      end
    end
  end

  -- Departures
  local head = TWR.depQueue[1]
  if head then
    local f = TWR.flights[head]
    if f and f.state == "holding" then
      TWR.tryClearTakeoff(f, false)
    end
  end

  return timer.getTime() + 4
end
timer.scheduleFunction(TWR.tick, nil, timer.getTime() + 10)

-- Players already seated (e.g. script reload)
for _, side in ipairs({ coalition.side.BLUE, coalition.side.RED }) do
  for _, u in ipairs(coalition.getPlayers(side) or {}) do registerPlayer(u) end
end

env.info("KutaisiTower: bereit, aktive Bahn " .. select(2, TWR.activeRunway()))
