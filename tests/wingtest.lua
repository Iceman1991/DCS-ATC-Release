-- DcsAtcWing.lua (wingman commands) against the real DCS radio menus (F-16C, FA-18C, Su-25T = LockOnAirplane, TF-51D = FighterWWII).
-- luae.exe tests\wingtest.lua mod\Scripts\DcsAtc\DcsAtcWing.lua "C:\Program Files\Eagle Dynamics\DCS World"
local hook, dcs = arg[1], arg[2]
local tmp = os.getenv('TEMP') .. '\\DcsAtcHookTest'
os.execute('mkdir "' .. tmp .. '" 2>nul')
local getenv, clock = os.getenv, 0
os.getenv = function(k) if k == 'TMP' or k == 'TEMP' then return tmp end return getenv(k) end
os.clock = function() clock = clock + 1 return clock end
local tick
timer = { getTime = function() return 0 end, scheduleFunction = function(f) tick = f return 1 end, removeFunction = function() end }
lfs = { mkdir = function(d) os.execute('mkdir "' .. d .. '" 2>nul') end, attributes = function() return nil end, writedir = function() return tmp .. '\\' end, currentdir = function() return dcs end,
        dir = function() return function() end end }
package.loaded.lfs = lfs
net = { log = function() end }
Message = setmetatable({}, { __index = function(t, k) return k:match('^wMsgLeader') and k or nil end })   -- ids = names; unknown names nil
os.execute('mkdir "' .. tmp .. '\\Scripts\\DcsAtc" 2>nul')
do local h = io.open(tmp .. '\\Scripts\\DcsAtc\\DcsAtcWing.lua', 'w') h:write('-- probe file for the io check (arm)') h:close() end   -- Issue #55: arm() probes io with this file
local started
afterStart = function() started = true end   -- autoexec.lua
dofile(hook)

local fails, sent, tuned = 0, {}, nil
local function check(ok, what) print((ok and 'OK   ' or 'FAIL ') .. what) if not ok then fails = fails + 1 end end

