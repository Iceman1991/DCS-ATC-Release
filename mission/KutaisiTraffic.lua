--[[
  AI air traffic Kutaisi (MOOSE OPS)

  - Departures: AI aircraft appear cold and "uncontrolled" on free parking spots.
    With SRS-FLIGHTCONTROL they start only when the tower gives startup/taxi clearance,
    and line up with you in the departure sequence.
  - Fighter jets fly an exercise (orbit 15-25 min) and return to land.
  - Transports fly to Batumi/Senaki.
  - Arrivals: AI aircraft come from outside, are put into holding stacks and
    landed in sequence. After parking they disappear after 5 minutes.
  - Player parking spots are blocked for AI.

  F10 -> "KI-Verkehr Kutaisi": on/off, density, immediate departure/arrival, status.
]]

TRF = {}
TRF.enabled = true
TRF.density = 2            -- 1 light, 2 normal, 3 heavy
TRF.intervals = { 900, 480, 240 }   -- Seconds between new flights (±30 %)
TRF.maxFlights = { 3, 4, 6 }
TRF.flights = {}           -- [groupName] = FLIGHTGROUP

local KUT = AIRBASE:FindByName("Kutaisi")
local DESTS = { "Batumi", "Senaki-Kolkhi", "Kobuleti" }

-- Templates (in the editor: Late Activation, prefix AI_TPL_)
TRF.fighters  = { "AI_TPL_F16", "AI_TPL_F18", "AI_TPL_A10", "AI_TPL_F15E" }
TRF.transports = { "AI_TPL_C130", "AI_TPL_C17" }

-- Entry/exit points (lat/lon), deliberately away from the A2A arena and SEAD zone
TRF.entry = {
  { 42.16, 43.10, "from the east" },
  { 41.80, 42.85, "from the south-east" },
  { 42.60, 42.10, "from the north-west" },
}
TRF.orbits = {
  { 42.40, 42.95 },
  { 41.95, 42.55 },
  { 42.45, 42.30 },
}

local function coordLL(lat, lon, altM)
  return COORDINATE:NewFromLLDD(lat, lon, altM)
end

local function msg(t, d) trigger.action.outText(t, d or 10) end

