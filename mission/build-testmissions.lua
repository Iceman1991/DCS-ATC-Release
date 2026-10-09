-- Create, pack and check DCS-ATC test missions (Caucasus).
-- "C:\Program Files\Eagle Dynamics\DCS World\bin\luae.exe" mission\build-testmissions.lua [template.miz] [target folder]
-- Template (read only): Saved Games\DCS\Missions\Training.miz (mission, options, warehouses, theatre, mapResource).
-- Target: Saved Games\DCS\Missions\DCS-ATC-Tests\ (only the four own files are overwritten).
-- Pack/unpack with Windows tar (bsdtar, --format zip), then unpack each .miz again and check:
-- Lua syntax, groups/units, types, start spots, carrier linkage, frequencies and what DCS-ATC
-- (mod\Scripts\DcsAtc\DcsAtcMission.lua) reads from it (K/F lines, carrier, AI with carrier landing).

local SG = os.getenv("USERPROFILE") .. "\\Saved Games\\DCS\\Missions"
local TEMPLATE = arg[1] or (SG .. "\\Training.miz")
local OUTDIR = arg[2] or (SG .. "\\DCS-ATC-Tests")
local TMP = os.getenv("TEMP") .. "\\dcsatc-testmis"
local TAR = os.getenv("SystemRoot") .. "\\System32\\tar.exe"   -- bsdtar: can zip (Git tar cannot)

local NM, FT, KT = 1852, 0.3048, 0.514444

local function readFile(p) local f = assert(io.open(p, "rb")) local s = f:read("*a") f:close() return s end
local function writeFile(p, s) local f = assert(io.open(p, "wb")) f:write(s) f:close() end
local function sh(cmd)   -- via a .cmd file: os.execute of luae fails on long command lines (> ~255 characters, returns nil)
  local bat = os.getenv("TEMP") .. "\\dcsatc-testmis.cmd"
  writeFile(bat, "@" .. cmd .. "\r\n@exit /b %errorlevel%\r\n")
  local ok = os.execute('"' .. bat .. '"')
  assert(ok == 0 or ok == true, "Befehl fehlgeschlagen (" .. tostring(ok) .. "): " .. cmd)
end
local function loadTable(text, name) local env = {} local fn = assert(loadstring(text)) setfenv(fn, env) fn() return assert(env[name], name .. " fehlt") end
local function copy(t) if type(t) ~= "table" then return t end local r = {} for k, v in pairs(t) do r[k] = copy(v) end return r end

---------------------------------------------------------------------------------------------------
-- Serialize like the mission editor ("-- end of [...]"), keys sorted
local function num(v)
  if v == math.floor(v) and math.abs(v) < 1e15 then return string.format("%.0f", v) end
  return string.format("%.14g", v)
