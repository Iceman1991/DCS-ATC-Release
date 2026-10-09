-- Offline test DcsAtcMission.lua with a rebuilt DCS API.
-- "C:\Program Files\Eagle Dynamics\DCS World\bin\luae.exe" tests\missiontest.lua mod\Scripts\DcsAtc\DcsAtcMission.lua
local T, queue, handlers, groups, events, files = 0, {}, {}, {}, {}, {}
local function vec(x, z) return { x = x, y = 0, z = z } end
local tmp = os.getenv("TEMP") .. "\\dcsatc-missiontest"
os.execute('mkdir "' .. tmp .. '\\DcsAtc-Menu" 2>nul')
for _, id in ipairs({ "23", "26", "map" }) do os.remove(tmp .. "\\DcsAtc-Terrain-Caucasus-" .. id .. ".txt.tmp") end
local realGetenv = os.getenv
os.getenv = function(k) if k == "TMP" then return tmp end return realGetenv(k) end
lfs = { mkdir = function() end, attributes = function() return { modification = os.time() } end,
        dir = function() local l = {} for k in pairs(files) do l[#l + 1] = k end local i = 0 return function() i = i + 1 return l[i] end end }
-- Simulate the app's files: io.open on DcsAtc-Menu\*.cmd reads from files
local realOpen = io.open
io.open = function(path, mode)
  local name = path:match("DcsAtc%-Menu\\(.+)$")
  if mode == "r" and name and files[name] then local s = files[name] return { read = function() return s end, close = function() end } end
  if mode == "w" and name then
    return { write = function(_, s) files["w:" .. name] = s end, close = function() end }
  end
  return realOpen(path, mode)
end
local realRemove = os.remove
os.remove = function(p) local name = p:match("DcsAtc%-Menu\\(.+)$") if name then files[name] = nil return true end return realRemove(p) end
os.rename = function(a, b)
  local an, bn = a:match("DcsAtc%-Menu\\(.+)$"), b:match("DcsAtc%-Menu\\(.+)$")
  if an and bn and bn:match("%.evt$") then events[#events + 1] = files["w:" .. an] end
  if an then files["w:" .. an] = nil end
  return true
end

env = { info = function(s) if os.getenv("DBG") then print(s) end end, error = function(s) print("ENV ERROR " .. s) end, mission = { theatre = "Caucasus", weather = {}, coalition = {
  blue = { bullseye = { x = 0, y = 0 }, country = { { id = 2, plane = { group = { { name = "Players", units = { { skill = "Client", parking = "1" } },
           route = { points = { { airdromeId = 26 } } } } } } } } } } } }
country = { id = { USA = 2, RUSSIA = 0 } }
timer = { getTime = function() return T end, scheduleFunction = function(f, a, t) queue[#queue + 1] = { f = f, a = a, t = t } end }
world = { event = { S_EVENT_BIRTH = 15, S_EVENT_LAND = 4, S_EVENT_CRASH = 5, S_EVENT_DEAD = 8, S_EVENT_UNIT_LOST = 30, S_EVENT_ENGINE_SHUTDOWN = 19,
                    S_EVENT_SHOT = 1, S_EVENT_HIT = 2, S_EVENT_EJECTION = 6, S_EVENT_PILOT_DEAD = 9, S_EVENT_SHOOTING_START = 23, S_EVENT_SHOOTING_END = 24, S_EVENT_KILL = 28, S_EVENT_REFUELING = 7, S_EVENT_REFUELING_STOP = 14, S_EVENT_TAKEOFF = 3, S_EVENT_LANDING_QUALITY_MARK = 36 },
          addEventHandler = function(h) handlers[#handlers + 1] = h end, VolumeType = { SPHERE = 1 },
          weather = { getFogThickness = function() return 0 end }, removeJunk = function() end }
MENU = {}
GROUPS = {}   -- R390: submenus { name, n = entries } – at most 9 per level (DCS F10)
missionCommands = { addSubMenuForGroup = function(_, name, parent) local g = { name = name, n = 0 }; if parent then parent.n = parent.n + 1 end; GROUPS[#GROUPS + 1] = g; return g end,
                    addCommandForGroup = function(_, name, parent, fn, arg) if parent then parent.n = parent.n + 1 end; MENU[#MENU + 1] = { name = name, fn = fn, arg = arg } end }
function menu(name, v) for _, m in ipairs(MENU) do if m.name == name and (v == nil or m.arg.v == v) then m.fn(m.arg) return end end error("Menü fehlt: " .. name) end
OUT = {}   -- Texts in game: { gid, text, display duration }
trigger = { action = { outText = function(t, d) OUT[#OUT + 1] = { 0, t, d } end, outTextForGroup = function(g, t, d) OUT[#OUT + 1] = { g, t, d } end,
                       outTextForUnit = function(u, t, d, c) OUT[#OUT + 1] = { "u" .. u, t, d, c } end,
                       outTextForCoalition = function(s, t, d) OUT[#OUT + 1] = { "c" .. s, t, d } end } }
atmosphere = { getWind = function() return vec(1, 1) end, getTemperatureAndPressure = function() return 288, 101325 end }
landX = nil   -- Land from this x coordinate (CVTURN test), otherwise water (3)
land = { getHeight = function() return 0 end, getSurfaceType = function(v) return landX and v.x >= landX and 1 or 3 end }
AI = { Option = { Ground = { id = { ROE = 0 }, val = { ROE = { WEAPON_HOLD = 4 } } }, Air = { id = { SILENCE = "silence", PROHIBIT_WP_PASS_REPORT = "nowp" } } } }
gSilent = {}   -- R304: radio silence per group (setOption SILENCE)
gNoWp = {}   -- R312: waypoint report prohibited per group
Airbase = { Category = { AIRDROME = 0, SHIP = 2 } }
Weapon = { Category = { SHELL = 0, MISSILE = 1, ROCKET = 2, BOMB = 3 }, MissileCategory = { AAM = 1, SAM = 2 }, GuidanceType = { IR = 2, RADAR_ACTIVE = 3, RADAR_SEMI_ACTIVE = 4 } }
abSilent = {}
local function airbase(name, id, x, z)
  local spots = {}
  for i = 1, 6 do spots[i] = { vTerminalPos = vec(x, z + (i - 1) * 50), Term_Index = i, Term_Type = (i == 6) and 104 or 72 } end
  return { getName = function() return name end, getID = function() return id end, getPoint = function() return vec(x, z) end,
           getDesc = function() return { category = 0 } end, getRunways = function() return {} end, getCoalition = function() return 2 end,
           getParking = function() return spots end, setRadioSilentMode = function(_, s) abSilent[name] = s end }
end
local cvSilent
local bases = { airbase("Kutaisi", 26, 0, 0), airbase("Senaki", 23, 0, 100000),
  { getName = function() return "CVN-73" end, getDesc = function() return { category = 2 } end, getID = function() return 99 end, getPoint = function() return vec(0, 300000) end, setRadioSilentMode = function(_, s) cvSilent = s end } }
world.getAirbases = function() return bases end

local planes = {}
local function unit(name, x, z, player, side)
  local u = { name = name, p = vec(x, z), v = vec(0, 0), air = false }
  function u:getName() return self.name end
  function u:getPoint() return self.p end
  function u:getPosition() return { p = self.p, x = vec(1, 0), z = vec(0, 1) } end
  function u:getVelocity() return self.v end
  function u:inAir() return self.air end
  function u:getPlayerName() return player end
  function u:getDesc() return { box = { max = { x = 8, z = 6 }, min = { x = -9 } }, category = 0, fuelMassMax = 4899 } end
  function u:getAmmo() return self.ammo or {} end
  function u:getDrawArgumentValue(a) return a == 0 and 0.5 or 0 end   -- Gear half: distinguishable from tank (1.00)
  function u:getCountry() return 2 end
  function u:getCoalition() return side or 2 end
  function u:getLife() return 1 end
  function u:getLife0() return 1 end
  function u:isExist() return true end
  function u:isActive() return true end
  function u:getCallsign() return name end
  function u:getTypeName() return "FA-18C_hornet" end
  function u:getID() return 1 end
  function u:getGroup() return { getName = function() return u.group end, getID = function() return 1 end } end
  function u:getCategory() return 1 end
  function u:hasAttribute(a) return a == "AWACS" and (self.group or ""):find("Overlord", 1, true) ~= nil end
  return u
end
local function plane(name, x, z, player)
  local u = unit(name, x, z, player)
  local g = { getUnits = function() return { u } end, getName = function() return name end, getID = function() return 1 end, getUnit = function() return u end,
    getController = function() return { setOption = function(_, id, v) if id == 7 then gSilent[name] = v elseif id == 19 then gNoWp[name] = v end end } end }
  planes[#planes + 1] = { g = g, u = u }
  return u
end
local tasks, RADAR = {}, {}   -- RADAR: what the AWACS has detected
coalition = {
  getGroups = function(side, cat)
    local l = {}
    if cat == 0 and side == 2 then for _, p in ipairs(planes) do l[#l + 1] = p.g end end
    if cat == 2 and side == 1 then for _, g in ipairs(REDGROUND or {}) do l[#l + 1] = g end end
    if cat == 3 and side == 1 then for _, g in ipairs(REDSHIPS or {}) do l[#l + 1] = g end end
    for _, g in pairs(groups) do if g.cat == cat and side == 2 then l[#l + 1] = g end end
    return l
  end,
  getStaticObjects = function(side) return side == 1 and REDSTATIC or {} end,
  addGroup = function(ctry, cat, d)
    local g = { d = d, cat = cat }
    local u = unit(d.units[1].name, d.units[1].x, d.units[1].y, nil)
    u.group = d.name
    u.air = d.units[1].alt and d.units[1].alt > 100 or false
    function g:getController() return { setOption = function(_, id, v) if id == 7 then gSilent[d.name] = v end end, setTask = function(_, t) g.task = t end,
      setCommand = function(_, c) tasks[#tasks + 1] = d.name .. ":" .. c.id end,
      setAltitude = function(_, m) tasks[#tasks + 1] = d.name .. ":alt " .. math.floor(m + 0.5) end,
      pushTask = function(_, t) tasks[#tasks + 1] = d.name .. ":push " .. t.id end, popTask = function() tasks[#tasks + 1] = d.name .. ":pop" end,
      getDetectedTargets = function() return d.name:find("Overlord", 1, true) and RADAR or {} end } end
    function g:getUnit() return u end
    function g:getUnits() return { u } end
    function g:getName() return d.name end
    function g:getID() return 1 end
    function g:destroy() groups[d.name] = nil end
    groups[d.name] = g
    return g
  end,
}
Group = { Category = { AIRPLANE = 0, HELICOPTER = 1, GROUND = 2, SHIP = 3 }, getByName = function(n) return groups[n] end }
-- Carrier (M5): editor data (radio, TACAN, ICLS) and ship in the world
env.mission.coalition.blue.country[1].ship = { group = { { name = "CVN", units = { { type = "CVN_73", frequency = 127500000, unitId = 501 } }, route = { points = { { x = 1000, y = 2000, speed = 10, task = { id = "ComboTask", params = { tasks = {
  { id = "WrappedAction", params = { action = { id = "ActivateBeacon", params = { channel = 73, modeChannel = "X" } } } },
  { id = "WrappedAction", params = { action = { id = "ActivateICLS", params = { channel = 11 } } } } } } } }, { x = 5000, y = 2000, speed = 12 }, { x = 9000, y = 2000, speed = 12 } } } } } }
env.mission.coalition.blue.country[1].plane.group[2] = { name = "Hornet CV", units = { {} }, route = { points = { { type = "Land", linkUnit = 501 } } } }
plane("Hornet CV", 3000, 2000).air = true
env.mission.coalition.blue.country[1].plane.group[3] = { name = "Viper", frequency = 141, task = "CAP", units = { {} } }   -- AI radio: flight frequency, editor task (R376)
local cv = unit("CVN-73", 1000, 2000); cv.v = vec(10, 0); cv.air = false
function cv:getTypeName() return "CVN_73" end
groups["CVN"] = { cat = 3, getUnits = function() return { cv } end, getUnit = function() return cv end, getName = function() return "CVN" end, getController = function() return { getDetectedTargets = function() return {} end, setTask = function(_, k) cvTask = k end } end }

local function run(to)   -- Advance time, run due functions (return value = next due time)
  while true do
    table.sort(queue, function(a, b) return a.t < b.t end)
    local q = queue[1]
    if not q or q.t > to then break end
    table.remove(queue, 1)
    T = q.t
    local r = q.f(q.a, T)
    if r then queue[#queue + 1] = { f = q.f, a = q.a, t = r } end
  end
  T = to
end
local function find(prefix) local l = {} for n, g in pairs(groups) do if n:find(prefix, 1, true) then l[#l + 1] = g end end return l end
local function has(e) for _, x in ipairs(events) do if x == e then return true end end end
local function hasTask(s) for _, x in ipairs(tasks) do if x:find(s, 1, true) then return true end end end

-- A17: range zone of the mission and a red tank in it
env.mission.triggers = { zones = { { name = "Range Alpha", x = 20000, y = 0, radius = 3000 } } }
tank = unit("Tank1", 20000, 0, nil, 1)
function tank:getTypeName() return "T-72B" end
REDGROUND = { { getUnits = function() return { tank } end, getUnit = function() return tank end } }
static1 = unit("Depot1", 20000, 2000, nil, 1); function static1:getTypeName() return "Warehouse" end; REDSTATIC = { static1 }   -- R369: static object and ship of the red side in the range
ship1 = unit("Boat1", 20000, -2000, nil, 1); function ship1:getTypeName() return "Speedboat" end
REDSHIPS = { { getUnits = function() return { ship1 } end, getUnit = function() return ship1 end, getName = function() return "RedBoat" end,
  getController = function() return { getDetectedTargets = function() return {} end } end } }
DCSATC_SPAWNTEST = true   -- since 0.9.4 the mod spawns nothing; the spawn code is still tested here
DCSATC_OPTIONS = { arrivals = { Senaki = 2 }, crew = true }   -- Ground crew is off by default, test it here too   -- Arrivals at mission start
dofile(arg[1])
-- R313: silent already on load (starting lineup) and immediately on birth, without a timer
assert(gSilent["Hornet CV"] == true and gNoWp["Hornet CV"] == true, "R313: Startaufstellung nicht beim Laden stumm")
local late = plane("Late 1", 9000, 0); late.air = true
local lg = planes[#planes].g
lg.getCategory = function() return Group.Category.AIRPLANE end
late.getGroup = function() return lg end
for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_BIRTH, initiator = late }) end
assert(gSilent["Late 1"] == true and gNoWp["Late 1"] == true, "R313: später erzeugter KI-Flug nicht bei der Geburt stumm")
assert(DCSATC, "DCSATC fehlt")

-- Player stands on Kutaisi parking spot 1 (player slot, taboo for AI)
local p1 = plane("P1", 0, 0, "Player1")
assert(cvSilent == nil); run(18); assert(cvSilent == true, "DCS-ATC des Trägers nicht stumm"); assert(#find("ATC GC") == 1 and has("C;P1;crew"), "Crew P1 fehlt: GC=" .. #find("ATC GC") .. " ev=" .. table.concat(events, ","))

assert(#find("ATC Senaki ARR") == 2, "Startanflüge Senaki: " .. #find("ATC Senaki ARR"))
-- R8: F10 Ground -> IFR clearance: without destination, then own/neutral airfields without the own one (Kutaisi under 5 km)
local ifr = {} for _, m in ipairs(MENU) do if type(m.arg) == "table" and tostring(m.arg.text):find("IFR clearance", 1, true) then ifr[#ifr + 1] = m.arg.text end end
assert(table.concat(ifr, "|") == "Ground: request IFR clearance|Ground: request IFR clearance to Senaki", "F10 IFR clearance: " .. table.concat(ifr, "|"))

-- Tanker + AWACS (mission has none)
assert(#find("ATC Texaco") == 1 and #find("ATC Arco") == 1 and #find("ATC Overlord") == 1, "Tanker/AWACS fehlen")
assert(hasTask("Texaco #") and hasTask(":ActivateBeacon"), "TACAN fehlt")

-- V9: low flyer appears only when the AWACS detects it (D lines)
Controller = { Detection = { RADAR = 1, DLINK = 32 } }
Object = { Category = { UNIT = 1 } }
local function stateFile() local f = realOpen(tmp .. "\\DcsAtc-State.txt.tmp", "r") local s = f:read("*a") f:close() return s end
local low = unit("Bandit1", 50000, 0, nil, 1); low.air = true
run(T + 3); assert(not stateFile():find("D;2;Bandit1", 1, true), "Tiefflieger ohne Erfassung gemeldet")
RADAR[1] = { object = low }
run(T + 3); assert(stateFile():find("D;2;Bandit1;0", 1, true), "Erfassung fehlt: " .. stateFile())
RADAR[1].type = true
run(T + 3); assert(stateFile():find("D;2;Bandit1;1", 1, true), "Typ erkannt fehlt")
assert(("\n" .. stateFile()):find("\nP;[^\n]*;1%.00;0%.50;a\n"), "Fahrwerk/Kategorie fehlt in der P-Zeile: " .. stateFile())
local huey = coalition.addGroup(2, Group.Category.HELICOPTER, { name = "Huey", units = { { name = "Huey", x = 900000, y = 0, alt = 300 } } })   -- N50: category h/a in P and T line
run(T + 3); assert(("\n" .. stateFile()):find("\nT;Huey;Huey;[^\n]*;h;%d+;[^;\n]*\n") and stateFile():find("T;Hornet CV;[^\n]*;a;%d+;[^;\n]*\n"), "Kategorie fehlt in der T-Zeile: " .. stateFile())
huey:destroy()
assert(stateFile():find("C;CVN-73;CVN;CVN_73;1000.0;2000.0;0.0000;10.0;2;127.500;73X;11;1.0;1.0;101325;0", 1, true), "Träger-Zeile fehlt: " .. stateFile())
assert(stateFile():find("T;Hornet CV;Hornet CV;FA%-18C_hornet;[^\n]*;cv;"), "KI mit Landung auf dem Träger ohne Flag cv")
files["9-7.cmd"] = "CVTURN;CVN;0.5000;20"; run(T + 1)
local cp = cvTask and cvTask.params.route.points
assert(cp and math.abs(cp[2].x - (1000 + math.cos(0.5) * 50 * 1852)) < 1 and math.abs(cp[2].speed - 20 * 0.514444) < 0.01, "CVTURN wirkt nicht")
landX = 1000 + math.cos(0.5) * 20 * 1852   -- Landing 20 NM ahead: leg 13 NM (20 - 5 - 2)
cvTask = nil; files["9-7.cmd"] = "CVTURN;CVN;0.5000;20"; run(T + 1)
cp = cvTask and cvTask.params.route.points
assert(cp and math.abs(cp[2].x - (1000 + math.cos(0.5) * 13 * 1852)) < 1, "CVTURN: Schenkel nicht vor dem Land gekürzt")
landX = 1000 + math.cos(0.5) * 3 * 1852   -- Land already at 5 NM: do not turn
cvTask = nil; files["9-7.cmd"] = "CVTURN;CVN;0.5000;20"; run(T + 1)
assert(cvTask == nil, "CVTURN: dreht trotz Land in 5 NM")
run(T + 3); assert(stateFile():find("2;127.500;73X;11;1.0;1.0;101325;1", 1, true), "C-Zeile ohne abgelehntes CVTURN")
landX = nil
files["9-8.cmd"] = "CVRESUME;CVN"; run(T + 1)
cp = cvTask.params.route.points
assert(#cp == 3 and cp[2].x == 5000 and cp[1].x == 1000, "CVRESUME: Route ab dem nächsten Wegpunkt")
run(T + 3); assert(stateFile():find("2;127.500;73X;11;1.0;1.0;101325;0", 1, true), "abgelehntes CVTURN bleibt nach CVRESUME stehen")
local hcv for _, p in ipairs(planes) do if p.g:getName() == "Hornet CV" then hcv = p.u end end   -- Redirect AI after three waveoffs (M6): landing waypoint at the nearest airfield, flag cv gone
groups["Hornet CV"] = { cat = 99, getUnit = function() return hcv end, getController = function() return { setTask = function(_, k) divTask = k end } end }
files["9-9.cmd"] = "DIVERT;Hornet CV"; run(T + 1)
groups["Hornet CV"] = nil
local dp = divTask and divTask.params.route.points
assert(dp and dp[2].type == "Land" and dp[2].airdromeId, "DIVERT ohne Landewegpunkt")
run(T + 1); assert(not stateFile():find("T;Hornet CV;[^\n]*;cv;"), "DIVERT: Flag cv bleibt")

-- AI traffic: after 30 s first flight at Kutaisi
run(80)
local ai = {}
for n, g in pairs(groups) do if g.cat == 0 and (n:find("DEP") or n:find("ARR")) then ai[#ai + 1] = n end end
assert(#ai >= 1, "kein KI-Verkehr")
for _, n in ipairs(ai) do
  local flag = DCSATC.ai[n]
  local st, fld = tostring(flag):match("^([%a-]+):(.+)$")
  assert(st == "dep" or st == "arr", "Flag " .. tostring(flag))
  local u = groups[n].d.units[1]
  if st == "dep" then
    assert(u.parking ~= 1, "KI auf Spieler-Slot")
    files["9-1.cmd"] = "START;" .. n
    run(T + 1)
    assert(DCSATC.ai[n] == "dep-go:" .. fld and hasTask(n .. ":Start"), "START wirkt nicht")
  else
    files["9-2.cmd"] = "WAIT;" .. n .. ";600;6000"
    run(T + 1)
    assert(DCSATC.ai[n] == "arr-hold:" .. fld and hasTask(n .. ":push Orbit"), "WAIT wirkt nicht")
    files["9-3.cmd"] = "RESUME;" .. n
    run(T + 1)
    assert(DCSATC.ai[n] == "arr:" .. fld and hasTask(n .. ":pop"), "RESUME wirkt nicht")
    -- LD1: AWACS assignment -> AttackGroup; second assignment replaces the first (pop, push); DISENGAGE takes it away; target gone -> no pop
    local other = ai[1] == n and ai[2] or ai[1]
    if other then
      local before = #tasks
      files["9-4.cmd"] = "ENGAGE;" .. n .. ";" .. other; run(T + 1)
      assert(tasks[before + 1] == n .. ":push AttackGroup" and #tasks == before + 1, "LD1 ENGAGE")
      files["9-4.cmd"] = "ENGAGE;" .. n .. ";" .. other; run(T + 1)
      assert(tasks[before + 2] == n .. ":pop" and tasks[before + 3] == n .. ":push AttackGroup", "LD1 ENGAGE ersetzt")
      files["9-4.cmd"] = "DISENGAGE;" .. n; run(T + 1)
      assert(tasks[before + 4] == n .. ":pop" and #tasks == before + 4, "LD1 DISENGAGE")
      files["9-4.cmd"] = "DISENGAGE;" .. n; run(T + 1)
      assert(#tasks == before + 4, "LD1 DISENGAGE ohne Zuweisung: kein pop")
    end
    -- LD7: fighter from the mission editor checks out -> flag arr, landing at the airfield; second RTB changes nothing
    coalition.addGroup(2, 0, { name = "Ford " .. n, units = { { name = "Ford11", x = 0, y = 0, alt = 7000 } }, route = { points = {} } })
    files["9-5.cmd"] = "RTB;Ford " .. n .. ";" .. fld; run(T + 1)
    local rt = groups["Ford " .. n].task
    assert(DCSATC.ai["Ford " .. n] == "arr:" .. fld and rt and rt.id == "Mission" and rt.params.route.points[1].type == "Land", "LD7 RTB -> Anflug")
    groups["Ford " .. n].task = nil; files["9-5.cmd"] = "RTB;Ford " .. n .. ";" .. fld; run(T + 1)
    assert(groups["Ford " .. n].task == nil, "LD7 RTB nur einmal")
    groups["Ford " .. n] = nil
    -- LD9: AWACS evades -> circle at the new point (time-limited, laid over the mission task)
    local nt = #tasks
    files["9-6.cmd"] = "ORBIT;" .. n .. ";-185200;0"; run(T + 1)
    assert(tasks[nt + 1] == n .. ":push ControlledTask", "LD9 ORBIT")
    local pts = groups[n].d.route.points   -- no own approach waypoints: DCS flies to the landing point itself
    assert(#pts == 2 and pts[2].type == "Land" and pts[2].speed > 100, "Anflug-Route")
    files["9-2.cmd"] = "WAIT;" .. n .. ";600;6000"; run(T + 1)   -- N40: airfield captured -> DIVERT, flag "arr:<new airfield>" (anew in the sequence, also from the holding)
    files["9-3.cmd"] = "DIVERT;" .. n; run(T + 1)
    local dt = groups[n].task and groups[n].task.params.route.points
    assert(tostring(DCSATC.ai[n]):match("^arr:%a") and dt and dt[2].type == "Land", "DIVERT setzt das Flag nicht: " .. tostring(DCSATC.ai[n]))
  end
end

-- P2 lands, parks and gets trucks
local p2 = plane("P2", 0, 100, "Wingman")
for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_LAND, initiator = p2 }) end
run(T + 14); assert(has("C;P2;back"), "Welcome back fehlt")
run(T + 25); assert(has("C;P2;fuel"), "Lkw fehlen")
p1.v = vec(5, 0)
run(T + 50)
run(T + 300); assert(not has("C;P2;done"), "Lkw melden \"done\", getankt wird nichts (A37)")
-- DCS radio menu: tanker position in the DCS list (by distance), airfield only at night
coalition.service = { ATC = 0, AWACS = 1, TANKER = 2 }
coalition.side = { NEUTRAL = 0, RED = 1, BLUE = 2 }
coalition.getServiceProviders = function(_, s)
  local l = {}
  if s == 2 then for _, g in ipairs(find("ATC Texaco")) do l[#l + 1] = g:getUnit() end end
  if s == 0 then for _, b in ipairs(bases) do l[#l + 1] = b end end
  return l
end
Unit = { getByName = function(n) return n == "P1" and p1 or nil end }
coord = { LOtoLL = function() return 42.18, 42.48 end }   -- Kutaisi; Caucasus UTC+4, 21 June: sunset ~20:45
local function ev() return table.concat(events, " ") end
timer.getAbsTime = function() return 19 * 3600 end
files["9-5.cmd"] = "MENU;P1;tanker;" .. find("ATC Texaco")[1]:getName() .. ";s"   -- R304: s = tanker stays silent
files["9-6.cmd"] = "MENU;P1;atc;26"
files["9-7.cmd"] = "MENU;P1;atc;77"
run(T + 1)
assert(has("M;P1;tanker;1;1"), "MENU tanker fehlt")
assert(gSilent[find("ATC Texaco")[1]:getName()] == true, "R304: Tanker nicht stumm (Spawn bzw. MENU mit s)")
assert(gSilent["Hornet CV"] == true and gSilent.P1 == nil, "R308: KI-Flug der Mission nicht stumm bzw. Spielergruppe stumm")
assert(gNoWp["Hornet CV"] == true and gNoWp.P1 == nil, "R312: Wegpunktmeldung des KI-Flugs nicht verboten")
assert(ev():find("M;P1;atc;0;Tag (Sonne 1", 1, true) and has("M;P1;atc;0;Platz 77 nicht in der Mission"), "Befeuerung am Tag / Platz unbekannt: " .. ev())
timer.getAbsTime = function() return 20.5 * 3600 end   -- Twilight: sun ~2°, old calculation (fixed 43°, noon 12:30) gave -4°
files["9-8.cmd"] = "MENU;P1;atc;26"
run(T + 1)
assert(ev():find("M;P1;atc;1;", 1, true), "MENU atc in der Dämmerung fehlt: " .. ev())
assert(abSilent.Kutaisi == false, "ohne s: Platz nicht kurz hörbar")
assert(("\n" .. stateFile()):find("\nY;73800;%-?%d+%.%d\n"), "Y-Zeile ohne Sonnenhöhe: " .. stateFile())   -- Y;clock;sun elevation (case selection of the carrier)
assert(("\n" .. stateFile()):find("\nS;%d+%.%d\n"), "S-Zeile (Missionslaufzeit, R120) fehlt: " .. stateFile())
files["9-9.cmd"] = "MENU;P1;atc;26"
run(T + 1)
assert(ev():find("M;P1;atc;0;DCS-ATC-Dialog offen", 1, true), "zweites Inbound trotz offenem DCS-Dialog: " .. ev())
p1.air = true; for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_ENGINE_SHUTDOWN, initiator = p1 }) end; p1.air = false   -- Engine off on approach
files["9-9b.cmd"] = "MENU;P1;atc;26"
run(T + 1)
assert(select(2, ev():gsub("M;P1;atc;0;DCS%-ATC%-Dialog offen", "")) == 2, "Triebwerk aus in der Luft schließt den DCS-Dialog: " .. ev())
for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_TAKEOFF, initiator = p1 }) end
timer.getAbsTime = function() return 12 * 3600 end
abSilent.Kutaisi = true
files["9-10.cmd"] = "MENU;P1;dep;26;ws"   -- Noon, but poor visibility (app: Tower.Ifr)
run(T + 1)
assert(ev():find("M;P1;dep;1;", 1, true), "Abflug bei schlechter Sicht fehlt: " .. ev())
assert(abSilent.Kutaisi == true, "s (RunwayLights silent): Platz wurde hörbar geschaltet")
world.BirthPlace = { wsBirthPlace_Park = 5, wsBirthPlace_Heliport_Cold = 10, wsBirthPlace_Park_Hot = 13 }
local hot = { getName = function() return "P1" end, getPlayerName = function() return "Player1" end, inAir = function() return false end, getPoint = function() return vec(1e6, 1e6) end }
for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_ENGINE_SHUTDOWN, initiator = p1 }) end
for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_BIRTH, initiator = hot, subPlace = 13 }) end
files["9-11.cmd"] = "MENU;P1;dep;26;w"
run(T + 1)
assert(select(2, ev():gsub("M;P1;dep;0;DCS%-ATC%-Dialog offen", "")) == 1, "Heißstart: DCS-Dialog offen fehlt: " .. ev())
files["9-12.cmd"] = "MENU;P1;tanker;" .. find("ATC Texaco")[1]:getName()
run(T + 1)
assert(ev():find("M;P1;tanker;0;DCS-ATC-Dialog offen", 1, true) and select(2, ev():gsub("M;P1;tanker;1;1", "")) == 1, "Heißstart: Tanker trotz offenem DCS-Dialog: " .. ev())
menu("Bodenpersonal an/aus"); run(T + 310); assert(#find("ATC GC") == 0, "Bodenpersonal aus wirkt nicht")
do local kinds = 0 for _, m in ipairs(MENU) do if type(m.arg) == "table" and m.arg.text and m.arg.text:find("^mayday mayday mayday, .+, request immediate landing$") then kinds = kinds + 1 end end
  assert(kinds > 0 and kinds % 5 == 0, "F10 MAYDAY mit Art (A30): " .. kinds) end
do local n = {} for _, m in ipairs(MENU) do if type(m.arg) == "table" and m.arg.text then n[m.arg.text] = (n[m.arg.text] or 0) + 1 end end   -- R297: like the radio wheel
  local pf = false for t in pairs(n) do if t:find("^pan pan.*fuel") then pf = true end end
  assert(n["mayday mayday mayday fuel, emergency fuel, request immediate landing"] and not pf and n["pan pan, pan pan, pan pan, bird strike, request priority landing"]
    and n["minimum fuel"] and n["Approach: hung ordnance"] and n["Ground: hot brakes"] and n["Tower: flameout, high key"], "F10 Notfall wie Funkrad (R297) fehlt") end
do local tr = false for _, m in ipairs(MENU) do if m.name == "Request zone transit" and m.arg.text == "Approach: request zone transit" then tr = true end end
  assert(tr, "F10 Allgemein: Request zone transit (N3) fehlt") end
do local g, tx = {}, {}   -- R390: F10 like the radio wheel – groups, wheel name for "Base, touch and go", answer entries, at most 9 per level
  for _, x in ipairs(GROUPS) do g[x.name] = true; assert(x.n <= 9, "F10 Ebene mit mehr als 9 Einträgen: " .. x.name .. " " .. x.n) end
  for _, m in ipairs(MENU) do if type(m.arg) == "table" and m.arg.text then tx[m.arg.text] = m.name end end
  assert(g["Closed / SFO"] and g["AWACS / Tanker"] and g["AWACS"] and g["Tanker"] and g["Verkehr"] and g["Gefecht"] and g["Mehr"] and g["Range"] and g["Carrier"],
    "F10 Untergruppen (R390)")
  assert(tx["Tower: base, gear down, touch and go"] == "Base, touch and go" and not tx["Tower: final, gear down, touch and go"]
    and tx["Approach: traffic in sight"] and tx["Approach: report airspeed"] and tx["Tanker: visual"] and tx["Tanker: refuel complete"]
    and tx["Range: in hot"] == "In hot" and tx["Carrier: ball"] and tx["AWACS: checking in"] and tx["AWACS: bingo, RTB"] and tx["Tower: request S F O"] and tx["Approach: cancel approach"], "F10 Antwort-Einträge (R390)") end
do local fh = realOpen(tmp .. "\\DcsAtc-Terrain-Caucasus-map.txt.tmp", "r"); assert(fh and fh:read("*l"):find("^Map;"), "Kartenraster (V15) fehlt"); fh:close() end
for _, id in ipairs({ "23", "26" }) do local fh = realOpen(tmp .. "\\DcsAtc-Terrain-Caucasus-" .. id .. ".txt.tmp", "r"); assert(fh, "Platzraster " .. id .. " fehlt"); local s = fh:read("*a") fh:close()
  assert(select(2, s:gsub("\n", "")) == 62 and select(2, s:match("\n([^\n]*)"):gsub(",", "")) == 60, "Platzraster " .. id .. " nicht 61x61") end   -- one after another (queue)
-- AI radio: AI Viper (flight frequency 141) fires AIM-120 at Bandit1 -> X;shot, Pitbull under 10 NM, Trashed without a hit; kill, ejection
local vip = plane("Viper", 60000, 0); vip.air = true; vip.group = "Viper"
vip.ammo = { { count = 4, desc = { category = 1, missileCategory = 1, guidance = 3 } }, { count = 2, desc = { category = 1, missileCategory = 1, guidance = 2 } }, { count = 500, desc = { category = 0 } } }
run(T + 1); assert(stateFile():find("T;Viper;Viper;FA%-18C_hornet;[^\n]*;141%.000;4/0/2/500/0;a;4899;CAP\n"), "T-Zeile ohne Flight-Frequenz/Munition/Innentank (LK15)/Auftrag (R376): " .. stateFile())
local wpn = { id_ = 77, p = vec(90000, 0), alive = true }
function wpn:getDesc() return { category = 1, missileCategory = 1, guidance = 3 } end
function wpn:getTypeName() return "AIM_120C" end
function wpn:getTarget() return low end
function wpn:isExist() return self.alive end
function wpn:getPoint() return self.p end
local function fire(e) for _, h in ipairs(handlers) do h:onEvent(e) end end
fire({ id = world.event.S_EVENT_SHOT, initiator = vip, weapon = wpn })
assert(has("X;shot;Viper;Viper;2;AIM_120C;missile;aam;radar_active;Bandit1;;60000;0"), "X;shot fehlt: " .. table.concat(events, " "))
run(T + 4); assert(not table.concat(events, " "):find("pitbull"), "Pitbull zu früh")
wpn.p = vec(60000, 0); run(T + 1); assert(has("X;pitbull;Viper;Bandit1"), "Pitbull mit Ziel (R379) fehlt")
wpn.alive = false; run(T + 3); assert(has("X;trashed;Viper;Bandit1"), "Trashed fehlt")
-- R382: gun burst, 40 hits in 2 s without damage change -> at most 3 X;hit
for _ = 1, 40 do T = T + 0.05; fire({ id = world.event.S_EVENT_HIT, initiator = vip, target = low, weapon = wpn }) end
local nHit = 0; for _, x in ipairs(events) do if x:find("^X;hit;Viper;") then nHit = nHit + 1 end end
assert(nHit >= 1 and nHit <= 3, "Treffer-Drossel (R382): " .. nHit .. " X;hit in 2 s")
fire({ id = world.event.S_EVENT_KILL, initiator = vip, target = low }); assert(has("X;kill;Viper;Viper;Bandit1;;FA-18C_hornet;1"), "X;kill fehlt")
landX = 30000   -- R361: Viper (x 60000) over land, P1 (x 0) over water
fire({ id = world.event.S_EVENT_EJECTION, initiator = vip }); assert(has("X;eject;Viper;Viper;60000;0;0"), "X;eject fehlt")
fire({ id = world.event.S_EVENT_PILOT_DEAD, initiator = low }); fire({ id = world.event.S_EVENT_PILOT_DEAD, initiator = vip })
assert(has("X;pilotdead;Viper") and not has("X;pilotdead;Bandit1"), "X;pilotdead nur nach Ausstieg (R361) fehlt")
p1.group = "P1grp"; p1.air = true
fire({ id = world.event.S_EVENT_EJECTION, initiator = p1 }); assert(has("X;eject;P1;P1grp;0;0;1"), "X;eject für Spieler (N45) über Wasser (R361) fehlt: " .. table.concat(events, " "))
landX = nil
-- KF6/LK14: red SAM radar tracks the AI Viper -> X;spike (once, with type and location), ends -> X;naked; radar on the player -> nothing
local sr = unit("SR1", 70000, 1000, nil, 1)
function sr:getTypeName() return "Kub 1S91 str" end
sr.lock = vip
function sr:getRadar() return self.lock ~= nil, self.lock end
local sr2 = unit("SR2", 0, 1000, nil, 1)
function sr2:getRadar() return true, p1 end
REDGROUND[#REDGROUND + 1] = { getUnits = function() return { sr, sr2 } end, getUnit = function() return sr end }
run(T + 3)
assert(has("X;spike;Viper;Kub 1S91 str;sam;70000;1000") and not ev():find("X;spike;P1", 1, true), "X;spike (KF6) fehlt: " .. ev())
local spikes0 = select(2, ev():gsub("X;spike;", ""))
run(T + 3); assert(select(2, ev():gsub("X;spike;", "")) == spikes0 and not ev():find("X;naked", 1, true), "X;spike wiederholt oder naked zu früh: " .. ev())
sr.lock = nil; run(T + 3); assert(has("X;naked;Viper"), "X;naked fehlt: " .. ev())
wpn.getTarget = function() return nil end   -- Player bomb without AI target: no AI radio
fire({ id = world.event.S_EVENT_SHOT, initiator = p1, weapon = wpn }); assert(not table.concat(events, " "):find("X;shot;P1"), "Spieler ohne KI-Ziel gemeldet")
-- R6: player shuts down the engine -> C;P1;stopped (parked), AI not
fire({ id = world.event.S_EVENT_ENGINE_SHUTDOWN, initiator = p1 }); fire({ id = world.event.S_EVENT_ENGINE_SHUTDOWN, initiator = vip })
assert(has("C;P1;stopped") and not has("C;Viper;stopped"), "C;stopped: " .. table.concat(events, " "))
-- .out display (A134): "gid|sek|text" with display duration from the app, old "gid|text" still readable (at least 20 s), text with "|" and digits stays whole
OUT = {}
files["9-30.out"] = "7|6|Viper (141.0): Fox three"
files["9-31.out"] = "0|Tower (265.0): alter Text"
files["9-32.out"] = "0|9|ATC: 5|3 Flugzeuge"
files["9-34-0-c1.out"] = "0|8|Guard (243.0): Mayday, mayday, mayday"   -- R356: to all of the red coalition only
run(T + 1)
assert(#OUT == 4, "nicht alle .out gelesen: " .. #OUT)
assert(OUT[4][1] == "c1" and OUT[4][2] == "Guard (243.0): Mayday, mayday, mayday" and OUT[4][3] == 8, ".out Koalition: " .. tostring(OUT[4][1]))
assert(OUT[1][1] == 7 and OUT[1][2] == "Viper (141.0): Fox three" and OUT[1][3] == 6, ".out neu: " .. tostring(OUT[1][2]) .. " / " .. tostring(OUT[1][3]))
assert(OUT[2][1] == 0 and OUT[2][2] == "Tower (265.0): alter Text" and OUT[2][3] == 20, ".out alt: " .. tostring(OUT[2][2]) .. " / " .. tostring(OUT[2][3]))
assert(OUT[3][1] == 0 and OUT[3][2] == "ATC: 5|3 Flugzeuge" and OUT[3][3] == 9, ".out neu mit |: " .. tostring(OUT[3][2]))
-- VR radio wheel: wheel text to the player's unit (clearView), closing clears immediately, unknown unit nothing
OUT = {}
files["9-33.cmd"] = "WHEEL;P1;DCS-ATC – Tower\n> 1 Ready for departure; 2 Initial\n   0 Vorschlag: Tower: ready"
files["9-34.cmd"] = "WHEEL;P1;"
files["9-35.cmd"] = "WHEEL;P9;x"
run(T + 1)
assert(#OUT == 2 and OUT[1][1] == "u1" and OUT[1][2] == "DCS-ATC – Tower\n> 1 Ready for departure; 2 Initial\n   0 Vorschlag: Tower: ready" and OUT[1][3] >= 60 and OUT[1][4] == true,
       "Radtext: " .. #OUT .. " " .. tostring(OUT[1] and OUT[1][2]))
assert(OUT[2][1] == "u1" and OUT[2][2] == "" and OUT[2][3] == 1 and OUT[2][4] == true, "Rad zu leert nicht: " .. tostring(OUT[2][2]) .. " / " .. tostring(OUT[2][3]))
function p1:getFuel() return 0.4 end   -- only here, the P line above expects the default value 1.00
fire({ id = world.event.S_EVENT_REFUELING, initiator = p1 }); assert(has("F;P1;start;0.400;4899"), "F;start mit Innentank (A124) fehlt: " .. table.concat(events, " "))
fire({ id = world.event.S_EVENT_REFUELING_STOP, initiator = p1 }); assert(has("F;P1;stop;0.400;4899"), "F;stop mit Innentank fehlt")
-- A17: range zone "Range Alpha" -> Z line; player bomb 12 m before the red tank (approach to the north: 6 o'clock) -> R with flight time; gun hit/kill in the zone -> H/K
assert(stateFile():find("Z;20000;0;3000", 1, true), "Z-Zeile (Range) fehlt: " .. stateFile())
local bomb = { p = vec(19988, 0), alive = true }
function bomb:getDesc() return { category = 3 } end
function bomb:getTypeName() return "weapons.bombs.Mk_82" end
function bomb:isExist() return self.alive end
function bomb:getPoint() return self.p end
function bomb:getVelocity() return vec(0, 0) end
fire({ id = world.event.S_EVENT_SHOT, initiator = p1, weapon = bomb })
run(T + 1); bomb.alive = false; run(T + 1)
assert(table.concat(events, " "):find("R;P1;weapons.bombs.Mk_82;12;6;T%-72B;1%.%d"), "R-Ereignis (Einschlag) fehlt: " .. table.concat(events, " "))
-- R369: static objects and ships count as range targets, not only ground groups
for _, c in ipairs({ { 20000, 1980, "Warehouse" }, { 20000, -2020, "Speedboat" } }) do
  local b2 = { p = vec(c[1], c[2]), alive = true }
  function b2:getDesc() return { category = 3 } end
  function b2:getTypeName() return "weapons.bombs.Mk_82" end
  function b2:isExist() return self.alive end
  function b2:getPoint() return self.p end
  function b2:getVelocity() return vec(0, 0) end
  fire({ id = world.event.S_EVENT_SHOT, initiator = p1, weapon = b2 })
  run(T + 1); b2.alive = false; run(T + 1)
  assert(table.concat(events, " "):find("R;P1;weapons.bombs.Mk_82;20;%d+;" .. c[3] .. ";1%.%d"), "R-Ereignis auf " .. c[3] .. " fehlt: " .. table.concat(events, " "))
end
local shell = { getDesc = function() return { category = 0 } end, getTypeName = function() return "shell" end }
fire({ id = world.event.S_EVENT_HIT, initiator = p1, target = tank, weapon = shell }); assert(has("H;P1;T-72B"), "H-Ereignis (Kanonentreffer) fehlt")
fire({ id = world.event.S_EVENT_KILL, initiator = p1, target = tank }); assert(has("K;P1;T-72B;1"), "K-Ereignis (Abschuss) fehlt")
-- R369: K carries the coalition of the target (the app drops own-side kills)
fire({ id = world.event.S_EVENT_KILL, initiator = p1, target = static1 }); assert(has("K;P1;Warehouse;1"), "K-Ereignis auf Static fehlt")
local own = unit("Own1", 20000, 500, nil, 2); function own:getTypeName() return "Truck" end
fire({ id = world.event.S_EVENT_KILL, initiator = p1, target = own }); assert(has("K;P1;Truck;2"), "K-Ereignis auf eigene Seite ohne Koalition")
-- R27: DCS LSO grade on trap -> W;Unit;Wire;grade (player only, only with wire); player under 2 NM at the carrier in the air -> state every 0.25 s instead of 0.5 s
fire({ id = world.event.S_EVENT_LANDING_QUALITY_MARK, initiator = p1, comment = "LSO: GRADE:OK  : WIRE# 3" })
fire({ id = world.event.S_EVENT_LANDING_QUALITY_MARK, initiator = vip, comment = "LSO: GRADE:OK  : WIRE# 2" })
fire({ id = world.event.S_EVENT_LANDING_QUALITY_MARK, initiator = p1, comment = "LSO: GRADE:B" })
assert(has("W;P1;3;LSO: GRADE:OK  : WIRE# 3") and not ev():find("W;Viper", 1, true) and not ev():find("GRADE:B", 1, true), "W-Ereignis (R27): " .. ev())
local writes, ro = 0, io.open
io.open = function(path, mode) if mode == "w" and path:find("DcsAtc%-State%.txt%.tmp$") then writes = writes + 1 end return ro(path, mode) end
local function rate(x, z) p1.p = vec(x, z); run(T + 1); writes = 0; run(T + 2); return writes end
p1.air = true
local near, far = rate(1000, 3000), rate(1000, 2000 + 5 * 1852)
p1.air = false; local deck = rate(1000, 2100)
io.open = ro
assert(near >= 7 and far <= 4 and deck <= 4, "Zustand am Träger (R27): nah " .. near .. ", weit " .. far .. ", Deck " .. deck)
do   -- R390: trigger zone "Range…" in the mission -> F10 group Range (like the radio wheel), without carrier no group Carrier
  local blue, zones = env.mission.coalition.blue.country[1], env.mission.triggers
  env.mission.triggers, blue.ship = nil, nil
  for i = #queue, 1, -1 do queue[i] = nil end
  for i = #handlers, 1, -1 do handlers[i] = nil end
  DCSATC, DCSATC_SPAWNTEST, DCSATC_OPTIONS, MENU, GROUPS = nil, nil, { crew = true }, {}, {}
  dofile(arg[1])
  run(T + 3)
  local g = {}
  for _, x in ipairs(GROUPS) do g[x.name] = true end
  assert(g["Tower"] and not g["Range"] and not g["Carrier"], "F10 ohne Range-Zone und Träger (R390)")
  env.mission.triggers = zones
end
-- Modules (installer/settings): DcsAtcModules.txt "atc" -> F10 without AWACS/Tanker entries and without the ground crew switch, crew off despite the option
do
  local io0 = io.open
  io.open = function(p, m) if p:find("DcsAtcModules%.txt$") then return { read = function() return "core,atc" end, close = function() end } end return io0(p, m) end
  lfs.writedir = function() return tmp .. "\\" end
  for i = #queue, 1, -1 do queue[i] = nil end
  for i = #handlers, 1, -1 do handlers[i] = nil end
  DCSATC, DCSATC_SPAWNTEST, DCSATC_OPTIONS, MENU = nil, nil, { crew = true }, {}
  dofile(arg[1])
  run(T + 3)
  io.open, lfs.writedir = io0, nil
  local n = {} for _, m in ipairs(MENU) do n[#n + 1] = m.name end
  local s = "|" .. table.concat(n, "|") .. "|"
  assert(s:find("|Request taxi|", 1, true) and s:find("|Radio check|", 1, true) and s:find("|Spritmangel|", 1, true) and not s:find("AWACS", 1, true)
         and not s:find("Tanker", 1, true) and not s:find("|Picture|", 1, true) and not s:find("|Visual|", 1, true) and not s:find("Bodenpersonal", 1, true) and DCSATC.opt.crew == false, "Module F10: " .. s)
end
-- Hook: file paths with a real backslash (Lua 5.1: '\D' becomes 'D' -> %TMP%DcsAtc-..., autostart/session/DCS folder never arrived)
local opened = {}
io.open = function(p) opened[#opened + 1] = p end
lfs.writedir, lfs.currentdir, package.loaded.lfs = function() return "W\\" end, function() return "C:\\DCS" end, lfs
local hookCb
DCS = { setUserCallbacks = function(c) hookCb = c end, isMultiplayer = function() return false end, isServer = function() return true end }
net = { log = function() end, get_my_player_id = function() return 1 end, get_player_info = function() return "Me" end }
dofile((arg[1]:gsub("DcsAtc[\\/]DcsAtcMission%.lua$", "Hooks\\DcsAtcHook.lua")))
hookCb.onSimulationStart()
assert(table.concat(opened, "|") == tmp .. "\\DcsAtc-DcsDir.txt|" .. tmp .. "\\DcsAtc-WriteDir.txt|" .. tmp .. "\\DcsAtc-Session.txt|W\\Scripts\\DcsAtc\\DcsAtcPath.txt", "Hook-Pfade: " .. table.concat(opened, " "))
print("missiontest OK: " .. table.concat(events, " ") .. " | KI: " .. table.concat(ai, ", "))