-- Block player parking spots --------------------------------------------
do
  local black = {}
  local kutId = KUT:GetID()
  for _, coa in pairs(env.mission.coalition) do
    for _, ctry in pairs(coa.country or {}) do
      for _, cat in ipairs({ "plane", "helicopter" }) do
        if ctry[cat] and ctry[cat].group then
          for _, g in pairs(ctry[cat].group) do
            local wp = g.route and g.route.points and g.route.points[1]
            if wp and wp.airdromeId == kutId then
              for _, u in pairs(g.units) do
                if (u.skill == "Client" or u.skill == "Player") and u.parking then
                  table.insert(black, tonumber(u.parking))
                end
              end
            end
          end
        end
      end
    end
  end
  KUT:SetParkingSpotBlacklist(black)
  env.info("KutaisiTraffic: " .. #black .. " Spieler-Parkplätze für KI gesperrt.")
end

-- Dynamic slots choose their spot themselves: if a player spawns beside/on parked AI, remove the AI immediately
world.addEventHandler({ onEvent = function(_, e)
  if e.id ~= world.event.S_EVENT_BIRTH or not e.initiator or not e.initiator.getPlayerName
     or not e.initiator:getPlayerName() then return end
  local p = e.initiator:getPoint()
  for name in pairs(TRF.flights) do
    local g = Group.getByName(name)
    for _, u in ipairs(g and g:getUnits() or {}) do
      local q = u:getPoint()
      if not u:inAir() and (p.x - q.x) ^ 2 + (p.z - q.z) ^ 2 < 40 ^ 2 then
        env.info("KutaisiTraffic: " .. name .. " stand auf Spielerplatz – entfernt.")
        g:destroy(); TRF.flights[name] = nil; if DCSATC then DCSATC.ai[name] = nil end
        break
      end
    end
  end
end })

-- Helpers -----------------------------------------------------------------
local function aliveCount()
  local n = 0
  for name, fg in pairs(TRF.flights) do
    if fg and fg:IsAlive() then
      n = n + 1
    else
      TRF.flights[name] = nil
    end
  end
  return n
end

local function pick(t) return t[math.random(#t)] end

local function despawnWhenArrived(fg)
  function fg:OnAfterArrived(From, Event, To)
    self:Despawn(300)
  end
end

-- Departure -----------------------------------------------------------------
function TRF.spawnDeparture(forceType)
  local isTransport = forceType == "transport" or (forceType == nil and math.random() < 0.3)
  local tpl = isTransport and pick(TRF.transports) or pick(TRF.fighters)

  local sp = SPAWN:NewWithAlias(tpl, tpl:gsub("AI_TPL_", "") .. " DEP")
  sp:InitRandomizeCallsign()
  if FC_KUTAISI or DCSATC then sp:InitUnControlled(true) end
  local grp = sp:SpawnAtAirbase(KUT, SPAWN.Takeoff.Cold)
  if not grp then
    env.info("KutaisiTraffic: kein freier Parkplatz für " .. tpl)
    return nil
  end

  local fg = FLIGHTGROUP:New(grp)
  fg:SetHomebase(KUT)

  if isTransport then
    local dest = AIRBASE:FindByName(pick(DESTS))
    fg:SetDestinationbase(dest)
    fg:SetDespawnAfterLanding()
  else
    fg:SetDestinationbase(KUT)
    local o = pick(TRF.orbits)
    local alt = UTILS.FeetToMeters(math.random(14, 24) * 1000)
    local mission = AUFTRAG:NewORBIT_CIRCLE(coordLL(o[1], o[2], alt), UTILS.MetersToFeet(alt), 300)
    mission:SetDuration(math.random(15, 25) * 60)
    fg:AddMission(mission)
    despawnWhenArrived(fg)
  end

  -- Some time on the apron, then the crew reports ready for departure
  fg:SetReadyForTakeoff(true, math.random(60, 300))

  -- DCS-ATC app releases the start (START) when the runway is free; if the app is not running: after 10 min by itself
  if DCSATC then
    local name = grp:GetName()
    DCSATC.ai[name] = "dep"
    timer.scheduleFunction(function()
      if DCSATC.ai[name] == "dep" and fg:IsAlive() then fg:StartUncontrolled(); DCSATC.ai[name] = "dep-go" end
    end, nil, timer.getTime() + 600)
  end

  TRF.flights[grp:GetName()] = fg
  return fg
end

-- Arrival -----------------------------------------------------------------
function TRF.spawnArrival(forceType)
  local isTransport = forceType == "transport" or (forceType == nil and math.random() < 0.35)
  local tpl = isTransport and pick(TRF.transports) or pick(TRF.fighters)
  local e = pick(TRF.entry)
  local alt = UTILS.FeetToMeters(math.random(12, 20) * 1000)
  local c = coordLL(e[1], e[2], alt)

  local sp = SPAWN:NewWithAlias(tpl, tpl:gsub("AI_TPL_", "") .. " ARR")
  sp:InitRandomizeCallsign()
  sp:InitHeading(c:HeadingTo(KUT:GetCoordinate()))
  local grp = sp:SpawnFromCoordinate(c)
  if not grp then return nil end

  local fg = FLIGHTGROUP:New(grp)
  fg:SetHomebase(KUT)
  fg:SetDestinationbase(KUT)
  despawnWhenArrived(fg)
  fg:__RTB(3, KUT)
  if DCSATC then DCSATC.ai[grp:GetName()] = "arr" end   -- DCS-ATC app may send into the holding (WAIT/RESUME)

  TRF.flights[grp:GetName()] = fg
  return fg
end

-- Ticker --------------------------------------------------------------
function TRF.tick()
  if TRF.enabled and aliveCount() < TRF.maxFlights[TRF.density] then
    if math.random() < 0.5 then TRF.spawnDeparture() else TRF.spawnArrival() end
  end
  local base = TRF.intervals[TRF.density]
  return timer.getTime() + base * (0.7 + math.random() * 0.6)
end

-- Start: one aircraft is already ready for departure, an arrival is coming soon
timer.scheduleFunction(function() TRF.spawnDeparture("fighter") end, nil, timer.getTime() + 20)
timer.scheduleFunction(function() TRF.spawnArrival() end, nil, timer.getTime() + 150)
timer.scheduleFunction(TRF.tick, nil, timer.getTime() + 600)

-- Cleanup ----------------------------------------------------------------
-- Wrecks/debris at airfields (5 km) gone after 60 s; range targets stay.
local function nearAirbase(p)
  for _, ab in ipairs(world.getAirbases()) do
    local q = ab:getPoint()
    if (p.x - q.x) ^ 2 + (p.z - q.z) ^ 2 < 5000 ^ 2 then return true end
  end
end
local junkEv = { [world.event.S_EVENT_CRASH] = true, [world.event.S_EVENT_DEAD] = true, [world.event.S_EVENT_UNIT_LOST] = true }
world.addEventHandler({ onEvent = function(_, e)
  if not junkEv[e.id] or not e.initiator or not e.initiator.getPoint then return end
  local ok, p = pcall(e.initiator.getPoint, e.initiator)
  if not ok or not p or not nearAirbase(p) then return end
  timer.scheduleFunction(function()
    local n = world.removeJunk({ id = world.VolumeType.SPHERE, params = { point = p, radius = 150 } })
    if n and n > 0 then env.info("KutaisiTraffic: " .. n .. " Wrack/Trümmer weggeräumt.") end
  end, nil, timer.getTime() + 60)
end })

-- AI on the ground that is damaged or stuck 10 min after takeoff clearance/landing blocks taxiways → gone.
local stuck = {}
timer.scheduleFunction(function()
  for name, fg in pairs(TRF.flights) do
    local g = Group.getByName(name)
    local u = g and g:getUnit(1)
    if not u then
      TRF.flights[name] = nil; stuck[name] = nil
    elseif not u:inAir() then
      local damaged = false
      for _, x in ipairs(g:getUnits()) do if x:getLife() < x:getLife0() then damaged = true end end
      local p, s = u:getPoint(), stuck[name]
      if s and (p.x - s.x) ^ 2 + (p.z - s.z) ^ 2 < 25 then s.n = s.n + 1 else stuck[name] = { x = p.x, z = p.z, n = 0 } end
      local waiting = fg:IsUncontrolled() or (DCSATC and DCSATC.ai[name] == "dep")   -- legitimately waits for START
      if damaged or (not waiting and stuck[name].n >= 10) then
        env.info("KutaisiTraffic: " .. name .. (damaged and " beschädigt" or " sitzt fest") .. " – entfernt.")
        g:destroy(); TRF.flights[name] = nil; stuck[name] = nil
        if DCSATC then DCSATC.ai[name] = nil end
      end
    end
  end
  return timer.getTime() + 60
end, nil, timer.getTime() + 60)

-- Ground crew --------------------------------------------------------------
-- When an aircraft (player or AI) stands at a parking spot: crew chief front left, fire watch front right.
-- After a landing, a fuel truck and an ammunition truck additionally drive up from the next free parking spot (4 min).
-- When it starts rolling: soldiers step back, trucks drive away, then all disappear. Crash/crash landing at the airfield: fire brigade.
-- Refueling/rearming continues via F8 – the trucks are scenery. The app speaks the crew chief for this (C;unit;crew|fuel|done).
local GC = { soldier = "Soldier M4", fuel = "M978 HEMTT Tanker", ammo = "M 818", fire = "HEMTT TFFT", turn = 240 }
local crew, landed, still, gcN = {}, {}, {}, 0
local kutP = Airbase.getByName("Kutaisi"):getPoint()
local spots = {}
for _, s in ipairs(Airbase.getByName("Kutaisi"):getParking() or {}) do spots[#spots + 1] = s.vTerminalPos end

local function d2(a, b) return (a.x - b.x) ^ 2 + (a.z - b.z) ^ 2 end
local function evt(line) if DCSATC and DCSATC.evt then DCSATC.evt(line) end end

-- Point relative to the aircraft: f forward, r right (m)
local function rel(pos, f, r)
  return { x = pos.p.x + pos.x.x * f + pos.z.x * r, z = pos.p.z + pos.x.z * f + pos.z.z * r }
end

local function spawn(ctry, units, route)
  gcN = gcN + 1
  local name = "GC " .. gcN
  local us = {}
  for i, u in ipairs(units) do
    us[i] = { name = name .. "-" .. i, type = u.type, x = u.x, y = u.z, heading = u.hdg or 0, skill = "Average", playerCanDrive = false }
  end
  local g = coalition.addGroup(ctry, Group.Category.GROUND, { name = name, task = "Ground Nothing", units = us,
    route = route and { points = route } or nil })
  if g then g:getController():setOption(AI.Option.Ground.id.ROE, AI.Option.Ground.val.ROE.WEAPON_HOLD) end
  return name
end

local function wp(p, speed) return { x = p.x, y = p.z, type = "Turning Point", action = "Off Road", speed = speed or 6 } end

local function drive(name, from, to, speed)
  local g = Group.getByName(name)
  if g then g:getController():setTask({ id = "Mission", params = { route = { points = { wp(from, speed), wp(to, speed) } } } }) end
end

local function kill(names) for _, n in ipairs(names) do local g = Group.getByName(n); if g then g:destroy() end end end
local function clear(c) kill(c.soldiers); for _, t in ipairs(c.trucks) do kill({ t.name }) end end

-- next free parking spot 80–500 m away (start point of the trucks), otherwise 150 m behind the aircraft
local function depot(pos)
  local best, bd
  for _, s in ipairs(spots) do
    local d = d2(s, pos.p)
    if d > 80 ^ 2 and d < 500 ^ 2 and (not bd or d < bd) then
      local free = true
      for _, g in ipairs(coalition.getGroups(2, Group.Category.AIRPLANE)) do
        for _, u in ipairs(g:getUnits()) do if d2(u:getPoint(), s) < 30 ^ 2 then free = false end end
      end
      if free then best, bd = s, d end
    end
  end
  return best and { x = best.x, z = best.z } or rel(pos, -150, 0)
end

local function taxiingNear(p, r)
  for _, g in ipairs(coalition.getGroups(2, Group.Category.AIRPLANE)) do
    for _, u in ipairs(g:getUnits()) do
      local v = u:getVelocity()
      if not u:inAir() and v.x ^ 2 + v.z ^ 2 > 4 and d2(u:getPoint(), p) < r ^ 2 then return true end
    end
  end
end

local function fireTruck(p, ctry)
  local pos = { p = p, x = { x = 1, z = 0 }, z = { x = 0, z = 1 } }
  local from = depot(pos)
  local n = spawn(ctry, { { type = GC.fire, x = from.x, z = from.z } }, { wp(from, 15), wp({ x = p.x + 25, z = p.z }, 15) })
  timer.scheduleFunction(function() kill({ n }) end, nil, timer.getTime() + 300)
end

world.addEventHandler({ onEvent = function(_, e)
  local u = e.initiator
  if not u or not u.getPoint then return end
  local ok, p = pcall(u.getPoint, u)
  if not ok or d2(p, kutP) > 3000 ^ 2 then return end
  if e.id == world.event.S_EVENT_LAND and u.getName then
    landed[u:getName()] = true
    if u:getLife() < u:getLife0() * 0.8 then fireTruck(p, u:getCountry()) end
  elseif e.id == world.event.S_EVENT_CRASH and u.getCountry then
    fireTruck(p, u:getCountry())
  end
end })

local function tickCrew()
  local now = timer.getTime()
  local seen = {}
  for _, g in ipairs(coalition.getGroups(2, Group.Category.AIRPLANE)) do
    for _, u in ipairs(g:getUnits()) do
      local name, pos = u:getName(), u:getPosition()
      local v = u:getVelocity()
      local spd = math.sqrt(v.x ^ 2 + v.z ^ 2)
      local c = crew[name]
      seen[name] = true
      if not u:inAir() and d2(pos.p, kutP) < 3000 ^ 2 then
        local player = u:getPlayerName() ~= nil
        if not c and spd < 0.5 then
          local atSpot = false
          for _, s in ipairs(spots) do if d2(s, pos.p) < 30 ^ 2 then atSpot = true break end end
          still[name] = atSpot and (still[name] or now) or nil
          if atSpot and now - still[name] >= 10 then   -- 10 s standstill: parked, not just briefly stopped
            still[name] = nil
            local b = u:getDesc().box or { max = { x = 8, z = 5 }, min = { x = -8 } }
            local cc, fg = rel(pos, b.max.x + 5, -b.max.z * 0.7), rel(pos, b.max.x * 0.4, b.max.z + 5)
            local face = function(q) return math.atan2(pos.p.z - q.z, pos.p.x - q.x) end
            c = { soldiers = { spawn(u:getCountry(), { { type = GC.soldier, x = cc.x, z = cc.z, hdg = face(cc) },
                                                       { type = GC.soldier, x = fg.x, z = fg.z, hdg = face(fg) } }) },
                  trucks = {}, box = b, ctry = u:getCountry(), player = player }
            if landed[name] then c.trucksAt = now + 20 end
            if player then evt("C;" .. name .. (landed[name] and ";back" or ";crew")) end
            landed[name] = nil
            crew[name] = c
          end
        elseif c and not c.leaving and spd > 1.5 then
          -- rolls off: soldiers 25 m to the side, trucks back, then gone
          c.leaving = now
          local s = Group.getByName(c.soldiers[1])
          local q = s and s:getUnit(1) and s:getUnit(1):getPoint()
          if q then drive(c.soldiers[1], q, { x = q.x + (q.x - pos.p.x), z = q.z + (q.z - pos.p.z) }, 3) end
          for _, t in ipairs(c.trucks) do drive(t.name, t.at, t.home, 8) end
        elseif c and not c.leaving and c.trucksAt and now >= c.trucksAt and #c.trucks == 0 then
          if taxiingNear(pos.p, 200) then
            c.trucksAt = now + 10   -- drive only when nobody is taxiing here
          else
            local b, home = c.box, depot(pos)
            for _, t in ipairs({ { GC.fuel, rel(pos, b.min.x * 0.3, b.max.z + 9) }, { GC.ammo, rel(pos, b.min.x * 0.5, -b.max.z - 10) } }) do
              local n = spawn(c.ctry, { { type = t[1], x = home.x, z = home.z } }, { wp(home), wp(t[2]) })
              c.trucks[#c.trucks + 1] = { name = n, at = t[2], home = home }
              home = { x = home.x + 12, z = home.z }   -- second truck next to it
            end
            c.doneAt = now + GC.turn
            if c.player then evt("C;" .. name .. ";fuel") end
          end
        elseif c and not c.leaving and c.doneAt and now >= c.doneAt then
          c.doneAt = nil
          for _, t in ipairs(c.trucks) do drive(t.name, t.at, t.home, 8) end
          c.trucksGone = now
          if c.player then evt("C;" .. name .. ";done") end
        end
      end
      -- cleanup: soldiers 40 s after rolling off, trucks 90 s after departure
      if c and c.leaving and now - c.leaving > 40 then clear(c); crew[name] = nil
      elseif c and c.trucksGone and now - c.trucksGone > 90 then
        for _, t in ipairs(c.trucks) do kill({ t.name }) end
        c.trucks, c.trucksGone, c.trucksAt = {}, nil, nil
      end
    end
  end
  for name, c in pairs(crew) do   -- Aircraft gone (despawned, destroyed): personnel gone too
    if not seen[name] then clear(c); crew[name] = nil end
  end
  return now + 2
end
timer.scheduleFunction(tickCrew, nil, timer.getTime() + 5)

-- Menu --------------------------------------------------------------------
local root = missionCommands.addSubMenu("KI-Verkehr Kutaisi")
missionCommands.addCommand("Verkehr an/aus", root, function()
  TRF.enabled = not TRF.enabled
  msg("KI-Verkehr " .. (TRF.enabled and "AN" or "AUS") .. " (laufende Flüge bleiben).")
end)
local mD = missionCommands.addSubMenu("Dichte", root)
for i, l in ipairs({ "Wenig", "Normal", "Viel" }) do
  missionCommands.addCommand(l, mD, function()
    TRF.density = i
    msg("KI-Verkehr: " .. l .. " (max. " .. TRF.maxFlights[i] .. " Flüge gleichzeitig).")
  end)
end
missionCommands.addCommand("Abflug jetzt (Jet)", root, function()
  if TRF.spawnDeparture("fighter") then msg("KI-Jet steht auf dem Vorfeld und meldet sich gleich beim Tower.") end
end)
missionCommands.addCommand("Abflug jetzt (Transporter)", root, function()
  if TRF.spawnDeparture("transport") then msg("KI-Transporter steht auf dem Vorfeld.") end
end)
missionCommands.addCommand("Anflug jetzt", root, function()
  if TRF.spawnArrival() then msg("KI-Flug im Anflug auf Kutaisi.") end
end)
missionCommands.addCommand("Status", root, function()
  local lines = {}
  for name, fg in pairs(TRF.flights) do
    if fg:IsAlive() then
      table.insert(lines, string.format("%s (%s) – %s", fg:GetCallsignName() or name, fg.actype or "?", fg:GetState()))
    end
  end
  msg("KI-VERKEHR (" .. (TRF.enabled and "an" or "aus") .. ", Dichte " .. TRF.density .. ")\n" ..
      (#lines > 0 and table.concat(lines, "\n") or "keine aktiven Flüge"), 15)
end)

env.info("KutaisiTraffic: bereit (FLIGHTCONTROL " .. (FC_KUTAISI and "aktiv" or "nicht aktiv") .. ")")
