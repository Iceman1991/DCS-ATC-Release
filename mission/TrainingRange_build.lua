TR = TR or {}
TR.zones = {
  range = { x = -293963, z = 656825, r = 2500, label = "RANGE ALPHA - Bodenziele" },
  sead = { x = -289512, z = 811215, r = 18000, label = "SEAD-ZONE GORI" },
  a2a = { x = -278763, z = 555455, r = 45000, label = "A2A-ARENA (See)" },
}

--[[
  Caucasus training range  –  pure DCS scripting (no MOOSE/MIST dependency)

  Principle: All spawnable targets are in the mission editor as "Late Activation" groups
  with the prefix TPL_ (templates). The script reads them at runtime from env.mission,
  clones them and spawns them with the chosen difficulty (skill, ROE, composition).
  -> Moving/changing templates in the editor = changing targets, without touching code.

  Operation in game: radio menu F10 -> "Trainings-Range"
  Splash Damage 3.4.7 (stevey666 et al., MIT) is loaded separately beforehand.
]]

TR = TR or {}

---------------------------------------------------------------------------
-- Configuration
---------------------------------------------------------------------------
TR.diff = 2   -- Starting difficulty (1..4)

TR.levels = {
  { name = "Anfänger", skill = "Average",   samROE = "hold", a2aROE = "hold",
    rangeExtra = {} },
  { name = "Normal",   skill = "Good",      samROE = "free", a2aROE = "return",
    rangeExtra = { "TPL_RANGE_AAA_LIGHT" } },
  { name = "Veteran",  skill = "High",      samROE = "free", a2aROE = "free",
    rangeExtra = { "TPL_RANGE_AAA_LIGHT", "TPL_RANGE_AAA_HEAVY" } },
  { name = "Ass",      skill = "Excellent", samROE = "free", a2aROE = "free",
    rangeExtra = { "TPL_RANGE_AAA_LIGHT", "TPL_RANGE_AAA_HEAVY", "TPL_RANGE_MANPAD" } },
}

TR.rangeBase = { "TPL_RANGE_ARMOR", "TPL_RANGE_SOFT" }

TR.sams = {
  { label = "SA-2 Guideline", tpl = "TPL_SAM_SA2" },
  { label = "SA-3 Goa",       tpl = "TPL_SAM_SA3" },
  { label = "SA-6 Gainful",   tpl = "TPL_SAM_SA6" },
  { label = "SA-8 Gecko",     tpl = "TPL_SAM_SA8" },
  { label = "SA-11 Gadfly",   tpl = "TPL_SAM_SA11" },
  { label = "SA-15 Gauntlet", tpl = "TPL_SAM_SA15" },
}

TR.bandits = {
  { label = "MiG-21bis",  tpl = "TPL_A2A_MIG21" },
  { label = "F-5E",       tpl = "TPL_A2A_F5" },
  { label = "MiG-23MLD",  tpl = "TPL_A2A_MIG23" },
  { label = "MiG-29A",    tpl = "TPL_A2A_MIG29" },
  { label = "Su-27",      tpl = "TPL_A2A_SU27" },
}

-- Zones are entered by the generator (DCS coordinates: x = north, z = east)
-- (zones see above)

---------------------------------------------------------------------------
-- Helper functions
---------------------------------------------------------------------------
local function msg(text, t)
  trigger.action.outText(text, t or 10)
end

local function deepCopy(o, seen)
  if type(o) ~= "table" then return o end
  seen = seen or {}
  if seen[o] then return seen[o] end
  local r = {}
  seen[o] = r
  for k, v in pairs(o) do r[deepCopy(k, seen)] = deepCopy(v, seen) end
  return r
end

local function resolveName(n)
  if type(n) == "string" and n:find("^DictKey_") then
    return env.getValueDictByKey(n)
  end
  return n
end

local catMap = {
  plane      = Group.Category.AIRPLANE,
  helicopter = Group.Category.HELICOPTER,
  vehicle    = Group.Category.GROUND,
  ship       = Group.Category.SHIP,
}

-- Read templates from the mission
TR.templates = {}
for _, coa in pairs(env.mission.coalition) do
  for _, ctry in pairs(coa.country or {}) do
    for cat, _ in pairs(catMap) do
      if ctry[cat] and ctry[cat].group then
        for _, g in pairs(ctry[cat].group) do
          local n = resolveName(g.name)
          if n and n:find("^TPL_") then
            TR.templates[n] = { data = g, countryId = ctry.id, category = cat }
          end
        end
      end
    end
  end
end