-- Menu config of a module in a stub of the RadioCommandDialogsPanel environment, wingmen present: 2..units
local function load(file, units, ...)
  local group = { getUnit = function(_, i) return i <= units and { getCommunicator = function() return 'comm' .. i end } or nil end }
  local data = { menus = { ['Ground Crew'] = { items = {} } }, showingOnlyPresentRecepients = true, menuOther = { submenu = { items = {} } }, menuEmbarkToTransport = { submenu = { items = {} } },
                 pUnit = { isExist = function() return true end, getGroup = function() return group end } }
  local sm = {}
  sm.__index = sm
  function sm.perform(self, p)
    local all = {}
    for _, v in ipairs(p) do all[#all + 1] = tostring(v) end
    for _, v in ipairs(self.parameters or {}) do all[#all + 1] = tostring(v) end
    sent[#sent + 1] = self.command .. '(' .. table.concat(all, ',') .. ')'
  end
  local env = setmetatable({ data = data, _ = function(s) return s end, Message = Message, Airbase = { Category = { AIRDROME = 0, SHIP = 2 } },
    sendMessage = { new = function(c, ...) return setmetatable({ command = c, parameters = { ... } }, sm) end, new2 = function() return setmetatable({}, sm) end, new3 = function(c) return setmetatable({ command = c }, sm) end },
    staticParamsEvent = function() return { perform = function() end } end, getRecepientColor = function() end, selectAndTuneCommunicator = function(c) tuned = c end,
    db = { Formations = {}, FormationID = setmetatable({}, { __index = function() return 1 end }) },
    utils = { verifyChunk = function(f) return f end } }, { __index = _G })
  env.utils.loadfileIn = function(path, e)   -- module config -> LockOnAirplane/FighterWWII for real, Common (ATC, Tanker …) left out
    if path:find('/Common/') then return function() end end
    return setfenv(assert(loadfile(dcs .. '\\' .. path)), e)
  end
  local chunk = assert(loadfile(dcs .. '\\' .. file))
  setfenv(chunk, env)(...)
  package.loaded.RadioCommandDialogsPanel = { getDataParameter = function(k) return data[k] end }
end

local function wing(cmd)
  sent, tuned = {}, nil
  local f = io.open(tmp .. '\\DcsAtc-Wing.txt', 'w') f:write(cmd) f:close()
  tick()
  local res
  local p = io.popen('dir /b "' .. tmp .. '\\DcsAtc-Menu\\*.evt" 2>nul')
  for name in p:lines() do
    local h = io.open(tmp .. '\\DcsAtc-Menu\\' .. name) res = h:read('*a') h:close()
    os.remove(tmp .. '\\DcsAtc-Menu\\' .. name)
  end
  p:close()
  return (res or 'nothing'):match('^WING;([^;]*)'), table.concat(sent, ' '), tuned
end

local r, s, t = wing('1;wMsgLeaderJoinUp')
check(r == 'no radio menu' and s == '', 'ohne Funkmenue: ' .. tostring(r))

load('Mods\\aircraft\\F-16C\\comm.lua', 4)
r, s, t = wing('1;wMsgLeaderJoinUp')
check(r == 'ok' and s == 'wMsgLeaderJoinUp(1)' and t == 'comm2', 'F-16C Two rejoin -> ' .. tostring(r) .. ' ' .. s .. ' ' .. tostring(t))
r, s = wing('5;wMsgLeaderGoTrail')
check(r == 'ok' and s == 'wMsgLeaderGoTrail(5)', 'F-16C Flight go trail (Formation per getSubmenu) -> ' .. tostring(r) .. ' ' .. s)
r, s = wing('5;wMsgLeaderEngageBandits')
check(r == 'ok' and s == 'wMsgLeaderEngageBandits(5,false,false)', 'F-16C Flight engage bandits mit Menue-Parametern -> ' .. tostring(r) .. ' ' .. s)
r, s = wing('1;wMsgLeaderGoTrail')
check(r and r:find('not in the radio menu') and s == '', 'F-16C Two go trail (Wingman ohne Formationsmenue) -> ' .. tostring(r))
r = wing('1;wMsgNoSuch')
check(r and r:find('unknown message'), 'unbekannte Nachricht -> ' .. tostring(r))
r, s = wing('3;wMsgLeaderJoinUp')
check(r and r:find('not in the radio menu') and s == '', 'F-16C Four im Vierer (DCS listet Wingman 4 nicht) -> ' .. tostring(r))

load('Mods\\aircraft\\F-16C\\comm.lua', 2)
r, s, t = wing('1;wMsgLeaderGoTrail')
check(r == 'ok' and s == 'wMsgLeaderGoTrail(5)' and t == 'comm2', 'F-16C Zweier: Two go trail ueber Flight -> ' .. tostring(r) .. ' ' .. s)

load('Mods\\aircraft\\FA-18C\\comm.lua', 2)
r, s = wing('1;wMsgLeaderCoverMe')
check(r == 'ok' and s == 'wMsgLeaderCoverMe(5)', 'FA-18C Zweier: Two cover me ueber Flight -> ' .. tostring(r) .. ' ' .. s)
r, s = wing('2;wMsgLeaderJoinUp')
check(r and r:find('not in the radio menu') and s == '', 'FA-18C Three ohne Nummer 3 in der Gruppe -> ' .. tostring(r))

load('Mods\\aircraft\\Su-25T\\comm.lua', 4)
r, s, t = wing('2;wMsgLeaderJoinUp')
check(r == 'ok' and s == 'wMsgLeaderJoinUp(2)' and t == 'comm3', 'Su-25T Three rejoin -> ' .. tostring(r) .. ' ' .. s .. ' ' .. tostring(t))
r, s = wing('5;wMsgLeaderEchelonRight')
check(r == 'ok' and s == 'wMsgLeaderEchelonRight(5)', 'Su-25T Flight echelon right (Go Formation) -> ' .. tostring(r) .. ' ' .. s)

load('Mods\\aircraft\\TF-51D\\comm.lua', 2)
r, s = wing('1;wMsgLeaderJoinUp')
check(r == 'ok' and s:find('^wMsgLeaderJoinUp%(1'), 'TF-51D (FighterWWII) Two rejoin -> ' .. tostring(r) .. ' ' .. s)

local vis = true
package.loaded.RadioCommandDialogsPanel.isVisible = function() return vis end
r, s = wing('MENU;state')
check(r == 'open' and s == '', 'MENU;state, Menue sichtbar -> ' .. tostring(r))
vis = false
r, s = wing('MENU;state')
check(r == 'closed' and s == '', 'MENU;state, Menue zu -> ' .. tostring(r))
-- DCS-internal dialog state: three words (menu, main/dialog, stage of the tanker dialog); without the internals the old one-word reply (checked above)
local P, mainIt = package.loaded.RadioCommandDialogsPanel, {}
local tk = { dialogFsmData = { transitions = { ['Cleared contact'] = {} } }, dialogFsm = { state = 'Pre-contact' } }
local other = { dialogFsmData = { transitions = {} }, dialogFsm = { state = 'Closed' } }
P.commonCommandMenuIt, P.curDialogIt, P.commandDialogIts = mainIt, mainIt, {}
r = wing('MENU;state')
check(r == 'closed main -', 'MENU;state, kein Tanker-Dialog -> ' .. tostring(r))
P.commandDialogIts = { [7] = { Tanker = { element_ = tk }, ATC = { element_ = other } } }
P.curDialogIt, vis = { element_ = tk }, true
r = wing('MENU;state')
check(r == 'open dialog Pre-contact', 'MENU;state, Tanker-Dialog aktuell, sichtbar -> ' .. tostring(r))
tk.dialogFsm.state, P.curDialogIt = 'Cleared contact', mainIt
r = wing('MENU;state')
check(r == 'open main Cleared contact', 'MENU;state, Stufe mit Leerzeichen, Hauptmenue -> ' .. tostring(r))
P.commandDialogIts = nil
r = wing('MENU;state')
check(r == 'open', 'MENU;state, Interna fehlen -> ' .. tostring(r))
P.commonCommandMenuIt, P.curDialogIt = nil, nil

tick = nil
afterStart()
check(started and tick ~= nil, 'Missionsstart: afterStart von DCS laeuft weiter, Abfrage neu gestartet')

-- Issue #55: MP client = io sandboxed in the simulation state: every failed io.open is an ERROR line in dcs.log. Load the script with a fake io, tick 100 times.
local realopen = io.open
local function fakeLoad(open)
  local realio, realtimer, calls, scheduled = io, timer, 0, 0
  local fakeio = setmetatable({ open = function(p, m) calls = calls + 1 return open(p, m) end }, { __index = realio })
  io = fakeio
  timer = { getTime = function() return 0 end, removeFunction = function() end, scheduleFunction = function(f) scheduled = scheduled + 1 tick = f return 1 end }
  local after = afterStart
  tick = nil
  local ok, err = pcall(dofile, hook)
  if tick then for _ = 1, 100 do tick() end end
  io, timer, afterStart = realio, realtimer, after
  assert(ok, err)
  return calls, scheduled
end
local calls, scheduled = fakeLoad(function() return nil end)
check(calls <= 2 and scheduled == 0, 'gesperrtes io (MP-Client): hoechstens 2 io.open, keine Abfrage -> ' .. calls .. ' io.open, ' .. scheduled .. ' geplant')
calls, scheduled = fakeLoad(function(p, m) if p:find('DcsAtcOff', 1, true) then return nil end return realopen(p, m) end)   -- io works, no Off file
check(scheduled == 1, 'io frei, DCS-ATC an: Abfrage geplant -> ' .. scheduled)
calls, scheduled = fakeLoad(function(p, m) if p:find('DcsAtcOff', 1, true) then return { close = function() end } end return realopen(p, m) end)
check(scheduled == 0, 'DCS-ATC aus (DcsAtcOff.txt): keine Abfrage -> ' .. scheduled)

os.execute('rmdir /s /q "' .. tmp .. '" 2>nul')
if fails > 0 then print('FAIL ' .. fails .. ' Fehler') os.exit(1) end
print("wingtest OK")
