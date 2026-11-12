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

-- Radio wheel as a picture in the game (#30, setting WheelInGame "window"): DcsAtc.exe draws the open wheel into %TMP%\DcsAtc-Wheel\<hash>.png and names
-- the file and its size in show.txt ("file|fraction of the screen height"); no show.txt = wheel closed. The window is DCS's own GUI (dxgui),
-- so DCS draws it in the headset too. Display only: no cursor, the wheel is operated with its keys / HOTAS as before.
local wheelDir = tmp .. '\\DcsAtc-Wheel'
local wheel = { next = 0 }
local WHEEL_DLG = [[dialog = {
  ["type"] = "Window",
  ["params"] = { ["bounds"] = { [1] = { ["x"] = 0, ["y"] = 0, ["w"] = 600, ["h"] = 600 } }, ["draggable"] = false, ["enabled"] = true, ["hasCursor"] = false,
                 ["lockFlow"] = false, ["modal"] = false, ["offscreen"] = false, ["resizable"] = false, ["zOrder"] = 111, ["text"] = "" },
  ["skin"] = { ["params"] = { ["headerHeight"] = 0, ["name"] = "windowSkin" }, ["states"] = { ["released"] = { [1] = { ["bkg"] = { ["center_center"] = "0x00000000" } } } } },
  ["children"] = {
    ["pic"] = { ["type"] = "Static", ["params"] = { ["bounds"] = { ["x"] = 0, ["y"] = 0, ["w"] = 600, ["h"] = 600 }, ["text"] = "" },
                ["skin"] = { ["params"] = { ["name"] = "staticSkin" } } },
  },
}]]
local function wheelShow(line)
  if not line then if wheel.win then wheel.win:setVisible(false) end return end
  local file, frac = line:match('^([%w]+%.png)|([%d.]+)$')
  if not file then return end
  if not wheel.win then
    local dlg = wheelDir .. '\\wheel.dlg'
    local d = assert(io.open(dlg, 'w'))
    d:write(WHEEL_DLG) d:close()
    wheel.win = require('DialogLoader').spawnDialogFromFile(dlg)
    wheel.win:setHasCursor(false)
    net.log('DcsAtcHook: Rad-Fenster angelegt')
  end
  local sw, sh = require('dxgui').GetScreenSize()
  local s = math.floor(sh * tonumber(frac))
  local skin = require('SkinUtils').setStaticPicture(wheelDir .. '\\' .. file, wheel.win.pic:getSkin())
  skin.skinData.states.released[1].picture.size = { horz = s, vert = s }   -- the PNG is 840 px, DCS scales it
  wheel.win.pic:setSkin(skin)
  wheel.win:setBounds(math.floor((sw - s) / 2), math.floor((sh - s) / 2), s, s)
  wheel.win.pic:setBounds(0, 0, s, s)
  wheel.win:setVisible(true)
end
local function wheelPoll()
  if wheel.dead then return end
  local h = io.open(wheelDir .. '\\show.txt', 'r')
  local line = h and h:read('*l') or nil
  if h then h:close() end
  if line == wheel.line then return end
  wheel.line = line
  local ok, err = pcall(wheelShow, line)
  if not ok then wheel.dead = true net.log('DcsAtcHook: Rad-Fenster abgeschaltet: ' .. tostring(err)) end   -- no GUI (dedicated server) or another dxgui: once, not every frame
end

function cb.onSimulationFrame()
  local now = os.clock()
  if now >= wheel.next then wheel.next = now + 0.05; wheelPoll() end
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
