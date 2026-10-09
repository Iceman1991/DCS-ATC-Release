-- Offline test of ground crew (KutaisiTraffic.lua) with a rebuilt DCS/MOOSE API. luae.exe crewtest.lua <path>
local T, sched, handlers, groups, events, destroyed = 0, {}, {}, {}, {}, {}
local function vec(x, z) return { x = x, y = 0, z = z } end
env = { info = function() end, mission = { coalition = {} } }
timer = { getTime = function() return T end, scheduleFunction = function(f, a, t) sched[#sched + 1] = { f = f, t = t } end }
world = { event = { S_EVENT_BIRTH = 15, S_EVENT_LAND = 4, S_EVENT_CRASH = 5, S_EVENT_DEAD = 8, S_EVENT_UNIT_LOST = 30 },
          addEventHandler = function(h) handlers[#handlers + 1] = h end, VolumeType = { SPHERE = 1 }, getAirbases = function() return {} end }
missionCommands = { addSubMenu = function() return {} end, addCommand = function() end }
AI = { Option = { Ground = { id = { ROE = 0 }, val = { ROE = { WEAPON_HOLD = 4 } } } } }
AIRBASE = { FindByName = function() return { GetID = function() return 26 end, SetParkingSpotBlacklist = function() end } end }
Airbase = { getByName = function() return { getPoint = function() return vec(0, 0) end,
  getParking = function() return { { vTerminalPos = vec(0, 0) }, { vTerminalPos = vec(0, 50) }, { vTerminalPos = vec(0, 200) } } end } end }
DCSATC = { ai = {}, evt = function(l) events[#events + 1] = l end }

local planes = {}
local function plane(name, x, z, player)
  local u = { name = name, p = vec(x, z), v = vec(0, 0), air = false }
  function u:getName() return self.name end
  function u:getPoint() return self.p end
  function u:getPosition() return { p = self.p, x = vec(1, 0), z = vec(0, 1) } end
  function u:getVelocity() return self.v end
  function u:inAir() return self.air end
  function u:getPlayerName() return player end
  function u:getDesc() return { box = { max = { x = 8, z = 6 }, min = { x = -9 } } } end
  function u:getCountry() return 2 end
  function u:getLife() return 1 end
  function u:getLife0() return 1 end
  planes[#planes + 1] = u
  return u
end
coalition = {
  getGroups = function() local us = {} for _, u in ipairs(planes) do us[#us + 1] = u end return { { getUnits = function() return us end } } end,
  addGroup = function(_, _, d)
    local g = { d = d, task = nil }
    function g:getController() return { setOption = function() end, setTask = function(_, t) g.task = t end } end
    function g:getUnit(i) return { getPoint = function() return vec(d.units[i].x, d.units[i].y) end } end
    function g:getUnits() return { g:getUnit(1) } end
    function g:destroy() groups[d.name] = nil; destroyed[#destroyed + 1] = d.name end
    groups[d.name] = g
    return g
  end,
}
Group = { Category = { AIRPLANE = 0, GROUND = 2 }, getByName = function(n) return groups[n] end }

dofile(arg[1])
local tick
for _, s in ipairs(sched) do if s.t == 5 then tick = s.f end end
assert(tick, "tickCrew nicht gefunden")
local function run(to) while T < to do T = T + 2; tick() end end
local function count() local n = 0 for _ in pairs(groups) do n = n + 1 end return n end
local function has(e) for _, x in ipairs(events) do if x == e then return true end end end

local p1 = plane("P1", 0, 0, "Player1")
run(6);  assert(count() == 0, "zu früh: Personal vor 10 s Stillstand")
run(14); assert(count() == 1 and has("C;P1;crew"), "Crew P1 fehlt")
local soldiers = groups["GC 1"].d.units
assert(#soldiers == 2 and soldiers[1].type == "Soldier M4", "Soldaten")
assert(soldiers[1].x > 8, "Crew Chief steht nicht vor der Nase")

-- P2 lands, taxis to the parking spot at z=50
local p2 = plane("P2", 0, 50, "Wingman")
for _, h in ipairs(handlers) do h:onEvent({ id = world.event.S_EVENT_LAND, initiator = p2 }) end
run(26); assert(has("C;P2;back"), "Welcome back fehlt")
run(50); assert(has("C;P2;fuel") and count() == 4, "Lkw fehlen: " .. count())
local fuel = groups["GC 3"].d
assert(fuel.units[1].type == "M978 HEMTT Tanker" and #fuel.route.points == 2, "Tankwagen-Route")
assert(math.abs(fuel.units[1].x) > 1 or math.abs(fuel.units[1].y - 50) > 79, "Lkw startet am Flugzeug statt am Depot")

-- P1 starts rolling
p1.v = vec(5, 0)
run(52); assert(groups["GC 1"].task, "Soldaten treten nicht zurück")
run(96); assert(not groups["GC 1"], "Soldaten P1 nicht weg")

run(300); assert(has("C;P2;done"), "done fehlt")
run(400); assert(not groups["GC 3"] and not groups["GC 4"] and groups["GC 2"], "Lkw nicht weg / Crew P2 fehlt")

-- P2 despawns
table.remove(planes, #planes)
run(404); assert(count() == 0, "Personal bleibt nach Despawn")
print("crewtest OK: " .. table.concat(events, " "))