TR.spawned = { range = {}, convoy = {}, sam = {}, a2a = {} }
TR.counter = 0

local function lvl() return TR.levels[TR.diff] end

-- Set ROE/alert state (slightly delayed so the group certainly exists)
local function applyROE(groupName, kind, roe)
  timer.scheduleFunction(function()
    local grp = Group.getByName(groupName)
    if not grp or not grp:isExist() then return end
    local c = grp:getController()
    if kind == "ground" then
      c:setOption(AI.Option.Ground.id.ALARM_STATE, AI.Option.Ground.val.ALARM_STATE.RED)
      if roe == "hold" then
        c:setOption(AI.Option.Ground.id.ROE, AI.Option.Ground.val.ROE.WEAPON_HOLD)
      else
        c:setOption(AI.Option.Ground.id.ROE, AI.Option.Ground.val.ROE.OPEN_FIRE)
      end
    else
      local map = {
        hold     = AI.Option.Air.val.ROE.WEAPON_HOLD,
        ["return"] = AI.Option.Air.val.ROE.RETURN_FIRE,
        free     = AI.Option.Air.val.ROE.WEAPON_FREE,
      }
      c:setOption(AI.Option.Air.id.ROE, map[roe] or map.free)
      if roe == "hold" then
        c:setOption(AI.Option.Air.id.REACTION_ON_THREAT, AI.Option.Air.val.REACTION_ON_THREAT.NO_REACTION)
      end
    end
  end, nil, timer.getTime() + 1)
end

