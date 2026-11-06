-- DCS-ATC (mod): starts DcsAtc.exe with every mission and forwards radio wheel requests from fellow players to the host.
--  Mission start: %TMP%\DcsAtc-Session.txt = role (host = single player/server, client = fellow player) + own player name,
--                 then DcsAtc.exe --auto (path is written by the installer). Mission end: "stop" -> the app exits.
--  Fellow players: DcsAtc.exe puts wheel requests into %TMP%\DcsAtc-Chat\*.txt, this hook sends them as chat "ATC>..." to the host.
--  Host:       intercepts "ATC>..." (nobody sees it) and files the request for DcsAtc.exe (%TMP%\DcsAtc-Menu\*.txt, "@player|text").
local lfs = require('lfs')
local tmp = os.getenv('TMP') or os.getenv('TEMP') or ''
local outDir, inDir = tmp .. '\\DcsAtc-Chat', tmp .. '\\DcsAtc-Menu'   -- '\\' required: Lua 5.1 turns '\D' into just 'D'
local n, nextPoll = 0, 0
local cb = {}
local Sim = Sim or DCS

do   -- DCS installation folder for the app (Radio.lua/Beacons.lua, MissionScripting.lua), also with Steam/another drive
  local h = io.open(tmp .. '\\DcsAtc-DcsDir.txt', 'w')
  if h then h:write(lfs.currentdir()) h:close() end
  h = io.open(tmp .. '\\DcsAtc-WriteDir.txt', 'w')   -- Saved Games of this instance (dedicated server: DCS.server or -w <name>)
  if h then h:write(lfs.writedir()) h:close() end
end

local function session(text)
  local h = io.open(tmp .. '\\DcsAtc-Session.txt', 'w')
  if h then h:write(text) h:close() end
end