end
local function key(k) return type(k) == "number" and ("[" .. num(k) .. "]") or string.format("[%q]", k) end
local function ser(t, ind, out)
  local ks = {}
  for k in pairs(t) do ks[#ks + 1] = k end
  table.sort(ks, function(a, b)
    if type(a) ~= type(b) then return type(a) == "number" end
    return a < b
  end)
  for _, k in ipairs(ks) do
    local v = t[k]
    if type(v) == "table" then
      if next(v) == nil then out[#out + 1] = ind .. key(k) .. " = {},"
      else
        out[#out + 1] = ind .. key(k) .. " = "
        out[#out + 1] = ind .. "{"
        ser(v, ind .. "\t", out)
        out[#out + 1] = ind .. "}, -- end of " .. key(k)
      end
    elseif type(v) == "string" then out[#out + 1] = ind .. key(k) .. " = " .. string.format("%q", v) .. ","
    elseif type(v) == "number" then out[#out + 1] = ind .. key(k) .. " = " .. num(v) .. ","
    else out[#out + 1] = ind .. key(k) .. " = " .. tostring(v) .. "," end
  end
end
local function serialize(name, t)
  local out = { name .. " = ", "{" }
  ser(t, "\t", out)
  out[#out + 1] = "} -- end of " .. name
  return table.concat(out, "\n") .. "\n"
end

---------------------------------------------------------------------------------------------------
-- Read template (unpack into TMP, the .miz itself stays untouched)
sh('mkdir "' .. TMP .. '\\tpl" 2>nul & exit /b 0')
sh(TAR .. ' -xf "' .. TEMPLATE .. '" -C "' .. TMP .. '\\tpl"')
local TPL = {
  mission = loadTable(readFile(TMP .. "\\tpl\\mission"), "mission"),
  options = readFile(TMP .. "\\tpl\\options"),
  warehouses = loadTable(readFile(TMP .. "\\tpl\\warehouses"), "warehouses"),
  theatre = readFile(TMP .. "\\tpl\\theatre"),
}
assert(TPL.mission.theatre == "Caucasus", "Vorlage ist nicht Kaukasus")
for _, ad in ipairs({ 22, 23, 24, 25 }) do   -- Batumi, Senaki, Kobuleti, Kutaisi blue (rearm/refuel, DCS airfields of the side)
  if TPL.warehouses.airports[ad] then TPL.warehouses.airports[ad].coalition = "BLUE" end
end

---------------------------------------------------------------------------------------------------
-- Building blocks
local function off(p, brg, nm)   -- Point at bearing brg (degrees, true) and nm NM from p (x = north, y = east)
  local a = math.rad(brg)
  return { x = p.x + math.cos(a) * nm * NM, y = p.y + math.sin(a) * nm * NM }
end
local function hdgTo(a, b) local h = math.atan2(b.y - a.y, b.x - a.x) if h < 0 then h = h + 2 * math.pi end return h end
local function tacanMHz(ch) return 1087 + ch end   -- Mode X like the mission editor (74X = 1161 MHz, 51X = 1138 MHz)

local T = {}   -- Waypoint tasks
function T.engage(types, k) return { id = "EngageTargets", key = k, auto = true, params = { targetTypes = types, priority = 0 } } end
function T.option(name, value) return { id = "WrappedAction", params = { action = { id = "Option", params = { name = name, value = value } } } } end
function T.cmd(id, params) return { id = "WrappedAction", params = { action = { id = id, params = params or {} } } } end
function T.roeFree() return T.option(0, 0) end   -- ROE: weapon free
function T.orbit(alt, spd, pattern) return { id = "Orbit", params = { altitude = alt, pattern = pattern or "Race-Track", speed = spd, speedEdited = true } } end
function T.hold(alt, spd, sec)   -- Circle over the waypoint, then continue (marshal stack of the AI)
  return { id = "ControlledTask", params = { task = { id = "Orbit", params = { altitude = alt, pattern = "Circle", speed = spd, speedEdited = true } }, stopCondition = { duration = sec } } }
end
function T.tacanAA(ch, ident, uid)
  return T.cmd("ActivateBeacon", { type = 4, AA = true, system = 4, callsign = ident, channel = ch, modeChannel = "X", bearing = true, frequency = tacanMHz(ch) * 1e6, unitId = uid })
end
function T.tacanShip(ch, ident, uid)
  return T.cmd("ActivateBeacon", { type = 4, AA = false, system = 3, callsign = ident, channel = ch, modeChannel = "X", bearing = true, frequency = tacanMHz(ch) * 1e6, unitId = uid })
end
function T.icls(ch, uid) return T.cmd("ActivateICLS", { type = 131584, channel = ch, unitId = uid }) end

local function combo(list)
  local tasks = {}
  for i, t in ipairs(list or {}) do
    local c = copy(t)
    c.number, c.enabled = i, true
    if c.auto == nil then c.auto = false end
    tasks[i] = c
  end
  return { id = "ComboTask", params = { tasks = tasks } }
end
local function wp(p, alt, spd, o)   -- Waypoint; o: type/action/tasks/airdromeId/link
  o = o or {}
  local w = { x = p.x, y = p.y, alt = alt, alt_type = "BARO", speed = spd, speed_locked = true, ETA = 0, ETA_locked = false,
              type = o.type or "Turning Point", action = o.action or "Turning Point", formation_template = "", name = "", task = combo(o.tasks) }
  if o.airdromeId then w.airdromeId = o.airdromeId end
  if o.link then w.linkUnit, w.helipadId = o.link, o.link end
  return w
end
local START = { cold = { "TakeOffParking", "From Parking Area" }, hot = { "TakeOffParkingHot", "From Parking Area Hot" } }

-- Kutaisi (airdromeId 25): parking spots from Kaukasus_Trainings_Range_v2 (used there in game), airfield elevation 45 m
local KUTAISI = { id = 25, x = -284860, y = 683840, elev = 45, stands = {
  ["22"] = { park = "8", x = -284717.09375, y = 682696.6875, hdg = 4.0909302126926 },
  ["23"] = { park = "35", x = -284890.5625, y = 682746.125, hdg = 4.09712563484 },
  ["24"] = { park = "9", x = -284684, y = 682812.125, hdg = 4.0923913514974 },
  ["25"] = { park = "36", x = -284857.46875, y = 682861.375, hdg = 4.0985605457317 },
} }
local SENAKI = { x = -281700, y = 647800 }
local KOBULETI = { id = 24, x = -317900, y = 636600 }

-- Armament (CLSIDs from ED missions or MissionEditor\data\scripts\UnitPayloads)
local AIM9M, AIM120C, AIM7M_H, AIM120_HF, FPU8A = "{6CEB49FC-DED8-4DED-B053-E1F033FF72D3}", "{40EF17B7-F508-45de-8566-6FFECC0C1AB8}", "{LAU-115 - AIM-7M}", "{C8E06185-7CD6-4C90-959F-044679E90751}", "{FPU_8A_FUEL_TANK}"
local function pyl(t) local r = {} for n, c in pairs(t) do r[n] = { CLSID = c } end return r end
local GBU12 = "{DB769D48-67D7-42ED-A2BE-108D566C8B1E}"
local LOAD = {
  hornetAA = { pylons = pyl({ [1] = AIM9M, [2] = AIM7M_H, [3] = AIM7M_H, [4] = AIM120_HF, [5] = FPU8A, [6] = AIM120_HF, [7] = AIM7M_H, [8] = AIM7M_H, [9] = AIM9M }), chaff = 60, flare = 30, gun = 100 },
  hornetLight = { pylons = pyl({ [1] = AIM9M, [9] = AIM9M }), chaff = 60, flare = 30, gun = 100 },   -- Carrier: below the landing weight
  viperAA = { pylons = pyl({ [1] = AIM120C, [2] = AIM120C, [3] = "{5CE2FF2A-645A-4197-B48D-8720AC69394F}", [4] = "{F376DBEE-4CAE-41BA-ADD9-B2910AC95DEC}",
                             [5] = "{8A0BE8AE-58D4-4572-9263-3144C0D06364}", [6] = "{F376DBEE-4CAE-41BA-ADD9-B2910AC95DEC}", [7] = "{5CE2FF2A-645A-4197-B48D-8720AC69394F}", [8] = AIM120C, [9] = AIM120C }), chaff = 60, flare = 60, gun = 100 },
  viperClean = { pylons = pyl({ [1] = AIM120C, [2] = AIM9M, [8] = AIM9M, [9] = AIM120C }), chaff = 60, flare = 60, gun = 100 },   -- without drop tanks (little fuel)
  eagle = { pylons = pyl({ [1] = AIM9M, [2] = "{E1F29B21-F291-4589-9FD8-3272EEC69506}", [3] = AIM9M, [9] = AIM9M, [10] = "{E1F29B21-F291-4589-9FD8-3272EEC69506}", [11] = AIM9M }), chaff = 120, flare = 60, gun = 100 },
  fulcrum = { pylons = pyl({ [1] = "{FBC29BFE-3D24-4C64-B81D-941239D12249}", [2] = "{B4C01D60-A8A3-4237-BD72-CA7655BC0FE9}", [3] = "{9B25D316-0434-4954-868F-D51DB1A38DF0}",
                             [4] = "{2BEC576B-CDF5-4B7F-961F-B0FA4312B841}", [5] = "{9B25D316-0434-4954-868F-D51DB1A38DF0}", [6] = "{B4C01D60-A8A3-4237-BD72-CA7655BC0FE9}", [7] = "{FBC29BFE-3D24-4C64-B81D-941239D12249}" }), chaff = 30, flare = 30, gun = 100 },
  flanker = { pylons = pyl({ [1] = "{FBC29BFE-3D24-4C64-B81D-941239D12249}", [2] = "{FBC29BFE-3D24-4C64-B81D-941239D12249}", [3] = "{B79C379A-9E87-4E50-A1EE-7F7E29C2E87A}",
                             [4] = "{E8069896-8435-4B90-95C0-01A03AE6E400}", [5] = "{E8069896-8435-4B90-95C0-01A03AE6E400}", [6] = "{E8069896-8435-4B90-95C0-01A03AE6E400}",
                             [7] = "{E8069896-8435-4B90-95C0-01A03AE6E400}", [8] = "{B79C379A-9E87-4E50-A1EE-7F7E29C2E87A}", [9] = "{FBC29BFE-3D24-4C64-B81D-941239D12249}", [10] = "{FBC29BFE-3D24-4C64-B81D-941239D12249}" }), chaff = 96, flare = 96, gun = 100 },
  frogfoot = { pylons = pyl({ [1] = "{682A481F-0CB5-4693-A382-D00DD4A156D7}", [2] = "{FC56DF80-9B09-44C5-8976-DCFAFF219062}", [3] = "{3C612111-C7AD-476E-8A8E-2485812F4E5C}",
                              [4] = "{3C612111-C7AD-476E-8A8E-2485812F4E5C}", [5] = "{E92CBFE5-C153-11d8-9897-000476191836}", [6] = "{E92CBFE5-C153-11d8-9897-000476191836}",
                              [7] = "{3C612111-C7AD-476E-8A8E-2485812F4E5C}", [8] = "{3C612111-C7AD-476E-8A8E-2485812F4E5C}", [9] = "{FC56DF80-9B09-44C5-8976-DCFAFF219062}", [10] = "{682A481F-0CB5-4693-A382-D00DD4A156D7}" }), chaff = 30, flare = 30, gun = 100 },
  foxhound = { pylons = pyl({ [1] = "{5F26DBC2-FB43-4153-92DE-6BBCE26CB0FF}", [2] = "{F1243568-8EF0-49D4-9CB5-4DA90D92BC1D}", [3] = "{F1243568-8EF0-49D4-9CB5-4DA90D92BC1D}",
                              [4] = "{F1243568-8EF0-49D4-9CB5-4DA90D92BC1D}", [5] = "{F1243568-8EF0-49D4-9CB5-4DA90D92BC1D}", [6] = "{5F26DBC2-FB43-4153-92DE-6BBCE26CB0FF}" }), chaff = 0, flare = 0, gun = 100 },
  none = { pylons = {}, chaff = 0, flare = 0, gun = 100 },
  hornetCAS = { pylons = pyl({ [1] = AIM9M, [2] = GBU12, [3] = GBU12, [5] = FPU8A, [7] = GBU12, [8] = GBU12, [9] = AIM9M }), chaff = 60, flare = 30, gun = 100 },   -- J: JTAC test, laser bombs on the JTAC spot
  viperCAS = { pylons = pyl({ [1] = AIM120C, [2] = AIM9M, [3] = GBU12, [4] = "{F376DBEE-4CAE-41BA-ADD9-B2910AC95DEC}", [6] = "{F376DBEE-4CAE-41BA-ADD9-B2910AC95DEC}", [7] = GBU12, [8] = AIM9M, [9] = AIM120C }), chaff = 60, flare = 60, gun = 100 },
}
local FUELMAX = { ["FA-18C_hornet"] = 4900, ["F-16C_50"] = 3249, ["F-15C"] = 6103, ["E-2C"] = 5624, ["E-3A"] = 65000, ["KC-135"] = 90700, ["KC135MPRS"] = 90700,
                  ["S-3B Tanker"] = 7813, ["MiG-29S"] = 3493, ["Su-27"] = 5590.18, ["Su-25"] = 2835, ["MiG-31"] = 15500, ["MQ-9 Reaper"] = 1300 }
local CS = { Enfield = 1, Springfield = 2, Uzi = 3, Colt = 4, Dodge = 5, Ford = 6, Chevy = 7, Pontiac = 8, Texaco = 1, Arco = 2, Shell = 3, Overlord = 1, Magic = 2 }

-- Radio presets of the players (channel 1..n, rest like the template)
local UHF_DEF = { 305, 264, 265, 256, 254, 250, 270, 257, 255, 262, 259, 268, 269, 260, 263, 261, 267, 251, 253, 266 }
local VHF_DEF = { 127, 135, 136, 127, 125, 121, 141, 128, 126, 133, 130, 139, 140, 131, 134, 132, 138, 122, 124, 137 }
local function radio(first, def)
  local ch, mo = {}, {}
  for i = 1, 20 do ch[i], mo[i] = first[i] or def[i], 0 end
  return { channels = ch, modulations = mo, channelsNames = {} }
end

---------------------------------------------------------------------------------------------------
-- Mission
local Mission = {}
Mission.__index = Mission
local function newMission(o)
  local m = setmetatable({ o = o, gid = 0, uid = 0, onboard = 100, red = {}, blue = {} }, Mission)
  return m
end
function Mission:cat(side, cat)
  local c = self[side]
  c[cat] = c[cat] or { group = {} }
  return c[cat].group
end
-- Aircraft group. g: name, task, freq, start, side, points(fn(uids) -> waypoints), units = { {type, name, cs, skill, load, fuel, p, alt, spd, hdg, parking, radio} }
function Mission:plane(g)
  self.gid = self.gid + 1
  local gid, side = self.gid, g.side or "blue"
  local units = {}
  for i, u in ipairs(g.units) do
    self.uid = self.uid + 1
    self.onboard = self.onboard + 1
    local pl = copy(LOAD[u.load or "none"])
    pl.fuel = u.fuel or FUELMAX[u.type]
    local un = { unitId = self.uid, type = u.type, name = u.name, skill = u.skill or g.skill or "High", x = u.p.x, y = u.p.y, alt = u.alt, alt_type = "BARO",
                 speed = u.spd, heading = u.hdg or 0, psi = -(u.hdg or 0), payload = pl, onboard_num = string.format("%03d", self.onboard), hardpoint_racks = true }
    if side == "blue" then
      local n = u.cs or g.cs
      un.callsign = { [1] = CS[n], [2] = g.csGroup or 1, [3] = i, name = n .. (g.csGroup or 1) .. i }
    else
      un.callsign = (g.csNum or 100) + i
    end
    if u.parking then un.parking, un.parking_id = u.parking.park, u.parking.id end
    if u.radio then un.Radio = u.radio end
    if un.skill == "Client" then un.AddPropAircraft = { HelmetMountedDevice = 1, OuterBoard = 0, InnerBoard = 0 } end
    units[i] = un
  end
  local pts = g.points(units)
  pts[1].ETA, pts[1].ETA_locked = g.start or 0, true
  local grp = { groupId = gid, name = g.name, task = g.task or "Nothing", route = { points = pts }, units = units, x = units[1].x, y = units[1].y,
                frequency = g.freq, modulation = 0, communication = true, start_time = g.start or 0, hidden = false, uncontrolled = false,
                uncontrollable = false, tasks = {}, radioSet = false }
  table.insert(self:cat(side, "plane"), grp)
  return grp
end
function Mission:ship(g)   -- Ship group; first unit = carrier
  self.gid = self.gid + 1
  local units = {}
  for i, u in ipairs(g.units) do
    self.uid = self.uid + 1
    units[i] = { unitId = self.uid, type = u.type, name = u.name, skill = "Excellent", x = u.p.x, y = u.p.y, heading = g.hdg, transportable = { randomTransportable = false } }
    if u.freq then units[i].frequency, units[i].modulation = u.freq * 1e6, 0 end
  end
  local grp = { groupId = self.gid, name = g.name, units = units, x = units[1].x, y = units[1].y, visible = false, hidden = false, start_time = 0, tasks = {},
                route = { points = g.points(units) } }
  grp.route.points[1].ETA_locked = true
  table.insert(self:cat("blue", "ship"), grp)
  return grp
end
function Mission:vehicles(g)
  self.gid = self.gid + 1
  local units = {}
  for i, u in ipairs(g.units) do
    self.uid = self.uid + 1
    units[i] = { unitId = self.uid, type = u.type, name = g.name .. " " .. i, skill = "Average", x = g.p.x + (i - 1) * 25, y = g.p.y + (i - 1) * 15, heading = g.hdg or 0, playerCanDrive = false }
  end
  local p = wp(g.p, 10, 0, { action = "Off Road" })
  p.ETA_locked = true
  table.insert(self:cat(g.side or "blue", "vehicle"), { groupId = self.gid, name = g.name, units = units, x = g.p.x, y = g.p.y, visible = false, hidden = false,
    start_time = 0, tasks = {}, task = "Ground Nothing", uncontrollable = false, route = { points = { p } } })
end

-- Player slot (client, one aircraft per group)
local function slot(m, s)
  local isHornet = s.type == "FA-18C_hornet"
  local rad = isHornet and { radio(s.uhf, UHF_DEF), radio(s.uhf, UHF_DEF) } or { radio(s.uhf, UHF_DEF), radio(s.vhf or {}, VHF_DEF) }
  local u = { type = s.type, name = s.cs .. " 1-1", cs = s.cs, skill = "Client", load = s.load, fuel = s.fuel, radio = rad }
  local points
  if s.deck then   -- on the carrier
    u.p, u.alt, u.spd, u.hdg, u.parking = s.deck.p, 0, 0, s.deck.hdg, { park = "1", id = "1" }
    points = function() local st = START[s.deck.state] return { wp(s.deck.p, 0, 0, { type = st[1], action = st[2], link = s.deck.uid }), wp(s.to, 150 * FT, 150, {}) } end
  elseif s.stand then   -- Kutaisi ramp
    local st = KUTAISI.stands[s.stand]
    u.p, u.alt, u.spd, u.hdg, u.parking = { x = st.x, y = st.y }, KUTAISI.elev, 0, st.hdg, { park = st.park, id = s.stand }
    points = function() local t = START[s.state or "cold"] return { wp(u.p, KUTAISI.elev, 0, { type = t[1], action = t[2], airdromeId = KUTAISI.id }), wp(s.to, 3000 * FT, 150, {}) } end
  else   -- in the air
    u.p, u.alt, u.spd, u.hdg = s.p, s.alt, s.spd, hdgTo(s.p, s.to)
    points = function() return { wp(s.p, s.alt, s.spd), wp(s.to, s.alt, s.spd) } end
  end
  return m:plane({ name = s.name, cs = s.cs, task = "CAP", freq = s.freq, units = { u }, points = points, skill = "Client" })
end

local function orbiter(m, o)   -- Tanker/AWACS on racetrack p1 -> p2
  return m:plane({ name = o.name, cs = o.cs, csGroup = o.csGroup, task = o.task, freq = o.freq, start = o.start, skill = "Excellent",
    units = { { type = o.type, name = o.unit, p = o.p1, alt = o.alt, spd = o.spd, hdg = hdgTo(o.p1, o.p2) } },
    points = function(us)
      local tasks = { o.task == "AWACS" and { id = "AWACS", auto = true, params = {} } or { id = "Tanker", auto = true, params = {} } }
      if o.tacan then tasks[#tasks + 1] = T.tacanAA(o.tacan, o.ident, us[1].unitId) end
      tasks[#tasks + 1] = T.cmd("SetUnlimitedFuel", { value = true })
      if o.protect then tasks[#tasks + 1] = T.cmd("SetImmortal", { value = true }) tasks[#tasks + 1] = T.cmd("SetInvisible", { value = true }) end
      tasks[#tasks + 1] = T.orbit(o.alt, o.spd)
      return { wp(o.p1, o.alt, o.spd, { tasks = tasks }), wp(o.p2, o.alt, o.spd) }
    end })
end

local function carrier(m, c)   -- CVN-74 Stennis with escort, course c.brc, TACAN 74X STN, ICLS 11, radio 127.5 AM
  return m:ship({ name = "CVN-74 Stennis", hdg = math.rad(c.brc), units = {
      { type = "Stennis", name = "CVN-74 Stennis", p = c.p, freq = 127.5 },
      { type = "TICONDEROG", name = "CG-60 Normandy", p = off(c.p, c.brc - 40, 1.2) },
      { type = "PERRY", name = "FFG-58 Samuel B. Roberts", p = off(c.p, c.brc + 140, 1.5) } },
    points = function(us)
      return { wp(c.p, 0, c.kt * KT, { tasks = { T.tacanShip(74, "STN", us[1].unitId), T.icls(11, us[1].unitId) } }), wp(off(c.p, c.brc, 80), 0, c.kt * KT) }
    end })
end

local function aiLand(m, a)   -- AI Hornet comes back and lands on the carrier (optionally circles over the marshal point)
  return m:plane({ name = a.name, cs = a.cs, task = "CAP", freq = 127.5, start = a.start, skill = "High",
    units = { { type = "FA-18C_hornet", name = a.cs .. " 1-1", p = a.p, alt = a.alt, spd = a.spd, hdg = hdgTo(a.p, a.cv.p), load = "hornetLight", fuel = 3000 } },
    points = function()
      local pts = { wp(a.p, a.alt, a.spd) }
      if a.hold then pts[#pts + 1] = wp(a.hold.p, a.alt, 130, { tasks = { T.hold(a.alt, 130, a.hold.sec) } }) end
      pts[#pts + 1] = wp(a.cv.p, 0, 70, { type = "Land", action = "Landing", link = a.cv.uid })
      return pts
    end })
end

local function fighters(m, f)   -- AI fighter: approach p1 -> p2, there racetrack to p3 (air combat via EngageTargets, ROE weapon free)
  local units = {}
  for i = 1, f.n do units[i] = { type = f.type, name = f.name .. " " .. i, p = off(f.p1, f.side == "red" and 90 or 0, (i - 1) * 0.5), alt = f.alt, spd = f.spd,
                                 hdg = hdgTo(f.p1, f.p2), load = f.load, skill = f.skill } end
  return m:plane({ name = f.name, side = f.side, cs = f.cs, csNum = f.csNum, task = f.task or "CAP", freq = f.freq, start = f.start, skill = f.skill, units = units,
    points = function()
      local first = { T.engage(f.targets or { "Air" }, f.task or "CAP"), T.roeFree() }
      local pts = { wp(f.p1, f.alt, f.spd, { tasks = first }) }
      if f.p3 then
        pts[2] = wp(f.p2, f.alt2 or f.alt, f.spd2 or f.spd, { tasks = { T.orbit(f.alt2 or f.alt, f.spd2 or f.spd, f.pattern) } })
        pts[3] = wp(f.p3, f.alt2 or f.alt, f.spd2 or f.spd)
      else
        pts[2] = wp(f.p2, f.alt2 or f.alt, f.spd2 or f.spd)
      end
      return pts
    end })
end

---------------------------------------------------------------------------------------------------
-- Write mission
local function weather(o)
  local w = copy(TPL.mission.weather)
  w.wind = { atGround = { speed = o.wind[2], dir = o.wind[1] }, at2000 = { speed = o.wind[3], dir = o.wind[1] + 4 }, at8000 = { speed = o.wind[4], dir = o.wind[1] + 9 } }   -- dir = where the wind blows to (mission file)
  w.clouds = o.clouds
  w.visibility = { distance = o.vis or 80000 }
  w.enable_fog, w.fog = false, { visibility = 0, thickness = 0 }
  w.name = o.wxName or "DCS-ATC Test"
  return w
end
function Mission:build()
  local o = self.o
  local ms = copy(TPL.mission)
  ms.date = { Year = 2024, Month = 6, Day = 15 }
  ms.start_time = o.start
  ms.weather = weather(o)
  ms.triggers = { zones = {} }
  ms.map = { centerX = o.center.x, centerY = o.center.y, zoom = 250000 }
  ms.coalition.blue.country = { { id = 2, name = "USA" } }
  for k, v in pairs(self.blue) do if type(v) == "table" and v.group then ms.coalition.blue.country[1][k] = v end end
  ms.coalition.red.country = {}
  if self.red.plane or self.red.vehicle then ms.coalition.red.country[1] = { id = 0, name = "Russia", plane = self.red.plane, vehicle = self.red.vehicle } end
  ms.maxDictId = 5
  ms.descriptionText, ms.descriptionRedTask, ms.descriptionBlueTask, ms.descriptionNeutralsTask, ms.sortie =
    "DictKey_descriptionText_1", "DictKey_descriptionRedTask_2", "DictKey_descriptionBlueTask_3", "DictKey_descriptionNeutralsTask_4", "DictKey_sortie_5"
  local dict = { DictKey_descriptionText_1 = o.brief, DictKey_descriptionRedTask_2 = o.redTask or "AI only. / Nur KI.",
                 DictKey_descriptionBlueTask_3 = o.blueTask, DictKey_descriptionNeutralsTask_4 = "", DictKey_sortie_5 = o.title }
  return ms, dict
end

---------------------------------------------------------------------------------------------------
-- Check (after packing, on the unpacked .miz)
local KNOWN = { ["FA-18C_hornet"] = 1, ["F-16C_50"] = 1, ["F-15C"] = 1, ["E-2C"] = 1, ["E-3A"] = 1, ["KC-135"] = 1, ["KC135MPRS"] = 1, ["S-3B Tanker"] = 1,
                ["MiG-29S"] = 1, ["Su-27"] = 1, ["Su-25"] = 1, ["MiG-31"] = 1, Stennis = 1, TICONDEROG = 1, PERRY = 1, ["M-113"] = 1, Hummer = 1,
                ["T-72B"] = 1, ["BTR-80"] = 1, ["ZSU-23-4 Shilka"] = 1, ["MQ-9 Reaper"] = 1 }
local FIELD = {}   -- DCS airfield frequencies Caucasus (whole MHz UHF 250-270, VHF 121-141) and DCS-ATC controllers
for f = 250, 270 do FIELD[f * 10] = true end
for f = 121, 141 do FIELD[f * 10] = true end
for _, f in ipairs({ 263.5, 264.5, 265.0, 266.5, 267.5 }) do FIELD[math.floor(f * 10 + 0.5)] = true end
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
local function check(ms, dict, file)
  local errs, info, ids, uids, names = {}, {}, {}, {}, {}
  local function err(s) errs[#errs + 1] = s end
  local function add(s) info[#info + 1] = "  " .. s end
  local shipUnits, cvUnits = {}, {}
  for _, side in ipairs({ "blue", "red" }) do
    for _, ctry in ipairs(ms.coalition[side].country) do
      for _, g in ipairs(ctry.ship and ctry.ship.group or {}) do
        for _, u in ipairs(g.units) do shipUnits[u.unitId] = u if u == g.units[1] and isCarrier(u.type) then cvUnits[u.unitId] = g end end
      end
    end
  end
  local clients, beacons = {}, {}
  for _, side in ipairs({ "blue", "red" }) do
    for _, ctry in ipairs(ms.coalition[side].country) do
      if (side == "blue") ~= (ctry.id == 2) then err("Land/Seite falsch: " .. ctry.name) end
      for _, cat in ipairs({ "plane", "ship", "vehicle" }) do
        for _, g in ipairs(ctry[cat] and ctry[cat].group or {}) do
          if ids[g.groupId] then err("groupId doppelt " .. g.groupId) end
          ids[g.groupId] = true
          if names["g" .. g.name] then err("Gruppenname doppelt " .. g.name) end
          names["g" .. g.name] = true
          local pts = g.route and g.route.points or {}
          if #pts < 1 or #(g.units or {}) < 1 then err(g.name .. ": route/units fehlen") end
          for _, u in ipairs(g.units) do
            if uids[u.unitId] then err("unitId doppelt " .. u.unitId) end
            uids[u.unitId] = true
            if names["u" .. u.name] then err("Einheitenname doppelt " .. u.name) end
            names["u" .. u.name] = true
            if not KNOWN[u.type] then err(g.name .. ": Typ unbekannt " .. tostring(u.type)) end
            if type(u.x) ~= "number" or type(u.y) ~= "number" then err(u.name .. ": Position fehlt") end
            if cat == "plane" and not (u.payload and u.payload.fuel and u.payload.fuel <= FUELMAX[u.type] + 0.01) then err(u.name .. ": Sprit fehlt/zu viel") end
            if u.skill == "Client" then clients[#clients + 1] = { g = g, u = u } end
          end
          if cat == "plane" then
            local p1 = pts[1]
            if p1.type == "TakeOffParking" or p1.type == "TakeOffParkingHot" or p1.type == "TakeOffGround" then
              if p1.airdromeId then
                for _, u in ipairs(g.units) do if not (u.parking and u.parking_id) then err(u.name .. ": Parkplatz fehlt") end end
              elseif p1.linkUnit then
                if p1.helipadId ~= p1.linkUnit or not cvUnits[p1.linkUnit] then err(g.name .. ": Trägerstart ohne gültige linkUnit/helipadId") end
              else err(g.name .. ": Bodenstart ohne Platz/Träger") end
            elseif p1.type ~= "Turning Point" then err(g.name .. ": erster Wegpunkt " .. tostring(p1.type)) end
            if (g.start_time or 0) ~= (p1.ETA or 0) then err(g.name .. ": start_time ~= ETA") end
            for i, p in ipairs(pts) do
              if p.type == "Land" and p.linkUnit and (p.helipadId ~= p.linkUnit or not cvUnits[p.linkUnit]) then err(g.name .. ": Landung ohne Träger") end
              if p.type == "Land" and not p.linkUnit and not p.airdromeId then err(g.name .. ": Landung ohne Ziel") end
              if findTask(p.task or {}, "Orbit") and findTask(p.task, "Orbit").pattern == "Race-Track" and not pts[i + 1] then err(g.name .. ": Rennbahn ohne zweiten Punkt") end
            end
            -- DCS-ATC (DcsAtcMission.lua): K line per ActivateBeacon, F line per AWACS with frequency
            local b = findTask(g.route, "ActivateBeacon")
            if b and b.channel then beacons[#beacons + 1] = string.format("K;%s;%d%s", g.name, b.channel, b.modeChannel or "X") end
            if g.task == "AWACS" then
              if not g.frequency then err(g.name .. ": AWACS ohne Frequenz") end
              beacons[#beacons + 1] = string.format("F;%s;%.3f", g.name, g.frequency)
            end
            if g.task == "Refueling" and not b then err(g.name .. ": Tanker ohne TACAN") end
            if (g.task == "AWACS" or g.task == "Refueling") and FIELD[math.floor(g.frequency * 10 + 0.5)] then err(g.name .. ": Frequenz auf Platz/Lotse " .. g.frequency) end
            local land
            for _, p in ipairs(pts) do if p.type == "Land" and p.linkUnit and cvUnits[p.linkUnit] then land = true end end
            if land then add("KI mit Trägerlandung (Flag cv): " .. g.name .. ", " .. #g.units .. "x " .. g.units[1].type .. (g.start_time > 0 and (", ab T+" .. g.start_time .. " s") or "")) end
          end
        end
      end
    end
  end
  for uid, g in pairs(cvUnits) do
    local u1 = g.units[1]
    local tb, ic = findTask(g.route, "ActivateBeacon"), findTask(g.route, "ActivateICLS")
    if not (u1.frequency and tb and ic) then err(g.name .. ": Träger ohne Funk/TACAN/ICLS") end
    if FIELD[math.floor(u1.frequency / 1e5 + 0.5)] then err(g.name .. ": Trägerfrequenz auf Platz/Lotse") end
    add(string.format("Träger %s (%s): Funk %.3f, TACAN %s, ICLS %s", g.name, u1.type, u1.frequency / 1e6, tb.channel .. (tb.modeChannel or "X"), ic.channel))
  end
  for _, b in ipairs(beacons) do add(b) end
  for _, c in ipairs(clients) do
    local p1 = c.g.route.points[1]
    local where = p1.linkUnit and ("Deck " .. (p1.type == "TakeOffParkingHot" and "heiß" or "kalt")) or p1.airdromeId and ("Kutaisi Stand " .. c.u.parking_id .. (p1.type == "TakeOffParkingHot" and " heiß" or " kalt"))
                  or string.format("Luft %.0f ft, %.0f kg Sprit", c.u.alt / FT, c.u.payload.fuel)
    add(string.format("Slot %-34s %-14s %s, Funk %.1f", c.g.name, c.u.type, where, c.g.frequency))
  end
  local wx = ms.weather
  local cl = wx.clouds
  local cover = (cl.preset and 1) or (((cl.density or 0) > 0) and 1 or 0)
  local ceil = cover == 1 and cl.base / FT or 99999
  local h = ms.start_time / 3600
  local night = h < 6.5 or h > 18.5   -- DCS-ATC without sun elevation (Carrier.CaseNow); in game the sun counts (< -6°)
  local case = (night or ceil < 1000 or (wx.visibility.distance / NM) < 5) and 3 or ceil < 3000 and 2 or 1
  add(string.format("Wetter: G;%d;%.0f;%.0f -> Träger-Case %d (Uhrzeit %02d:%02d, Wind aus %03d° %.0f kt)", cover, cl.base or 0, wx.visibility.distance, case,
    math.floor(h), math.floor(ms.start_time % 3600 / 60), (wx.wind.atGround.dir + 180) % 360, wx.wind.atGround.speed / KT))
  local br = dict.DictKey_descriptionText_1 or ""
  if not (br:find("\nEN\n", 1, true) and br:find("\nDE\n", 1, true) and br:find("Frequen")) then err("Briefing EN/DE fehlt") end
  print(file .. ": " .. (#errs == 0 and "OK" or ("FEHLER\n    " .. table.concat(errs, "\n    "))))
  print(table.concat(info, "\n"))
  return #errs == 0, case
end

---------------------------------------------------------------------------------------------------
-- The four missions
local CV = { p = { x = -330000, y = 545000 }, brc = 270, kt = 16 }   -- Black Sea, ~50 NM west of Kobuleti, heading west into the wind
local CASE_WIND = { 81, 11 * KT, 8, 12 }   -- Wind from 261° (mission file: blows to 081°) = along the angled deck, 11 kt
local UHF_CV = { 127.5, 252.5, 255.5, 262.0, 263.0, 251.5, 263.5 }
local UHF_LAND = { 251.5, 255.5, 263.0, 261.0, 263.5, 264.5, 265.0, 266.5 }
local VHF_LAND = { 127.5, 134.0, 132.0, 133.0 }

local function carrierCommon(m, cvg)
  local cvp = { p = CV.p, uid = cvg.units[1].unitId }
  orbiter(m, { name = "Arco 1-1 (S-3B Recovery)", unit = "Arco 1-1", cs = "Arco", type = "S-3B Tanker", task = "Refueling", freq = 255.5, tacan = 41, ident = "ARC",
               p1 = off(CV.p, 0, 2), p2 = off(off(CV.p, 0, 2), 270, 20), alt = 6000 * FT, spd = 250 * KT, protect = false })
  orbiter(m, { name = "Magic 1-1 (E-2C)", unit = "Magic 1-1", cs = "Magic", type = "E-2C", task = "AWACS", freq = 252.5,
               p1 = off(CV.p, 90, 30), p2 = off(off(CV.p, 90, 30), 0, 20), alt = 25000 * FT, spd = 270 * KT })
  return cvp
end

local missions = {}

missions[#missions + 1] = { file = "DCS-ATC Test - Carrier Case I.miz", build = function()
  local m = newMission({ title = "DCS-ATC Test - Carrier Case I", start = 10 * 3600, center = CV.p, wind = CASE_WIND,
    clouds = { thickness = 200, density = 0, base = 3000, iprecptns = 0 }, wxName = "Clear, wind 261/11",
    brief = [[
DCS-ATC TEST – CARRIER CASE I

EN
Purpose: DCS-ATC carrier stations in Case I (Marshal, Tower/Air Boss, Paddles, Case I departure), recovery tanker and E-2C AWACS. Start SRS and DCS-ATC before taking a slot.
Conditions: 15 June, 10:00 local, clear sky, visibility 80 km, wind 11 kt from 261° (down the angled deck). DCS-ATC must pick CASE I.
Ship: CVN-74 Stennis (free model, no Supercarrier needed), BRC 270, 16 kt, escorts CG-60 Normandy (Ticonderoga) and FFG-58 Samuel B. Roberts (Perry). About 50 NM west of Kobuleti.

Frequencies / NAVAIDs (Hornet COMM1/COMM2 presets):
- ch 1  127.5 AM  Carrier (Marshal, Tower, Approach, Paddles)
- ch 2  252.5     Magic 1-1, E-2C AWACS (mission frequency, not the 251.5 default)
- ch 3  255.5     Arco 1-1, S-3B recovery tanker, 6000 ft, TACAN 41X ARC
- ch 4  262.0     Kobuleti   ch 5  263.0 Kutaisi (divert)
- Carrier TACAN 74X STN, ICLS 11

Slots (all Client, SP and MP):
- Enfield 1-1      Hornet, cold on deck
- Springfield 1-1  Hornet, hot on deck
- Colt 1-1         Hornet airborne 25 NM east of the ship, 5000 ft, 3.5 t fuel (Case I return)
- Uzi 1-1          Hornet airborne 50 NM east, angels 20, 4 t fuel (Marshal check-in)
AI: Chevy 1-1 (30 NM out, lands at once), Pontiac 1-1 (appears at T+4 min, 45 NM out, lands), Dodge 1-1 (spawns on deck at T+5 min and launches).

Flow:
1. Airborne: tune 127.5. "Marshal, Uzi 1-1, checking in, mother's 255 for 50, angels 20, state 8.8." -> case one recovery, expected BRC, altimeter, hold angels 2 or higher, expected Charlie, "report see me".
2. "Uzi 1-1, see you at angels 2." -> "update state, switch Tower". Answer with the state only ("8.2"). Hold overhead, left turns.
3. Tower: "signal Charlie" -> initial 3 NM astern at 800 ft, break, downwind. Zip lip: no calls until the ball.
4. Groove: "Uzi 1-1, Hornet ball, 6.1." -> "Roger ball", LSO calls, grade as text after the trap.
5. Anytime: "Marshal, say position" / "pigeons" / "TACAN".
6. Deck slots: no calls on deck; Case I departure is text only (parallel to BRC, 500 ft until 7 NM). Then "Magic, Enfield 1-1, checking in", "picture"; tanker: "Arco, Enfield 1-1, request rejoin" -> "visual" -> "pre-contact" -> "disconnect" -> "complete".
Check: Case I announced, AI Hornets land, DCS-ATC turns the ship into the wind while someone recovers or launches, Dodge 1-1 launches over the catapult.

DE
Zweck: DCS-ATC-Trägerstationen im Case I (Marshal, Tower/Air Boss, Paddles, Case-I-Abflug), Recovery-Tanker und E-2C-AWACS. SRS und DCS-ATC vor der Slotwahl starten.
Lage: 15. Juni, 10:00 Ortszeit, wolkenlos, Sicht 80 km, Wind 11 kt aus 261° (längs des Winkeldecks). DCS-ATC muss CASE I wählen.
Schiff: CVN-74 Stennis (freies Modell, kein Supercarrier nötig), BRC 270, 16 kt, Geleit CG-60 Normandy (Ticonderoga) und FFG-58 Samuel B. Roberts (Perry), etwa 50 NM westlich Kobuleti.

Frequenzen / Navigation (Hornet COMM1/COMM2 voreingestellt):
- Kanal 1  127.5 AM  Träger (Marshal, Tower, Approach, Paddles)
- Kanal 2  252.5     Magic 1-1, E-2C-AWACS (Missionsfrequenz, nicht der Standard 251.5)
- Kanal 3  255.5     Arco 1-1, S-3B-Recovery-Tanker, 6000 ft, TACAN 41X ARC
- Kanal 4  262.0     Kobuleti   Kanal 5  263.0 Kutaisi (Ausweichplatz)
- Träger-TACAN 74X STN, ICLS 11

Slots (alle Client, SP und MP):
- Enfield 1-1      Hornet, kalt auf dem Deck
- Springfield 1-1  Hornet, heiß auf dem Deck
- Colt 1-1         Hornet in der Luft, 25 NM östlich, 5000 ft, 3,5 t Sprit (Case-I-Rückkehr)
- Uzi 1-1          Hornet in der Luft, 50 NM östlich, Angels 20, 4 t Sprit (Marshal-Check-in)
KI: Chevy 1-1 (30 NM, landet sofort), Pontiac 1-1 (ab T+4 min, 45 NM, landet), Dodge 1-1 (ab T+5 min auf dem Deck, startet).

Ablauf:
1. In der Luft 127.5 rasten. "Marshal, Uzi 1-1, checking in, mother's 255 for 50, angels 20, state 8.8." -> case one recovery, BRC, Höhenmesser, Holding ab Angels 2, Charlie-Zeit, "report see me".
2. "Uzi 1-1, see you at angels 2." -> "update state, switch Tower". Nur mit dem State antworten ("8.2"). Holding über dem Schiff, Linkskreise.
3. Tower: "signal Charlie" -> Initial 3 NM achteraus in 800 ft, Break, Gegenanflug. Zip lip bis zum Ball.
4. Groove: "Uzi 1-1, Hornet ball, 6.1." -> "Roger ball", LSO-Rufe, Bewertung als Text nach dem Trap.
5. Jederzeit: "Marshal, say position" / "pigeons" / "TACAN".
6. Deck-Slots: auf dem Deck kein Funk; Case-I-Abflug nur als Text (parallel zum BRC, 500 ft bis 7 NM). Danach "Magic, Enfield 1-1, checking in", "picture"; Tanker: "Arco, Enfield 1-1, request rejoin" -> "visual" -> "pre-contact" -> "disconnect" -> "complete".
Prüfen: Case I angesagt, KI-Hornets landen, DCS-ATC dreht das Schiff während Recovery/Start in den Wind, Dodge 1-1 startet vom Katapult.]],
    blueTask = "Slots: Enfield 1-1 (deck cold / Deck kalt), Springfield 1-1 (deck hot / Deck heiß), Colt 1-1 (25 NM), Uzi 1-1 (50 NM, Marshal). Carrier/Träger 127.5, TACAN 74X, ICLS 11." })
  local cvg = carrier(m, CV)
  local cvp = carrierCommon(m, cvg)
  local deckTo = off(CV.p, 270, 10)
  slot(m, { name = "Enfield 1-1 (Deck kalt)", cs = "Enfield", type = "FA-18C_hornet", deck = { p = CV.p, hdg = math.rad(CV.brc), uid = cvp.uid, state = "cold" }, to = deckTo, load = "hornetLight", freq = 127.5, uhf = UHF_CV })
  slot(m, { name = "Springfield 1-1 (Deck heiss)", cs = "Springfield", type = "FA-18C_hornet", deck = { p = CV.p, hdg = math.rad(CV.brc), uid = cvp.uid, state = "hot" }, to = deckTo, load = "hornetLight", freq = 127.5, uhf = UHF_CV })
  slot(m, { name = "Colt 1-1 (25 NM)", cs = "Colt", type = "FA-18C_hornet", p = off(CV.p, 60, 25), alt = 5000 * FT, spd = 300 * KT, to = CV.p, load = "hornetLight", fuel = 3500, freq = 127.5, uhf = UHF_CV })
  slot(m, { name = "Uzi 1-1 (50 NM Marshal)", cs = "Uzi", type = "FA-18C_hornet", p = off(CV.p, 75, 50), alt = 20000 * FT, spd = 330 * KT * 1.35, to = CV.p, load = "hornetLight", fuel = 4000, freq = 127.5, uhf = UHF_CV })
  aiLand(m, { name = "Chevy 1-1 (KI Landung)", cs = "Chevy", cv = cvp, p = off(CV.p, 45, 30), alt = 6000 * FT, spd = 300 * KT })
  aiLand(m, { name = "Pontiac 1-1 (KI Landung)", cs = "Pontiac", cv = cvp, p = off(CV.p, 100, 45), alt = 15000 * FT, spd = 330 * KT, start = 240 })
  m:plane({ name = "Dodge 1-1 (KI Start)", cs = "Dodge", task = "CAP", freq = 127.5, start = 300, skill = "High",
    units = { { type = "FA-18C_hornet", name = "Dodge 1-1", p = CV.p, alt = 0, spd = 0, hdg = math.rad(CV.brc), load = "hornetAA", parking = { park = "1", id = "1" } } },
    points = function() return { wp(CV.p, 0, 0, { type = "TakeOffParking", action = "From Parking Area", link = cvp.uid }),
                                  wp(off(CV.p, 270, 10), 3000 * FT, 150), wp(off(CV.p, 330, 40), 20000 * FT, 200, { tasks = { T.orbit(20000 * FT, 200, "Circle") } }) } end })
  return m
end }

missions[#missions + 1] = { file = "DCS-ATC Test - Carrier Case III.miz", build = function()
  local m = newMission({ title = "DCS-ATC Test - Carrier Case III", start = 22 * 3600 + 1800, center = CV.p, wind = { 81, 12 * KT, 9, 13 },
    clouds = { preset = "Preset19", thickness = 200, density = 0, base = 280, iprecptns = 0 }, wxName = "Night, OVC 900 ft",
    brief = [[
DCS-ATC TEST – CARRIER CASE III

EN
Purpose: DCS-ATC Case III (Marshal stack with radial/DME/angels/EAT, CV-1 approach with needles, Paddles, Case III departure), recovery tanker, AI traffic in the marshal stack.
Conditions: 15 June, 22:30 local (night, sun far below -6°), overcast at about 900 ft (preset 19, base 280 m), wind 12 kt from 261°. Both night and ceiling below 1000 ft make DCS-ATC pick CASE III.
Ship: CVN-74 Stennis, BRC 270, 16 kt, escorts Ticonderoga and Perry, about 50 NM west of Kobuleti. Final bearing about 261, marshal radial about 081.

Frequencies / NAVAIDs (Hornet COMM1/COMM2 presets):
- ch 1  127.5 AM  Carrier (Marshal, Approach, Departure, Paddles)
- ch 2  252.5     Magic 1-1, E-2C AWACS
- ch 3  255.5     Arco 1-1, S-3B recovery tanker, 6000 ft, TACAN 41X ARC
- ch 4  262.0     Kobuleti   ch 5  263.0 Kutaisi (divert)
- Carrier TACAN 74X STN, ICLS 11

Slots:
- Enfield 1-1      Hornet airborne 50 NM east, angels 22, 4.2 t fuel
- Colt 1-1         Hornet airborne 30 NM east, angels 15, 3.8 t fuel
- Springfield 1-1  Hornet, hot on deck (Case III departure)
AI in the stack (hold over the marshal radial, then land): Chevy 1-1 angels 6 (holds 4 min), Pontiac 1-1 angels 7 (8 min), Ford 1-1 angels 8 (appears at T+5 min, 12 min).

Flow:
1. On 127.5: "Marshal, Enfield 1-1, checking in, mother's 260 for 50, angels 22, state 9.2." -> case three recovery, CV-1 approach, expected final bearing, marshal radial, DME, angels, EAT.
2. Read back: "Enfield 1-1, 081 radial, 22 DME, angels 7." -> "readback correct". "see you at angels 7" when established.
3. At the EAT: "Enfield 1-1, commencing, angels 7, state 6.5." -> "radar contact, final bearing ...".
4. "Platform" passing 5000 ft, needles at about 8 NM ("up and on", "down and left" ...), 3 miles / 3/4 mile calls.
5. "Enfield 1-1, Hornet ball, 5.4." -> "Roger ball, 25 knots, axial."
6. Deck slot: "airborne" -> "radar contact"; "passing 2.5", "arcing", "established outbound"; "on top, angels 8" -> switch Magic 252.5.
Check: Case III announced, stack assignments differ per aircraft, AI traffic keeps its slots, bolter/waveoff -> radar vectors and new approach.

DE
Zweck: DCS-ATC Case III (Marshal-Stack mit Radial/DME/Angels/EAT, CV-1-Anflug mit Nadeln, Paddles, Case-III-Abflug), Recovery-Tanker, KI-Verkehr im Marshal-Stack.
Lage: 15. Juni, 22:30 Ortszeit (Nacht, Sonne weit unter -6°), bedeckt in etwa 900 ft (Preset 19, Basis 280 m), Wind 12 kt aus 261°. Nacht und Wolkenuntergrenze unter 1000 ft ergeben jeweils CASE III.
Schiff: CVN-74 Stennis, BRC 270, 16 kt, Geleit Ticonderoga und Perry, etwa 50 NM westlich Kobuleti. Final Bearing etwa 261, Marshal-Radial etwa 081.

Frequenzen / Navigation (Hornet COMM1/COMM2 voreingestellt):
- Kanal 1  127.5 AM  Träger (Marshal, Approach, Departure, Paddles)
- Kanal 2  252.5     Magic 1-1, E-2C-AWACS
- Kanal 3  255.5     Arco 1-1, S-3B-Recovery-Tanker, 6000 ft, TACAN 41X ARC
- Kanal 4  262.0     Kobuleti   Kanal 5  263.0 Kutaisi (Ausweichplatz)
- Träger-TACAN 74X STN, ICLS 11

Slots:
- Enfield 1-1      Hornet in der Luft, 50 NM östlich, Angels 22, 4,2 t Sprit
- Colt 1-1         Hornet in der Luft, 30 NM östlich, Angels 15, 3,8 t Sprit
- Springfield 1-1  Hornet, heiß auf dem Deck (Case-III-Abflug)
KI im Stack (kreist über dem Marshal-Radial, landet dann): Chevy 1-1 Angels 6 (4 min), Pontiac 1-1 Angels 7 (8 min), Ford 1-1 Angels 8 (ab T+5 min, 12 min).

Ablauf:
1. Auf 127.5: "Marshal, Enfield 1-1, checking in, mother's 260 for 50, angels 22, state 9.2." -> case three recovery, CV-1 approach, Final Bearing, Marshal-Radial, DME, Angels, EAT.
2. Rücklesen: "Enfield 1-1, 081 radial, 22 DME, angels 7." -> "readback correct". Im Holding "see you at angels 7".
3. Zur EAT: "Enfield 1-1, commencing, angels 7, state 6.5." -> "radar contact, final bearing ...".
4. "Platform" bei 5000 ft, Nadeln ab etwa 8 NM ("up and on", "down and left" ...), Rufe bei 3 NM und 3/4 NM.
5. "Enfield 1-1, Hornet ball, 5.4." -> "Roger ball, 25 knots, axial."
6. Deck-Slot: "airborne" -> "radar contact"; "passing 2.5", "arcing", "established outbound"; "on top, angels 8" -> Wechsel zu Magic 252.5.
Prüfen: Case III angesagt, verschiedene Stack-Plätze je Flugzeug, KI hält ihre Plätze, Bolter/Waveoff -> Radarführung und neuer Anflug.]],
    blueTask = "Slots: Enfield 1-1 (50 NM), Colt 1-1 (30 NM), Springfield 1-1 (deck hot / Deck heiß). Carrier/Träger 127.5, TACAN 74X, ICLS 11. Night / Nacht, OVC 900 ft." })
  local cvg = carrier(m, CV)
  local cvp = carrierCommon(m, cvg)
  slot(m, { name = "Enfield 1-1 (50 NM)", cs = "Enfield", type = "FA-18C_hornet", p = off(CV.p, 80, 50), alt = 22000 * FT, spd = 300 * KT * 1.44, to = CV.p, load = "hornetLight", fuel = 4200, freq = 127.5, uhf = UHF_CV })
  slot(m, { name = "Colt 1-1 (30 NM)", cs = "Colt", type = "FA-18C_hornet", p = off(CV.p, 95, 30), alt = 15000 * FT, spd = 280 * KT * 1.3, to = CV.p, load = "hornetLight", fuel = 3800, freq = 127.5, uhf = UHF_CV })
  slot(m, { name = "Springfield 1-1 (Deck heiss)", cs = "Springfield", type = "FA-18C_hornet", deck = { p = CV.p, hdg = math.rad(CV.brc), uid = cvp.uid, state = "hot" }, to = off(CV.p, 270, 10), load = "hornetLight", freq = 127.5, uhf = UHF_CV })
  aiLand(m, { name = "Chevy 1-1 (KI Stack)", cs = "Chevy", cv = cvp, p = off(CV.p, 81, 28), alt = 6000 * FT, spd = 250 * KT, hold = { p = off(CV.p, 81, 21), sec = 240 } })
  aiLand(m, { name = "Pontiac 1-1 (KI Stack)", cs = "Pontiac", cv = cvp, p = off(CV.p, 81, 32), alt = 7000 * FT, spd = 250 * KT, hold = { p = off(CV.p, 81, 22), sec = 480 } })
  aiLand(m, { name = "Ford 1-1 (KI Stack)", cs = "Ford", cv = cvp, p = off(CV.p, 75, 45), alt = 8000 * FT, spd = 280 * KT, start = 300, hold = { p = off(CV.p, 81, 23), sec = 720 } })
  return m
end }

missions[#missions + 1] = { file = "DCS-ATC Test - AWACS and air combat.miz", build = function()
  local K = KUTAISI
  local m = newMission({ title = "DCS-ATC Test - AWACS and air combat", start = 9 * 3600, center = { x = -260000, y = 650000 }, wind = { 70, 4, 7, 11 },
    clouds = { preset = "Preset2", thickness = 200, density = 0, base = 2500, iprecptns = 0 }, wxName = "Few clouds",
    brief = [[
DCS-ATC TEST – AWACS AND AIR COMBAT

EN
Purpose: DCS-ATC AWACS (check-in, picture, bogey dope, declare, spike, sort, threat calls), AI flight chatter in real air combat, tanker for the F-16, airfield ground/tower at Kutaisi with handoff to the AWACS.
Conditions: 15 June, 09:00 local, few clouds, wind 4 m/s from 250°.

Frequencies / NAVAIDs (UHF presets; F-16 VHF: 1 127.5, 2 134.0 Kutaisi, 3 132.0 Senaki):
- ch 1  251.5  Overlord 1-1, E-3A AWACS (orbit 25 NM south of Kutaisi, FL300, immortal/invisible)
- ch 2  255.5  Shell 1-1, KC-135 boom tanker (F-16 only), FL240, TACAN 52X SHL, south-east hinterland
- ch 3  263.0  Kutaisi (DCS-ATC Ground/Tower/Approach on the field frequency)   ch 4 261.0 Senaki
- ch 5  263.5 ATIS, ch 6 264.5 Ground, ch 7 265.0 Tower, ch 8 266.5 Approach (DCS-ATC defaults)

Slots:
- Enfield 1-1      Hornet airborne 15 NM north-west of Kutaisi, 18000 ft
- Colt 1-1         F-16C airborne 10 NM north of Senaki, 15000 ft
- Springfield 1-1  Hornet, cold, Kutaisi stand 24
- Uzi 1-1          F-16C, cold, Kutaisi stand 22
Blue AI: Dodge 1 (2x F-15C) CAP north of Kutaisi FL250, Ford 1 (2x F-16C) CAP north-west of Senaki FL220, both on 251.5. Convoy (2 M-113, 2 Hummer) 3 NM north of Senaki.
Red AI (weapons free, staggered start time):
- T+2 min   2x MiG-29S from the north-west (Gagra), FL230, then CAP over Senaki/Kutaisi
- T+10 min  2x Su-27 from the north over the main ridge, FL260
- T+15 min  2x Su-25 low from Sukhumi along the coast, attack the convoy
- T+20 min  1x MiG-31 from the far north at 49000 ft, about Mach 1.6

Flow:
1. "Overlord, Enfield 1-1, checking in, state 9.5." -> radar contact, picture (or threat first).
2. "picture", "bogey dope", "declare 330 for 40" or with radar lock "declare", "spike 340", "request sort" (2-ship), "committing north group".
3. Listen for AWACS threat calls and AI chatter (Fox 3, splash, defending) on 251.5.
4. Ramp: "Kutaisi Ground, Uzi 1-1, request startup" -> taxi -> takeoff -> "leaving control zone, contact Overlord 251.5".
5. F-16: "Overlord, request nearest tanker" -> "Shell, Colt 1-1, request rejoin" ... "pre-contact".
Check: only contacts the blue sensors have detected are called, new groups are reported on their own, fuel/bingo of AI flights.

DE
Zweck: DCS-ATC-AWACS (Check-in, Picture, Bogey Dope, Declare, Spike, Sort, Bedrohungsrufe), KI-Funk im echten Luftkampf, Tanker für die F-16, Platz Kutaisi (Ground/Tower) mit Übergabe ans AWACS.
Lage: 15. Juni, 09:00 Ortszeit, wenige Wolken, Wind 4 m/s aus 250°.

Frequenzen / Navigation (UHF voreingestellt; F-16 VHF: 1 127.5, 2 134.0 Kutaisi, 3 132.0 Senaki):
- Kanal 1  251.5  Overlord 1-1, E-3A-AWACS (Rennbahn 25 NM südlich Kutaisi, FL300, unverwundbar/unsichtbar)
- Kanal 2  255.5  Shell 1-1, KC-135 mit Boom (nur F-16), FL240, TACAN 52X SHL, Hinterland Südost
- Kanal 3  263.0  Kutaisi (DCS-ATC Ground/Tower/Approach auf der Platzfrequenz)   Kanal 4 261.0 Senaki
- Kanal 5  263.5 ATIS, 6 264.5 Ground, 7 265.0 Tower, 8 266.5 Approach (DCS-ATC-Standard)

Slots:
- Enfield 1-1      Hornet in der Luft, 15 NM nordwestlich Kutaisi, 18000 ft
- Colt 1-1         F-16C in der Luft, 10 NM nördlich Senaki, 15000 ft
- Springfield 1-1  Hornet, kalt, Kutaisi Stand 24
- Uzi 1-1          F-16C, kalt, Kutaisi Stand 22
Blaue KI: Dodge 1 (2x F-15C) CAP nördlich Kutaisi FL250, Ford 1 (2x F-16C) CAP nordwestlich Senaki FL220, beide auf 251.5. Konvoi (2 M-113, 2 Hummer) 3 NM nördlich Senaki.
Rote KI (Waffen frei, gestaffelte Startzeit):
- T+2 min   2x MiG-29S aus Nordwest (Gagra), FL230, dann CAP über Senaki/Kutaisi
- T+10 min  2x Su-27 aus Norden über den Hauptkamm, FL260
- T+15 min  2x Su-25 tief von Suchumi die Küste entlang, greifen den Konvoi an
- T+20 min  1x MiG-31 aus dem hohen Norden in 49000 ft, etwa Mach 1,6

Ablauf:
1. "Overlord, Enfield 1-1, checking in, state 9.5." -> radar contact, Picture (oder zuerst die Bedrohung).
2. "picture", "bogey dope", "declare 330 for 40" bzw. mit Radar-Lock "declare", "spike 340", "request sort" (Rotte), "committing north group".
3. Bedrohungsrufe des AWACS und KI-Funk (Fox 3, Splash, Defending) auf 251.5 hören.
4. Rampe: "Kutaisi Ground, Uzi 1-1, request startup" -> Rollen -> Start -> "leaving control zone, contact Overlord 251.5".
5. F-16: "Overlord, request nearest tanker" -> "Shell, Colt 1-1, request rejoin" ... "pre-contact".
Prüfen: nur von blauen Sensoren erfasste Kontakte werden genannt, neue Gruppen kommen von selbst, Sprit/Bingo der KI-Flights.]],
    blueTask = "Slots: Enfield 1-1 (Hornet air/Luft), Colt 1-1 (F-16 air/Luft), Springfield 1-1 (Hornet Kutaisi), Uzi 1-1 (F-16 Kutaisi). AWACS Overlord 251.5, Tanker Shell 255.5 TACAN 52X.",
    redTask = "AI only: MiG-29S T+2, Su-27 T+10, Su-25 T+15, MiG-31 T+20 min. / Nur KI." })
  orbiter(m, { name = "Overlord 1-1 (E-3A)", unit = "Overlord 1-1", cs = "Overlord", type = "E-3A", task = "AWACS", freq = 251.5,
               p1 = { x = -325000, y = 690000 }, p2 = { x = -325000, y = 750000 }, alt = 30000 * FT, spd = 180, protect = true })
  orbiter(m, { name = "Shell 1-1 (KC-135)", unit = "Shell 1-1", cs = "Shell", type = "KC-135", task = "Refueling", freq = 255.5, tacan = 52, ident = "SHL",
               p1 = { x = -345000, y = 760000 }, p2 = { x = -345000, y = 820000 }, alt = 24000 * FT, spd = 280 * KT * 1.48, protect = true })
  fighters(m, { name = "Dodge 1 (F-15C CAP)", cs = "Dodge", type = "F-15C", n = 2, load = "eagle", skill = "High", freq = 251.5,
                p1 = { x = -245000, y = 660000 }, p2 = { x = -235000, y = 660000 }, p3 = { x = -235000, y = 710000 }, alt = 25000 * FT, spd = 210 })
  fighters(m, { name = "Ford 1 (F-16C CAP)", cs = "Ford", type = "F-16C_50", n = 2, load = "viperAA", skill = "High", freq = 251.5,
                p1 = { x = -255000, y = 615000 }, p2 = { x = -245000, y = 600000 }, p3 = { x = -230000, y = 580000 }, alt = 22000 * FT, spd = 200 })
  m:vehicles({ name = "Konvoi Senaki", p = { x = SENAKI.x + 5500, y = SENAKI.y + 200 }, hdg = 0, units = { { type = "M-113" }, { type = "M-113" }, { type = "Hummer" }, { type = "Hummer" } } })
  m:vehicles({ name = "JTAC Axeman", p = { x = SENAKI.x + 9000, y = SENAKI.y + 1500 }, hdg = 0, units = { { type = "Hummer" } } })   -- J1: JTAC by name (leader detection), red armor 3 km east in sight
  m:vehicles({ side = "red", name = "Rot Panzer", p = { x = SENAKI.x + 12000, y = SENAKI.y + 1500 }, hdg = 0, units = { { type = "T-72B" }, { type = "T-72B" }, { type = "BTR-80" } } })
  fighters(m, { side = "red", name = "MiG-29S Rot 1", csNum = 110, type = "MiG-29S", n = 2, load = "fulcrum", skill = "Good", freq = 124.5, start = 120,
                p1 = { x = -150000, y = 520000 }, p2 = { x = -255000, y = 630000 }, p3 = { x = -255000, y = 680000 }, alt = 23000 * FT, spd = 250, spd2 = 220 })
  fighters(m, { side = "red", name = "Su-27 Rot 2", csNum = 120, type = "Su-27", n = 2, load = "flanker", skill = "Good", freq = 124.5, start = 600,
                p1 = { x = -120000, y = 690000 }, p2 = { x = -240000, y = 690000 }, p3 = { x = -240000, y = 640000 }, alt = 26000 * FT, spd = 250, spd2 = 220 })
  fighters(m, { side = "red", name = "Su-25 Rot 3", csNum = 130, task = "CAS", targets = { "Helicopters", "Ground Units", "Light armed ships" }, type = "Su-25", n = 2, load = "frogfoot",
                skill = "Good", freq = 124.5, start = 900, p1 = { x = -215000, y = 565000 }, p2 = { x = -268000, y = 640000 }, p3 = { x = -276000, y = 652000 },
                alt = 1500, alt2 = 800, spd = 190, spd2 = 170, pattern = "Circle" })
  fighters(m, { side = "red", name = "MiG-31 Rot 4", csNum = 140, task = "Intercept", type = "MiG-31", n = 1, load = "foxhound", skill = "Excellent", freq = 124.5, start = 1200,
                p1 = { x = -60000, y = 640000 }, p2 = { x = -300000, y = 700000 }, alt = 15000, alt2 = 12000, spd = 500, spd2 = 450 })
  slot(m, { name = "Enfield 1-1 (Hornet Luft)", cs = "Enfield", type = "FA-18C_hornet", p = off(K, 300, 15), alt = 18000 * FT, spd = 320 * KT * 1.36, to = off(K, 330, 60), load = "hornetAA", freq = 251.5, uhf = UHF_LAND })
  slot(m, { name = "Colt 1-1 (F-16 Luft)", cs = "Colt", type = "F-16C_50", p = off(SENAKI, 0, 10), alt = 15000 * FT, spd = 320 * KT * 1.3, to = off(SENAKI, 330, 60), load = "viperAA", freq = 251.5, uhf = UHF_LAND, vhf = VHF_LAND })
  slot(m, { name = "Springfield 1-1 (Hornet Kutaisi)", cs = "Springfield", type = "FA-18C_hornet", stand = "24", to = off(K, 250, 10), load = "hornetAA", freq = 263.0, uhf = UHF_LAND })
  slot(m, { name = "Uzi 1-1 (F-16 Kutaisi)", cs = "Uzi", type = "F-16C_50", stand = "22", to = off(K, 250, 10), load = "viperAA", freq = 263.0, uhf = UHF_LAND, vhf = VHF_LAND })
  return m
end }

missions[#missions + 1] = { file = "DCS-ATC Test - Tanker.miz", build = function()
  local K = KUTAISI
  local tex1, tex2, shell, arco = { x = -330000, y = 670000 }, { x = -355000, y = 680000 }, { x = -310000, y = 760000 }, { x = -300000, y = 615000 }
  local m = newMission({ title = "DCS-ATC Test - Tanker", start = 11 * 3600, center = { x = -320000, y = 680000 }, wind = { 70, 4, 7, 11 },
    clouds = { preset = "Preset2", thickness = 200, density = 0, base = 2500, iprecptns = 0 }, wxName = "Few clouds",
    brief = [[
DCS-ATC TEST – TANKER

EN
Purpose: DCS-ATC tanker (rejoin with vectors, visual, observation, pre-contact, contact/disconnect from the real DCS refuelling events, queue, breakaway, offload, handoff to the AWACS), choosing between two tankers with the same name (Texaco 1-1 / Texaco 2-1), boom vs. basket, S-3B.
Conditions: 15 June, 11:00 local, few clouds, wind 4 m/s from 250°. All tankers have unlimited fuel. DCS-ATC presses the DCS F-menu (Intent to refuel / Ready pre-contact) in the background for the PC player.

Tankers (all on 255.5 = DCS-ATC tanker frequency, so Easy Communication keeps you on one frequency):
- Texaco 1-1  KC-135MPRS (basket)  FL220, 270 KIAS, TACAN 51X TEX, track east-west 25 NM south-west of Kutaisi
- Texaco 2-1  KC-135MPRS (basket)  FL200, 250 KIAS, TACAN 53X TXB, track 15 NM further south
- Shell 1-1   KC-135 (boom, F-16)  FL240, 280 KIAS, TACAN 52X SHL, track south-east of Kutaisi
- Arco 1-1    S-3B (basket)        12000 ft, 230 KIAS, TACAN 54X ARC, over the sea west of Poti
- Overlord 1-1 E-3A AWACS 251.5 (handoff after "complete", "request nearest tanker")
UHF presets: 1 251.5 AWACS, 2 255.5 tanker, 3 263.0 Kutaisi, 4 261.0 Senaki, 5 263.5 ATIS, 6 264.5 Ground, 7 265.0 Tower, 8 266.5 Approach.

Slots:
- Enfield 1-1      Hornet airborne 35 NM north-west of Texaco 1-1, angels 20, 1.1 t fuel, no tank
- Colt 1-1         F-16C airborne 35 NM north-east of Shell 1-1, angels 22, 0.9 t fuel, no tanks
- Springfield 1-1  Hornet airborne 32 NM west of Arco 1-1 (S-3B), 12000 ft, 1.1 t fuel
- Pontiac 1-1      Hornet, cold, Kutaisi stand 24
- Uzi 1-1          F-16C, cold, Kutaisi stand 22

Flow:
1. "Texaco 1-1, Enfield 1-1, request rejoin" -> bearing/range/angels/track/TACAN, vectors at 20/10/5 NM.
2. "Texaco, Enfield 1-1, request rejoin" (no number) -> DCS-ATC must say which Texaco (two in the air: "Texaco one one" / "Texaco two one"). Then "Texaco 2-1, ..." to switch.
3. "visual" -> "cleared to join, observation left wing"; "pre-contact" -> "cleared contact, basket/boom ready"; plug -> "contact"; full -> "disconnect, you received ... pounds".
4. "disconnect" -> "move to the right wing"; "complete" -> "total offload ..., cleared to depart, contact Overlord 251.5".
5. Hornet on Shell (boom) -> "negative, no compatible tanker" or redirect; F-16 on Texaco (basket) likewise.
6. Two players at one tanker: queue "number 2", "cleared pre-contact" for the next one.

DE
Zweck: DCS-ATC-Tanker (Rejoin mit Vektoren, Visual, Beobachtung, Pre-Contact, Kontakt/Trennung über die echten DCS-Betankungsereignisse, Warteschlange, Breakaway, Abgabemenge, Übergabe ans AWACS), Unterscheidung zweier gleichnamiger Tanker (Texaco 1-1 / Texaco 2-1), Boom und Korb, S-3B.
Lage: 15. Juni, 11:00 Ortszeit, wenige Wolken, Wind 4 m/s aus 250°. Alle Tanker mit unbegrenztem Sprit. DCS-ATC drückt für den Spieler am PC im Hintergrund das DCS-F-Menü (Intent to refuel / Ready pre-contact).

Tanker (alle auf 255.5 = DCS-ATC-Tankerfrequenz, mit Easy Communication bleibt man auf einer Frequenz):
- Texaco 1-1  KC-135MPRS (Korb)    FL220, 270 KIAS, TACAN 51X TEX, Rennbahn Ost-West 25 NM südwestlich Kutaisi
- Texaco 2-1  KC-135MPRS (Korb)    FL200, 250 KIAS, TACAN 53X TXB, Rennbahn 15 NM weiter südlich
- Shell 1-1   KC-135 (Boom, F-16)  FL240, 280 KIAS, TACAN 52X SHL, Rennbahn südöstlich Kutaisi
- Arco 1-1    S-3B (Korb)          12000 ft, 230 KIAS, TACAN 54X ARC, über dem Meer westlich Poti
- Overlord 1-1 E-3A-AWACS 251.5 (Übergabe nach "complete", "request nearest tanker")
UHF voreingestellt: 1 251.5 AWACS, 2 255.5 Tanker, 3 263.0 Kutaisi, 4 261.0 Senaki, 5 263.5 ATIS, 6 264.5 Ground, 7 265.0 Tower, 8 266.5 Approach.

Slots:
- Enfield 1-1      Hornet in der Luft, 35 NM nordwestlich Texaco 1-1, Angels 20, 1,1 t Sprit, ohne Tank
- Colt 1-1         F-16C in der Luft, 35 NM nordöstlich Shell 1-1, Angels 22, 0,9 t Sprit, ohne Tanks
- Springfield 1-1  Hornet in der Luft, 32 NM westlich Arco 1-1 (S-3B), 12000 ft, 1,1 t Sprit
- Pontiac 1-1      Hornet, kalt, Kutaisi Stand 24
- Uzi 1-1          F-16C, kalt, Kutaisi Stand 22

Ablauf:
1. "Texaco 1-1, Enfield 1-1, request rejoin" -> Peilung/Entfernung/Angels/Track/TACAN, Vektoren bei 20/10/5 NM.
2. "Texaco, Enfield 1-1, request rejoin" (ohne Nummer) -> DCS-ATC muss den Texaco mit Nummer nennen ("Texaco one one" / "Texaco two one"). Mit "Texaco 2-1, ..." wechseln.
3. "visual" -> "cleared to join, observation left wing"; "pre-contact" -> "cleared contact, basket/boom ready"; Kontakt -> "contact"; voll -> "disconnect, you received ... pounds".
4. "disconnect" -> "move to the right wing"; "complete" -> "total offload ..., cleared to depart, contact Overlord 251.5".
5. Hornet an Shell (Boom) -> "negative, no compatible tanker" bzw. Hinweis; F-16 an Texaco (Korb) ebenso.
6. Zwei Spieler an einem Tanker: Warteschlange "number 2", "cleared pre-contact" für den Nächsten.]],
    blueTask = "Slots: Enfield 1-1 (Hornet -> Texaco), Colt 1-1 (F-16 -> Shell), Springfield 1-1 (Hornet -> Arco S-3B), Pontiac 1-1 / Uzi 1-1 (Kutaisi). Tanker 255.5: Texaco 1-1 51X, Texaco 2-1 53X, Shell 1-1 52X, Arco 1-1 54X. AWACS 251.5." })
  local function track(c) return { x = c.x, y = c.y - 30000 }, { x = c.x, y = c.y + 30000 } end
  local a1, a2 = track(tex1)
  orbiter(m, { name = "Texaco 1-1 (KC-135MPRS)", unit = "Texaco 1-1", cs = "Texaco", csGroup = 1, type = "KC135MPRS", task = "Refueling", freq = 255.5, tacan = 51, ident = "TEX",
               p1 = a1, p2 = a2, alt = 22000 * FT, spd = 270 * KT * 1.44 })
  a1, a2 = track(tex2)
  orbiter(m, { name = "Texaco 2-1 (KC-135MPRS)", unit = "Texaco 2-1", cs = "Texaco", csGroup = 2, type = "KC135MPRS", task = "Refueling", freq = 255.5, tacan = 53, ident = "TXB",
               p1 = a2, p2 = a1, alt = 20000 * FT, spd = 250 * KT * 1.4 })
  a1, a2 = track(shell)
  orbiter(m, { name = "Shell 1-1 (KC-135 Boom)", unit = "Shell 1-1", cs = "Shell", type = "KC-135", task = "Refueling", freq = 255.5, tacan = 52, ident = "SHL",
               p1 = a1, p2 = a2, alt = 24000 * FT, spd = 280 * KT * 1.48 })
  orbiter(m, { name = "Arco 1-1 (S-3B)", unit = "Arco 1-1", cs = "Arco", type = "S-3B Tanker", task = "Refueling", freq = 255.5, tacan = 54, ident = "ARC",
               p1 = { x = arco.x, y = arco.y - 15000 }, p2 = { x = arco.x, y = arco.y + 15000 }, alt = 12000 * FT, spd = 230 * KT * 1.24 })
  orbiter(m, { name = "Overlord 1-1 (E-3A)", unit = "Overlord 1-1", cs = "Overlord", type = "E-3A", task = "AWACS", freq = 251.5,
               p1 = { x = -300000, y = 830000 }, p2 = { x = -360000, y = 830000 }, alt = 30000 * FT, spd = 180 })
  slot(m, { name = "Enfield 1-1 (Hornet zu Texaco)", cs = "Enfield", type = "FA-18C_hornet", p = off(tex1, 315, 35), alt = 20000 * FT, spd = 300 * KT * 1.4, to = tex1, load = "hornetLight", fuel = 1100, freq = 255.5, uhf = UHF_LAND })
  slot(m, { name = "Colt 1-1 (F-16 zu Shell)", cs = "Colt", type = "F-16C_50", p = off(shell, 45, 35), alt = 22000 * FT, spd = 300 * KT * 1.44, to = shell, load = "viperClean", fuel = 900, freq = 255.5, uhf = UHF_LAND, vhf = VHF_LAND })
  slot(m, { name = "Springfield 1-1 (Hornet zu S-3B)", cs = "Springfield", type = "FA-18C_hornet", p = off(arco, 270, 32), alt = 12000 * FT, spd = 280 * KT * 1.24, to = arco, load = "hornetLight", fuel = 1100, freq = 255.5, uhf = UHF_LAND })
  slot(m, { name = "Pontiac 1-1 (Hornet Kutaisi)", cs = "Pontiac", type = "FA-18C_hornet", stand = "24", to = off(K, 250, 10), load = "hornetAA", freq = 263.0, uhf = UHF_LAND })
  slot(m, { name = "Uzi 1-1 (F-16 Kutaisi)", cs = "Uzi", type = "F-16C_50", stand = "22", to = off(K, 250, 10), load = "viperAA", freq = 263.0, uhf = UHF_LAND, vhf = VHF_LAND })
  return m
end }

missions[#missions + 1] = { file = "DCS-ATC Test - JTAC.miz", build = function()
  local A = { x = SENAKI.x + 9000, y = SENAKI.y + 1500 }   -- JTAC Axeman; flat Colchis lowland, line of sight to the armor
  local tgt = { x = A.x + 3000, y = A.y }
  local m = newMission({ title = "DCS-ATC Test - JTAC", start = 10 * 3600, center = A, wind = { 70, 4, 7, 11 },
    clouds = { preset = "Preset2", thickness = 200, density = 0, base = 2500, iprecptns = 0 }, wxName = "Few clouds",
    brief = [[
DCS-ATC TEST – JTAC

EN
Purpose: DCS-ATC JTAC and FAC(A): check-in, situation, 9-line, readback, marks (smoke, laser 1688), IN / cleared hot, BDA, check-out; FAC(A) and handover.
Conditions: 15 June, 10:00 local, few clouds. JTAC frequency 133.5 AM (DCS-ATC default), laser code 1688.

Ground (9 km north of Senaki):
- Axeman  JTAC (Hummer), friendly platoon (2x M-113) 600 m west of him
- Enemy armor 3 km north of Axeman (2x T-72B, BTR-80), ZSU-23-4 Shilka 1.5 km behind it (threat)
- Pontiac 1-1  MQ-9 FAC(A), on station after 15 min, 15000 ft over the target area

Slots:
- Enfield 1-1  Hornet airborne 15 NM south-west, 12000 ft, 4x GBU-12, COMM1 133.5 (lat/long in line 6)
- Colt 1-1     F-16C airborne 15 NM south, 12000 ft, 2x GBU-12, VHF preset 1 133.5 (lat/long)
- Uzi 1-1      Hornet cold at Kutaisi stand 24 (rearm as you like, e.g. Litening/Mavericks)

Flow:
1. "Axeman, Enfield 1-1, checking in" -> situation, type 2, "advise when ready for 9-line".
2. "ready to copy" -> 9-line; read back lines 4, 6 and restrictions -> "readback correct".
3. "request mark" -> smoke on the target after about 30 s; "tally target"; "laser on".
4. "Enfield 1-1, in from the south" -> "cleared hot"; GBU-12 on the spot; "off" -> BDA.
5. Second player checks in during an attack -> hold with altitude stack.
6. After 15 min: Pontiac 1-1 (FAC(A), MQ-9) on station.

DE
Zweck: DCS-ATC-JTAC und FAC(A): Check-in, Lage, 9-Liner, Readback, Markierung (Rauch, Laser 1688), IN / cleared hot, BDA, Check-out; FAC(A) und Übergabe.
Lage: 15. Juni, 10:00 Ortszeit, wenige Wolken. JTAC-Frequenz 133.5 AM (DCS-ATC-Standard), Laser-Code 1688.

Boden (9 km nördlich Senaki):
- Axeman  JTAC (Hummer), eigener Zug (2x M-113) 600 m westlich von ihm
- Feindpanzer 3 km nördlich von Axeman (2x T-72B, BTR-80), ZSU-23-4 Schilka 1,5 km dahinter (Bedrohung)
- Pontiac 1-1  MQ-9 FAC(A), ab Minute 15 auf Station, 15000 ft über dem Zielgebiet

Slots:
- Enfield 1-1  Hornet in der Luft, 15 NM südwestlich, 12000 ft, 4x GBU-12, COMM1 133.5 (Lat/Long in Zeile 6)
- Colt 1-1     F-16C in der Luft, 15 NM südlich, 12000 ft, 2x GBU-12, VHF-Preset 1 133.5 (Lat/Long)
- Uzi 1-1      Hornet kalt in Kutaisi Stand 24 (Bewaffnung frei, z. B. Litening/Mavericks)

Ablauf:
1. "Axeman, Enfield 1-1, checking in" -> Lage, Type 2, "advise when ready for 9-line".
2. "ready to copy" -> 9-Liner; Zeilen 4, 6 und Restrictions zurücklesen -> "readback correct".
3. "request mark" -> Rauch aufs Ziel nach ca. 30 s; "tally target"; "laser on".
4. "Enfield 1-1, in from the south" -> "cleared hot"; GBU-12 auf den Spot; "off" -> BDA.
5. Zweiter Spieler checkt während eines Angriffs ein -> Warten mit Höhenstaffel.
6. Nach 15 min: Pontiac 1-1 (FAC(A), MQ-9) auf Station.
]] })
  m:vehicles({ name = "JTAC Axeman", p = A, hdg = 0, units = { { type = "Hummer" } } })   -- J1: leader by name
  m:vehicles({ name = "Blau Zug", p = { x = A.x, y = A.y - 600 }, hdg = 0, units = { { type = "M-113" }, { type = "M-113" } } })   -- line 8 friendlies
  m:vehicles({ side = "red", name = "Rot Panzer", p = tgt, hdg = 180, units = { { type = "T-72B" }, { type = "T-72B" }, { type = "BTR-80" } } })
  m:vehicles({ side = "red", name = "Rot Flak", p = { x = tgt.x + 1500, y = tgt.y + 300 }, hdg = 180, units = { { type = "ZSU-23-4 Shilka" } } })   -- threat remark
  m:plane({ name = "FACA Pontiac", cs = "Pontiac", task = "AFAC", freq = 133.5, start = 900, skill = "Excellent",
    units = { { type = "MQ-9 Reaper", name = "Pontiac 1-1", p = off(tgt, 200, 8), alt = 15000 * FT, spd = 70, hdg = 20 } },
    points = function() return { wp(off(tgt, 200, 8), 15000 * FT, 70), wp(tgt, 15000 * FT, 70, { tasks = { T.cmd("SetInvisible", { value = true }), T.orbit(15000 * FT, 70, "Circle") } }) } end })
  slot(m, { name = "Enfield 1-1 (Hornet CAS)", cs = "Enfield", type = "FA-18C_hornet", p = off(tgt, 225, 15), alt = 12000 * FT, spd = 300 * KT * 1.2, to = tgt, load = "hornetCAS", freq = 133.5, uhf = { 133.5, 251.5, 263.0, 261.0 } })
  slot(m, { name = "Colt 1-1 (F-16 CAS)", cs = "Colt", type = "F-16C_50", p = off(tgt, 180, 15), alt = 12000 * FT, spd = 300 * KT * 1.2, to = tgt, load = "viperCAS", freq = 251.5, uhf = UHF_LAND, vhf = { 133.5, 134.0, 132.0 } })
  slot(m, { name = "Uzi 1-1 (Hornet Kutaisi)", cs = "Uzi", type = "FA-18C_hornet", stand = "24", to = tgt, load = "hornetCAS", freq = 263.0, uhf = { 263.0, 133.5, 251.5 } })
  return m
end }

---------------------------------------------------------------------------------------------------
-- Build, pack, unpack again, check
sh('mkdir "' .. OUTDIR .. '" 2>nul & exit /b 0')
local allOk, cases = true, {}
for i, def in ipairs(missions) do
  local ms, dict = def.build():build()
  local stage = TMP .. "\\m" .. i
  sh('rmdir /s /q "' .. stage .. '" 2>nul & mkdir "' .. stage .. '\\l10n\\DEFAULT"')
  writeFile(stage .. "\\mission", serialize("mission", ms))
  writeFile(stage .. "\\options", TPL.options)
  writeFile(stage .. "\\warehouses", serialize("warehouses", TPL.warehouses))
  writeFile(stage .. "\\theatre", TPL.theatre)
  writeFile(stage .. "\\l10n\\DEFAULT\\dictionary", serialize("dictionary", dict))
  writeFile(stage .. "\\l10n\\DEFAULT\\mapResource", "mapResource = {}\n")
  local out = OUTDIR .. "\\" .. def.file
  os.remove(out)
  sh(TAR .. ' --format zip -cf "' .. out .. '" -C "' .. stage .. '" mission options warehouses theatre l10n/DEFAULT/dictionary l10n/DEFAULT/mapResource')
  local chk = TMP .. "\\c" .. i
  sh('rmdir /s /q "' .. chk .. '" 2>nul & mkdir "' .. chk .. '"')
  sh(TAR .. ' -xf "' .. out .. '" -C "' .. chk .. '"')
  local back = loadTable(readFile(chk .. "\\mission"), "mission")
  loadTable(readFile(chk .. "\\warehouses"), "warehouses")
  loadTable(readFile(chk .. "\\options"), "options")
  local ok, case = check(back, loadTable(readFile(chk .. "\\l10n\\DEFAULT\\dictionary"), "dictionary"), def.file)
  allOk = allOk and ok
  cases[def.file] = case
end
assert(cases["DCS-ATC Test - Carrier Case I.miz"] == 1 and cases["DCS-ATC Test - Carrier Case III.miz"] == 3, "Case-Wahl stimmt nicht")
print(allOk and "Alle Missionen OK -> " .. OUTDIR or "FEHLER")
os.exit(allOk and 0 or 1)