-- Clone and spawn template
function TR.spawn(tplName, opts)
  opts = opts or {}
  local t = TR.templates[tplName]
  if not t then
    msg("Template fehlt im Editor: " .. tplName, 15)
    return nil
  end
  local g = deepCopy(t.data)
  TR.counter = TR.counter + 1
  local newName = tplName:gsub("^TPL_", "") .. " #" .. TR.counter

  g.name = newName
  g.groupId = nil
  g.lateActivation = false
  g.hidden = false

  local skill = opts.skill or lvl().skill
  local units = {}
  for i, u in ipairs(g.units) do
    if not opts.count or i <= opts.count then
      u.unitId = nil
      u.name = newName .. "-" .. i
      u.skill = skill
      units[#units + 1] = u
    end
  end
  g.units = units

  local grp = coalition.addGroup(t.countryId, catMap[t.category], g)
  if grp then return newName end
  return nil
end

local function destroyList(list)
  for _, n in ipairs(list) do
    local g = Group.getByName(n)
    if g and g:isExist() then g:destroy() end
  end
  for i = #list, 1, -1 do list[i] = nil end
end

local function aliveCount(list)
  local groups, units = 0, 0
  for _, n in ipairs(list) do
    local g = Group.getByName(n)
    if g and g:isExist() and g:getSize() > 0 then
      groups = groups + 1
      units = units + g:getSize()
    end
  end
  return groups, units
end

---------------------------------------------------------------------------
-- Ground targets
---------------------------------------------------------------------------
function TR.buildRange(silent)
  destroyList(TR.spawned.range)
  local tpls = {}
  for _, n in ipairs(TR.rangeBase) do tpls[#tpls + 1] = n end
  for _, n in ipairs(lvl().rangeExtra) do tpls[#tpls + 1] = n end
  for _, n in ipairs(tpls) do
    local name = TR.spawn(n)
    if name then
      table.insert(TR.spawned.range, name)
      applyROE(name, "ground", lvl().samROE)
    end
  end
  if not silent then
    msg("Range Alpha aufgebaut (" .. lvl().name .. "). Siehe Wegpunkt 1 / F10-Karte.")
  end
end

function TR.startConvoy()
  local name = TR.spawn("TPL_CONVOY")
  if name then
    table.insert(TR.spawned.convoy, name)
    applyROE(name, "ground", lvl().samROE)
    msg("Konvoi gestartet: Samtredia -> Lanchkhuti (auf der Straße).")
  end
end

function TR.clearGround()
  destroyList(TR.spawned.range)
  destroyList(TR.spawned.convoy)
  msg("Alle Bodenziele entfernt.")
end

---------------------------------------------------------------------------
-- SEAD
---------------------------------------------------------------------------
function TR.spawnSAM(entry)
  local name = TR.spawn(entry.tpl)
  if name then
    table.insert(TR.spawned.sam, name)
    applyROE(name, "ground", lvl().samROE)
    local hint = lvl().samROE == "hold" and " (Radar an, schießt NICHT)" or " (Waffen frei!)"
    msg(entry.label .. " aktiv in SEAD-Zone Gori" .. hint)
  end
end

function TR.spawnRandomSAM()
  TR.spawnSAM(TR.sams[math.random(#TR.sams)])
end

function TR.setSamROE(roe)
  for _, n in ipairs(TR.spawned.sam) do applyROE(n, "ground", roe) end
  for _, n in ipairs(TR.spawned.range) do applyROE(n, "ground", roe) end
  msg(roe == "hold" and "Alle SAM/AAA: WAFFEN HALTEN (Radar bleibt an)." or "Alle SAM/AAA: WAFFEN FREI.")
end

function TR.clearSAM()
  destroyList(TR.spawned.sam)
  msg("Alle SAM-Sites entfernt.")
end

---------------------------------------------------------------------------
-- Air-to-air
---------------------------------------------------------------------------
function TR.spawnBandits(entry, count)
  local name = TR.spawn(entry.tpl, { count = count })
  if name then
    table.insert(TR.spawned.a2a, name)
    applyROE(name, "air", lvl().a2aROE)
    local roeText = ({ hold = "wehrlos", ["return"] = "erwidern Feuer", free = "Waffen frei" })[lvl().a2aROE]
    msg(count .. "x " .. entry.label .. " gespawnt in der A2A-Arena über dem Meer (" .. lvl().name .. ", " .. roeText .. ").")
  end
end

function TR.spawnTransport()
  local name = TR.spawn("TPL_A2A_TRANSPORT", { skill = "Average" })
  if name then
    table.insert(TR.spawned.a2a, name)
    applyROE(name, "air", "hold")
    msg("An-26 Zielflugzeug fliegt Racetrack in der A2A-Arena (wehrlos).")
  end
end

function TR.clearA2A()
  destroyList(TR.spawned.a2a)
  msg("Alle Luftziele entfernt.")
end

---------------------------------------------------------------------------
-- Difficulty / status / markers
---------------------------------------------------------------------------
function TR.setDifficulty(i)
  TR.diff = i
  for _, n in ipairs(TR.spawned.sam) do applyROE(n, "ground", lvl().samROE) end
  for _, n in ipairs(TR.spawned.a2a) do applyROE(n, "air", lvl().a2aROE) end
  TR.buildRange(true)
  msg("Schwierigkeit: " .. lvl().name .. " (Skill " .. lvl().skill .. ").\n" ..
      "Range Alpha wurde neu aufgebaut. Neue Spawns nutzen diesen Skill.", 12)
end

function TR.status()
  local rg, ru = aliveCount(TR.spawned.range)
  local cg, cu = aliveCount(TR.spawned.convoy)
  local sg, su = aliveCount(TR.spawned.sam)
  local ag, au = aliveCount(TR.spawned.a2a)
  msg(string.format(
    "TRAININGS-STATUS  |  Schwierigkeit: %s\n" ..
    "Range Alpha: %d Gruppen / %d Fahrzeuge\n" ..
    "Konvois: %d / %d Fahrzeuge\n" ..
    "SAM-Sites: %d / %d Einheiten\n" ..
    "Luftziele: %d Flights / %d Flugzeuge",
    lvl().name, rg, ru, cg, cu, sg, su, ag, au), 15)
end

TR.markIds = {}
function TR.drawZones()
  for _, id in ipairs(TR.markIds) do trigger.action.removeMark(id) end
  TR.markIds = {}
  local id = 7000
  local colors = {
    range = { { 1, 0.6, 0, 1 }, { 1, 0.6, 0, 0.15 } },
    sead  = { { 1, 0, 0, 1 },   { 1, 0, 0, 0.12 } },
    a2a   = { { 0, 0.6, 1, 1 }, { 0, 0.6, 1, 0.12 } },
  }
  for key, z in pairs(TR.zones) do
    local c = colors[key] or colors.range
    local p = { x = z.x, y = 0, z = z.z }
    id = id + 1
    trigger.action.circleToAll(-1, id, p, z.r, c[1], c[2], 1, true)
    table.insert(TR.markIds, id)
    id = id + 1
    trigger.action.markToAll(id, z.label, p, true)
    table.insert(TR.markIds, id)
  end
end

function TR.resetAll()
  destroyList(TR.spawned.convoy)
  destroyList(TR.spawned.sam)
  destroyList(TR.spawned.a2a)
  TR.buildRange(true)
  msg("Training zurückgesetzt. Range Alpha neu aufgebaut, alles andere entfernt.")
end

---------------------------------------------------------------------------
-- Hit reports
---------------------------------------------------------------------------
TR.eh = {}
TR.reported = {}
-- S_EVENT_KILL delivers the shooter. Splash Damage explosions have no shooter,
-- these kills only come as S_EVENT_DEAD -> wait briefly, then report without a shooter.
function TR.eh:onEvent(e)
  if e.id == world.event.S_EVENT_KILL then
    local tgt, shooter = e.target, e.initiator
    if not tgt or not tgt.getCoalition or tgt:getCoalition() ~= coalition.side.RED then return end
    local key = tgt.getName and tgt:getName() or tostring(tgt)
    if TR.reported[key] then return end
    TR.reported[key] = true
    local who = "?"
    if shooter and shooter.getPlayerName and shooter:getPlayerName() then
      who = shooter:getPlayerName()
    elseif shooter and shooter.getTypeName then
      who = shooter:getTypeName()
    end
    local tname = tgt.getTypeName and tgt:getTypeName() or "Ziel"
    msg("ZERSTÖRT: " .. tname .. "  (durch " .. who .. ")", 8)
  elseif e.id == world.event.S_EVENT_DEAD or e.id == world.event.S_EVENT_UNIT_LOST then
    local u = e.initiator
    if not u or not u.getCoalition then return end
    local ok, side = pcall(u.getCoalition, u)
    if not ok or side ~= coalition.side.RED then return end
    local key = u.getName and u:getName() or tostring(u)
    local tname = u.getTypeName and u:getTypeName() or "Ziel"
    timer.scheduleFunction(function()
      if TR.reported[key] then return end
      TR.reported[key] = true
      msg("ZERSTÖRT: " .. tname .. "  (Splash/Sekundärschaden)", 8)
    end, nil, timer.getTime() + 2)
  end
end
world.addEventHandler(TR.eh)

---------------------------------------------------------------------------
-- F10 menu
---------------------------------------------------------------------------
local root = missionCommands.addSubMenu("Trainings-Range")

local mDiff = missionCommands.addSubMenu("Schwierigkeit", root)
for i, l in ipairs(TR.levels) do
  missionCommands.addCommand(l.name, mDiff, TR.setDifficulty, i)
end

local mGround = missionCommands.addSubMenu("Bodenziele (Range Alpha)", root)
missionCommands.addCommand("Range neu aufbauen", mGround, TR.buildRange, false)
missionCommands.addCommand("Fahrenden Konvoi starten", mGround, TR.startConvoy)
missionCommands.addCommand("Bodenziele entfernen", mGround, TR.clearGround)

local mSead = missionCommands.addSubMenu("SEAD (Zone Gori)", root)
for _, s in ipairs(TR.sams) do
  missionCommands.addCommand(s.label, mSead, TR.spawnSAM, s)
end
missionCommands.addCommand("Zufällige Site", mSead, TR.spawnRandomSAM)
missionCommands.addCommand("SAMs: Waffen halten", mSead, TR.setSamROE, "hold")
missionCommands.addCommand("SAMs: Waffen frei", mSead, TR.setSamROE, "free")
missionCommands.addCommand("Alle SAMs entfernen", mSead, TR.clearSAM)

local mAir = missionCommands.addSubMenu("Luftziele (Arena See)", root)
for _, b in ipairs(TR.bandits) do
  local sub = missionCommands.addSubMenu(b.label, mAir)
  for _, n in ipairs({ 1, 2, 4 }) do
    missionCommands.addCommand(n .. "x spawnen", sub, TR.spawnBandits, b, n)
  end
end
missionCommands.addCommand("An-26 Zielflugzeug", mAir, TR.spawnTransport)
missionCommands.addCommand("Alle Luftziele entfernen", mAir, TR.clearA2A)

missionCommands.addCommand("Status anzeigen", root, TR.status)
missionCommands.addCommand("Kartenmarker neu zeichnen", root, TR.drawZones)
missionCommands.addCommand("Alles zurücksetzen", root, TR.resetAll)

---------------------------------------------------------------------------
-- Start
---------------------------------------------------------------------------
TR.buildRange(true)
local ewr = TR.spawn("TPL_EWR")
TR.drawZones()

local found = 0
for _ in pairs(TR.templates) do found = found + 1 end
env.info("TrainingRange: " .. found .. " Templates geladen.")
msg("Kaukasus Trainings-Range bereit.\nF10 -> Trainings-Range für Ziele, SAMs, Banditen und Schwierigkeit.\n" ..
    "Aktuelle Schwierigkeit: " .. lvl().name, 20)