function cb.onSimulationStart()
  local off = io.open(lfs.writedir() .. 'Scripts\\DcsAtc\\DcsAtcOff.txt', 'r')   -- forum 0.13: switched off in the settings window, no app start
  if off then off:close() return end
  local host = not Sim.isMultiplayer() or Sim.isServer()
  local ok, me = pcall(function() return net.get_player_info(net.get_my_player_id(), 'name') end)
  session((host and 'host' or 'client') .. '\n' .. (ok and me or '') .. '\n')
  pcall(function()   -- stand names as in the ME/kneeboard ("F125") by airfield and Term_Index (ME: unit.parking = crossroad_index); the mission script only knows the index
    local T, m = require('terrain'), Sim.getCurrentMission()
    local l = { 'M;' .. tostring((m.mission or m).theatre) }
    for id, a in pairs(T.GetTerrainConfig('Airdromes') or {}) do
      for _, s in pairs(a.roadnet and T.getStandList(a.roadnet, {"SHELTER","FOR_HELICOPTERS","FOR_AIRPLANES","WIDTH","LENGTH","HEIGHT"}) or {}) do
        l[#l + 1] = 'S;' .. tostring(id) .. ';' .. tostring(s.crossroad_index) .. ';' .. tostring(s.name):gsub('[;\r\n]', ' ')
      end
    end
    local f = io.open(tmp .. '\\DcsAtc-Stands.txt', 'w')
    if f then f:write(table.concat(l, '\n') .. '\n') f:close() end
  end)
  local h = io.open(lfs.writedir() .. 'Scripts\\DcsAtc\\DcsAtcPath.txt', 'r')
  local exe = h and h:read('*l')
  if h then h:close() end
  if exe and exe ~= '' and lfs.attributes(exe) then   -- if it is already running, the second one exits immediately
    os.execute('start "DCS-ATC" /min "' .. exe .. '" --auto')
  end
end

function cb.onSimulationStop() session('stop\n') end

function cb.onPlayerTrySendChat(id, msg, all)
  if type(msg) ~= 'string' or msg:sub(1, 4) ~= 'ATC>' then return end
  local name = (net.get_player_info(id, 'name') or ''):gsub('[|\r\n]', ' ')
  n = n + 1
  lfs.mkdir(inDir)
  local f = string.format('%s\\%010d-c%04d', inDir, os.time(), n)
  local h = io.open(f .. '.tmp', 'w')
  if h then
    h:write('@' .. name .. '|' .. msg:sub(5))
    h:close()
    os.rename(f .. '.tmp', f .. '.txt')
  end
  return ''   -- do not show in chat
end

-- AI wingmen by voice: the app writes "par;wMsgLeader..." (par as in the DCS radio menu: 1-3 wingman 2-4, 5 flight). Performs the menu item
-- that sends this message to that recipient, with the menu's own parameters: independent of the module's numbering and of the DCS language, no keys.
local wingFile = tmp .. '\\DcsAtc-Wing.txt'
local function wing(par, name)
  local P, id = package.loaded['RadioCommandDialogsPanel'] or rawget(_G, 'RadioCommandDialogsPanel'), rawget(_G, 'Message') and Message[name]
  local root = P and P.getDataParameter and P.getDataParameter('rootItem')
  if not root then return 'no radio menu' end
  if not id then return 'unknown message ' .. name end
  local function only()   -- the addressed wingman is the only one: "Flight" reaches just him (F-16C/FA-18C list "Wingman n" only if unit n+1 exists, so never in a two-ship)
    local u = P.getDataParameter('pUnit')
    local g = u and u:getGroup()
    if not g or par > 3 then return false end
    for i = 2, 4 do if (g:getUnit(i) ~= nil) ~= (i == par + 1) then return false end end
    return true
  end
  local function sub(it) return it.submenu or it.getSubmenu and it:getSubmenu() end
  local path = {}
  local function find(m, d)
    for _, it in pairs(m and m.items or {}) do
      path[d] = it
      local c = it.command
      if c and (c.command == id or c.sendMessage and c.sendMessage.command == id) or d < 5 and sub(it) and find(sub(it), d + 1) then return true end
    end
    path[d] = nil
  end
  for _, it in pairs(sub(root).items or {}) do
    if it.parameter == par and (not it.condition or it.condition:check()) then
      path = { it }
      if find(sub(it), 2) then
        local p = {}
        for _, x in ipairs(path) do   -- as CommandMenu.performCommands: each command gets the parameters collected before it (recipient first tunes the radio)
          if x.command then x.command:perform(p) p = {} end
          if x.parameter then p[#p + 1] = x.parameter elseif x.parameters then for _, v in ipairs(x.parameters) do p[#p + 1] = v end end
        end
        return 'ok'
      end
    end
  end
  if only() then return wing(5, name) end
  return 'not in the radio menu (recipient ' .. par .. ')'
end

local function wingPoll()
  local h = io.open(wingFile, 'r')
  if not h then return end
  local text = h:read('*a') or ''
  h:close()
  os.remove(wingFile)
  local par, name = text:match('^(%d+);([%w_]+)')
  local ok, r = pcall(wing, tonumber(par), name or '')
  r = ok and r or 'error ' .. tostring(r)
  lfs.mkdir(inDir)
  n = n + 1
  local f = string.format('%s\\%010d-w%04d', inDir, os.time(), n)
  h = io.open(f .. '.tmp', 'w')
  if h then h:write('WING;' .. tostring(r):gsub('[;\r\n]', ' ') .. ';' .. text:gsub('[\r\n]', '')) h:close() os.rename(f .. '.tmp', f .. '.evt') end
end

function cb.onSimulationFrame()
  local now = os.clock()
  if now < nextPoll then return end
  nextPoll = now + 0.3
  wingPoll()
  if Sim.isServer() or not lfs.attributes(outDir) then return end
  for f in lfs.dir(outDir) do
    if f:sub(-4) == '.txt' then
      local path = outDir .. '\\' .. f
      local h = io.open(path, 'r')
      if h then
        local text = h:read('*a')
        h:close()
        os.remove(path)
        if text and #text > 0 then net.send_chat('ATC>' .. text, false) end
      end
    end
  end
end

Sim.setUserCallbacks(cb)
net.log('DcsAtcHook geladen')
