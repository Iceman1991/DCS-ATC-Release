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
  local host = not Sim.isMultiplayer() or Sim.isServer()
  local ok, me = pcall(function() return net.get_player_info(net.get_my_player_id(), 'name') end)
  session((host and 'host' or 'client') .. '\n' .. (ok and me or '') .. '\n')
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

function cb.onSimulationFrame()
  local now = os.clock()
  if now < nextPoll then return end
  nextPoll = now + 0.3
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
